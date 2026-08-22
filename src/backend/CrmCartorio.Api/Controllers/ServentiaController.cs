using CrmCartorio.Api.Modules.Tenants.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrmCartorio.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/serventia")]
public class ServentiaController(ITenantService servico) : ControllerBase
{
    /// <summary>
    /// Leitura aberta a qualquer atendente autenticado: e o fio de mensagens que
    /// consome isto para desenhar o fundo. Somente a ESCRITA exige perfil de gestao.
    /// </summary>
    [HttpGet("aparencia")]
    public async Task<ActionResult<AparenciaResponse>> Aparencia(CancellationToken ct) =>
        Ok(await servico.ObterAparenciaAsync(ct));

    [HttpPut("aparencia")]
    public async Task<ActionResult<AparenciaResponse>> DefinirAparencia(
        [FromBody] SalvarAparenciaRequest request,
        CancellationToken ct) =>
        Ok(await servico.DefinirAparenciaAsync(request, ct));

    /// <summary>
    /// Tempo de silencio ate perguntar e ate encerrar a conversa parada.
    /// </summary>
    /// <remarks>
    /// Diferente da aparencia, a LEITURA tambem exige perfil de gestao: isto nao
    /// alimenta nenhuma tela de atendimento, so a de configuracao.
    /// </remarks>
    [HttpGet("encerramento-automatico")]
    public async Task<ActionResult<EncerramentoAutomaticoResponse>> EncerramentoAutomatico(
        CancellationToken ct) =>
        Ok(await servico.ObterEncerramentoAutomaticoAsync(ct));

    [HttpPut("encerramento-automatico")]
    public async Task<ActionResult<EncerramentoAutomaticoResponse>> DefinirEncerramentoAutomatico(
        [FromBody] SalvarEncerramentoAutomaticoRequest request,
        CancellationToken ct) =>
        Ok(await servico.DefinirEncerramentoAutomaticoAsync(request, ct));
}
