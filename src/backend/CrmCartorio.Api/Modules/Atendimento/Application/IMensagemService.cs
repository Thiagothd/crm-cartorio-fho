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
