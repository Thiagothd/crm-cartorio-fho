using CrmCartorio.Api.Modules.Devedores.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrmCartorio.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/devedores")]
public class DevedoresController(IDevedorService servico) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ListagemDeDevedoresResponse>> Listar(
        [FromQuery] FiltroDeDevedores filtro,
        CancellationToken ct) =>
        Ok(await servico.ListarAsync(filtro, ct));

    [HttpGet("cliente/{clienteId:guid}")]
    public async Task<ActionResult<ResumoDeDividaDoClienteResponse>> ObterPorCliente(
        Guid clienteId,
        CancellationToken ct) =>
        Ok(await servico.ObterPorClienteAsync(clienteId, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PendenciaFinanceiraResponse>> Obter(
        Guid id,
        CancellationToken ct) =>
        Ok(await servico.ObterAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<PendenciaFinanceiraResponse>> Criar(
        [FromBody] CriarPendenciaFinanceiraRequest request,
        CancellationToken ct)
    {
        var criada = await servico.CriarAsync(request, ct);
        return CreatedAtAction(nameof(Obter), new { id = criada.Id }, criada);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PendenciaFinanceiraResponse>> Atualizar(
        Guid id,
        [FromBody] AtualizarPendenciaFinanceiraRequest request,
        CancellationToken ct) =>
        Ok(await servico.AtualizarAsync(id, request, ct));

    [HttpPost("{id:guid}/pagamentos")]
    public async Task<ActionResult<PendenciaFinanceiraResponse>> RegistrarPagamento(
        Guid id,
        [FromBody] RegistrarPagamentoRequest request,
        CancellationToken ct) =>
        Ok(await servico.RegistrarPagamentoAsync(id, request, ct));

    [HttpPost("{id:guid}/reabrir")]
    public async Task<ActionResult<PendenciaFinanceiraResponse>> Reabrir(
        Guid id,
        CancellationToken ct) =>
        Ok(await servico.ReabrirAsync(id, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Cancelar(Guid id, CancellationToken ct)
    {
        await servico.CancelarAsync(id, ct);
        return NoContent();
    }

    [HttpPost("cobrar-whatsapp")]
    public async Task<ActionResult<CobrarNoWhatsappResponse>> CobrarNoWhatsapp(
        [FromBody] CobrarNoWhatsappRequest request,
        CancellationToken ct) =>
        Ok(await servico.CobrarNoWhatsappAsync(request, ct));

    [HttpPost("cliente/{clienteId:guid}/abrir-conversa")]
    public async Task<ActionResult<AbrirConversaComDevedorResponse>> AbrirConversa(
        Guid clienteId,
        CancellationToken ct) =>
        Ok(await servico.AbrirConversaAsync(clienteId, ct));

    [HttpPost("importacao")]
    public async Task<ActionResult<ResultadoDeImportacaoDeDevedoresResponse>> ImportarEmLote(
        [FromBody] ImportarDevedoresEmLoteRequest request,
        CancellationToken ct) =>
        Ok(await servico.ImportarEmLoteAsync(request, ct));
}
