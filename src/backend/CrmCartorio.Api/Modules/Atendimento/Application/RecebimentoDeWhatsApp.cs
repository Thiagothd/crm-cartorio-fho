using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.Clientes.Domain.Entities;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <inheritdoc cref="IRecebimentoDeWhatsApp"/>
public class RecebimentoDeWhatsApp(
    CrmDbContext db,
    IGeradorDeProtocolo protocolos,
    IDisparadorDeMensagemAutomatica automaticas,
    INotificadorDeAtendimento notificador,
    IEnviadorDeCanal canal,
    ILogger<RecebimentoDeWhatsApp> log) : IRecebimentoDeWhatsApp
{
    public async Task<Guid> ReceberAsync(
        Guid tenantId,
        MensagemDoCanal mensagem,
        CancellationToken ct = default)
    {
        // O gateway reentrega o mesmo evento quando nao recebe 200 a tempo. Sem
        // esta checagem, uma reentrega viraria mensagem duplicada no fio — e, no
        // primeiro contato, um segundo protocolo para o mesmo "bom dia".
        var jaRegistrada = await db.Mensagens.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.IdExternoWhatsapp == mensagem.IdExterno)
            .Select(m => (Guid?)m.ConversaId)
            .FirstOrDefaultAsync(ct);

        if (jaRegistrada is { } existente)
        {
            log.LogInformation(
                "Mensagem {IdExterno} ja registrada; reentrega do gateway ignorada.",
                mensagem.IdExterno);

            return existente;
        }

        var agora = DateTime.UtcNow;

        await using var transacao = await db.Database.BeginTransactionAsync(ct);

        var cliente = await ResolverClienteAsync(tenantId, mensagem, agora, ct);
        var (conversa, ehNova) = await ResolverConversaAsync(tenantId, cliente, agora, ct);

        db.Mensagens.Add(new Mensagem
        {
            TenantId = tenantId,
            ConversaId = conversa.Id,
            AutorTipo = AutorTipo.Cliente,
            AtendenteId = null,
            Conteudo = mensagem.Conteudo,
            Tipo = mensagem.Tipo,

            // Chegou de verdade: Entregue, nao Enviada. Quem entrega para nos e o
            // WhatsApp, e ele so entrega o que ja chegou.
            StatusEntrega = StatusEntrega.Entregue,
            IdExternoWhatsapp = mensagem.IdExterno,
            EnviadaEm = agora
        });

        conversa.UltimaMensagemEm = agora;
        conversa.MensagensNaoLidas += 1;

        // A mensagem do cliente ja esta na frente da fila de insercao, entao a
        // resposta da triagem aparece DEPOIS dela no fio — Guid v7 e sequencial, e
        // o fio ordena por EnviadaEm e depois por Id.
        if (conversa.AguardandoTriagem)
        {
            await AplicadorDeTriagem.AplicarAsync(
                db, automaticas, conversa, mensagem.Conteudo, agora, log, ct);
        }

        await db.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);

        log.LogInformation(
            "WhatsApp: mensagem de {Telefone} registrada na conversa {ConversaId} ({Origem}).",
            mensagem.TelefoneE164, conversa.Id, ehNova ? "conversa nova" : "conversa existente");

        // Boas-vindas, menu de triagem e o aviso de encaminhamento saem AQUI —
        // depois do commit, nunca dentro da transacao. Uma chamada de rede presa
        // numa transacao aberta segura o lock da conversa pelo tempo do gateway.
        await canal.EnviarPendentesAsync(conversa.Id, ct);

        await NotificarAsync(tenantId, conversa.Id, ct);

        return conversa.Id;
    }

    public async Task AtualizarEntregaAsync(
        Guid tenantId,
        string idExternoWhatsapp,
        StatusEntrega status,
        CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;

        // So anda para frente. O WhatsApp entrega confirmacoes fora de ordem com
        // frequencia; sem o filtro por StatusEntrega, um DELIVERY_ACK atrasado
        // rebaixaria para "Entregue" uma mensagem que o cliente ja leu.
        //
        // ExecuteUpdate e nao entidade rastreada: Conversa carrega RowVersion, e o
        // caminho do webhook nao pode colidir com quem esta atendendo.
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

    /// <summary>
    /// Acha o cliente pelo telefone ou cria um com o nome do perfil do WhatsApp.
    /// </summary>
    /// <remarks>
    /// O telefone e a identidade (README de Clientes): unico por tenant, guardado
    /// em E.164 sem simbolos. Nome nasce do perfil e e corrigido pelo atendente
    /// quando a identificacao real aparece — exigir qualificacao no primeiro "bom
    /// dia" travaria o atendimento.
    /// </remarks>
    private async Task<Cliente> ResolverClienteAsync(
        Guid tenantId,
        MensagemDoCanal mensagem,
        DateTime agora,
        CancellationToken ct)
    {
        var cliente = await db.Clientes
            .FirstOrDefaultAsync(
                c => c.TenantId == tenantId && c.TelefoneWhatsapp == mensagem.TelefoneE164, ct);

        if (cliente is not null)
        {
            // Cliente cadastrado sem nome util ("Contato", vazio) ganha o nome do
            // perfil. Nome ja preenchido NAO e sobrescrito: pode ter sido corrigido
            // a mao por um atendente, e o perfil do WhatsApp e do cliente, nao nosso.
            if (!string.IsNullOrWhiteSpace(mensagem.NomeDoPerfil)
                && string.IsNullOrWhiteSpace(cliente.Nome))
            {
                cliente.Nome = mensagem.NomeDoPerfil;
                cliente.AtualizadoEm = agora;
            }

            return cliente;
        }

        cliente = new Cliente
        {
            TenantId = tenantId,
            Nome = string.IsNullOrWhiteSpace(mensagem.NomeDoPerfil)
                ? mensagem.TelefoneE164
                : mensagem.NomeDoPerfil,
            TelefoneWhatsapp = mensagem.TelefoneE164,
            Ativo = true,
            CriadoEm = agora,
            AtualizadoEm = agora
        };

        db.Clientes.Add(cliente);

        log.LogInformation("WhatsApp: cliente novo criado para {Telefone}.", mensagem.TelefoneE164);

        return cliente;
    }

    /// <summary>
    /// Reaproveita a conversa ativa do cliente ou abre uma nova.
    /// </summary>
    /// <remarks>
    /// Reaproveitar e o comportamento certo: cada mensagem nova do mesmo cliente
    /// abrindo um protocolo faria uma conversa de cinco linhas virar cinco
    /// atendimentos. Conversa encerrada NAO e reaberta automaticamente — o cliente
    /// que volta depois de encerrado tem um assunto novo, e o historico anterior
    /// continua consultavel pelo protocolo.
    /// </remarks>
    private async Task<(Conversa Conversa, bool EhNova)> ResolverConversaAsync(
        Guid tenantId,
        Cliente cliente,
        DateTime agora,
        CancellationToken ct)
    {
        var ativa = await db.Conversas
            .FirstOrDefaultAsync(
                c => c.TenantId == tenantId
                  && c.ClienteId == cliente.Id
                  && c.Status == StatusConversa.Ativa, ct);

        if (ativa is not null)
        {
            return (ativa, false);
        }

        var etapaInicial = await db.EtapasFunil.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Ativo)
            .OrderBy(e => e.Ordem)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(ct);

        var protocolo = await protocolos.GerarAsync(tenantId, agora, ct);

        var conversa = new Conversa
        {
            TenantId = tenantId,
            ClienteId = cliente.Id,
            Protocolo = protocolo,
            EtapaId = etapaInicial,
            Status = StatusConversa.Ativa,

            // O que separa esta conversa das 11 de demonstracao. E por este campo
            // que o envio decide se sai pelo gateway ou fica so no painel.
            Canal = CanalOrigem.WhatsApp,
            IniciadaEm = agora,
            UltimaMensagemEm = agora
        };

        db.Conversas.Add(conversa);

        db.HistoricoEtapas.Add(new HistoricoEtapa
        {
            TenantId = tenantId,
            ConversaId = conversa.Id,
            TipoMudanca = TipoMudancaHistorico.Abertura,
            EtapaNovaId = etapaInicial,
            AtendenteId = null,
            OcorridoEm = agora,
            Observacao = $"Atendimento aberto pelo WhatsApp com o protocolo {protocolo}."
        });

        // Boas-vindas com o menu. A flag depende de o menu ter REALMENTE saido:
        // sem regra cadastrada nao ha menu, e esperar resposta de uma pergunta que
        // ninguem fez faria a proxima mensagem receber um fallback vindo do nada.
        var boasVindas = await automaticas.EnfileirarAsync(
            conversa, GatilhoDeMensagem.NovoAtendimento, ct: ct);

        conversa.AguardandoTriagem = boasVindas.Count > 0;

        return (conversa, true);
    }

    /// <summary>
    /// Empurra a conversa para os paineis abertos.
    /// </summary>
    /// <remarks>
    /// Nao usa <c>IConversaService</c> de proposito — aquele servico le TenantId de
    /// <c>IContextoDoUsuario</c>, que aqui nao existe. As projecoes estaticas
    /// recebem o tenant explicitamente e servem aos dois mundos.
    ///
    /// Falha de notificacao nao pode desfazer o que ja foi gravado: a mensagem
    /// aparece na proxima leitura do painel de qualquer forma.
    /// </remarks>
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
            log.LogWarning(
                ex,
                "Mensagem gravada na conversa {ConversaId}, mas a notificacao em tempo real falhou",
                conversaId);
        }
    }
}
