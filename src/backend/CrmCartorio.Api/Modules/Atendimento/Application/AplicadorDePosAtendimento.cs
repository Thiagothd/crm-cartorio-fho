using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Atendimento.Domain;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Enums;
using CrmCartorio.Api.Modules.RespostaInteligente.Application;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <summary>
/// Duas coisas: <see cref="AplicarAsync"/> trata a mensagem do cliente quando
/// NENHUMA das outras flags de fluxo esta ligada — o atendimento estruturado
/// ja terminou (ou nunca chegou a comecar via triagem, ex.: conversa aberta
/// pelo painel). <see cref="ReabrirDoZeroAsync"/> e a saida de emergencia
/// "menu"/"voltar", chamada pelo despacho ANTES de olhar qualquer flag.
/// </summary>
/// <remarks>
/// <b>`AplicarAsync` e o "else" que faltava.</b> Achado ao vivo em 2026-08-17:
/// sem esta classe, o `if/else if` de <see cref="RecebimentoDeWhatsApp"/> e
/// <see cref="MensagemService"/> simplesmente acabava quando nenhuma flag batia
/// — a mensagem era gravada e nada respondia. Um "obrigada" depois do checklist,
/// ou qualquer mensagem numa conversa aberta pelo painel sem passar pela triagem,
/// ficava muda. Esta classe existe para que ISSO NUNCA MAIS ACONTECA: todo
/// caminho aqui termina numa mensagem ao cliente, nem que seja a IA.
///
/// Mesma forma dos outros Aplicador* e pelo mesmo motivo: ctx-free, dois
/// chamadores (simulacao do painel e webhook real).
/// </remarks>
public static class AplicadorDePosAtendimento
{
    private static readonly Guid OpcaoVerOutroServico = new("0199b2c0-0009-7000-8000-000000000001");
    private static readonly Guid OpcaoTrocarDeSetor = new("0199b2c0-0009-7000-8000-000000000002");
    private static readonly Guid OpcaoFalarComAtendente = new("0199b2c0-0009-7000-8000-000000000003");
    private static readonly Guid OpcaoEncerrar = new("0199b2c0-0009-7000-8000-000000000004");

    /// <summary>
    /// As 4 opcoes de <see cref="GatilhoDeMensagem.MenuDeContinuacao"/>. Guids
    /// fixos (nao vem de banco) so para reaproveitar o mesmo interpretador
    /// numero/nome de <see cref="TriagemAutomatica"/> — o menu de continuacao
    /// nao e cadastro, e as 4 opcoes nunca mudam.
    /// </summary>
    private static readonly TriagemAutomatica.OpcaoDeMenu[] OpcoesDoMenuDeContinuacao =
    [
        new(OpcaoVerOutroServico, "Ver outro serviço"),
        new(OpcaoTrocarDeSetor, "Trocar de setor"),
        new(OpcaoFalarComAtendente, "Falar com um atendente"),
        new(OpcaoEncerrar, "Encerrar atendimento")
    ];

    public static async Task AplicarAsync(
        CrmDbContext db,
        IDisparadorDeMensagemAutomatica automaticas,
        IRespostaInteligenteService ia,
        OpcoesDaIA opcoesDaIA,
        Conversa conversa,
        string conteudo,
        DateTime agora,
        ILogger log,
        CancellationToken ct,
        Guid? mensagemAtualId = null)
    {
        // "menu"/"voltar" ja foi tratado no despacho, antes desta classe ser
        // chamada (ver ReabrirDoZeroAsync) — nao precisa checar de novo aqui.

        // 1. Menu de continuacao no ar — le a escolha 1-4.
        if (conversa.AguardandoMenuDeContinuacao)
        {
            var escolhido = TriagemAutomatica.InterpretarResposta(conteudo, OpcoesDoMenuDeContinuacao);

            if (escolhido is { } id)
            {
                await AplicarEscolhaDeContinuacaoAsync(db, automaticas, conversa, id, agora, ct);

                log.LogInformation(
                    "Conversa {ConversaId} escolheu {Opcao} no menu de continuacao.", conversa.Id, id);

                return;
            }

            // Sem corresponder a nenhuma das 4 opcoes, nao trava a conversa
            // esperando de novo: desliga a flag e deixa a MESMA mensagem cair
            // para o piso da IA, na sequencia. Uma rodada extra pedindo "responda
            // 1, 2, 3 ou 4" e o tipo de atrito que afasta gente de atendimento por
            // WhatsApp — mesmo principio ja aplicado na triagem de setor.
            conversa.AguardandoMenuDeContinuacao = false;
        }

        // 2. Piso: a IA tenta responder; sem sucesso, responde sem escalar
        // sozinha (so por pedido explicito). Nunca sobra mensagem sem passar
        // por aqui.
        await AplicadorDeRespostaInteligente.AplicarAsync(
            db, ia, automaticas, opcoesDaIA, conversa, conteudo, agora, log, ct, mensagemAtualId);
    }

