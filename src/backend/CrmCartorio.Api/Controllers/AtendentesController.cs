using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Usuarios.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CrmCartorio.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/atendentes")]
public class AtendentesController(IAtendenteService servico, IMfaService mfa, IContextoDoUsuario ctx) : ControllerBase
{
    private IdentidadeParaMfa AtorIdentidade => new(ctx.TenantId, ctx.AtendenteId);

    private string? Ip => ResolvedorDeIpDoCliente.Resolver(HttpContext);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AtendenteResponse>>> Listar(
        [FromQuery] bool incluirInativos = false,
        CancellationToken ct = default) =>
        Ok(await servico.ListarAsync(incluirInativos, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AtendenteResponse>> Obter(Guid id, CancellationToken ct) =>
        Ok(await servico.ObterAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<AtendenteResponse>> Criar(
        [FromBody] CriarAtendenteRequest request,
        CancellationToken ct)
    {
        var criado = await servico.CriarAsync(request, ct);
        return CreatedAtAction(nameof(Obter), new { id = criado.Id }, criado);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AtendenteResponse>> Atualizar(
        Guid id,
        [FromBody] AtualizarAtendenteRequest request,
        CancellationToken ct) =>
        Ok(await servico.AtualizarAsync(id, request, ct));

    [HttpPost("{id:guid}/senha")]
    public async Task<IActionResult> RedefinirSenha(
        Guid id,
        [FromBody] RedefinirSenhaRequest request,
        CancellationToken ct)
    {
        await servico.RedefinirSenhaAsync(id, request, ct);
        return NoContent();
    }

    // ------------------------------------------------------- SEC-03 (MFA)

    /// <summary>
    /// Emite ticket de matricula privilegiada fora de banda para outro
    /// Administrador/Titular. Step-up do proprio ator; alvo Titular exige ator
    /// que tambem seja Titular.
    /// </summary>
    [HttpPost("{id:guid}/mfa/ticket")]
    [EnableRateLimiting(PoliticaDeLimiteDeLogin.NomeGestao)]
    public async Task<ActionResult<TicketResponse>> EmitirTicketDeMfa(
        Guid id, [FromBody] EmitirTicketRequest request, CancellationToken ct) =>
        Ok(await mfa.EmitirTicketAsync(AtorIdentidade, id, request, Ip, ct));

    /// <summary>
    /// Reset administrativo: remove o MFA do alvo, apaga recovery codes,
    /// invalida desafios pendentes, sobe TokenVersion — a conta fica sem login
    /// ate nova matricula via ticket (se a politica exigir MFA para ela).
    /// </summary>
    [HttpPost("{id:guid}/mfa/reset")]
    [EnableRateLimiting(PoliticaDeLimiteDeLogin.NomeGestao)]
    public async Task<IActionResult> ResetarMfa(
        Guid id, [FromBody] ResetarMfaRequest request, CancellationToken ct)
    {
        await mfa.ResetarAsync(AtorIdentidade, id, request, Ip, ct);
        return NoContent();
    }
}
