namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <summary>
/// Acoes do menu de contexto da mensagem.
/// </summary>
/// <remarks>
/// NAO existe "apagar". Ocultar e o mais proximo disso, e de proposito: o sistema
/// nao apaga conversa encerrada nem cliente desativado, e nao ha razao para a
/// mensagem — a unica peca com fe de conversa — ser a excecao. Exclusao real, se
/// um dia for pedida, e decisao a ser tomada explicitamente, nao padrao.
/// </remarks>
public interface IAcoesDeMensagem
{
    /// <summary>
    /// Fixa a mensagem no topo. Uma por conversa: fixar outra desafixa a anterior.
    /// </summary>
    Task<MensagemResponse> FixarAsync(
        Guid conversaId, Guid mensagemId, bool fixar, CancellationToken ct = default);

    /// <summary>Oculta ou reexibe. A linha permanece no banco em ambos os casos.</summary>
    Task<int> OcultarAsync(
        Guid conversaId, OcultarMensagensRequest request, CancellationToken ct = default);

    /// <summary>Reenvia mensagens desta conversa para outra conversa ativa.</summary>
    Task<int> EncaminharAsync(
        Guid conversaId, EncaminharMensagensRequest request, CancellationToken ct = default);

    /// <summary>Conversas ativas que podem receber um encaminhamento.</summary>
    Task<IReadOnlyList<DestinoDeEncaminhamentoResponse>> DestinosAsync(
        Guid conversaOrigemId, string? busca, CancellationToken ct = default);

    /// <summary>
    /// Troca o texto de uma mensagem que o atendente mandou por engano, inclusive
    /// no aparelho do cliente.
    /// </summary>
    /// <remarks>
    /// Vale so para texto escrito por atendente, dentro da janela que o WhatsApp
    /// aceita editar (<see cref="MensagemService.MinutosParaCorrigir"/>). O texto
    /// anterior nao se perde: a primeira correcao o guarda em
    /// <c>Mensagem.ConteudoOriginal</c>.
    ///
    /// Conversa encerrada tambem aceita correcao — o engano chegou ao cliente do
    /// mesmo jeito, e obrigar a reabrir o atendimento so para consertar uma frase
    /// mexeria no estado do funil por um motivo que nao e dele.
    /// </remarks>
    Task<MensagemResponse> CorrigirAsync(
        Guid conversaId,
        Guid mensagemId,
        CorrigirMensagemRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Apaga no aparelho do cliente uma mensagem enviada por engano. A linha
    /// permanece no fio, marcada.
    /// </summary>
    /// <remarks>
    /// Diferente de <see cref="OcultarAsync"/>, que so esconde de quem atende:
    /// aqui o pedido vai ao WhatsApp e o cliente passa a ver "Esta mensagem foi
    /// apagada". Serve para o caso que a correcao nao resolve — anexo errado, ou
    /// mensagem que foi para a conversa errada.
    /// </remarks>
    Task<MensagemResponse> ApagarParaTodosAsync(
        Guid conversaId,
        Guid mensagemId,
        CancellationToken ct = default);
}

public interface IMensagemService
{
    Task<IReadOnlyList<MensagemResponse>> ListarAsync(Guid conversaId, CancellationToken ct = default);

    /// <summary>Resposta do atendente. Registra quem enviou (RF-007).</summary>
    Task<MensagemResponse> EnviarAsync(Guid conversaId, EnviarMensagemRequest request, CancellationToken ct = default);

    /// <summary>
    /// Resposta do atendente com arquivo anexado.
    /// </summary>
    /// <remarks>
    /// Caminho separado de <see cref="EnviarAsync"/> porque a requisicao e
    /// multipart, nao JSON — e porque a legenda pode ser vazia, ao contrario de
    /// uma mensagem de texto, que vazia nao e mensagem nenhuma.
    /// </remarks>
    Task<MensagemResponse> EnviarAnexoAsync(
        Guid conversaId,
        EnviarAnexoRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Devolve o binario do anexo de uma mensagem.
    /// </summary>
    /// <remarks>
    /// Passa pelo servico, e nao por arquivo estatico, porque anexo de cartorio e
    /// documento de cliente: a leitura confere tenant e conversa antes de
    /// entregar. Uma pasta servida por URL adivinhavel entregaria RG de terceiro
    /// a quem tentasse o id certo.
    /// </remarks>
    Task<AnexoParaDownload> ObterAnexoAsync(
        Guid conversaId,
        Guid mensagemId,
        CancellationToken ct = default);

    /// <summary>
    /// Fase 1: simula mensagem chegando do cliente, para demonstrar o fluxo sem
    /// WhatsApp real. Na Fase 2 este e o ponto que o webhook da Cloud API chama.
    /// </summary>
    Task<MensagemResponse> SimularRecebimentoAsync(
        Guid conversaId,
        SimularMensagemDeClienteRequest request,
        CancellationToken ct = default);
}
