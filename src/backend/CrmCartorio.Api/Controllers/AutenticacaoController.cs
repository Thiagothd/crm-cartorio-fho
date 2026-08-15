using CrmCartorio.Api.Modules.Usuarios.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrmCartorio.Api.Controllers;

[ApiController]
[Route("api/autenticacao")]
public class AutenticacaoController(IAutenticacaoService servico) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken ct) =>
        Ok(await servico.AutenticarAsync(request, ct));

    [HttpGet("sessao")]
    [Authorize]
    public async Task<ActionResult<LoginResponse>> Sessao(CancellationToken ct) =>
        Ok(await servico.ObterSessaoAsync(ct));

    [HttpPost("senha")]
    [Authorize]
    public async Task<IActionResult> AlterarSenha(
        [FromBody] AlterarSenhaRequest request,
        CancellationToken ct)
    {
        await servico.AlterarSenhaAsync(request, ct);
        return NoContent();
    }
}
