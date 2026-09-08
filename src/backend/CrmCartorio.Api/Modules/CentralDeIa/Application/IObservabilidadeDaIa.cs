namespace CrmCartorio.Api.Modules.CentralDeIa.Application;

public interface IObservabilidadeDaIa
{
    Task<ObservabilidadeDaIaResponse> ObterAsync(FiltroDeObservabilidadeDaIa filtro, CancellationToken ct = default);

    Task<PaginaDeTenantsResponse> ObterTenantsAsync(
        FiltroDeObservabilidadeDaIa filtro,
        string? busca,
        OrdenarTenantsPor ordenarPor,
        bool decrescente,
        int pagina,
        int tamanhoDaPagina,
        CancellationToken ct = default);

    Task<PaginaDeHistoricoResponse> ObterHistoricoAsync(
        FiltroDeObservabilidadeDaIa filtro,
        Guid? rodadaId,
        int pagina,
        int tamanhoDaPagina,
        CancellationToken ct = default);
}
