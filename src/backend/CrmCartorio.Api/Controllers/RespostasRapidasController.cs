using CrmCartorio.Api.Modules.Configuracoes.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrmCartorio.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/respostas-rapidas")]
public class RespostasRapidasController(IRespostaRapidaService servico) : ControllerBase
{
    /// <summary>
    /// Listagem aberta a qualquer atendente autenticado: e o seletor "/" da caixa de
    /// resposta que consome isto. Somente a ESCRITA exige perfil de gestao.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RespostaRapidaResponse>>> Listar(
        [FromQuery] bool incluirInativas = false,
        CancellationToken ct = default) =>
        Ok(await servico.ListarAsync(incluirInativas, ct));

    [HttpPost]
    public async Task<ActionResult<RespostaRapidaResponse>> Criar(
        [FromBody] SalvarRespostaRapidaRequest request,
        CancellationToken ct) =>
        Ok(await servico.CriarAsync(request, ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<RespostaRapidaResponse>> Atualizar(
        Guid id,
        [FromBody] SalvarRespostaRapidaRequest request,
        CancellationToken ct) =>
        Ok(await servico.AtualizarAsync(id, request, ct));
}
