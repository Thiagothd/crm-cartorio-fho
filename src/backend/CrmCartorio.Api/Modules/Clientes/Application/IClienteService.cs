namespace CrmCartorio.Api.Modules.Clientes.Application;

public interface IClienteService
{
    Task<ResultadoPaginadoDeClientes> ListarAsync(FiltroDeClientes filtro, CancellationToken ct = default);

    Task<ClienteResponse> ObterAsync(Guid id, CancellationToken ct = default);

    Task<ClienteResponse> CriarAsync(DadosDeCliente dados, CancellationToken ct = default);

    Task<ClienteResponse> AtualizarAsync(Guid id, DadosDeCliente dados, CancellationToken ct = default);

    /// <summary>Cidades ja cadastradas, para o filtro da tela.</summary>
    Task<OpcoesDeClientesResponse> ObterOpcoesAsync(CancellationToken ct = default);
}
