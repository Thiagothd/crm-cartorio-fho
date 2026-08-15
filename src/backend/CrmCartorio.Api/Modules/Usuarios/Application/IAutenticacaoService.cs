namespace CrmCartorio.Api.Modules.Usuarios.Application;

public interface IAutenticacaoService
{
    Task<LoginResponse> AutenticarAsync(LoginRequest request, CancellationToken ct = default);

    /// <summary>Dados do atendente autenticado na requisicao atual.</summary>
    Task<LoginResponse> ObterSessaoAsync(CancellationToken ct = default);

    /// <summary>
    /// Nome da serventia para a tela de login, sem exigir autenticacao.
    /// </summary>
    /// <remarks>
    /// FASE 3: com mais de um cartorio na base, "a" serventia deixa de existir e
    /// este metodo precisa receber de qual se trata — subdominio ou codigo na URL.
    /// A mesma resolucao que AutenticarAsync vai precisar.
    /// </remarks>
    Task<ServentiaPublicaResponse> ObterServentiaPublicaAsync(CancellationToken ct = default);

    Task AlterarSenhaAsync(AlterarSenhaRequest request, CancellationToken ct = default);
}
