namespace CrmCartorio.Api.Modules.Atendimento.Application;

public interface IMensagemService
{
    Task<IReadOnlyList<MensagemResponse>> ListarAsync(Guid conversaId, CancellationToken ct = default);

    /// <summary>Resposta do atendente. Registra quem enviou (RF-007).</summary>
    Task<MensagemResponse> EnviarAsync(Guid conversaId, EnviarMensagemRequest request, CancellationToken ct = default);

    /// <summary>
    /// Fase 1: simula mensagem chegando do cliente, para demonstrar o fluxo sem
    /// WhatsApp real. Na Fase 2 este e o ponto que o webhook da Cloud API chama.
    /// </summary>
    Task<MensagemResponse> SimularRecebimentoAsync(
        Guid conversaId,
        SimularMensagemDeClienteRequest request,
        CancellationToken ct = default);
}
