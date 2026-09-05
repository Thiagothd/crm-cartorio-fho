using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.CentralDeIa.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CrmCartorio.Api.Controllers;

/// <summary>
/// Área privada do desenvolvedor da equipe do projeto — fora do modelo Atendente/Tenant.
/// Ver Modules/CentralDeIa/README.md.
/// </summary>
[ApiController]
[Route("api/central-ia")]
public class CentralDeIaController(
    ICentralDeIaAutenticador autenticador,
    ISituacaoGlobalDaIa situacao) : ControllerBase
{
    /// <summary>
    /// Único endpoint anônimo do módulo. Confere o segredo compartilhado
    /// (<c>CentralDeIa:Chave</c>) em tempo fixo e, se bater, emite um JWT com a
    /// claim própria <c>central_ia_operador</c> — nunca as claims de Atendente.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting(PoliticaDeLimiteDeLogin.NomeCentralDeIa)]
    [HttpPost("entrar")]
    public async Task<ActionResult<EntrarNaCentralDeIaResponse>> Entrar(
        [FromBody] EntrarNaCentralDeIaRequest requisicao, CancellationToken ct) =>
        Ok(await autenticador.EntrarAsync(requisicao.Segredo, ct));

    /// <summary>
    /// <paramref name="de"/>/<paramref name="ate"/> omitidos caem no dia UTC
    /// corrente — mesmo recorte de <c>GET /api/ia/situacao</c>. Exige a policy
    /// <c>CentralDeIa</c> (claim <c>central_ia_operador=true</c>), nunca a
    /// policy padrão de Atendente.
    /// </summary>
    [Authorize(Policy = "CentralDeIa")]
    [HttpGet("situacao")]
    public async Task<ActionResult<SituacaoGlobalDaIaResponse>> Situacao(
        [FromQuery] DateTime? de, [FromQuery] DateTime? ate, CancellationToken ct) =>
        Ok(await situacao.ObterAsync(de, ate, ct));
}
