namespace CrmCartorio.Api.Modules.Usuarios.Application;

public interface IAutenticacaoService
{
    /// <summary>
    /// Etapa 1. Devolve sessao completa quando MFA nao entra no caminho, ou um
    /// desafio quando entra — ver <see cref="LoginEtapa1Response.RequerMfa"/>.
    /// Nunca as duas coisas.
    /// </summary>
    Task<LoginEtapa1Response> AutenticarAsync(LoginRequest request, CancellationToken ct = default);

    /// <summary>Etapa 2 — conclui um login que abriu desafio de segundo fator.</summary>
    Task<LoginResponse> ConcluirLoginMfaAsync(ConcluirLoginMfaRequest request, CancellationToken ct = default);

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
