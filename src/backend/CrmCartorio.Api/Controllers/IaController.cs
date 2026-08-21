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
    [HttpGet("situacao")]
    public async Task<ActionResult<SituacaoDaIaResponse>> Situacao(CancellationToken ct) =>
        Ok(await situacao.ObterAsync(ct));
}
