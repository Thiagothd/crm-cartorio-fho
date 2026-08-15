namespace CrmCartorio.Api.Modules.Configuracoes.Application;

public interface IMensagemAutomaticaService
{
    Task<IReadOnlyList<MensagemAutomaticaResponse>> ListarAsync(
        bool incluirInativas,
        CancellationToken ct = default);

    Task<MensagemAutomaticaResponse> CriarAsync(
        SalvarMensagemAutomaticaRequest request,
        CancellationToken ct = default);

    Task<MensagemAutomaticaResponse> AtualizarAsync(
        Guid id,
        SalvarMensagemAutomaticaRequest request,
        CancellationToken ct = default);
}
