using CrmCartorio.Api.Modules.Configuracoes.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrmCartorio.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/etapas")]
public class EtapasController(IEtapaFunilService servico) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EtapaResponse>>> Listar(
        [FromQuery] bool incluirInativas = false,
        CancellationToken ct = default) =>
        Ok(await servico.ListarAsync(incluirInativas, ct));

    [HttpPost]
    public async Task<ActionResult<EtapaResponse>> Criar(
        [FromBody] SalvarEtapaRequest request,
        CancellationToken ct) =>
        Ok(await servico.CriarAsync(request, ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<EtapaResponse>> Atualizar(
        Guid id,
        [FromBody] SalvarEtapaRequest request,
        CancellationToken ct) =>
        Ok(await servico.AtualizarAsync(id, request, ct));

    [HttpPost("reordenar")]
    public async Task<ActionResult<IReadOnlyList<EtapaResponse>>> Reordenar(
        [FromBody] IReadOnlyList<Guid> idsNaOrdem,
        CancellationToken ct) =>
        Ok(await servico.ReordenarAsync(idsNaOrdem, ct));
}
