using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
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
    /// <summary>
    /// Achado ao investigar por que uma conta técnica aprovada continuava sem
    /// nenhuma tela para ler a conversa (2026-09-12): esta função devolvia
    /// SEMPRE <c>CatalogoDePermissoes.ChavesEfetivas(atendente.Permissoes,
    /// atendente.Perfil)</c> — o conjunto ESTÁTICO gravado no banco, nunca o
    /// conjunto efetivo que <see cref="ValidadorDeSessao"/> calcula por
    /// requisição (que acrescenta <c>AtendimentoVer</c>/<c>ClientesVer</c>
    /// quando a conta técnica tem autorização ativa). O backend sempre
    /// autorizava a leitura de verdade — cada `[Authorize]` já passa por
    /// `SessaoAtivaHandler` → `ValidadorDeSessao` a cada request — mas a
    /// LISTA de permissões devolvida ao FRONTEND (aqui, e só aqui) nunca
    /// refletia isso, nem no login inicial nem no refresh de sessão
    /// (`GET /api/autenticacao/sessao`, chamado ao voltar para a aba). Sem a
    /// chave `atendimento.ver` nessa lista, `pode('atendimento.ver')` no
    /// frontend nunca liberava o item de menu nem a rota `/atendimentos` —
    /// a conta técnica aprovada não tinha NENHUM caminho de tela até a
    /// conversa, mesmo com a API já pronta para atender. Corrigido: usa o
    /// mesmo `IValidadorDeSessao` que os guardas HTTP usam, então o que o
    /// menu mostra e o que a API aceita são sempre a mesma fonte.
    /// </summary>
    public static async Task<AtendenteResponse> MapearAtendenteAsync(
        CrmDbContext db, Atendente atendente, IValidadorDeSessao validador, CancellationToken ct)
    {
        var setor = atendente.SetorPadraoId is null
            ? null
            : await db.Setores.AsNoTracking()
                .Where(s => s.Id == atendente.SetorPadraoId)
                .Select(s => s.Nome)
                .FirstOrDefaultAsync(ct);

        var sessaoResolvida = await validador.CarregarSessaoAsync(
            atendente.Id, atendente.TenantId, atendente.TokenVersion, ct);

        var chaves = sessaoResolvida is not null
            ? CatalogoDePermissoes.ChavesDoConjunto(sessaoResolvida.Permissoes)
            : CatalogoDePermissoes.ChavesEfetivas(atendente.Permissoes, atendente.Perfil);

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
            chaves);
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
