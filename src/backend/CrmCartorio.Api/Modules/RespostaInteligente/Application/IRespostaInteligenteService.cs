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
    Task<RespostaDaIA> ResponderAsync(
        Guid tenantId,
        IReadOnlyList<TrocaAnterior> historico,
        string mensagemDoCliente,
        CancellationToken ct = default);
}

/// <summary>Uma troca já ocorrida nesta tentativa de resposta inteligente.</summary>
public sealed record TrocaAnterior(bool DoCliente, string Conteudo);

/// <summary>
/// O sinal explícito de "não sei responder isso" pedido no escopo — nunca texto
/// livre interpretado, sempre um campo booleano que o modelo é obrigado a preencher.
/// </summary>
public sealed record RespostaDaIA(bool PodeResponder, string Resposta, string Motivo);
