using CrmCartorio.Api.Modules.Clientes.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrmCartorio.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/clientes")]
public class ClientesController(IClienteService servico, IImportacaoDeContatosService importacao) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ResultadoPaginadoDeClientes>> Listar(
        [FromQuery] FiltroDeClientes filtro,
        CancellationToken ct) =>
        Ok(await servico.ListarAsync(filtro, ct));

    /// <summary>Cidades ja cadastradas, para montar o filtro da tela.</summary>
    [HttpGet("opcoes")]
    public async Task<ActionResult<OpcoesDeClientesResponse>> Opcoes(CancellationToken ct) =>
        Ok(await servico.ObterOpcoesAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ClienteResponse>> Obter(Guid id, CancellationToken ct) =>
        Ok(await servico.ObterAsync(id, ct));

    /// <summary>
    /// Devolve os bytes da foto de perfil real do WhatsApp do cliente.
    /// Retorna 404 se o cliente nao possui foto, permitindo fallback imediato para as iniciais.
    /// </summary>
    [HttpGet("{id:guid}/foto-perfil")]
    public async Task<IActionResult> ObterFotoPerfil(Guid id, CancellationToken ct)
    {
        var bytes = await servico.ObterFotoPerfilAsync(id, ct);
        if (bytes is null || bytes.Length == 0)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, max-age=3600";
        Response.Headers.XContentTypeOptions = "nosniff";

        return File(bytes, "image/jpeg");
    }

    [HttpPost]
    public async Task<ActionResult<ClienteResponse>> Criar(
        [FromBody] DadosDeCliente dados,
        CancellationToken ct)
    {
        var criado = await servico.CriarAsync(dados, ct);
        return CreatedAtAction(nameof(Obter), new { id = criado.Id }, criado);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ClienteResponse>> Atualizar(
        Guid id,
        [FromBody] DadosDeCliente dados,
        CancellationToken ct) =>
        Ok(await servico.AtualizarAsync(id, dados, ct));

    /// <summary>Exclusão física — recusa se o cliente tiver atendimento vinculado.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken ct)
    {
        await servico.ExcluirAsync(id, ct);
        return NoContent();
    }

    /// <summary>Classifica cada linha da lista sem gravar nada — a prévia que o operador revisa.</summary>
    [HttpPost("importacao/analisar")]
    public async Task<ActionResult<AnalisarImportacaoResponse>> AnalisarImportacao(
        [FromBody] AnalisarImportacaoRequest request,
        CancellationToken ct) =>
        Ok(await importacao.AnalisarAsync(request, ct));

    /// <summary>Grava só as linhas que o operador aprovou na prévia.</summary>
    [HttpPost("importacao/confirmar")]
    public async Task<ActionResult<ConfirmarImportacaoResponse>> ConfirmarImportacao(
        [FromBody] ConfirmarImportacaoRequest request,
        CancellationToken ct) =>
        Ok(await importacao.ConfirmarAsync(request, ct));
}
