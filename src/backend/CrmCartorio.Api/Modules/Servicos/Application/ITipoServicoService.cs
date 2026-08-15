namespace CrmCartorio.Api.Modules.Servicos.Application;

public interface ITipoServicoService
{
    Task<IReadOnlyList<TipoServicoResponse>> ListarAsync(bool incluirInativos, CancellationToken ct = default);

    Task<TipoServicoResponse> ObterAsync(Guid id, CancellationToken ct = default);

    Task<TipoServicoResponse> CriarAsync(SalvarTipoServicoRequest request, CancellationToken ct = default);

    Task<TipoServicoResponse> AtualizarAsync(Guid id, SalvarTipoServicoRequest request, CancellationToken ct = default);
}
