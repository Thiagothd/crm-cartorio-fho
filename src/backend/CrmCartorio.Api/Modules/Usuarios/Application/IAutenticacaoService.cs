namespace CrmCartorio.Api.Modules.Usuarios.Application;

public interface IAutenticacaoService
{
    Task<LoginResponse> AutenticarAsync(LoginRequest request, CancellationToken ct = default);

    /// <summary>Dados do atendente autenticado na requisicao atual.</summary>
    Task<LoginResponse> ObterSessaoAsync(CancellationToken ct = default);

    Task AlterarSenhaAsync(AlterarSenhaRequest request, CancellationToken ct = default);
}
