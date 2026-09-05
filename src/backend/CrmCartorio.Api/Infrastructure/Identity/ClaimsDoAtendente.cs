namespace CrmCartorio.Api.Infrastructure.Identity;

/// <summary>
/// Nomes das claims proprias. Centralizados para nao virarem string solta
/// espalhada entre gerador de token, contexto do usuario e hub do SignalR.
/// </summary>
public static class ClaimsDoAtendente
{
    public const string AtendenteId = "atendente_id";
    public const string TenantId = "tenant_id";
    public const string Nome = "nome";
    public const string Perfil = "perfil";

    /// <summary>
    /// SEC-01: versao de TokenVersion (Modules/Usuarios/Domain/Entities/Atendente.cs)
    /// no instante da emissao.
    /// </summary>
    public const string TokenVersion = "token_version";
}
