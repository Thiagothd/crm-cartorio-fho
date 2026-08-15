using CrmCartorio.Api.Infrastructure.Identity;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Usuarios.Application;

public class AutenticacaoService(
    CrmDbContext db,
    IHashDeSenha hash,
    IGeradorDeToken tokens,
    IContextoDoUsuario ctx,
    ILogger<AutenticacaoService> log) : IAutenticacaoService
{
    public async Task<LoginResponse> AutenticarAsync(LoginRequest request, CancellationToken ct = default)
    {
        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;

        // FASE 3: o e-mail e unico por tenant, nao globalmente. Com mais de um
        // cartorio na base, dois atendentes podem ter o mesmo e-mail e esta busca
        // ficaria ambigua. Antes de ativar o multi-tenant real, o tenant precisa
        // ser resolvido aqui (subdominio, codigo do cartorio na tela de login ou
        // indice unico global de e-mail).
        var atendente = await db.Atendentes
            .FirstOrDefaultAsync(a => a.Email == email, ct);

        // Verifica a senha mesmo com atendente inexistente ou inativo: sair antes
        // faria o tempo de resposta revelar quais e-mails existem na base.
        var hashParaConferir = atendente?.SenhaHash ?? HashDescartavel;
        var senhaConfere = hash.Verificar(request.Senha ?? string.Empty, hashParaConferir);

        if (atendente is null || !atendente.Ativo || !senhaConfere)
        {
            log.LogWarning("Tentativa de login rejeitada para {Email}", email);
            throw UsuariosServiceException.CredenciaisInvalidas();
        }

        atendente.UltimoAcessoEm = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var (token, expiraEm) = tokens.Gerar(atendente);

        log.LogInformation("Login de {AtendenteId} ({Nome})", atendente.Id, atendente.Nome);

        return new LoginResponse(
            token,
            expiraEm,
            await MapearAsync(atendente, ct),
            await ObterTenantAsync(atendente.TenantId, ct));
    }

    public async Task<LoginResponse> ObterSessaoAsync(CancellationToken ct = default)
    {
        var atendente = await db.Atendentes.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == ctx.AtendenteId && a.TenantId == ctx.TenantId, ct)
            ?? throw UsuariosServiceException.AtendenteNaoEncontrado();

        if (!atendente.Ativo)
        {
            throw UsuariosServiceException.CredenciaisInvalidas();
        }

        // Sessao nao emite token novo: o cliente ja tem o dele e a validade nao muda.
        return new LoginResponse(
            string.Empty,
            DateTime.MinValue,
            await MapearAsync(atendente, ct),
            await ObterTenantAsync(atendente.TenantId, ct));
    }

    public async Task AlterarSenhaAsync(AlterarSenhaRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.NovaSenha) || request.NovaSenha.Length < 8)
        {
            throw UsuariosServiceException.SenhaFraca();
        }

        var atendente = await db.Atendentes
            .FirstOrDefaultAsync(a => a.Id == ctx.AtendenteId && a.TenantId == ctx.TenantId, ct)
            ?? throw UsuariosServiceException.AtendenteNaoEncontrado();

        if (!hash.Verificar(request.SenhaAtual ?? string.Empty, atendente.SenhaHash))
        {
            throw UsuariosServiceException.SenhaAtualIncorreta();
        }

        atendente.SenhaHash = hash.Gerar(request.NovaSenha);

        await db.SaveChangesAsync(ct);

        log.LogInformation("Senha alterada por {AtendenteId}", atendente.Id);
    }

    private async Task<AtendenteResponse> MapearAsync(Atendente atendente, CancellationToken ct)
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
            atendente.UltimoAcessoEm);
    }

    private async Task<TenantResponse> ObterTenantAsync(Guid tenantId, CancellationToken ct)
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

    /// <summary>
    /// Hash valido de uma senha aleatoria, usado so para gastar o mesmo tempo de
    /// CPU quando o e-mail nao existe. Nunca confere com senha alguma.
    /// </summary>
    private const string HashDescartavel =
        "pbkdf2.sha256$210000$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
}
