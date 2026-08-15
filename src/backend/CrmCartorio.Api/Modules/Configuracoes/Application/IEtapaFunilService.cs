namespace CrmCartorio.Api.Modules.Configuracoes.Application;

public interface IEtapaFunilService
{
    Task<IReadOnlyList<EtapaResponse>> ListarAsync(bool incluirInativas, CancellationToken ct = default);

    Task<EtapaResponse> CriarAsync(SalvarEtapaRequest request, CancellationToken ct = default);

    Task<EtapaResponse> AtualizarAsync(Guid id, SalvarEtapaRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<EtapaResponse>> ReordenarAsync(IReadOnlyList<Guid> idsNaOrdem, CancellationToken ct = default);
}
