using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.Clientes.Domain;
using CrmCartorio.Api.Modules.Clientes.Domain.Entities;
using CrmCartorio.Api.Modules.Clientes.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <summary>
/// Aceita a entrada do webhook em uma transacao curta. Automacao, IA, Evolution
/// e SignalR ficam fora do commit que torna a mensagem duravel.
/// </summary>
public sealed class RecebimentoDeWhatsApp(
    CrmDbContext db,
    IArmazenamentoDeAnexos anexos,
    INotificadorDeAtendimento notificador,
    SinalizadorDeEntradaWhatsapp sinalizador,
    ICifraDeDados cifra,
    ILogger<RecebimentoDeWhatsApp> log) : IRecebimentoDeWhatsApp
{
    private const int MaximoDeTentativasDeAceitacao = 5;
    private readonly string? _connectionString = db.Database.GetConnectionString();

    public async Task<Guid> ReceberAsync(
        Guid tenantId,
        MensagemDoCanal mensagem,
        CancellationToken ct = default)
    {
        for (var tentativa = 1; tentativa <= MaximoDeTentativasDeAceitacao; tentativa++)
        {
            await using var aceitacao = NovoContexto();

            var existente = await ObterConversaDaMensagemAsync(
                aceitacao, tenantId, mensagem.IdExterno, ct);
            if (existente is { } conversaExistente)
            {
                sinalizador.Sinalizar(conversaExistente);
                return conversaExistente;
            }

            await using var transacao = await aceitacao.Database.BeginTransactionAsync(ct);
            var agora = DateTime.UtcNow;

            try
            {
                var cliente = await ResolverClienteAsync(aceitacao, tenantId, mensagem, agora, ct);
                var (conversa, ehNova) = await ResolverConversaAsync(
                    aceitacao, tenantId, cliente, agora, ct);

                // Cliente/conversa precisam existir antes da mensagem, mas ainda
                // dentro do mesmo commit curto. Nenhum efeito externo ocorreu.
                await aceitacao.SaveChangesAsync(ct);

                var recebida = new Mensagem
                {
                    TenantId = tenantId,
                    ConversaId = conversa.Id,
                    AutorTipo = AutorTipo.Cliente,
                    AtendenteId = null,
                    Conteudo = mensagem.Conteudo,
                    Tipo = mensagem.Tipo,
                    StatusEntrega = StatusEntrega.Entregue,
                    IdExternoWhatsapp = mensagem.IdExterno,
                    EnviadaEm = agora,
                    EstadoProcessamentoEntrada = EstadoProcessamentoEntradaWhatsapp.Pendente
                };

                await GravarAnexoAsync(recebida, mensagem.Arquivo, agora, ct);
                aceitacao.Mensagens.Add(recebida);

                // Este INSERT e o claim IA-03. O UNIQUE filtrado continua sendo a
                // autoridade para reentregas simultaneas do mesmo id externo.
                await aceitacao.SaveChangesAsync(ct);

                // Agregados sem tracking e sem RowVersion antiga: incremento
                // atomico e timestamp monotono, dentro do mesmo commit da entrada.
                var atualizadas = await aceitacao.Conversas
                    .Where(c => c.Id == conversa.Id && c.TenantId == tenantId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(c => c.MensagensNaoLidas, c => c.MensagensNaoLidas + 1)
                        .SetProperty(
                            c => c.UltimaMensagemEm,
                            c => c.UltimaMensagemEm < agora ? agora : c.UltimaMensagemEm)
                        .SetProperty(c => c.AtualizadoEm, agora), ct);

                if (atualizadas != 1)
                {
                    throw new InvalidOperationException(
                        $"Conversa {conversa.Id} nao foi atualizada durante a aceitacao da entrada.");
                }

                await transacao.CommitAsync(ct);

                log.LogInformation(
                    "WhatsApp: mensagem {IdExterno} aceita na conversa {ConversaId} ({Origem}).",
                    mensagem.IdExterno,
                    conversa.Id,
                    ehNova ? "conversa nova" : "conversa existente");

                sinalizador.Sinalizar(conversa.Id);
                await NotificarAsync(tenantId, conversa.Id, ct);
                return conversa.Id;
            }
            catch (DbUpdateException ex) when (EhViolacaoDeUnicidade(ex))
            {
                await transacao.RollbackAsync(CancellationToken.None);

                await using var consulta = NovoContexto();
                var vencedora = await ObterConversaDaMensagemAsync(
                    consulta, tenantId, mensagem.IdExterno, CancellationToken.None);
                if (vencedora is { } conversaVencedora)
                {
                    // Mesmo id externo: sucesso idempotente, sem novo ciclo logico.
                    sinalizador.Sinalizar(conversaVencedora);
                    return conversaVencedora;
                }

                // Telefone ou conversa ativa foram criados pela outra primeira
                // mensagem. Repetimos somente a transacao local de aceitacao; IA,
                // Evolution e automacoes ainda nao foram chamadas.
                if (tentativa == MaximoDeTentativasDeAceitacao)
                {
                    throw;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(10 * tentativa), ct);
            }
        }

        throw new InvalidOperationException("A aceitacao da mensagem excedeu o numero de tentativas.");
    }

    public async Task AtualizarEntregaAsync(
        Guid tenantId,
        string idExternoWhatsapp,
        StatusEntrega status,
        CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;
        var afetadas = status switch
        {
            StatusEntrega.Entregue => await db.Mensagens
                .Where(m => m.TenantId == tenantId
                         && m.IdExternoWhatsapp == idExternoWhatsapp
                         && m.StatusEntrega != StatusEntrega.Lida
                         && m.StatusEntrega != StatusEntrega.Entregue)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.StatusEntrega, StatusEntrega.Entregue)
                    .SetProperty(m => m.AtualizadoEm, agora), ct),

            StatusEntrega.Lida => await db.Mensagens
                .Where(m => m.TenantId == tenantId
                         && m.IdExternoWhatsapp == idExternoWhatsapp
                         && m.StatusEntrega != StatusEntrega.Lida)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.StatusEntrega, StatusEntrega.Lida)
                    .SetProperty(m => m.LidaEm, agora)
                    .SetProperty(m => m.AtualizadoEm, agora), ct),

            StatusEntrega.Falha => await db.Mensagens
                .Where(m => m.TenantId == tenantId
                         && m.IdExternoWhatsapp == idExternoWhatsapp
                         && m.StatusEntrega != StatusEntrega.Lida
                         && m.StatusEntrega != StatusEntrega.Entregue)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.StatusEntrega, StatusEntrega.Falha)
                    .SetProperty(m => m.AtualizadoEm, agora), ct),

            _ => 0
        };

        if (afetadas == 0)
        {
            return;
        }

        var conversaId = await db.Mensagens.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.IdExternoWhatsapp == idExternoWhatsapp)
            .Select(m => (Guid?)m.ConversaId)
            .FirstOrDefaultAsync(ct);

        if (conversaId is { } id)
        {
            await NotificarAsync(tenantId, id, ct);
        }
    }

    private CrmDbContext NovoContexto()
    {
        var opcoes = new DbContextOptionsBuilder<CrmDbContext>();
        ConfiguracaoDoBanco.Configurar(opcoes, _connectionString);
        return new CrmDbContext(opcoes.Options, cifra);
    }

    private static async Task<Guid?> ObterConversaDaMensagemAsync(
        CrmDbContext contexto,
        Guid tenantId,
        string idExterno,
        CancellationToken ct) =>
        await contexto.Mensagens.AsNoTracking()
            .Where(m => m.TenantId == tenantId
                     && m.AutorTipo == AutorTipo.Cliente
                     && m.IdExternoWhatsapp == idExterno)
            // A unicidade IA-03 protege as novas entradas. A ordenação torna a
            // recuperação idempotente também diante de registros históricos
            // eventualmente duplicados, sem transformar um redelivery em erro.
            .OrderBy(m => m.CriadoEm)
            .Select(m => (Guid?)m.ConversaId)
            .FirstOrDefaultAsync(ct);

    private static async Task<Cliente> ResolverClienteAsync(
        CrmDbContext contexto,
        Guid tenantId,
        MensagemDoCanal mensagem,
        DateTime agora,
        CancellationToken ct)
    {
        var cliente = await contexto.Clientes.FirstOrDefaultAsync(
            c => c.TenantId == tenantId && c.TelefoneWhatsapp == mensagem.TelefoneE164, ct);

        if (cliente is not null)
        {
            if (!string.IsNullOrWhiteSpace(mensagem.NomeDoPerfil)
                && !NomeDeContato.EhUtil(cliente.Nome)
                && NomeDeContato.EhUtil(mensagem.NomeDoPerfil))
            {
                cliente.Nome = NomeDeContato.DoPerfil(mensagem.NomeDoPerfil, mensagem.TelefoneE164);
                cliente.AtualizadoEm = agora;
            }

            if (!string.IsNullOrWhiteSpace(mensagem.FotoPerfilUrl)
                && cliente.FotoPerfilUrl != mensagem.FotoPerfilUrl)
            {
                cliente.FotoPerfilUrl = mensagem.FotoPerfilUrl;
                cliente.FotoPerfilAtualizadaEm = agora;
                cliente.AtualizadoEm = agora;
            }

            return cliente;
        }

        cliente = new Cliente
        {
            TenantId = tenantId,
            Nome = NomeDeContato.DoPerfil(mensagem.NomeDoPerfil, mensagem.TelefoneE164),
            TelefoneWhatsapp = mensagem.TelefoneE164,
            FotoPerfilUrl = mensagem.FotoPerfilUrl,
            FotoPerfilAtualizadaEm = mensagem.FotoPerfilUrl is not null ? agora : null,
            Ativo = true,
            OrigemDoCadastro = OrigemDoCadastro.Webhook,
            CriadoEm = agora,
            AtualizadoEm = agora
        };
        contexto.Clientes.Add(cliente);
        return cliente;
    }

    private static async Task<(Conversa Conversa, bool EhNova)> ResolverConversaAsync(
        CrmDbContext contexto,
        Guid tenantId,
        Cliente cliente,
        DateTime agora,
        CancellationToken ct)
    {
        var ativa = await contexto.Conversas.FirstOrDefaultAsync(
            c => c.TenantId == tenantId
              && c.ClienteId == cliente.Id
              && c.Canal == CanalOrigem.WhatsApp
              && c.Status == StatusConversa.Ativa, ct);

        if (ativa is not null)
        {
            return (ativa, false);
        }

        var etapaInicial = await contexto.EtapasFunil.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Ativo)
            .OrderBy(e => e.Ordem)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(ct);
        var protocolo = await new GeradorDeProtocolo(contexto).GerarAsync(tenantId, agora, ct);

        var conversa = new Conversa
        {
            TenantId = tenantId,
            ClienteId = cliente.Id,
            Protocolo = protocolo,
            EtapaId = etapaInicial,
            Status = StatusConversa.Ativa,
            Canal = CanalOrigem.WhatsApp,
            IniciadaEm = agora,
            UltimaMensagemEm = agora
        };
        contexto.Conversas.Add(conversa);
        contexto.HistoricoEtapas.Add(new HistoricoEtapa
        {
            TenantId = tenantId,
            ConversaId = conversa.Id,
            TipoMudanca = TipoMudancaHistorico.Abertura,
            EtapaNovaId = etapaInicial,
            AtendenteId = null,
            OcorridoEm = agora,
            Observacao = $"Atendimento aberto pelo WhatsApp com o protocolo {protocolo}."
        });

        return (conversa, true);
    }

    private async Task GravarAnexoAsync(
        Mensagem mensagem,
        ArquivoDoCanal? arquivo,
        DateTime agora,
        CancellationToken ct)
    {
        if (arquivo is null || arquivo.Conteudo.Length == 0)
        {
            return;
        }

        try
        {
            var gravado = await anexos.GravarAsync(
                mensagem.TenantId,
                mensagem.Id,
                agora,
                arquivo.Conteudo,
                arquivo.Nome,
                arquivo.TipoMime,
                ct);
            mensagem.ArquivoCaminho = gravado.CaminhoRelativo;
            mensagem.ArquivoNome = gravado.Nome;
            mensagem.ArquivoTipoMime = gravado.TipoMime;
            mensagem.ArquivoTamanhoBytes = gravado.TamanhoBytes;
            mensagem.Tipo = gravado.Tipo;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Anexo da mensagem {IdExterno} falhou; a entrada sera preservada sem arquivo.", mensagem.IdExternoWhatsapp);
        }
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
            log.LogWarning(ex, "Entrada aceita na conversa {ConversaId}, mas o SignalR falhou.", conversaId);
        }
    }

    private static bool EhViolacaoDeUnicidade(DbUpdateException falha) =>
        falha.InnerException is SqlException { Number: 2601 or 2627 };
}
