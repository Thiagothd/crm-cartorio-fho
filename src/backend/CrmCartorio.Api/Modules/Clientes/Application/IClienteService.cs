namespace CrmCartorio.Api.Modules.Clientes.Application;

public interface IClienteService
{
    Task<ResultadoPaginadoDeClientes> ListarAsync(FiltroDeClientes filtro, CancellationToken ct = default);

    Task<ClienteResponse> ObterAsync(Guid id, CancellationToken ct = default);

    Task<ClienteResponse> CriarAsync(DadosDeCliente dados, CancellationToken ct = default);

    Task<ClienteResponse> AtualizarAsync(Guid id, DadosDeCliente dados, CancellationToken ct = default);

    /// <summary>
    /// Exclusão física — único caso no sistema. Recusa se o cliente tiver
    /// conversa vinculada; para esses, o caminho é desativar (campo Ativo).
    /// </summary>
    Task ExcluirAsync(Guid id, CancellationToken ct = default);

    /// <summary>Cidades ja cadastradas, para o filtro da tela.</summary>
    Task<OpcoesDeClientesResponse> ObterOpcoesAsync(CancellationToken ct = default);

    /// <summary>Bytes da foto de perfil do WhatsApp do cliente, ou nulo quando indisponivel.</summary>
    Task<byte[]?> ObterFotoPerfilAsync(Guid id, CancellationToken ct = default);
}
