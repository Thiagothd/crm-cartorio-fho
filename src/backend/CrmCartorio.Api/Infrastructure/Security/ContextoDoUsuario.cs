using CrmCartorio.Api.Infrastructure.Identity;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;

namespace CrmCartorio.Api.Infrastructure.Security;

public class ContextoDoUsuario(IHttpContextAccessor acessor) : IContextoDoUsuario
{
    private ClaimsLidas Claims => _claims ??= Ler();
    private ClaimsLidas? _claims;

    public bool Autenticado => Claims.AtendenteId != Guid.Empty;

    public Guid TenantId => Claims.TenantId;

    public Guid AtendenteId => Claims.AtendenteId;

    public string Nome => Claims.Nome;

    public PerfilDeAtendente Perfil => Claims.Perfil;

    public bool PodeGerirCadastros =>
        Perfil is PerfilDeAtendente.Supervisor or PerfilDeAtendente.Administrador;

    public bool PodeGerirAtendentes =>
        Perfil is PerfilDeAtendente.Administrador;

    public bool PodeOperarConversaDeTerceiro =>
        Perfil is PerfilDeAtendente.Supervisor or PerfilDeAtendente.Administrador;

    private ClaimsLidas Ler()
    {
        var usuario = acessor.HttpContext?.User;
        if (usuario?.Identity?.IsAuthenticated != true)
        {
            return new ClaimsLidas(Guid.Empty, Guid.Empty, string.Empty, PerfilDeAtendente.Atendente);
        }

        Guid.TryParse(usuario.FindFirst(ClaimsDoAtendente.AtendenteId)?.Value, out var atendenteId);
        Guid.TryParse(usuario.FindFirst(ClaimsDoAtendente.TenantId)?.Value, out var tenantId);

        var nome = usuario.FindFirst(ClaimsDoAtendente.Nome)?.Value ?? string.Empty;

        if (!Enum.TryParse<PerfilDeAtendente>(
                usuario.FindFirst(ClaimsDoAtendente.Perfil)?.Value,
                out var perfil))
        {
            perfil = PerfilDeAtendente.Atendente;
        }

        return new ClaimsLidas(tenantId, atendenteId, nome, perfil);
    }

    private sealed record ClaimsLidas(Guid TenantId, Guid AtendenteId, string Nome, PerfilDeAtendente Perfil);
}
