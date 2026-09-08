using CrmCartorio.Api.Modules.RespostaInteligente.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrmCartorio.Api.Controllers;

/// <summary>Situação da Resposta Inteligente — <c>/situacao</c> é a leitura técnica; <c>/analise</c> é a tela Automação → IA do gestor.</summary>
[ApiController]
[Authorize]
[Route("api/ia")]
public class IaController(ISituacaoDaIa situacao, IAnaliseDaIa analise) : ControllerBase
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

    /// <summary>
    /// A nova tela do dono/gestor do cartório: valor entregue, utilização,
    /// sobre o que perguntam, lacunas de catálogo e insights — zero jargão de
    /// provedor/modelo, zero valor em dinheiro (isso é só da Central de IA).
    /// <paramref name="de"/>/<paramref name="ate"/> omitidos caem nos últimos 7 dias.
    /// </summary>
    [HttpGet("analise")]
    public async Task<ActionResult<AnaliseDaIaResponse>> Analise(
        [FromQuery] DateTime? de, [FromQuery] DateTime? ate, CancellationToken ct) =>
        Ok(await analise.ObterAsync(de, ate, ct));
}
