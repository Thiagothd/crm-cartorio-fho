using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Usuarios.Domain;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Usuarios.Application;

/// <summary>
/// Monta <see cref="AtendenteResponse"/> e <see cref="TenantResponse"/> —
/// compartilhado entre <see cref="AutenticacaoService"/> e <see cref="MfaService"/>
/// porque os dois emitem sessao completa (login sem MFA, e a conclusao da etapa
/// de MFA). Duplicar o de-para nos dois servicos os faria divergir no primeiro
/// ajuste de campo — mesmo raciocinio de <see cref="Application.AtendenteService.DescreverCargo"/>.
/// </summary>
public static class MapeadorDeSessao
{
    public static async Task<AtendenteResponse> MapearAtendenteAsync(
        CrmDbContext db, Atendente atendente, CancellationToken ct)
    {
        var setor = atendente.SetorPadraoId is null
            ? null
            : await db.Setores.AsNoTracking()
                .Where(s => s.Id == atendente.SetorPadraoId)
                .Select(s => s.Nome)
                .FirstOrDefaultAsync(ct);

        return new AtendenteResponse(
            atendente.Id,
            atendente.Nome,
            atendente.Email,
            atendente.Perfil,
            atendente.Cargo,
            AtendenteService.DescreverCargo(atendente.Cargo),
            atendente.SetorPadraoId,
            setor,
            atendente.Ativo,
            atendente.UltimoAcessoEm,
            atendente.MfaAtivo,
            atendente.ModeloDePermissao,
            CatalogoDePermissoes.ChavesEfetivas(atendente.Permissoes, atendente.Perfil));
    }

    public static async Task<TenantResponse> ObterTenantAsync(CrmDbContext db, Guid tenantId, CancellationToken ct)
    {
        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw UsuariosServiceException.TenantNaoEncontrado();

        return new TenantResponse(
            tenant.Id,
            tenant.RazaoSocial,
            tenant.NomeFantasia,
            tenant.Cnpj,
            tenant.WhatsappNumeroExibicao,
            !string.IsNullOrWhiteSpace(tenant.WhatsappPhoneNumberId));
    }
}