    /// <summary>
    /// Reabre o atendimento do zero — "menu", "voltar" e sinônimos, em
    /// QUALQUER ponto da conversa, mesmo com uma flag de fluxo já ligada.
    /// </summary>
    /// <remarks>
    /// Achado ao vivo em 2026-08-18: até então isto só era checado dentro de
    /// <see cref="AplicarAsync"/>, que só roda quando NENHUMA das três flags do
    /// despacho externo está ligada. Com <see cref="Conversa.AguardandoRespostaInteligente"/>
    /// permanecendo ligada mesmo sem resposta (2026-08-18, ver
    /// <see cref="AplicadorDeRespostaInteligente"/>), uma conversa podia ficar
    /// presa indo direto para a IA — e "quero voltar ao menu inicial" nunca
    /// chegava a esta classe, porque a IA respondia primeiro, sem entender
    /// "menu" como comando. Agora quem chama primeiro é o despacho externo
    /// (<see cref="MensagemService"/>, <see cref="RecebimentoDeWhatsApp"/>),
    /// antes de olhar qualquer flag — é a saída de emergência de verdade,
    /// nunca refém de onde a conversa está.
    ///
    /// <b>Limpa `SetorId` e `TipoServicoId`.</b> Segundo achado no mesmo dia,
    /// num teste automatizado: sem isto, um cliente que já tinha setor definido
    /// e pedia "menu" ficava com `SetorId` preenchido mesmo depois de
    /// "recomeçar" — e <see cref="AplicadorDeTriagem"/>, ao ver `SetorId != null`
    /// com `AguardandoTriagem` ligada, interpreta isso como "atendente já
    /// assumiu manualmente" (guarda pensada para OUTRO cenário) e não faz
    /// NADA — nem responde, nem escala. Cliente ficava mudo na mensagem
    /// seguinte ao "menu". "Do zero" agora é literal.
    /// </remarks>
    public static async Task ReabrirDoZeroAsync(
        CrmDbContext db,
        IDisparadorDeMensagemAutomatica automaticas,
        Conversa conversa,
        DateTime agora,
        CancellationToken ct)
    {
        conversa.AguardandoMenuDeContinuacao = false;
        conversa.AguardandoEscolhaDeServico = false;
        conversa.ServicoDoSubmenuId = null;
        conversa.AguardandoDadosDoServico = false;
        conversa.DadosDoServicoRecebidosEm = null;
        conversa.AguardandoComprovante = false;
        conversa.AguardandoRespostaInteligente = false;
        conversa.TentativasRespostaInteligente = 0;
        conversa.SetorId = null;
        conversa.TipoServicoId = null;

        // "Do zero" e literal: uma pergunta de encerramento pendente nao sobrevive
        // a um recomeco. Sem isto, o cliente que digita "menu" recomeca o
        // atendimento e mesmo assim ve a conversa fechar sozinha minutos depois.
        conversa.PerguntaDeEncerramentoEnviadaEm = null;

        // Pelo mesmo motivo: quem responde "menu" ao pedido de nome esta pedindo o
        // atendimento, nao se recusando a existir. Deixar a flag ligada faria a
        // mensagem SEGUINTE dele ser lida como se fosse o nome.
        conversa.AguardandoNome = false;

        db.Mensagens.Add(new Mensagem
        {
            TenantId = conversa.TenantId,
            ConversaId = conversa.Id,
            AutorTipo = AutorTipo.Sistema,
            Conteudo = "Cliente reabriu o atendimento pelo menu.",
            Tipo = TipoMensagem.Texto,
            StatusEntrega = StatusEntrega.Entregue,
            EnviadaEm = agora,
            LidaEm = agora
        });

        await automaticas.EnfileirarAsync(conversa, GatilhoDeMensagem.NovoAtendimento, ct: ct);
        conversa.AguardandoTriagem = true;
    }

