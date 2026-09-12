using CrmCartorio.Api.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;

namespace CrmCartorio.Api.Infrastructure.Security;

/// <summary>
/// SEC-01: exigencia acrescentada a policy padrao de autorizacao (ver
/// Program.cs). Todo <c>[Authorize]</c> sem policy nomeada — todo controller
/// e o Hub do SignalR, nenhum deste projeto usa policy nomeada hoje — passa a
/// exigir tambem que a sessao ainda esteja ativa, nao so que o JWT seja
/// valido.
/// </summary>
public class SessaoAtivaRequirement : IAuthorizationRequirement;

/// <summary>
/// Le as claims ja validadas pelo middleware de autenticacao e confere no
/// banco, via <see cref="IValidadorDeSessao"/>, se ainda representam uma
/// sessao ativa. Falhar aqui produz Forbid — traduzido para 401 pelo
/// <c>OnForbidden</c> configurado em Program.cs, porque para quem chama a API
/// "token revogado" deve parecer exatamente "nao autenticado", nao um 403 de
/// permissao insuficiente (que este projeto nunca usa via policy do
/// framework — permissao fina e sempre checada dentro do service, com
/// <see cref="IContextoDoUsuario"/>).
/// </summary>
/// <remarks>
/// Ao suceder, deposita o conjunto de permissões já resolvido em
/// <see cref="PermissoesDaRequisicao"/> — é o único lugar do pipeline HTTP
/// que preenche esse holder, então <c>ContextoDoUsuario.Pode</c> nunca faz
/// uma segunda consulta ao banco.
/// </remarks>
public class SessaoAtivaHandler(IValidadorDeSessao validador, PermissoesDaRequisicao permissoes)
    : AuthorizationHandler<SessaoAtivaRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SessaoAtivaRequirement requirement)
    {
        var usuario = context.User;

        if (usuario.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var atendenteIdClaim = usuario.FindFirst(ClaimsDoAtendente.AtendenteId)?.Value;
        var tenantIdClaim = usuario.FindFirst(ClaimsDoAtendente.TenantId)?.Value;
        var tokenVersionClaim = usuario.FindFirst(ClaimsDoAtendente.TokenVersion)?.Value;

        if (!Guid.TryParse(atendenteIdClaim, out var atendenteId) ||
            !Guid.TryParse(tenantIdClaim, out var tenantId) ||
            !int.TryParse(tokenVersionClaim, out var tokenVersion))
        {
            // Token emitido antes desta claim existir, ou claim ausente/invalida
            // por qualquer outro motivo: trata como sessao nao reconhecida.
            return;
        }

        var sessao = await validador.CarregarSessaoAsync(atendenteId, tenantId, tokenVersion);

        if (sessao is not null)
        {
            permissoes.Definir(sessao.Permissoes);
            permissoes.DefinirAcessoTecnico(sessao.AutorizacaoDeAcessoTecnicoId, sessao.EscopoDeConversaId);
            context.Succeed(requirement);
        }
    }
}
