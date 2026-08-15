namespace CrmCartorio.Api.Modules.Configuracoes.Application;

public interface IRespostaRapidaService
{
    Task<IReadOnlyList<RespostaRapidaResponse>> ListarAsync(
        bool incluirInativas,
        CancellationToken ct = default);

    Task<RespostaRapidaResponse> CriarAsync(
        SalvarRespostaRapidaRequest request,
        CancellationToken ct = default);

    Task<RespostaRapidaResponse> AtualizarAsync(
        Guid id,
        SalvarRespostaRapidaRequest request,
        CancellationToken ct = default);
}