    private static async Task AplicarEscolhaDeContinuacaoAsync(
        CrmDbContext db,
        IDisparadorDeMensagemAutomatica automaticas,
        Conversa conversa,
        Guid escolhido,
        DateTime agora,
        CancellationToken ct)
    {
        conversa.AguardandoMenuDeContinuacao = false;

        var servicosDoSetor = conversa.SetorId is { } setorId
            ? await automaticas.ServicosDoMenuAsync(conversa.TenantId, setorId, ct)
            : [];

        if (escolhido == OpcaoVerOutroServico && servicosDoSetor.Count > 0)
        {
            conversa.ServicoDoSubmenuId = null;
            await automaticas.EnfileirarAsync(conversa, GatilhoDeMensagem.TriagemComCorrespondencia, ct: ct);
            conversa.AguardandoEscolhaDeServico = true;
        }
        else if (escolhido == OpcaoFalarComAtendente)
        {
            var gatilho = conversa.SetorId is null
                ? GatilhoDeMensagem.TriagemSemCorrespondencia
                : GatilhoDeMensagem.PedidoDeAtendente;

            await automaticas.EnfileirarAsync(conversa, gatilho, ct: ct);

            // Segundo caminho de pedido de humano, paralelo ao de
            // AplicadorDeRespostaInteligente.EscalarAsync — mesmo efeito,
            // mesma necessidade de ligar o modo humano aqui (BUG-ATD-01).
            conversa.EmAtendimentoHumano = true;
        }
        else if (escolhido == OpcaoEncerrar)
        {
            await automaticas.EnfileirarAsync(conversa, GatilhoDeMensagem.Despedida, ct: ct);
            EncerrarPeloCliente(db, conversa, agora, "Encerrado pelo cliente, via menu do WhatsApp.");
        }
        else
        {
            // "Trocar de setor", ou "ver outro servico" sem nenhum disponivel no
            // setor atual — os dois resolvem do mesmo jeito: reabre do topo.
            await automaticas.EnfileirarAsync(conversa, GatilhoDeMensagem.NovoAtendimento, ct: ct);
            conversa.AguardandoTriagem = true;
        }
    }

    /// <remarks>
    /// Espelha <see cref="ConversaService.EncerrarAsync"/> — mesma arquivagem
    /// (RF-015, nunca DELETE) — mas ctx-free: quem fechou foi o proprio cliente
    /// pelo menu do WhatsApp, nao um atendente logado, entao
    /// <see cref="Conversa.EncerradaPorAtendenteId"/> fica nulo em vez de vir de
    /// um <c>ICurrentUserContext</c> que nao existe neste caminho. Achado ao vivo
    /// em 2026-08-17: o cliente escolhia "Encerrar" no menu de continuacao, a
    /// despedida saia, mas a conversa continuava Ativa no painel — nunca fechava
    /// de verdade.
    ///
    /// <b>Existe uma segunda copia desta decisao</b> em
    /// <see cref="VarredorDeInatividade"/>, escrita com <c>ExecuteUpdate</c> porque
    /// aquele caminho roda fora de requisicao HTTP e nao pode disputar o
    /// <see cref="Conversa.RowVersion"/> com quem esta atendendo. SE MEXER AQUI,
    /// MEXA LA — um campo de encerramento que entre so num dos dois produz duas
    /// conversas encerradas de jeitos diferentes no mesmo banco.
    ///
    /// <c>internal</c> e nao <c>private</c> desde 2026-08-19: o aplicador da
    /// resposta a pergunta de inatividade encerra pelo mesmo caminho.
    ///
    /// <b>Divergencia deliberada, desde 2026-09-04:</b> este caminho dispara o
    /// gatilho <see cref="Configuracoes.Domain.Enums.GatilhoDeMensagem.Despedida"/>
    /// (quem fechou foi o cliente, escolhendo); <see cref="ConversaService"/>
    /// dispara <see cref="Configuracoes.Domain.Enums.GatilhoDeMensagem.EncerramentoPeloAtendente"/>
    /// (quem fechou foi a serventia). Nao e a mesma mensagem por proposito — nao
    /// precisa ficar em sincronia com aquele gatilho.
    /// </remarks>
    internal static void EncerrarPeloCliente(
        CrmDbContext db,
        Conversa conversa,
        DateTime agora,
        string motivo)
    {
        conversa.Status = StatusConversa.Encerrada;
        conversa.EncerradaEm = agora;
        conversa.EncerradaPorAtendenteId = null;
        conversa.MotivoEncerramento = motivo;
        conversa.MensagensNaoLidas = 0;
        conversa.AguardandoRespostaInteligente = false;
        conversa.TentativasRespostaInteligente = 0;
        conversa.AguardandoTriagem = false;
        conversa.AguardandoEscolhaDeServico = false;
        conversa.ServicoDoSubmenuId = null;
        conversa.AguardandoDadosDoServico = false;
        conversa.DadosDoServicoRecebidosEm = null;
        conversa.AguardandoComprovante = false;
        conversa.AguardandoMenuDeContinuacao = false;
        conversa.AguardandoNome = false;
        conversa.PerguntaDeEncerramentoEnviadaEm = null;
        conversa.EmAtendimentoHumano = false;

        db.Mensagens.Add(new Mensagem
        {
            TenantId = conversa.TenantId,
            ConversaId = conversa.Id,
            AutorTipo = AutorTipo.Sistema,
            Conteudo = "Atendimento encerrado pelo cliente.",
            Tipo = TipoMensagem.Texto,
            StatusEntrega = StatusEntrega.Entregue,
            EnviadaEm = agora,
            LidaEm = agora
        });
    }
}
