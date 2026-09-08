using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Usuarios.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CrmCartorio.Api.Controllers;

[ApiController]
[Route("api/autenticacao")]
public class AutenticacaoController(IAutenticacaoService servico) : ControllerBase
{
    /// <remarks>
    /// SEC-02: o limite por IP fica SO aqui — a rota anonima vizinha
    /// (<see cref="Serventia"/>) e o resto da API continuam sem limite. Ele e o
    /// amortecedor raso; a protecao que de fato importa e o backoff por conta,
    /// dentro de <c>AutenticacaoService.AutenticarAsync</c>.
    /// </remarks>
    /// <summary>
    /// SEC-03 — etapa 1. Devolve sessao completa quando a conta nao tem MFA no
    /// caminho, ou um desafio (<see cref="LoginEtapa1Response.Desafio"/>) quando
    /// tem — nunca os dois. Ver <see cref="LoginMfa"/> para a etapa 2.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(PoliticaDeLimiteDeLogin.Nome)]
    public async Task<ActionResult<LoginEtapa1Response>> Login(
        [FromBody] LoginRequest request,
        CancellationToken ct) =>
        Ok(await servico.AutenticarAsync(request, ct));

    /// <summary>
    /// SEC-03 — etapa 2. O desafio (nao um JWT) e a unica credencial aqui; a
    /// mesma politica de limite do login se aplica, com janela propria e maior
    /// (a defesa real contra forca bruta de codigo e o bloqueio por CONTA, nao
    /// o limite por IP).
    /// </summary>
    [HttpPost("login/mfa")]
    [AllowAnonymous]
    [EnableRateLimiting(PoliticaDeLimiteDeLogin.NomeMfa)]
    public async Task<ActionResult<LoginResponse>> LoginMfa(
        [FromBody] ConcluirLoginMfaRequest request,
        CancellationToken ct) =>
        Ok(await servico.ConcluirLoginMfaAsync(request, ct));

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
