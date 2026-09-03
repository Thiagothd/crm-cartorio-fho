namespace CrmCartorio.Api.Modules.RespostaInteligente.Application;

public interface IConhecimentoDeIaService
{
    Task<IReadOnlyList<ConhecimentoDeIaResponse>> ListarAsync(
        bool incluirInativos,
        CancellationToken ct = default);

    Task<ConhecimentoDeIaResponse> CriarAsync(
        SalvarConhecimentoDeIaRequest request,
        CancellationToken ct = default);

    Task<ConhecimentoDeIaResponse> AtualizarAsync(
        Guid id,
        SalvarConhecimentoDeIaRequest request,
        CancellationToken ct = default);

    Task<IReadOnlyList<VersaoDeConhecimentoDeIaResponse>> HistoricoAsync(
        Guid id,
        CancellationToken ct = default);
}
