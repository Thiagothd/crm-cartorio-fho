using System.Data;
using System.Data.Common;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Atendimento.Domain;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Enums;
using CrmCartorio.Api.Modules.RespostaInteligente.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <inheritdoc cref="IProcessadorDeEntradaWhatsapp"/>
public sealed class ProcessadorDeEntradaWhatsapp(
    CrmDbContext db,
    IDisparadorDeMensagemAutomatica automaticas,
    INotificadorDeAtendimento notificador,
    IEnviadorDeCanal canal,
    IRespostaInteligenteService respostaInteligente,
    IOptions<OpcoesDaIA> opcoesDaIA,
    ILogger<ProcessadorDeEntradaWhatsapp> log) : IProcessadorDeEntradaWhatsapp
{
    private static readonly TimeSpan ExpiracaoDoClaim = TimeSpan.FromMinutes(5);
    private readonly OpcoesDaIA _opcoesDaIA = opcoesDaIA.Value;

    public async Task<IReadOnlyList<Guid>> ListarConversasPendentesAsync(
        int limite = 100,
        CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;
        var expiradoAntesDe = agora - ExpiracaoDoClaim;

        var entradas = await db.Mensagens.AsNoTracking()
            .Where(m => m.AutorTipo == AutorTipo.Cliente
                     && m.IdExternoWhatsapp != null
                     && (m.EstadoProcessamentoEntrada == EstadoProcessamentoEntradaWhatsapp.Pendente
                         || (m.EstadoProcessamentoEntrada == EstadoProcessamentoEntradaWhatsapp.Falha
                             && (m.ProcessamentoEntradaDisponivelEm == null
                                 || m.ProcessamentoEntradaDisponivelEm <= agora))
                         || (m.EstadoProcessamentoEntrada == EstadoProcessamentoEntradaWhatsapp.Processando
                             && m.ProcessamentoEntradaIniciadoEm < expiradoAntesDe)))
            .OrderBy(m => m.EnviadaEm)
            .Select(m => m.ConversaId)
            .Distinct()
            .Take(Math.Clamp(limite, 1, 500))
            .ToListAsync(ct);

        // Recupera tambem a janela "entrada processada + saida persistida, mas o
        // processo morreu antes de chamar a Evolution". O claim WA-OUT-01 continua
        // sendo a autoridade sobre o envio; aqui apenas acordamos o mesmo enviador.
        var saidas = await db.Mensagens.AsNoTracking()
            .Where(m => m.AutorTipo == AutorTipo.Sistema
                     && m.EhAutomatica
                     && m.StatusEntrega == StatusEntrega.Pendente
                     && !entradas.Contains(m.ConversaId))
            .OrderBy(m => m.EnviadaEm)
            .Select(m => m.ConversaId)
            .Distinct()
            .Take(Math.Clamp(limite - entradas.Count, 0, 500))
            .ToListAsync(ct);

        return [.. entradas, .. saidas];
    }

    public async Task ProcessarConversaAsync(Guid conversaId, CancellationToken ct = default)
    {
        var abriuConexao = db.Database.GetDbConnection().State != ConnectionState.Open;
        if (abriuConexao)
        {
            await db.Database.OpenConnectionAsync(ct);
        }

        var recurso = $"CRM:WA-IN:{conversaId:N}";
        var adquiriu = false;

        try
        {
            adquiriu = await TentarAdquirirLockAsync(recurso, ct);
            if (!adquiriu)
            {
                return;
            }

            while (!ct.IsCancellationRequested)
            {
                db.ChangeTracker.Clear();

                var mensagemId = await TentarAdquirirProximaMensagemAsync(conversaId, ct);
                if (mensagemId is not { } id)
                {
                    break;
                }

                if (!await ProcessarMensagemAsync(id, ct))
                {
                    // A ordem por conversa e uma invariante: uma falha na mensagem
                    // anterior impede a posterior de ultrapassa-la.
                    break;
                }
            }

            try
            {
                await canal.EnviarPendentesAsync(conversaId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(
                    ex,
                    "Entradas da conversa {ConversaId} foram processadas, mas o envio pendente falhou.",
                    conversaId);
            }
        }
        finally
        {
            if (adquiriu)
            {
                await LiberarLockAsync(recurso);
            }

            if (abriuConexao)
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }

    private async Task<Guid?> TentarAdquirirProximaMensagemAsync(Guid conversaId, CancellationToken ct)
    {
        var agora = DateTime.UtcNow;
        var expiradoAntesDe = agora - ExpiracaoDoClaim;

        var proxima = await db.Mensagens.AsNoTracking()
            .Where(m => m.ConversaId == conversaId
                     && m.AutorTipo == AutorTipo.Cliente
                     && m.IdExternoWhatsapp != null
                     && m.EstadoProcessamentoEntrada != null
                     && m.EstadoProcessamentoEntrada != EstadoProcessamentoEntradaWhatsapp.Processada)
            .OrderBy(m => m.EnviadaEm)
            .ThenBy(m => m.Id)
            .Select(m => new
            {
                m.Id,
                m.EstadoProcessamentoEntrada,
                m.ProcessamentoEntradaIniciadoEm,
                m.ProcessamentoEntradaDisponivelEm
            })
            .FirstOrDefaultAsync(ct);

        if (proxima is null
            || (proxima.EstadoProcessamentoEntrada == EstadoProcessamentoEntradaWhatsapp.Processando
                && proxima.ProcessamentoEntradaIniciadoEm >= expiradoAntesDe)
            || (proxima.EstadoProcessamentoEntrada == EstadoProcessamentoEntradaWhatsapp.Falha
                && proxima.ProcessamentoEntradaDisponivelEm > agora))
        {
            return null;
        }

        var afetadas = await db.Mensagens
            .Where(m => m.Id == proxima.Id
                     && (m.EstadoProcessamentoEntrada == EstadoProcessamentoEntradaWhatsapp.Pendente
                         || m.EstadoProcessamentoEntrada == EstadoProcessamentoEntradaWhatsapp.Falha
                         || (m.EstadoProcessamentoEntrada == EstadoProcessamentoEntradaWhatsapp.Processando
                             && m.ProcessamentoEntradaIniciadoEm < expiradoAntesDe)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.EstadoProcessamentoEntrada, EstadoProcessamentoEntradaWhatsapp.Processando)
                .SetProperty(m => m.ProcessamentoEntradaIniciadoEm, agora)
                .SetProperty(m => m.ProcessamentoEntradaDisponivelEm, (DateTime?)null)
                .SetProperty(m => m.ErroProcessamentoEntrada, (string?)null)
                .SetProperty(m => m.TentativasProcessamentoEntrada, m => m.TentativasProcessamentoEntrada + 1)
                .SetProperty(m => m.AtualizadoEm, agora), ct);

        return afetadas == 1 ? proxima.Id : null;
    }

    private async Task<bool> ProcessarMensagemAsync(Guid mensagemId, CancellationToken ct)
    {
        db.ChangeTracker.Clear();

        var recebida = await db.Mensagens
            .SingleAsync(m => m.Id == mensagemId, ct);
        var conversa = await db.Conversas
            .SingleAsync(c => c.Id == recebida.ConversaId, ct);
        var cliente = await db.Clientes
            .SingleAsync(c => c.Id == conversa.ClienteId, ct);

        try
        {
            var primeiraMensagemId = await db.Mensagens.AsNoTracking()
                .Where(m => m.ConversaId == conversa.Id && m.AutorTipo == AutorTipo.Cliente)
                .OrderBy(m => m.EnviadaEm)
                .ThenBy(m => m.Id)
                .Select(m => m.Id)
                .FirstAsync(ct);

            await DespacharAsync(
                conversa,
                cliente,
                recebida,
                primeiraMensagemId == recebida.Id,
                ct);

            var concluidoEm = DateTime.UtcNow;
            recebida.EstadoProcessamentoEntrada = EstadoProcessamentoEntradaWhatsapp.Processada;
            recebida.ProcessamentoEntradaConcluidoEm = concluidoEm;
            recebida.ProcessamentoEntradaDisponivelEm = null;
            recebida.ErroProcessamentoEntrada = null;

            await SalvarSemRepetirEfeitosAsync(conversa, ct);

            log.LogInformation(
                "WhatsApp: entrada {MensagemId} processada em ordem na conversa {ConversaId}.",
                recebida.Id,
                conversa.Id);

            await NotificarAsync(conversa.TenantId, conversa.Id, ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            db.ChangeTracker.Clear();

            var tentativas = await db.Mensagens.AsNoTracking()
                .Where(m => m.Id == mensagemId)
                .Select(m => m.TentativasProcessamentoEntrada)
                .SingleAsync(CancellationToken.None);
            var minutos = Math.Min(60, Math.Max(1, 1 << Math.Min(tentativas - 1, 5)));
            var agora = DateTime.UtcNow;
            var erro = $"{ex.GetType().Name}: {ex.Message}";
            if (erro.Length > 1000)
            {
                erro = erro[..1000];
            }

            await db.Mensagens
                .Where(m => m.Id == mensagemId
                         && m.EstadoProcessamentoEntrada == EstadoProcessamentoEntradaWhatsapp.Processando)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.EstadoProcessamentoEntrada, EstadoProcessamentoEntradaWhatsapp.Falha)
                    .SetProperty(m => m.ProcessamentoEntradaIniciadoEm, (DateTime?)null)
                    .SetProperty(m => m.ProcessamentoEntradaDisponivelEm, agora.AddMinutes(minutos))
                    .SetProperty(m => m.ErroProcessamentoEntrada, erro)
                    .SetProperty(m => m.AtualizadoEm, agora), CancellationToken.None);

            log.LogError(
                ex,
                "Falha ao processar entrada {MensagemId}; nova tentativa em {Minutos} minuto(s).",
                mensagemId,
                minutos);

            return false;
        }
    }

    private async Task DespacharAsync(
        Conversa conversa,
        Clientes.Domain.Entities.Cliente cliente,
        Mensagem recebida,
        bool ehPrimeiraMensagem,
        CancellationToken ct)
    {
        var agora = recebida.EnviadaEm;
        var conteudo = recebida.Conteudo;

        // BUG-ATD-01: o gate humano continua sendo o primeiro da cadeia.
        if (conversa.EmAtendimentoHumano)
        {
            if (conversa.PerguntaDeEncerramentoEnviadaEm is not null)
            {
                conversa.PerguntaDeEncerramentoEnviadaEm = null;
            }
        }
        else if (ehPrimeiraMensagem)
        {
            await EnfileirarPrimeiroContatoAsync(conversa, cliente, agora, ct);
        }
        else if (await AplicadorDeEncerramentoPorInatividade.TratarRespostaAsync(
                     db, automaticas, conversa, conteudo, agora.AddMilliseconds(1), ct))
        {
        }
        else if (TriagemAutomatica.PediuMenu(conteudo))
        {
            await AplicadorDePosAtendimento.ReabrirDoZeroAsync(
                db, automaticas, conversa, agora.AddMilliseconds(1), ct);
        }
        else if (conversa.AguardandoNome)
        {
            await AplicadorDePedidoDeNome.AplicarAsync(
                db, automaticas, conversa, conteudo, agora.AddMilliseconds(1), log, ct);
        }
        else if (conversa.AguardandoTriagem)
        {
            await AplicadorDeTriagem.AplicarAsync(
                db, automaticas, respostaInteligente, _opcoesDaIA,
                conversa, conteudo, agora.AddMilliseconds(1), log, ct, recebida.Id);
        }
        else if (conversa.AguardandoEscolhaDeServico)
        {
            await AplicadorDeEscolhaDeServico.AplicarAsync(
                db, automaticas, respostaInteligente, _opcoesDaIA,
                conversa, conteudo, agora.AddMilliseconds(1), log, ct, recebida.Id);
        }
        else if (conversa.AguardandoRespostaInteligente)
        {
            await AplicadorDeRespostaInteligente.AplicarAsync(
                db, respostaInteligente, automaticas, _opcoesDaIA,
                conversa, conteudo, agora.AddMilliseconds(1), log, ct, recebida.Id);
        }
        else
        {
            await AplicadorDePosAtendimento.AplicarAsync(
                db, automaticas, respostaInteligente, _opcoesDaIA,
                conversa, conteudo, agora.AddMilliseconds(1), log, ct, recebida.Id);
        }
    }

    private async Task EnfileirarPrimeiroContatoAsync(
        Conversa conversa,
        Clientes.Domain.Entities.Cliente cliente,
        DateTime agora,
        CancellationToken ct)
    {
        if (!Clientes.Domain.NomeDeContato.EhUtil(cliente.Nome) && cliente.NomePerguntadoEm is null)
        {
            var pedido = await automaticas.EnfileirarAsync(
                conversa, GatilhoDeMensagem.PedidoDeNome, ct: ct);

            if (pedido.Count > 0)
            {
                conversa.AguardandoNome = true;
                cliente.NomePerguntadoEm = agora;
                return;
            }
        }

        var boasVindas = await automaticas.EnfileirarAsync(
            conversa, GatilhoDeMensagem.NovoAtendimento, ct: ct);
        conversa.AguardandoTriagem = boasVindas.Count > 0;
    }

    private async Task SalvarSemRepetirEfeitosAsync(Conversa conversa, CancellationToken ct)
    {
        for (var tentativa = 1; ; tentativa++)
        {
            await SuprimirAutomacaoSeTakeoverAconteceuAsync(conversa, ct);

            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateConcurrencyException) when (tentativa < 5)
            {
                // Uma entrada aceita em paralelo atualiza apenas os agregados por
                // SQL atomico e, com isso, avanca a RowVersion. Rebaseamos somente
                // o token original: as propriedades nao modificadas pelo fluxo nao
                // sao sobrescritas, e IA/automacao nao sao executadas outra vez.
                var rowVersionAtual = await db.Conversas.AsNoTracking()
                    .Where(c => c.Id == conversa.Id)
                    .Select(c => c.RowVersion)
                    .SingleAsync(ct);

                db.Entry(conversa).Property(c => c.RowVersion).OriginalValue = rowVersionAtual;
            }
        }
    }

    private async Task SuprimirAutomacaoSeTakeoverAconteceuAsync(
        Conversa conversa,
        CancellationToken ct)
    {
        // Se esta propria mensagem pediu humano, o valor local ja e true e as
        // mensagens de encaminhamento fazem parte do resultado correto. O caso
        // abaixo e outro: um atendente assumiu DEPOIS de o despacho comecar.
        if (conversa.EmAtendimentoHumano)
        {
            return;
        }

        var humanoNoBanco = await db.Conversas.AsNoTracking()
            .Where(c => c.Id == conversa.Id)
            .Select(c => c.EmAtendimentoHumano)
            .SingleAsync(ct);

        if (!humanoNoBanco)
        {
            return;
        }

        foreach (var entrada in db.ChangeTracker.Entries<Mensagem>()
                     .Where(e => e.State == EntityState.Added)
                     .ToList())
        {
            entrada.State = EntityState.Detached;
        }

        foreach (var entrada in db.ChangeTracker.Entries<HistoricoEtapa>()
                     .Where(e => e.State == EntityState.Added)
                     .ToList())
        {
            entrada.State = EntityState.Detached;
        }

        var valoresDoBanco = await db.Entry(conversa).GetDatabaseValuesAsync(ct)
            ?? throw new InvalidOperationException($"Conversa {conversa.Id} deixou de existir durante o processamento.");
        db.Entry(conversa).CurrentValues.SetValues(valoresDoBanco);
        db.Entry(conversa).OriginalValues.SetValues(valoresDoBanco);
        db.Entry(conversa).State = EntityState.Unchanged;

        log.LogInformation(
            "Conversa {ConversaId} entrou em atendimento humano antes do commit; automacao local descartada.",
            conversa.Id);
    }

    private async Task NotificarAsync(Guid tenantId, Guid conversaId, CancellationToken ct)
    {
        try
        {
            var projecao = await ProjecaoDeConversa
                .De(db, db.Conversas.AsNoTracking().Where(c => c.Id == conversaId))
                .FirstAsync(ct);
            await notificador.ConversaAtualizadaAsync(
                tenantId, projecao.ParaResposta(DateTime.UtcNow), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Processamento concluido, mas o SignalR falhou na conversa {ConversaId}.", conversaId);
        }
    }

    private async Task<bool> TentarAdquirirLockAsync(string recurso, CancellationToken ct)
    {
        await using var comando = CriarComandoDeLock("sp_getapplock", recurso);
        var resultado = Convert.ToInt32(await comando.ExecuteScalarAsync(ct));
        return resultado >= 0;
    }

    private async Task LiberarLockAsync(string recurso)
    {
        try
        {
            await using var comando = CriarComandoDeLock("sp_releaseapplock", recurso);
            await comando.ExecuteScalarAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Falha ao liberar o lock SQL da conversa {Recurso}.", recurso);
        }
    }

    private DbCommand CriarComandoDeLock(string procedimento, string recurso)
    {
        var comando = db.Database.GetDbConnection().CreateCommand();
        comando.CommandText = procedimento == "sp_getapplock"
            ? """
              DECLARE @resultado int;
              EXEC @resultado = sys.sp_getapplock
                   @Resource = @recurso,
                   @LockMode = 'Exclusive',
                   @LockOwner = 'Session',
                   @LockTimeout = 0;
              SELECT @resultado;
              """
            : """
              DECLARE @resultado int;
              EXEC @resultado = sys.sp_releaseapplock
                   @Resource = @recurso,
                   @LockOwner = 'Session';
              SELECT @resultado;
              """;

        var parametro = comando.CreateParameter();
        parametro.ParameterName = "@recurso";
        parametro.Value = recurso;
        comando.Parameters.Add(parametro);
        return comando;
    }
}
