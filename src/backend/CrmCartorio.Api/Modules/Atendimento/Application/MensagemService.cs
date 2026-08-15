using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Atendimento.Domain;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

public class MensagemService(
    CrmDbContext db,
    IContextoDoUsuario ctx,
    IConversaService conversas,
    INotificadorDeAtendimento notificador,
    IDisparadorDeMensagemAutomatica automaticas,
    ILogger<MensagemService> log) : IMensagemService
{
    public async Task<IReadOnlyList<MensagemResponse>> ListarAsync(
        Guid conversaId,
        CancellationToken ct = default)
    {
        var tenantId = ctx.TenantId;

        var clienteNome = await db.Conversas.AsNoTracking()
            .Where(c => c.Id == conversaId && c.TenantId == tenantId)
            .Select(c => db.Clientes.Where(cl => cl.Id == c.ClienteId).Select(cl => cl.Nome).FirstOrDefault())
            .FirstOrDefaultAsync(ct);

        if (clienteNome is null)
        {
            throw AtendimentoServiceException.ConversaNaoEncontrada();
        }

        return await ProjecaoDeMensagem
            .De(
                db,
                db.Mensagens.AsNoTracking()
                    .Where(m => m.ConversaId == conversaId && m.TenantId == tenantId)
                    .OrderBy(m => m.EnviadaEm)
                    .ThenBy(m => m.Id),
                clienteNome)
            .ToListAsync(ct);
    }

    public async Task<MensagemResponse> EnviarAsync(
        Guid conversaId,
        EnviarMensagemRequest request,
        CancellationToken ct = default)
    {
        var conteudo = request.Conteudo?.Trim();
        if (string.IsNullOrWhiteSpace(conteudo))
        {
            throw AtendimentoServiceException.MensagemVazia();
        }

        var tenantId = ctx.TenantId;

        var conversa = await db.Conversas
            .FirstOrDefaultAsync(c => c.Id == conversaId && c.TenantId == tenantId, ct)
            ?? throw AtendimentoServiceException.ConversaNaoEncontrada();

        if (conversa.Status != StatusConversa.Ativa)
        {
            throw AtendimentoServiceException.ConversaEncerrada();
        }

        // Aqui esta a trava, do lado do envio: quem nao e o responsavel nao escreve.
        // Sem isso, dois atendentes responderiam o mesmo cliente ao mesmo tempo —
        // o erro que este produto existe para eliminar.
        if (conversa.AtendenteId is null)
        {
            throw AtendimentoServiceException.ConversaSemResponsavel();
        }

        if (conversa.AtendenteId != ctx.AtendenteId && !ctx.PodeOperarConversaDeTerceiro)
        {
            var responsavel = await db.Atendentes.AsNoTracking()
                .Where(a => a.Id == conversa.AtendenteId)
                .Select(a => a.Nome)
                .FirstOrDefaultAsync(ct) ?? "outro atendente";

            throw AtendimentoServiceException.NaoEhOResponsavel(responsavel);
        }

        // A mensagem citada tem de ser da MESMA conversa. Sem esta checagem daria
        // para citar mensagem de outro atendimento — e de outro cliente.
        if (request.MensagemRespondidaId is { } respondidaId)
        {
            var pertence = await db.Mensagens.AsNoTracking()
                .AnyAsync(m => m.Id == respondidaId
                            && m.ConversaId == conversaId
                            && m.TenantId == tenantId, ct);

            if (!pertence)
            {
                throw AtendimentoServiceException.MensagemCitadaInvalida();
            }
        }

        var agora = DateTime.UtcNow;

        var mensagem = new Mensagem
        {
            TenantId = tenantId,
            ConversaId = conversaId,
            AutorTipo = AutorTipo.Atendente,

            // RF-007: a mensagem carrega QUEM respondeu. Nao e inferido do dono da
            // conversa — se ela for transferida depois, o autor original permanece.
            AtendenteId = ctx.AtendenteId,

            Conteudo = conteudo,
            Tipo = TipoMensagem.Texto,
            MensagemRespondidaId = request.MensagemRespondidaId,

            // Fase 1 nao tem confirmacao real da Meta. Na Fase 2 isso nasce como
            // Pendente e o webhook de status promove para Entregue/Lida.
            StatusEntrega = StatusEntrega.Enviada,
            EnviadaEm = agora
        };

        db.Mensagens.Add(mensagem);

        conversa.UltimaMensagemEm = agora;

        await db.SaveChangesAsync(ct);

        var resposta = await ObterUmaAsync(mensagem.Id, conversaId, ct);

        var resumo = await conversas.ObterResumoAsync(conversaId, ct);
        await notificador.MensagemRecebidaAsync(tenantId, resposta, resumo, ct);

        return resposta;
    }

    public async Task<MensagemResponse> SimularRecebimentoAsync(
        Guid conversaId,
        SimularMensagemDeClienteRequest request,
        CancellationToken ct = default)
    {
        var conteudo = request.Conteudo?.Trim();
        if (string.IsNullOrWhiteSpace(conteudo))
        {
            throw AtendimentoServiceException.MensagemVazia();
        }

        var tenantId = ctx.TenantId;

        var conversa = await db.Conversas
            .FirstOrDefaultAsync(c => c.Id == conversaId && c.TenantId == tenantId, ct)
            ?? throw AtendimentoServiceException.ConversaNaoEncontrada();

        if (conversa.Status != StatusConversa.Ativa)
        {
            throw AtendimentoServiceException.ConversaEncerrada();
        }

        var agora = DateTime.UtcNow;

        // FASE 1: se o cliente esta respondendo, e porque leu o que foi enviado.
        // Promover as mensagens anteriores do atendente para Lida e o que permite
        // demonstrar os tiques de leitura sem WhatsApp real. Na Fase 2 isso sai:
        // quem move esse status passa a ser o webhook de status da Meta (RF-105),
        // que e a unica fonte honesta dessa informacao.
        await db.Mensagens
            .Where(m => m.ConversaId == conversaId
                     && m.TenantId == tenantId
                     && m.AutorTipo == AutorTipo.Atendente
                     && m.StatusEntrega != StatusEntrega.Lida
                     && m.StatusEntrega != StatusEntrega.Falha)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.StatusEntrega, StatusEntrega.Lida)
                .SetProperty(m => m.AtualizadoEm, agora), ct);

        var mensagem = new Mensagem
        {
            TenantId = tenantId,
            ConversaId = conversaId,
            AutorTipo = AutorTipo.Cliente,
            AtendenteId = null,
            Conteudo = conteudo,
            Tipo = TipoMensagem.Texto,
            StatusEntrega = StatusEntrega.Entregue,
            EnviadaEm = agora
        };

        db.Mensagens.Add(mensagem);

        conversa.UltimaMensagemEm = agora;
        conversa.MensagensNaoLidas += 1;

        // A mensagem do cliente ja esta na frente da fila de insercao, entao a
        // resposta da triagem aparece DEPOIS dela no fio — Guid v7 e sequencial, e o
        // fio ordena por EnviadaEm e depois por Id.
        if (conversa.AguardandoTriagem)
        {
            await ResponderTriagemAsync(conversa, conteudo, agora, ct);
        }

        await db.SaveChangesAsync(ct);

        var resposta = await ObterUmaAsync(mensagem.Id, conversaId, ct);

        var resumo = await conversas.ObterResumoAsync(conversaId, ct);
        await notificador.MensagemRecebidaAsync(tenantId, resposta, resumo, ct);

        return resposta;
    }

    /// <summary>
    /// Le a resposta do cliente ao menu de triagem: acerta o setor quando reconhece a
    /// opcao, avisa que vai para atendimento humano quando nao reconhece.
    /// </summary>
    /// <remarks>
    /// A flag sai em TODOS os caminhos, e isso e o requisito, nao um detalhe: uma
    /// unica tentativa e o suficiente. Repetir o menu ate o cliente acertar prende
    /// quem escreveu "bom dia" num loop de robo — o pior comportamento possivel num
    /// canal de cartorio, e o motivo pelo qual gente desiste de atendimento por
    /// WhatsApp.
    /// </remarks>
    private async Task ResponderTriagemAsync(
        Conversa conversa,
        string conteudo,
        DateTime agora,
        CancellationToken ct)
    {
        conversa.AguardandoTriagem = false;

        // Alguem da serventia roteou a conversa enquanto o menu estava no ar. A
        // escolha da pessoa vale mais que a do robo, e nao ha o que avisar ao
        // cliente: um atendente ja esta com o caso.
        if (conversa.SetorId is not null)
        {
            return;
        }

        var setores = await automaticas.SetoresDoMenuAsync(conversa.TenantId, ct);
        var escolhido = TriagemAutomatica.InterpretarResposta(conteudo, setores);

        if (escolhido is null)
        {
            log.LogInformation(
                "Triagem da conversa {ConversaId} sem correspondencia — encaminhada para atendimento humano",
                conversa.Id);

            await automaticas.EnfileirarAsync(
                conversa, GatilhoDeMensagem.TriagemSemCorrespondencia, ct: ct);

            return;
        }

        conversa.SetorId = escolhido;

        var nomeDoSetor = setores.First(s => s.Id == escolhido).Nome;

        // AtendenteId nulo no historico e o que o modelo definiu para "mudanca
        // automatica" (MODELO-DADOS.md, 3.10). A trilha registra que foi o sistema,
        // nao um atendente cujo nome nao caberia aqui.
        db.HistoricoEtapas.Add(new HistoricoEtapa
        {
            TenantId = conversa.TenantId,
            ConversaId = conversa.Id,
            TipoMudanca = TipoMudancaHistorico.Setor,
            SetorNovoId = escolhido,
            AtendenteId = null,
            OcorridoEm = agora,
            Observacao = "Setor definido pela triagem automatica do primeiro contato."
        });

        // Anotacao interna, nao mensagem ao cliente: EhAutomatica fica falso. Quem
        // abrir a conversa depois precisa saber por que ela esta neste setor sem
        // ninguem ter mexido.
        db.Mensagens.Add(new Mensagem
        {
            TenantId = conversa.TenantId,
            ConversaId = conversa.Id,
            AutorTipo = AutorTipo.Sistema,
            Conteudo = $"Triagem automatica encaminhou o atendimento ao setor {nomeDoSetor}.",
            Tipo = TipoMensagem.Texto,
            StatusEntrega = StatusEntrega.Entregue,
            EnviadaEm = agora,
            LidaEm = agora
        });

        log.LogInformation(
            "Triagem da conversa {ConversaId} encaminhou ao setor {SetorId}", conversa.Id, escolhido);
    }

    /// <summary>
    /// Le a mensagem de volta pela projecao, em vez de montar o response a mao.
    /// Assim a citacao ja vem resolvida e o objeto devolvido no envio e igual ao
    /// que a listagem devolve — duas formas do mesmo dado divergiriam.
    /// </summary>
    private async Task<MensagemResponse> ObterUmaAsync(
        Guid mensagemId,
        Guid conversaId,
        CancellationToken ct)
    {
        var clienteNome = await db.Conversas.AsNoTracking()
            .Where(c => c.Id == conversaId)
            .Select(c => db.Clientes.Where(cl => cl.Id == c.ClienteId).Select(cl => cl.Nome).FirstOrDefault())
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        return await ProjecaoDeMensagem
            .De(db, db.Mensagens.AsNoTracking().Where(m => m.Id == mensagemId), clienteNome)
            .FirstAsync(ct);
    }
}
