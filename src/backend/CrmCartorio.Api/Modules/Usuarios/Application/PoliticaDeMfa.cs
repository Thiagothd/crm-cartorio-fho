using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Usuarios.Application;

/// <summary>SEC-03: quem esta sob a exigencia de MFA — compartilhado entre login e gestao.</summary>
public static class PoliticaDeMfa
{
    /// <summary>
    /// So Titular, Administrador ou conta de suporte tecnico (blindagem LGPD)
    /// podem estar sob a politica; qualquer outro perfil/cargo nunca exige
    /// nada. A conta de suporte tecnico entra na MESMA fronteira do
    /// administrador — sujeita a <c>Tenant.MfaObrigatorioParaAdministradores</c>,
    /// nao a uma exigencia propria: e o cartorio quem decide ligar MFA
    /// obrigatorio, e quando liga, vale para quem pode ganhar leitura de dado
    /// de cliente (administrador direto, suporte via acesso tecnico
    /// autorizado). A primeira versao exigia MFA da conta tecnica
    /// incondicionalmente e deixava a conta impossivel de usar num cartorio
    /// sem nenhum administrador com autenticador matriculado para emitir o
    /// ticket dela — o portao real de leitura ja e o segundo fator de quem
    /// APROVA o acesso (IMfaService.ConferirSegundoFatorAsync), nao o login
    /// da conta que pede.
    /// </summary>
    public static async Task<bool> ExigeAsync(
        CrmDbContext db, Guid tenantId, PerfilDeAtendente perfil, CargoNaServentia cargo, CancellationToken ct,
        bool ehContaTecnica = false)
    {
        if (perfil != PerfilDeAtendente.Administrador && cargo != CargoNaServentia.Titular && !ehContaTecnica)
        {
            return false;
        }

        return await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.MfaObrigatorioParaAdministradores)
            .FirstOrDefaultAsync(ct);
    }
}
