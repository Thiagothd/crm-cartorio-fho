namespace CrmCartorio.Api.Modules.Devedores.Application;

public interface IDevedorService
{
    Task<ListagemDeDevedoresResponse> ListarAsync(FiltroDeDevedores filtro, CancellationToken ct = default);

    Task<ResumoDeDividaDoClienteResponse> ObterPorClienteAsync(Guid clienteId, CancellationToken ct = default);

    Task<PendenciaFinanceiraResponse> ObterAsync(Guid id, CancellationToken ct = default);

    Task<PendenciaFinanceiraResponse> CriarAsync(CriarPendenciaFinanceiraRequest request, CancellationToken ct = default);

    Task<PendenciaFinanceiraResponse> AtualizarAsync(
        Guid id,
        AtualizarPendenciaFinanceiraRequest request,
        CancellationToken ct = default);

    Task<PendenciaFinanceiraResponse> RegistrarPagamentoAsync(
        Guid id,
        RegistrarPagamentoRequest request,
        CancellationToken ct = default);

    Task<PendenciaFinanceiraResponse> ReabrirAsync(Guid id, CancellationToken ct = default);

    Task CancelarAsync(Guid id, CancellationToken ct = default);

    Task<CobrarNoWhatsappResponse> CobrarNoWhatsappAsync(
        CobrarNoWhatsappRequest request,
        CancellationToken ct = default);

    Task<AbrirConversaComDevedorResponse> AbrirConversaAsync(
        Guid clienteId,
        CancellationToken ct = default);

    Task<ResultadoDeImportacaoDeDevedoresResponse> ImportarEmLoteAsync(
        ImportarDevedoresEmLoteRequest request,
        CancellationToken ct = default);
}
