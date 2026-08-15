using CrmCartorio.Api.Modules.Usuarios.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrmCartorio.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/atendentes")]
public class AtendentesController(IAtendenteService servico) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AtendenteResponse>>> Listar(
        [FromQuery] bool incluirInativos = false,
        CancellationToken ct = default) =>
        Ok(await servico.ListarAsync(incluirInativos, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AtendenteResponse>> Obter(Guid id, CancellationToken ct) =>
        Ok(await servico.ObterAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<AtendenteResponse>> Criar(
        [FromBody] CriarAtendenteRequest request,
        CancellationToken ct)
    {
        var criado = await servico.CriarAsync(request, ct);
        return CreatedAtAction(nameof(Obter), new { id = criado.Id }, criado);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AtendenteResponse>> Atualizar(
        Guid id,
        [FromBody] AtualizarAtendenteRequest request,
        CancellationToken ct) =>
        Ok(await servico.AtualizarAsync(id, request, ct));

    [HttpPost("{id:guid}/senha")]
    public async Task<IActionResult> RedefinirSenha(
        Guid id,
        [FromBody] RedefinirSenhaRequest request,
        CancellationToken ct)
    {
        await servico.RedefinirSenhaAsync(id, request, ct);
        return NoContent();
    }
}
