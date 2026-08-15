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
    ILogger<MensagemService> log) : IMensagemService, IAcoesDeMensagem
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

        // Conversa na fila: responder E assumir. Quem digitou e apertou enviar ja
        // decidiu atender — exigir um clique extra em "Assumir" antes disso e
        // cerimonia, e cerimonia no meio de um atendimento vira conversa perdida.
        //
        // A TRAVA NAO ENFRAQUECE. Reusa AssumirAsync, que e o mesmo UPDATE
        // condicional ja exercitado sob 12 chamadas simultaneas em
        // verificar-concorrencia.cs: se outro atendente assumiu um instante antes,
        // ele levanta ConflitoDeAtribuicaoException e a mensagem NAO e gravada.
        // O que muda e quando o UPDATE dispara, nao a garantia que ele da.
        //
        // AssumirAsync recarrega a entidade rastreada depois do ExecuteUpdate,
        // entao `conversa` ja sai daqui com o responsavel preenchido.
        if (conversa.AtendenteId is null)
        {
            await conversas.AssumirAsync(conversaId, ct);
        }

        // A trava do lado do envio continua: quem nao e o responsavel nao escreve
        // por cima de quem e. Sem isso, dois atendentes responderiam o mesmo
        // cliente ao mesmo tempo — o erro que este produto existe para eliminar.
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

        await db.SaveChangesAsync(ct);

        // UltimaMensagemEm sai por ExecuteUpdate, e NAO pela entidade rastreada.
        //
        // Conversa carrega RowVersion como token de concorrencia, entao gravar o
        // carimbo pela entidade faria dois envios simultaneos na mesma conversa
        // colidirem — o segundo levantaria DbUpdateConcurrencyException e viraria
        // 409 na cara de quem so respondeu um cliente. E conflito inventado: o
        // token existe para proteger DECISAO sobre a conversa (quem atende, que
        // etapa, encerrar), nao para arbitrar qual de dois relogios venceu.
        //
        // Para um carimbo de "ultima mensagem", o ultimo a escrever vencer e a
        // resposta certa, e e o que o UPDATE direto faz.
        await db.Conversas
            .Where(c => c.Id == conversaId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.UltimaMensagemEm, agora)
                .SetProperty(c => c.AtualizadoEm, agora), ct);

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

    // ------------------------------------------------- acoes do menu de contexto

    public async Task<MensagemResponse> FixarAsync(
        Guid conversaId,
        Guid mensagemId,
        bool fixar,
        CancellationToken ct = default)
    {
        var conversa = await CarregarParaAcaoAsync(conversaId, ct);
        var agora = DateTime.UtcNow;

        var mensagem = await db.Mensagens
            .FirstOrDefaultAsync(m => m.Id == mensagemId
                                   && m.ConversaId == conversaId
                                   && m.TenantId == conversa.TenantId, ct)
            ?? throw AtendimentoServiceException.MensagemNaoEncontrada();

        if (fixar)
        {
            // Uma por conversa. Desafixa as demais no mesmo golpe, em vez de
            // acumular um mural no topo do fio — o lugar mais caro da tela.
            await db.Mensagens
                .Where(m => m.ConversaId == conversaId && m.FixadaEm != null && m.Id != mensagemId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.FixadaEm, (DateTime?)null)
                    .SetProperty(m => m.FixadaPorAtendenteId, (Guid?)null)
                    .SetProperty(m => m.AtualizadoEm, agora), ct);

            mensagem.FixadaEm = agora;
            mensagem.FixadaPorAtendenteId = ctx.AtendenteId;
        }
        else
        {
            mensagem.FixadaEm = null;
            mensagem.FixadaPorAtendenteId = null;
        }

        await db.SaveChangesAsync(ct);

        var resposta = await ObterUmaAsync(mensagem.Id, conversaId, ct);
        var resumo = await conversas.ObterResumoAsync(conversaId, ct);
        await notificador.MensagemRecebidaAsync(conversa.TenantId, resposta, resumo, ct);

        return resposta;
    }

    public async Task<int> OcultarAsync(
        Guid conversaId,
        OcultarMensagensRequest request,
        CancellationToken ct = default)
    {
        var conversa = await CarregarParaAcaoAsync(conversaId, ct);

        if (request.MensagemIds.Count == 0)
        {
            return 0;
        }

        var agora = DateTime.UtcNow;
        var ids = request.MensagemIds;

        // ExecuteUpdate, e nao entidade rastreada: e operacao em lote vinda da
        // selecao multipla, e carregar N mensagens para mudar duas colunas seria
        // trabalho a toa. O WHERE amarra a conversa e o tenant — nao da para
        // ocultar mensagem de outro atendimento passando o Id na mao.
        var afetadas = await db.Mensagens
            .Where(m => ids.Contains(m.Id)
                     && m.ConversaId == conversaId
                     && m.TenantId == conversa.TenantId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.OcultaEm, request.Ocultar ? agora : null)
                .SetProperty(m => m.OcultaPorAtendenteId, request.Ocultar ? ctx.AtendenteId : null)
                .SetProperty(m => m.AtualizadoEm, agora), ct);

        log.LogInformation(
            "{Total} mensagem(ns) {Acao} na conversa {ConversaId} por {AtendenteId}",
            afetadas, request.Ocultar ? "ocultada(s)" : "reexibida(s)", conversaId, ctx.AtendenteId);

        var resumo = await conversas.ObterResumoAsync(conversaId, ct);
        await notificador.ConversaAtualizadaAsync(conversa.TenantId, resumo, ct);

        return afetadas;
    }

    public async Task<int> EncaminharAsync(
        Guid conversaId,
        EncaminharMensagensRequest request,
        CancellationToken ct = default)
    {
        var origem = await CarregarParaAcaoAsync(conversaId, ct);

        if (request.MensagemIds.Count == 0)
        {
            return 0;
        }

        if (request.ConversaDestinoId == conversaId)
        {
            throw AtendimentoServiceException.EncaminhamentoParaMesmaConversa();
        }

        var destino = await db.Conversas
            .FirstOrDefaultAsync(c => c.Id == request.ConversaDestinoId
                                   && c.TenantId == origem.TenantId, ct)
            ?? throw AtendimentoServiceException.ConversaNaoEncontrada();

        if (destino.Status != StatusConversa.Ativa)
        {
            throw AtendimentoServiceException.ConversaEncerrada();
        }

        // Mesma regra de quem pode escrever no destino que vale para uma mensagem
        // digitada ali: conversa na fila e assumida, conversa de terceiro e
        // recusada. Encaminhar nao pode ser porta dos fundos para escrever numa
        // conversa que o atendente nao poderia responder.
        if (destino.AtendenteId is null)
        {
            await conversas.AssumirAsync(destino.Id, ct);
        }
        else if (destino.AtendenteId != ctx.AtendenteId && !ctx.PodeOperarConversaDeTerceiro)
        {
            var responsavel = await db.Atendentes.AsNoTracking()
                .Where(a => a.Id == destino.AtendenteId)
                .Select(a => a.Nome)
                .FirstOrDefaultAsync(ct) ?? "outro atendente";

            throw AtendimentoServiceException.NaoEhOResponsavel(responsavel);
        }

        var ids = request.MensagemIds;

        var originais = await db.Mensagens.AsNoTracking()
            .Where(m => ids.Contains(m.Id)
                     && m.ConversaId == conversaId
                     && m.TenantId == origem.TenantId)
            .OrderBy(m => m.EnviadaEm)
            .ThenBy(m => m.Id)
            .Select(m => new { m.Id, m.Conteudo })
            .ToListAsync(ct);

        if (originais.Count == 0)
        {
            throw AtendimentoServiceException.MensagemNaoEncontrada();
        }

        var agora = DateTime.UtcNow;

        foreach (var original in originais)
        {
            db.Mensagens.Add(new Mensagem
            {
                TenantId = origem.TenantId,
                ConversaId = destino.Id,

                // Quem encaminha ASSINA. O texto e de outra conversa, mas quem
                // decidiu trazê-lo para cá foi este atendente, e o RF-007 vale
                // igual: a mensagem carrega quem a colocou no fio.
                AutorTipo = AutorTipo.Atendente,
                AtendenteId = ctx.AtendenteId,

                Conteudo = original.Conteudo,
                Tipo = TipoMensagem.Texto,
                EncaminhadaDeMensagemId = original.Id,
                StatusEntrega = StatusEntrega.Enviada,
                EnviadaEm = agora
            });
        }

        await db.SaveChangesAsync(ct);

        // Encaminhamento e mensagem de gente: reordena a inbox, ao contrario do
        // aviso automatico.
        await db.Conversas
            .Where(c => c.Id == destino.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.UltimaMensagemEm, agora)
                .SetProperty(c => c.AtualizadoEm, agora), ct);

        log.LogInformation(
            "{Total} mensagem(ns) encaminhada(s) de {Origem} para {Destino} por {AtendenteId}",
            originais.Count, conversaId, destino.Id, ctx.AtendenteId);

        var resumo = await conversas.ObterResumoAsync(destino.Id, ct);
        await notificador.ConversaAtualizadaAsync(origem.TenantId, resumo, ct);

        return originais.Count;
    }

    public async Task<IReadOnlyList<DestinoDeEncaminhamentoResponse>> DestinosAsync(
        Guid conversaOrigemId,
        string? busca,
        CancellationToken ct = default)
    {
        var tenantId = ctx.TenantId;

        var consulta = db.Conversas.AsNoTracking()
            .Where(c => c.TenantId == tenantId
                     && c.Status == StatusConversa.Ativa
                     && c.Id != conversaOrigemId);

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = busca.Trim();

            consulta = consulta.Where(c =>
                c.Protocolo.Contains(termo) ||
                db.Clientes.Any(cl => cl.Id == c.ClienteId && cl.Nome.Contains(termo)));
        }

        return await consulta
            .OrderByDescending(c => c.UltimaMensagemEm)
            .Take(30)
            .Select(c => new DestinoDeEncaminhamentoResponse(
                c.Id,
                c.Protocolo,
                db.Clientes.Where(cl => cl.Id == c.ClienteId).Select(cl => cl.Nome).FirstOrDefault() ?? string.Empty,
                db.Setores.Where(s => s.Id == c.SetorId).Select(s => s.Nome).FirstOrDefault(),
                db.Atendentes.Where(a => a.Id == c.AtendenteId).Select(a => a.Nome).FirstOrDefault()))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Carrega a conversa e confere que quem pediu pode mexer nela.
    /// </summary>
    /// <remarks>
    /// Fixar e ocultar sao acoes sobre o fio, nao envio: nao assumem conversa. Quem
    /// nao e o responsavel — nem tem perfil de supervisao — nao reorganiza o
    /// atendimento alheio.
    /// </remarks>
    private async Task<Conversa> CarregarParaAcaoAsync(Guid conversaId, CancellationToken ct)
    {
        var conversa = await db.Conversas
            .FirstOrDefaultAsync(c => c.Id == conversaId && c.TenantId == ctx.TenantId, ct)
            ?? throw AtendimentoServiceException.ConversaNaoEncontrada();

        if (conversa.AtendenteId is not null
            && conversa.AtendenteId != ctx.AtendenteId
            && !ctx.PodeOperarConversaDeTerceiro)
        {
            var responsavel = await db.Atendentes.AsNoTracking()
                .Where(a => a.Id == conversa.AtendenteId)
                .Select(a => a.Nome)
                .FirstOrDefaultAsync(ct) ?? "outro atendente";

            throw AtendimentoServiceException.NaoEhOResponsavel(responsavel);
        }

        return conversa;
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
