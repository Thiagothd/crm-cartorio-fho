using System.Text.Json;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.CanalWhatsApp.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrmCartorio.Api.Controllers;

/// <summary>
/// Entrada do canal real de WhatsApp: webhook do gateway e tela de pareamento.
/// </summary>
[ApiController]
[Route("api/whatsapp")]
public class WhatsAppController(
    IProcessadorDeWebhook processador,
    IPareamentoDoCanal pareamento,
    IContextoDoUsuario ctx,
    ILogger<WhatsAppController> log) : ControllerBase
{
    /// <summary>
    /// Recebe os eventos do gateway.
    /// </summary>
    /// <remarks>
    /// <b>Anonimo por necessidade:</b> o gateway nao carrega JWT. Quem faz o papel
    /// da autenticacao e o token compartilhado da query — e por isso ele e a
    /// PRIMEIRA coisa conferida, antes de ler o corpo.
    ///
    /// Devolve 200 mesmo quando o evento e ignorado. O gateway reentrega o que nao
    /// recebe 200, entao devolver erro para um evento que nunca vamos querer
    /// (figurinha, grupo, eco) criaria laco de reentrega.
    /// </remarks>
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook(
        [FromQuery] string? token,
        [FromBody] JsonElement corpo,
        CancellationToken ct)
    {
        if (!processador.TokenConfere(token))
        {
            log.LogWarning(
                "Webhook de WhatsApp recusado: token invalido (origem {Ip}).",
                HttpContext.Connection.RemoteIpAddress);

            return Unauthorized();
        }

        var payload = new PayloadDoWebhook(
            LerTexto(corpo, "event"),
            LerTexto(corpo, "instance"),
            corpo.TryGetProperty("data", out var dados) ? dados : default);

        await processador.ProcessarAsync(payload, ct);

        return Ok();
    }

    /// <summary>Situacao do canal, para a tela de Configuracoes.</summary>
    [HttpGet("situacao")]
    [Authorize]
    public async Task<ActionResult<SituacaoDoCanal>> Situacao(CancellationToken ct)
    {
        GarantirAdministrador();

        return Ok(await pareamento.ConsultarAsync(ctx.TenantId, ct));
    }

    /// <summary>
    /// Prepara o pareamento e devolve o QR.
    /// </summary>
    /// <remarks>
    /// A URL do webhook e montada a partir do endereco desta requisicao. Em
    /// desenvolvimento, isso da `localhost` — que de dentro do conteiner e o
    /// PROPRIO conteiner. Por isso `UrlBaseParaWebhook` troca por
    /// `host.docker.internal`: sem essa troca o gateway registra sucesso, a API
    /// nunca recebe nada, e a conversa simplesmente nao aparece.
    /// </remarks>
    [HttpPost("parear")]
    [Authorize]
    public async Task<ActionResult<SituacaoDoCanal>> Parear(CancellationToken ct)
    {
        GarantirAdministrador();

        return Ok(await pareamento.PrepararAsync(ctx.TenantId, UrlBaseParaWebhook(), ct));
    }

    /// <remarks>
    /// Conectar o WhatsApp da serventia e decisao de quem responde por ela. Um
    /// escrevente nao pareia — nem despareia — o canal do cartorio.
    /// </remarks>
    private void GarantirAdministrador()
    {
        if (!ctx.PodeGerirAtendentes)
        {
            throw CanalWhatsAppServiceException.WebhookNaoAutorizado();
        }
    }

    private string UrlBaseParaWebhook()
    {
        var host = HttpContext.Request.Host.Host;
        var porta = HttpContext.Request.Host.Port;
        var esquema = HttpContext.Request.Scheme;

        // De dentro do conteiner, `localhost` e o conteiner. O host da maquina
        // atende por `host.docker.internal` no Docker Desktop.
        if (host is "localhost" or "127.0.0.1" or "::1")
        {
            host = "host.docker.internal";
        }

        return porta is null ? $"{esquema}://{host}" : $"{esquema}://{host}:{porta}";
    }

    private static string? LerTexto(JsonElement elemento, string propriedade) =>
        elemento.ValueKind == JsonValueKind.Object
        && elemento.TryGetProperty(propriedade, out var valor)
        && valor.ValueKind == JsonValueKind.String
            ? valor.GetString()
            : null;
}
