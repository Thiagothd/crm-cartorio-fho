using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Usuarios.Application;

/// <summary>SEC-03: quem esta sob a exigencia de MFA — compartilhado entre login e gestao.</summary>
public static class PoliticaDeMfa
{
    /// <summary>So Titular ou Administrador podem estar sob a politica; qualquer outro perfil/cargo nunca exige nada.</summary>
    public static async Task<bool> ExigeAsync(
        CrmDbContext db, Guid tenantId, PerfilDeAtendente perfil, CargoNaServentia cargo, CancellationToken ct)
    {
        if (perfil != PerfilDeAtendente.Administrador && cargo != CargoNaServentia.Titular)
        {
            return false;
        }

        return await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.MfaObrigatorioParaAdministradores)
            .FirstOrDefaultAsync(ct);
    }
}
