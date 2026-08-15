using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Tenants.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Tenants.Application;

public class TenantService(CrmDbContext db, IContextoDoUsuario ctx) : ITenantService
{
    public async Task<AparenciaResponse> ObterAparenciaAsync(CancellationToken ct = default)
    {
        var aparencia = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == ctx.TenantId)
            .Select(t => t.AparenciaDaConversa)
            .FirstOrDefaultAsync(ct);

        return new AparenciaResponse(
            aparencia == default ? AparenciaDaConversa.PapelLiso : aparencia);
    }

    public async Task<AparenciaResponse> DefinirAparenciaAsync(
        SalvarAparenciaRequest request,
        CancellationToken ct = default)
    {
        // Mesma exigencia dos demais cadastros da tela de Configuracoes: e a cara
        // do cartorio para todo mundo, nao preferencia de quem esta logado.
        if (!ctx.PodeGerirCadastros)
        {
            throw new TenantsServiceException(
                "sem_permissao", "Seu perfil nao permite alterar a aparencia.", 403);
        }

        if (!Enum.IsDefined(request.AparenciaDaConversa))
        {
            throw new TenantsServiceException(
                "aparencia_invalida", "Aparencia desconhecida.", 400);
        }

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == ctx.TenantId, ct)
            ?? throw new TenantsServiceException(
                "tenant_nao_encontrado", "Serventia nao encontrada.", 404);

        tenant.AparenciaDaConversa = request.AparenciaDaConversa;

        await db.SaveChangesAsync(ct);

        return new AparenciaResponse(tenant.AparenciaDaConversa);
    }
}

public class TenantsServiceException(string codigo, string mensagem, int statusHttp = 400)
    : ExcecaoDeAplicacao(codigo, mensagem, statusHttp);
