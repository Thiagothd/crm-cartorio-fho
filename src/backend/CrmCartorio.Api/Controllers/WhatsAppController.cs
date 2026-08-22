using System.Text.Json;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.CanalWhatsApp.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CrmCartorio.Api.Controllers;

/// <summary>
/// Entrada do canal real de WhatsApp: webhook do gateway e a tela de gestão do canal.
/// </summary>
[ApiController]
[Route("api/whatsapp")]
public class WhatsAppController(
    IProcessadorDeWebhook processador,
    IPareamentoDoCanal pareamento,
    IRegrasDoCanal regras,
    IContextoDoUsuario ctx,
    IOptions<OpcoesDoCanalWhatsApp> opcoes,
    ILogger<WhatsAppController> log) : ControllerBase
{
    private readonly OpcoesDoCanalWhatsApp _opcoes = opcoes.Value;

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

    /// <summary>Situacao do canal, para a tela de gestão do canal.</summary>
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

    /// <summary>
    /// Encerra a sessão atual e devolve um QR novo para parear outro número.
    /// </summary>
    /// <remarks>
    /// A instância e o webhook não mudam — só a sessão pareada dentro dela.
    /// Nenhuma conversa existente é tocada; o histórico continua íntegro.
    /// </remarks>
    [HttpPost("trocar-numero")]
    [Authorize]
    public async Task<ActionResult<SituacaoDoCanal>> TrocarNumero(CancellationToken ct)
    {
        GarantirAdministrador();

        return Ok(await pareamento.TrocarNumeroAsync(ctx.TenantId, ctx.AtendenteId, UrlBaseParaWebhook(), ct));
    }

    /// <summary>Encerra a sessão atual sem parear um número novo.</summary>
    [HttpPost("desconectar")]
    [Authorize]
    public async Task<ActionResult<SituacaoDoCanal>> Desconectar(CancellationToken ct)
    {
        GarantirAdministrador();

        return Ok(await pareamento.DesconectarAsync(ctx.TenantId, ctx.AtendenteId, ct));
    }

    /// <summary>Manda um texto fixo a um número, sem criar conversa nem protocolo — prova que o canal funciona.</summary>
    [HttpPost("testar-envio")]
    [Authorize]
    public async Task<ActionResult<TesteDeEnvioResponse>> TestarEnvio(
        [FromBody] TestarEnvioRequest request, CancellationToken ct)
    {
        GarantirAdministrador();

        return Ok(await pareamento.TestarEnvioAsync(ctx.TenantId, request.Telefone, ct));
    }

    /// <summary>Últimas sessões do canal — quando conectou, quando caiu, e quem trocou.</summary>
    [HttpGet("conexoes")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<ConexaoResponse>>> Conexoes(CancellationToken ct)
    {
        GarantirAdministrador();

        return Ok(await pareamento.HistoricoDeConexoesAsync(ctx.TenantId, ct: ct));
    }

    /// <summary>Modo de recebimento e as listas de números liberados/bloqueados.</summary>
    [HttpGet("regras")]
    [Authorize]
    public async Task<ActionResult<RegrasDoCanalResponse>> Regras(CancellationToken ct)
    {
        GarantirAdministrador();

        return Ok(await regras.ObterAsync(ct));
    }

    /// <summary>Troca o modo de recebimento (qualquer número, só liberados, ou todos menos bloqueados).</summary>
    [HttpPut("regras/modo")]
    [Authorize]
    public async Task<ActionResult<RegrasDoCanalResponse>> DefinirModo(
        [FromBody] DefinirModoRequest request, CancellationToken ct)
    {
        GarantirAdministrador();

        return Ok(await regras.DefinirModoAsync(request, ct));
    }

    /// <summary>Adiciona um número à lista de liberados ou de bloqueados.</summary>
    [HttpPost("regras/numeros")]
    [Authorize]
    public async Task<ActionResult<NumeroDoCanalResponse>> AdicionarNumero(
        [FromBody] AdicionarNumeroRequest request, CancellationToken ct)
    {
        GarantirAdministrador();

        return Ok(await regras.AdicionarAsync(request, ct));
    }

    /// <summary>Remove um número de qualquer uma das duas listas.</summary>
    [HttpDelete("regras/numeros/{id:guid}")]
    [Authorize]
    public async Task<IActionResult> RemoverNumero(Guid id, CancellationToken ct)
    {
        GarantirAdministrador();

        await regras.RemoverAsync(id, ct);

        return NoContent();
    }

    /// <summary>Contagem e últimas mensagens descartadas pelo filtro de números.</summary>
    [HttpGet("descartes")]
    [Authorize]
    public async Task<ActionResult<DescartesResponse>> Descartes(CancellationToken ct)
    {
        GarantirAdministrador();

        return Ok(await regras.DescartesRecentesAsync(ct));
    }

    /// <remarks>
    /// Conectar, trocar ou desconectar o WhatsApp da serventia — e decidir quem
    /// fala com ele — e decisao de quem responde pela serventia. Um escrevente
    /// nao mexe em nada disto.
    /// </remarks>
    private void GarantirAdministrador()
    {
        if (!ctx.PodeGerirAtendentes)
        {
            throw CanalWhatsAppServiceException.WebhookNaoAutorizado();
        }
    }

    /// <remarks>
    /// Configuracao explicita vence deducao, sempre que existir. Ver
    /// <see cref="OpcoesDoCanalWhatsApp.UrlPublica"/> para o porque — em resumo,
    /// atras do Cloudflare Tunnel a deducao acerta o host e erra o esquema, e o
    /// webhook fica registrado num endereco que nao atende.
    ///
    /// A deducao continua para o desenvolvimento na propria maquina, onde ela
    /// funciona e evita ter de configurar uma chave a mais para rodar local.
    /// </remarks>
    private string UrlBaseParaWebhook()
    {
        if (!string.IsNullOrWhiteSpace(_opcoes.UrlPublica))
        {
            return _opcoes.UrlPublica.TrimEnd('/');
        }

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

public record TestarEnvioRequest(string Telefone);
