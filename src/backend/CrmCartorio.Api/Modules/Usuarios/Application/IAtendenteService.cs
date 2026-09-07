namespace CrmCartorio.Api.Modules.Usuarios.Application;

public interface IAtendenteService
{
    Task<IReadOnlyList<AtendenteResponse>> ListarAsync(bool incluirInativos, CancellationToken ct = default);

    Task<IReadOnlyList<AtendenteOpcaoResponse>> ListarOpcoesAsync(CancellationToken ct = default);

    Task<AtendenteResponse> ObterAsync(Guid id, CancellationToken ct = default);

    Task<AtendenteResponse> CriarAsync(CriarAtendenteRequest request, CancellationToken ct = default);

    Task<AtendenteResponse> AtualizarAsync(Guid id, AtualizarAtendenteRequest request, CancellationToken ct = default);

    Task RedefinirSenhaAsync(Guid id, RedefinirSenhaRequest request, CancellationToken ct = default);

    Task<PermissoesDoAtendenteResponse> ObterPermissoesAsync(Guid id, CancellationToken ct = default);

    Task DefinirPermissoesAsync(Guid id, DefinirPermissoesRequest request, string? ip, CancellationToken ct = default);
}
