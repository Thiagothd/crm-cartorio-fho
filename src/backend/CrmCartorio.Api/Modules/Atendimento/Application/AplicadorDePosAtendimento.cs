using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Atendimento.Domain;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Enums;
using CrmCartorio.Api.Modules.RespostaInteligente.Application;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <summary>
/// Trata a mensagem do cliente quando NENHUMA das outras flags de fluxo esta
/// ligada — o atendimento estruturado ja terminou (ou nunca chegou a comecar
/// via triagem, ex.: conversa aberta pelo painel).
/// </summary>
/// <remarks>
/// <b>E o "else" que faltava.</b> Achado ao vivo em 2026-08-17: sem esta classe,
/// o `if/else if` de <see cref="RecebimentoDeWhatsApp"/> e
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
        CancellationToken ct)
    {
        // 1. "menu", "voltar" — reabre do zero, em QUALQUER ponto da conversa.
        // Verificado antes de tudo: e a saida de emergencia do cliente, e nao
        // pode depender de nenhuma flag estar (ou nao) ligada.
        if (TriagemAutomatica.PediuMenu(conteudo))
        {
            conversa.AguardandoMenuDeContinuacao = false;
            conversa.AguardandoRespostaInteligente = false;
            conversa.TentativasRespostaInteligente = 0;

            await automaticas.EnfileirarAsync(conversa, GatilhoDeMensagem.NovoAtendimento, ct: ct);
            conversa.AguardandoTriagem = true;

            log.LogInformation(
                "Conversa {ConversaId} reabriu o menu inicial a pedido do cliente.", conversa.Id);

            return;
        }

        // 2. Menu de continuacao no ar — le a escolha 1-4.
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

        // 3. Piso: a IA tenta responder, e escala pra humano sozinha quando pedem
        // atendente, ou nao sabe, ou estoura o limite de trocas. Nunca sobra
        // mensagem sem passar por aqui.
        await AplicadorDeRespostaInteligente.AplicarAsync(
            db, ia, automaticas, opcoesDaIA, conversa, conteudo, agora, log, ct);
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
            await automaticas.EnfileirarAsync(conversa, GatilhoDeMensagem.TriagemComCorrespondencia, ct: ct);
            conversa.AguardandoEscolhaDeServico = true;
        }
        else if (escolhido == OpcaoFalarComAtendente)
        {
            var gatilho = conversa.SetorId is null
                ? GatilhoDeMensagem.TriagemSemCorrespondencia
                : GatilhoDeMensagem.PedidoDeAtendente;

            await automaticas.EnfileirarAsync(conversa, gatilho, ct: ct);
        }
        else if (escolhido == OpcaoEncerrar)
        {
            await automaticas.EnfileirarAsync(conversa, GatilhoDeMensagem.Despedida, ct: ct);
            EncerrarPeloCliente(db, conversa, agora);
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
    /// </remarks>
    private static void EncerrarPeloCliente(CrmDbContext db, Conversa conversa, DateTime agora)
    {
        conversa.Status = StatusConversa.Encerrada;
        conversa.EncerradaEm = agora;
        conversa.EncerradaPorAtendenteId = null;
        conversa.MotivoEncerramento = "Encerrado pelo cliente, via menu do WhatsApp.";
        conversa.MensagensNaoLidas = 0;
        conversa.AguardandoRespostaInteligente = false;
        conversa.TentativasRespostaInteligente = 0;

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
