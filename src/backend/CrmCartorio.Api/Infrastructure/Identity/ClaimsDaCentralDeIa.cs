namespace CrmCartorio.Api.Infrastructure.Identity;

/// <summary>
/// Claim própria do token da Central de IA. Deliberadamente a ÚNICA claim de
/// aplicação que este token carrega — nunca <c>atendente_id</c>, nunca
/// <c>tenant_id</c>, nunca <c>perfil</c> (ver <see cref="ClaimsDoAtendente"/>).
/// É essa ausência que torna o token estruturalmente incapaz de servir
/// qualquer endpoint comum do sistema: <c>SessaoAtivaHandler</c> e
/// <c>ContextoDoUsuario</c> exigem claims que ele nunca vai ter. Ver
/// Modules/CentralDeIa/README.md.
/// </summary>
public static class ClaimsDaCentralDeIa
{
    public const string Operador = "central_ia_operador";
}
