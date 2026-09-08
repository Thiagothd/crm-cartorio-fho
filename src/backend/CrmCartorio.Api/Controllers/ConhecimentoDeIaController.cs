using CrmCartorio.Api.Modules.RespostaInteligente.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrmCartorio.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/conhecimento-de-ia")]
public class ConhecimentoDeIaController(IConhecimentoDeIaService servico) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ConhecimentoDeIaResponse>>> Listar(
        [FromQuery] bool incluirInativos = false,
        CancellationToken ct = default) =>
        Ok(await servico.ListarAsync(incluirInativos, ct));

    [HttpPost]
    public async Task<ActionResult<ConhecimentoDeIaResponse>> Criar(
        [FromBody] SalvarConhecimentoDeIaRequest request,
        CancellationToken ct) =>
        Ok(await servico.CriarAsync(request, ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ConhecimentoDeIaResponse>> Atualizar(
        Guid id,
        [FromBody] SalvarConhecimentoDeIaRequest request,
        CancellationToken ct) =>
        Ok(await servico.AtualizarAsync(id, request, ct));

    [HttpGet("{id:guid}/historico")]
    public async Task<ActionResult<IReadOnlyList<VersaoDeConhecimentoDeIaResponse>>> Historico(
        Guid id,
        CancellationToken ct) =>
        Ok(await servico.HistoricoAsync(id, ct));
}
