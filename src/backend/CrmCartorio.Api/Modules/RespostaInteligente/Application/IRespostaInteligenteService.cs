using CrmCartorio.Api.Modules.RespostaInteligente.Domain.Enums;

namespace CrmCartorio.Api.Modules.RespostaInteligente.Application;

/// <summary>
/// Camada de resposta grounded: tenta responder o cliente só com dado cadastrado,
/// antes de escalar para atendimento humano.
/// </summary>
/// <remarks>
/// Não depende de <c>IContextoDoUsuario</c> — mesma disciplina de
/// <see cref="Atendimento.Application.IDisparadorDeMensagemAutomatica"/>: quem chama
/// vem da triagem, que roda tanto pela simulação do painel quanto pelo webhook do
/// WhatsApp real, e o webhook não tem token de onde ler o tenant.
///
/// <b>Nunca lança.</b> Chave ausente, gateway fora do ar, timeout, resposta mal
/// formada — tudo isso é o mesmo resultado prático: a IA não respondeu agora. Quem
/// chama não precisa de try/catch; só olha <see cref="RespostaDaIA.PodeResponder"/>
/// e cai para o encaminhamento humano de sempre.
/// </remarks>
public interface IRespostaInteligenteService
{
    /// <param name="historico">
    /// Trocas anteriores desta tentativa, mais antiga primeiro. Vazio na primeira
    /// pergunta fora do menu.
    /// </param>
    /// <param name="contexto">
    /// Os fluxos reais (triagem, pós-atendimento, escolha de serviço, painel e
    /// webhook — todos via <c>AplicadorDeRespostaInteligente</c>) sempre passam a
    /// conversa. Opcional (nulo permitido) só pela mesma disciplina ctx-free do
    /// resto do slice, para não obrigar um chamador futuro sem conversa em mãos a
    /// inventar uma. Só usado para registrar <c>ChamadaDeIa</c> (fins de
    /// observabilidade) — nunca muda o comportamento da resposta em si.
    /// </param>
    Task<RespostaDaIA> ResponderAsync(
        Guid tenantId,
        IReadOnlyList<TrocaAnterior> historico,
        string mensagemDoCliente,
        ContextoDaChamada? contexto = null,
        CancellationToken ct = default);
}

/// <summary>Uma troca já ocorrida nesta tentativa de resposta inteligente.</summary>
public sealed record TrocaAnterior(bool DoCliente, string Conteudo);

/// <summary>
/// Dados de correlação da chamada, gravados em <c>ChamadaDeIa</c> só para
/// observabilidade — nenhum campo aqui muda o comportamento da resposta.
/// <see cref="TipoServicoId"/> e <see cref="SetorId"/> são o SNAPSHOT da conversa
/// no instante desta chamada, não o estado final dela (que pode mudar depois).
/// </summary>
public sealed record ContextoDaChamada(
    Guid? ConversaId = null,
    Guid? MensagemId = null,
    Guid? TipoServicoId = null,
    Guid? SetorId = null);

/// <summary>
/// O sinal explícito de "não sei responder isso" pedido no escopo — nunca texto
/// livre interpretado, sempre um campo booleano que o modelo é obrigado a preencher.
/// </summary>
/// <param name="CategoriaDaNaoResposta">
/// Só significativa quando <paramref name="PodeResponder"/> é false. Categoria
/// fechada devolvida pelo modelo — nunca o texto livre de <paramref name="Motivo"/>,
/// que é só para log. Provedor que não devolver a categoria cai no default
/// <see cref="MotivoDaNaoRespostaDeIa.NaoClassificado"/> — a IA nunca inventa uma
/// categoria só para preencher. Ver <see cref="MotivoDaNaoRespostaDeIa"/>.
/// </param>
public sealed record RespostaDaIA(
    bool PodeResponder,
    string Resposta,
    string Motivo,
    MotivoDaNaoRespostaDeIa CategoriaDaNaoResposta = MotivoDaNaoRespostaDeIa.NaoClassificado);
