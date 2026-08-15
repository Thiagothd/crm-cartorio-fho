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

    /// <summary>
    /// Nome da serventia para a tela de login. Anonimo por necessidade — e
    /// consumido antes de existir sessao — e por isso devolve somente o nome.
    /// </summary>
    [HttpGet("serventia")]
    [AllowAnonymous]
    public async Task<ActionResult<ServentiaPublicaResponse>> Serventia(CancellationToken ct) =>
        Ok(await servico.ObterServentiaPublicaAsync(ct));

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
