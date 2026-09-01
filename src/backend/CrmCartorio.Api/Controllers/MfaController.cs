using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Usuarios.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CrmCartorio.Api.Controllers;

/// <summary>
/// SEC-03 — gestao de MFA da PROPRIA conta (sessao JWT) e o fluxo de matricula
/// privilegiada por ticket (sem JWT — a conta ainda nao tem sessao). Reset e
/// emissao de ticket de TERCEIRO ficam em <see cref="AtendentesController"/>,
/// junto do resto da administracao de atendentes.
/// </summary>
[ApiController]
[Route("api/mfa")]
[EnableRateLimiting(PoliticaDeLimiteDeLogin.NomeGestao)]
public class MfaController(IMfaService servico, IContextoDoUsuario ctx) : ControllerBase
{
    private IdentidadeParaMfa Identidade => new(ctx.TenantId, ctx.AtendenteId);

    private string? Ip => ResolvedorDeIpDoCliente.Resolver(HttpContext);

    [HttpGet("situacao")]
    [Authorize]
    public async Task<ActionResult<MfaSituacaoResponse>> Situacao(CancellationToken ct) =>
        Ok(await servico.ObterSituacaoAsync(Identidade, ct));

    [HttpPost("iniciar")]
    [Authorize]
    public async Task<ActionResult<IniciarMfaResponse>> Iniciar(
        [FromBody] IniciarMfaRequest request, CancellationToken ct) =>
        Ok(await servico.IniciarAsync(Identidade, request, Ip, ct));

    [HttpPost("confirmar")]
    [Authorize]
    public async Task<ActionResult<ConfirmarMfaResponse>> Confirmar(
        [FromBody] ConfirmarMfaRequest request, CancellationToken ct) =>
        Ok(await servico.ConfirmarAsync(Identidade, request, Ip, ct));

    [HttpPost("desativar")]
    [Authorize]
    public async Task<IActionResult> Desativar([FromBody] DesativarMfaRequest request, CancellationToken ct)
    {
        await servico.DesativarAsync(Identidade, request, Ip, ct);
        return NoContent();
    }

    [HttpPost("codigos")]
    [Authorize]
    public async Task<ActionResult<RegenerarCodigosResponse>> RegenerarCodigos(
        [FromBody] RegenerarCodigosRequest request, CancellationToken ct) =>
        Ok(await servico.RegenerarCodigosAsync(Identidade, request, Ip, ct));

    // ------------------------------------------- matricula privilegiada (ticket)

    /// <summary>
    /// Anonimo por necessidade — a conta ainda nao tem MFA, entao nao tem
    /// sessao. As tres credenciais (e-mail + senha + ticket fora de banda)
    /// substituem o JWT que as outras rotas deste controller exigem.
    /// </summary>
    [HttpPost("matricula-privilegiada/sessao")]
    [AllowAnonymous]
    public async Task<ActionResult<DesafioMfaResponse>> IniciarMatriculaPrivilegiada(
        [FromBody] MatricularPrivilegiadoRequest request, CancellationToken ct) =>
        Ok(await servico.IniciarMatriculaPrivilegiadaAsync(request, Ip, ct));

    [HttpPost("matricula-privilegiada/iniciar-autenticador")]
    [AllowAnonymous]
    public async Task<ActionResult<IniciarMfaResponse>> IniciarAutenticadorViaDesafio(
        [FromBody] IniciarViaDesafioRequest request, CancellationToken ct) =>
        Ok(await servico.IniciarViaDesafioAsync(request, ct));

    [HttpPost("matricula-privilegiada/confirmar")]
    [AllowAnonymous]
    public async Task<ActionResult<ConfirmarMfaResponse>> ConfirmarAutenticadorViaDesafio(
        [FromBody] ConfirmarViaDesafioRequest request, CancellationToken ct) =>
        Ok(await servico.ConfirmarViaDesafioAsync(request, Ip, ct));

    // ------------------------------------------------------------- politica

    /// <summary>
    /// SEC-03 — liga a exigencia de MFA para Administrador/Titular. Step-up do
    /// proprio ator; aborta se houver alguem pendente. Ver
    /// IMfaService.AtivarObrigatoriedadeAsync.
    /// </summary>
    [HttpPost("politica/obrigatorio-para-administradores")]
    [Authorize]
    public async Task<ActionResult<LoginResponse>> AtivarObrigatoriedade(
        [FromBody] AtivarObrigatoriedadeRequest request, CancellationToken ct) =>
        Ok(await servico.AtivarObrigatoriedadeAsync(Identidade, request, Ip, ct));
}
