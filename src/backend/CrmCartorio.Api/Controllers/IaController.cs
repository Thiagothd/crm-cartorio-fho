using CrmCartorio.Api.Modules.RespostaInteligente.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrmCartorio.Api.Controllers;

/// <summary>Situação da Resposta Inteligente para a tela Automação → IA.</summary>
[ApiController]
[Authorize]
[Route("api/ia")]
public class IaController(ISituacaoDaIa situacao) : ControllerBase
{
    /// <summary>
    /// <paramref name="de"/>/<paramref name="ate"/> omitidos caem no dia UTC
    /// corrente — mesmo recorte de sempre. Informados, ampliam só os agregados de
    /// período (motivos de fallback, economia); o estado dos provedores e a
    /// franquia do Gemini são sempre "agora".
    /// </summary>
    [HttpGet("situacao")]
    public async Task<ActionResult<SituacaoDaIaResponse>> Situacao(
        [FromQuery] DateTime? de, [FromQuery] DateTime? ate, CancellationToken ct) =>
        Ok(await situacao.ObterAsync(de, ate, ct));
}
