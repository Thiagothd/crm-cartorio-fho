using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Usuarios.Application;

/// <summary>SEC-03: quem esta sob a exigencia de MFA — compartilhado entre login e gestao.</summary>
public static class PoliticaDeMfa
{
    /// <summary>
    /// So Titular ou Administrador podem estar sob a politica; qualquer outro
    /// perfil/cargo nunca exige nada — EXCETO conta tecnica da equipe do projeto
    /// (blindagem LGPD), que exige MFA sempre, independente do flag do
    /// tenant: e a conta que pode ganhar leitura de dado de cliente via
    /// acesso tecnico autorizado, e essa aprovacao depende de segundo fator
    /// (ver Modules/Seguranca, IMfaService.ConferirSegundoFatorAsync).
    /// </summary>
    public static async Task<bool> ExigeAsync(
        CrmDbContext db, Guid tenantId, PerfilDeAtendente perfil, CargoNaServentia cargo, CancellationToken ct,
        bool ehContaTecnica = false)
    {
        if (ehContaTecnica)
        {
            return true;
        }

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
