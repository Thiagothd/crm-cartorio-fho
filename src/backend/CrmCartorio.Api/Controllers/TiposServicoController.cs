using CrmCartorio.Api.Modules.Servicos.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrmCartorio.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/tipos-servico")]
public class TiposServicoController(ITipoServicoService servico) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TipoServicoResponse>>> Listar(
        [FromQuery] bool incluirInativos = false,
        CancellationToken ct = default) =>
        Ok(await servico.ListarAsync(incluirInativos, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TipoServicoResponse>> Obter(Guid id, CancellationToken ct) =>
        Ok(await servico.ObterAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<TipoServicoResponse>> Criar(
        [FromBody] SalvarTipoServicoRequest request,
        CancellationToken ct)
    {
        var criado = await servico.CriarAsync(request, ct);
        return CreatedAtAction(nameof(Obter), new { id = criado.Id }, criado);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TipoServicoResponse>> Atualizar(
        Guid id,
        [FromBody] SalvarTipoServicoRequest request,
        CancellationToken ct) =>
        Ok(await servico.AtualizarAsync(id, request, ct));
}
