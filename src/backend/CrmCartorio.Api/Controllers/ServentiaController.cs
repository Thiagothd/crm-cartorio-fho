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
}
