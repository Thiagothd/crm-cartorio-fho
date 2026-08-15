namespace CrmCartorio.Api.Modules.Configuracoes.Application;

public interface ISetorService
{
    Task<IReadOnlyList<SetorResponse>> ListarAsync(bool incluirInativos, CancellationToken ct = default);

    Task<SetorResponse> CriarAsync(SalvarSetorRequest request, CancellationToken ct = default);

    Task<SetorResponse> AtualizarAsync(Guid id, SalvarSetorRequest request, CancellationToken ct = default);
}
