using CrmCartorio.Api.Modules.Indicadores.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrmCartorio.Api.Controllers;

/// <summary>
/// Visão Geral, Indicadores e Relatórios, e alertas do sino — tudo consulta,
/// nenhuma escrita. A permissão de cada campo sensível é resolvida dentro do
/// service, não aqui: ver <see cref="IndicadoresService"/>.
/// </summary>
[ApiController]
[Authorize]
[Route("api/indicadores")]
public class IndicadoresController(IIndicadoresService servico) : ControllerBase
{
    [HttpGet("visao-geral")]
    public async Task<ActionResult<VisaoGeralResponse>> VisaoGeral(
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        [FromQuery] string? escopo,
        CancellationToken ct) =>
        Ok(await servico.ObterVisaoGeralAsync(new FiltroDeIndicadores(de, ate, escopo), ct));

    /// <summary>Restrito a Supervisor/Administrador — conferido dentro do service.</summary>
    [HttpGet("relatorio")]
    public async Task<ActionResult<RelatorioResponse>> Relatorio(
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        CancellationToken ct) =>
        Ok(await servico.ObterRelatorioAsync(new FiltroDeIndicadores(de, ate, null), ct));

    [HttpGet("alertas")]
    public async Task<ActionResult<IReadOnlyList<AlertaOperacionalResponse>>> Alertas(CancellationToken ct) =>
        Ok(await servico.ObterAlertasAsync(ct));

    // ------------------------------- Abas da tela de Indicadores e Relatórios
    // Todas restritas a Supervisor/Administrador, conferido dentro do service.

    /// <summary>Aba Dashboard. Sem filtro: as janelas do painel são fixas por definição.</summary>
    [HttpGet("painel")]
    public async Task<ActionResult<PainelResponse>> Painel(CancellationToken ct) =>
        Ok(await servico.ObterPainelAsync(ct));

    [HttpGet("atendimentos")]
    public async Task<ActionResult<RelatorioDeAtendimentosResponse>> Atendimentos(
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        CancellationToken ct) =>
        Ok(await servico.ObterRelatorioDeAtendimentosAsync(new FiltroDeIndicadores(de, ate, null), ct));

    [HttpGet("atendentes")]
    public async Task<ActionResult<RelatorioDeAtendentesResponse>> Atendentes(
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        CancellationToken ct) =>
        Ok(await servico.ObterRelatorioDeAtendentesAsync(new FiltroDeIndicadores(de, ate, null), ct));

    [HttpGet("clientes")]
    public async Task<ActionResult<RelatorioDeClientesResponse>> Clientes(
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        CancellationToken ct) =>
        Ok(await servico.ObterRelatorioDeClientesAsync(new FiltroDeIndicadores(de, ate, null), ct));
}
