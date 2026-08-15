using CrmCartorio.Api.Modules.Configuracoes.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrmCartorio.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/mensagens-automaticas")]
public class MensagensAutomaticasController(IMensagemAutomaticaService servico) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MensagemAutomaticaResponse>>> Listar(
        [FromQuery] bool incluirInativas = false,
        CancellationToken ct = default) =>
        Ok(await servico.ListarAsync(incluirInativas, ct));

    [HttpPost]
    public async Task<ActionResult<MensagemAutomaticaResponse>> Criar(
        [FromBody] SalvarMensagemAutomaticaRequest request,
        CancellationToken ct) =>
        Ok(await servico.CriarAsync(request, ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<MensagemAutomaticaResponse>> Atualizar(
        Guid id,
        [FromBody] SalvarMensagemAutomaticaRequest request,
        CancellationToken ct) =>
        Ok(await servico.AtualizarAsync(id, request, ct));
}
