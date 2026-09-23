using CrmCartorio.Api.Modules.Atendimento.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrmCartorio.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/conversas")]
public class ConversasController(
    IConversaService conversas,
    IMensagemService mensagens,
    IAcoesDeMensagem acoes) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ResultadoPaginado<ConversaResumoResponse>>> Listar(
        [FromQuery] FiltroDeConversas filtro,
        CancellationToken ct) =>
        Ok(await conversas.ListarAsync(filtro, ct));

    [HttpGet("resumo")]
    public async Task<ActionResult<PainelResumoResponse>> Resumo(CancellationToken ct) =>
        Ok(await conversas.ResumoDoPainelAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ConversaDetalheResponse>> Obter(Guid id, CancellationToken ct) =>
        Ok(await conversas.ObterAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<ConversaResumoResponse>> Criar(
        [FromBody] CriarConversaRequest request,
        CancellationToken ct)
    {
        var criada = await conversas.CriarAsync(request, ct);
        return CreatedAtAction(nameof(Obter), new { id = criada.Id }, criada);
    }

    /// <summary>
    /// Assume a conversa. Devolve 409 com o nome de quem assumiu primeiro quando
    /// dois atendentes clicam quase ao mesmo tempo.
    /// </summary>
    [HttpPost("{id:guid}/assumir")]
    public async Task<ActionResult<ConversaResumoResponse>> Assumir(Guid id, CancellationToken ct) =>
        Ok(await conversas.AssumirAsync(id, ct));

    [HttpPost("{id:guid}/liberar")]
    public async Task<ActionResult<ConversaResumoResponse>> Liberar(Guid id, CancellationToken ct) =>
        Ok(await conversas.LiberarAsync(id, ct));

    [HttpPost("{id:guid}/transferir")]
    public async Task<ActionResult<ConversaResumoResponse>> Transferir(
        Guid id,
        [FromBody] TransferirConversaRequest request,
        CancellationToken ct) =>
        Ok(await conversas.TransferirAsync(id, request, ct));

    [HttpPost("{id:guid}/mover")]
    public async Task<ActionResult<ConversaResumoResponse>> Mover(
        Guid id,
        [FromBody] MoverConversaRequest request,
        CancellationToken ct) =>
        Ok(await conversas.MoverAsync(id, request, ct));

    [HttpPost("{id:guid}/servico")]
    public async Task<ActionResult<ConversaResumoResponse>> VincularServico(
        Guid id,
        [FromBody] VincularServicoRequest request,
        CancellationToken ct) =>
        Ok(await conversas.VincularServicoAsync(id, request, ct));

    [HttpPost("{id:guid}/encerrar")]
    public async Task<ActionResult<ConversaResumoResponse>> Encerrar(
        Guid id,
        [FromBody] EncerrarConversaRequest request,
        CancellationToken ct) =>
        Ok(await conversas.EncerrarAsync(id, request, ct));

    [HttpPost("{id:guid}/reabrir")]
    public async Task<ActionResult<ConversaResumoResponse>> Reabrir(Guid id, CancellationToken ct) =>
        Ok(await conversas.ReabrirAsync(id, ct));

    [HttpPost("{id:guid}/ler")]
    public async Task<ActionResult<ConversaResumoResponse>> MarcarComoLida(Guid id, CancellationToken ct) =>
        Ok(await conversas.MarcarComoLidaAsync(id, ct));

    [HttpGet("{id:guid}/mensagens")]
    public async Task<ActionResult<IReadOnlyList<MensagemResponse>>> ListarMensagens(
        Guid id,
        CancellationToken ct) =>
        Ok(await mensagens.ListarAsync(id, ct));

    [HttpPost("{id:guid}/mensagens")]
    public async Task<ActionResult<MensagemResponse>> Enviar(
        Guid id,
        [FromBody] EnviarMensagemRequest request,
        CancellationToken ct) =>
        Ok(await mensagens.EnviarAsync(id, request, ct));

    /// <summary>
    /// Envia um arquivo para o cliente.
    /// </summary>
    /// <remarks>
    /// Multipart, e nao JSON com base64: base64 incha o corpo em um terco e
    /// obrigaria o navegador a carregar o arquivo inteiro em memoria como texto.
    /// </remarks>
    [HttpPost("{id:guid}/mensagens/anexo")]
    [RequestSizeLimit(32 * 1024 * 1024)]
    public async Task<ActionResult<MensagemResponse>> EnviarAnexo(
        Guid id,
        IFormFile arquivo,
        [FromForm] string? legenda,
        [FromForm] Guid? mensagemRespondidaId,
        CancellationToken ct)
    {
        // O teto real vive no servico, junto com a lista de tipos. O
        // RequestSizeLimit acima e mais folgado de proposito: ele existe para
        // cortar corpo absurdo antes de ler, nao para ser a regra.
        using var memoria = new MemoryStream();
        await arquivo.CopyToAsync(memoria, ct);

        return Ok(await mensagens.EnviarAnexoAsync(
            id,
            new EnviarAnexoRequest(
                memoria.ToArray(),
                arquivo.FileName,
                arquivo.ContentType,
                legenda,
                mensagemRespondidaId),
            ct));
    }

    /// <summary>
    /// Devolve o arquivo anexado a uma mensagem.
    /// </summary>
    /// <remarks>
    /// Autenticado e conferido por tenant e conversa no servico. Anexo de
    /// cartorio e documento de cliente — RG, escritura, comprovante: servir a
    /// pasta como arquivo estatico entregaria o documento de um terceiro a quem
    /// tentasse a URL certa.
    /// </remarks>
    [HttpGet("{id:guid}/mensagens/{mensagemId:guid}/anexo")]
    public async Task<IActionResult> BaixarAnexo(Guid id, Guid mensagemId, CancellationToken ct)
    {
        var anexo = await mensagens.ObterAnexoAsync(id, mensagemId, ct);

        // Sem cache no disco do navegador: o painel pode estar num computador de
        // balcao compartilhado, e documento de cliente nao fica para o proximo
        // turno em cache.
        Response.Headers.CacheControl = "private, no-store";

        // Trava o navegador no tipo que NOS declaramos. Sem isto, um arquivo com
        // conteudo enganoso poderia ser reinterpretado como HTML pelo sniffing e
        // executar no dominio do painel.
        Response.Headers.XContentTypeOptions = "nosniff";

        return anexo.ExibirNaTela
            ? File(anexo.Conteudo, anexo.TipoMime)
            : File(anexo.Conteudo, anexo.TipoMime, anexo.Nome);
    }

    // --------------------------------------- acoes do menu de contexto da mensagem

    [HttpPost("{id:guid}/mensagens/{mensagemId:guid}/fixar")]
    public async Task<ActionResult<MensagemResponse>> Fixar(
        Guid id,
        Guid mensagemId,
        [FromQuery] bool fixar = true,
        CancellationToken ct = default) =>
        Ok(await acoes.FixarAsync(id, mensagemId, fixar, ct));

    /// <summary>
    /// Oculta ou reexibe mensagens. NAO apaga: a linha permanece no banco, com
    /// registro de quem ocultou e quando (RNF-008).
    /// </summary>
    [HttpPost("{id:guid}/mensagens/ocultar")]
    public async Task<ActionResult<object>> Ocultar(
        Guid id,
        [FromBody] OcultarMensagensRequest request,
        CancellationToken ct) =>
        Ok(new { afetadas = await acoes.OcultarAsync(id, request, ct) });

    /// <summary>
    /// Corrige o texto de uma mensagem ja enviada, inclusive no aparelho do
    /// cliente. Recusa com codigo proprio quando a janela do WhatsApp fechou.
    /// </summary>
    [HttpPost("{id:guid}/mensagens/{mensagemId:guid}/corrigir")]
    public async Task<ActionResult<MensagemResponse>> Corrigir(
        Guid id,
        Guid mensagemId,
        [FromBody] CorrigirMensagemRequest request,
        CancellationToken ct) =>
        Ok(await acoes.CorrigirAsync(id, mensagemId, request, ct));

    /// <summary>
    /// Apaga a mensagem no aparelho do cliente. NAO apaga a linha: ela continua no
    /// fio, marcada, com o texto preservado (RNF-008).
    /// </summary>
    [HttpPost("{id:guid}/mensagens/{mensagemId:guid}/apagar-para-todos")]
    public async Task<ActionResult<MensagemResponse>> ApagarParaTodos(
        Guid id,
        Guid mensagemId,
        CancellationToken ct) =>
        Ok(await acoes.ApagarParaTodosAsync(id, mensagemId, ct));

    [HttpPost("{id:guid}/mensagens/encaminhar")]
    public async Task<ActionResult<object>> Encaminhar(
        Guid id,
        [FromBody] EncaminharMensagensRequest request,
        CancellationToken ct) =>
        Ok(new { encaminhadas = await acoes.EncaminharAsync(id, request, ct) });

    /// <summary>Conversas ativas que podem receber um encaminhamento.</summary>
    [HttpGet("{id:guid}/destinos")]
    public async Task<ActionResult<IReadOnlyList<DestinoDeEncaminhamentoResponse>>> Destinos(
        Guid id,
        [FromQuery] string? busca,
        CancellationToken ct) =>
        Ok(await acoes.DestinosAsync(id, busca, ct));

    /// <summary>
    /// FASE 1 apenas. Simula mensagem chegando do cliente para demonstrar o painel
    /// sem WhatsApp real. Na Fase 2 quem alimenta esse caminho e o webhook da Meta,
    /// e este endpoint sai da API.
    /// </summary>
    [HttpPost("{id:guid}/mensagens/simular-cliente")]
    public async Task<ActionResult<MensagemResponse>> SimularMensagemDeCliente(
        Guid id,
        [FromBody] SimularMensagemDeClienteRequest request,
        CancellationToken ct) =>
        Ok(await mensagens.SimularRecebimentoAsync(id, request, ct));
}
