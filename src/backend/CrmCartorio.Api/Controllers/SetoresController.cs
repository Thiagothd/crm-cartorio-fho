using CrmCartorio.Api.Modules.Configuracoes.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrmCartorio.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/setores")]
public class SetoresController(ISetorService servico) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SetorResponse>>> Listar(
        [FromQuery] bool incluirInativos = false,
        CancellationToken ct = default) =>
        Ok(await servico.ListarAsync(incluirInativos, ct));

    [HttpPost]
    public async Task<ActionResult<SetorResponse>> Criar(
        [FromBody] SalvarSetorRequest request,
        CancellationToken ct) =>
        Ok(await servico.CriarAsync(request, ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SetorResponse>> Atualizar(
        Guid id,
        [FromBody] SalvarSetorRequest request,
        CancellationToken ct) =>
        Ok(await servico.AtualizarAsync(id, request, ct));
}
