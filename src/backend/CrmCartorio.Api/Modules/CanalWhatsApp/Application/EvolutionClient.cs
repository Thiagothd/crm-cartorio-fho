using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using System.Linq;

namespace CrmCartorio.Api.Modules.CanalWhatsApp.Application;

/// <summary>
/// Cliente REST do gateway Evolution.
/// </summary>
/// <remarks>
/// Toda leitura de resposta e defensiva (<see cref="JsonElement"/>, campos
/// opcionais): o gateway e software de terceiro que muda de formato entre
/// versoes, e uma mudanca de campo nao pode derrubar o atendimento.
/// </remarks>
public class EvolutionClient(
    HttpClient http,
    IOptions<OpcoesDoCanalWhatsApp> opcoes,
    ILogger<EvolutionClient> log) : IEvolutionClient
{
    private readonly OpcoesDoCanalWhatsApp _opcoes = opcoes.Value;

    public async Task<EstadoDaInstancia> GarantirInstanciaAsync(
        string urlDoWebhook,
        CancellationToken ct)
    {
        GarantirConfigurado();

        // instance/create NUNCA e chamado quando a instancia ja existe — nem
        // idempotente. Incidente ao vivo em 2026-08-22 (ver DEPLOY-WHATSAPP.md):
        // repetir a criacao com a sessao ja aberta fez o gateway forcar um
        // LOGOUT de verdade, exigindo QR novo. Tratar 403/409 como "seguro"
        // supunha um idempotente que esta versao do gateway nao entrega.
        if (await ConsultarSeExisteAsync(ct) is null)
        {
            var criacao = await EnviarComStatusAsync(
                HttpMethod.Post,
                "instance/create",
                new
                {
                    instanceName = _opcoes.NomeDaInstancia,
                    integration = "WHATSAPP-BAILEYS",
                    // Criar a instancia nao pode iniciar o pareamento. O QR so e
                    // pedido depois de webhook confirmado e tenant persistido.
                    qrcode = false
                },
                ct);

            if (!criacao.Sucesso)
            {
                throw CanalWhatsAppServiceException.GatewayIndisponivel();
            }

            log.LogInformation("Instancia {Instancia} criada.", _opcoes.NomeDaInstancia);
        }

        await RegistrarWebhookAsync(urlDoWebhook, ct);

        // Mesmo que uma versao do gateway devolva QR na criacao, ele nunca e
        // propagado. Somente IniciarConexaoAsync pode entregar um QR.
        return await ObterEstadoAsync(ct);
    }

    public async Task<EstadoDaInstancia> ObterEstadoAsync(CancellationToken ct)
    {
        GarantirConfigurado();

        return await ConsultarSeExisteAsync(ct)
            ?? throw CanalWhatsAppServiceException.GatewayIndisponivel();
    }

    /// <summary>
    /// Consulta o estado sem exigir que a instancia exista.
    /// </summary>
    /// <remarks>
    /// Devolve <c>null</c> so quando o gateway confirma (404) que a instancia
    /// nao existe — o unico caso em que criar e seguro. Qualquer outra falha
    /// continua lancando <see cref="CanalWhatsAppServiceException"/>.
    /// </remarks>
    private async Task<EstadoDaInstancia?> ConsultarSeExisteAsync(CancellationToken ct)
    {
        var resposta = await EnviarComStatusAsync(
            HttpMethod.Get, $"instance/connectionState/{_opcoes.NomeDaInstancia}", null, ct);

        if (resposta.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!resposta.Sucesso || resposta.Corpo is not { } estado)
        {
            throw CanalWhatsAppServiceException.GatewayIndisponivel();
        }

        // O campo vem ANINHADO — {"instance":{"instanceName":...,"state":"open"}}
        // — e nao solto na raiz. Ler a raiz direto (bug encontrado ao vivo,
        // 2026-08-17: instancia pareada de verdade e o endpoint teimava em
        // devolver "close") faz ESTA leitura sempre cair no fallback "close",
        // que por sua vez faz esta funcao chamar instance/connect em toda
        // consulta — inclusive com a sessao ja aberta, o que arrisca perturbar
        // uma conexao que nao precisava de QR nenhum.
        var atual = estado.TryGetProperty("instance", out var instanciaAtual)
            ? LerTexto(instanciaAtual, "state")
            : LerTexto(estado, "state");

        // Resposta 2xx com formato desconhecido nao significa sessao fechada.
        // Tratar como close permitiria que o POST chamasse connect por engano.
        if (string.IsNullOrWhiteSpace(atual))
        {
            log.LogWarning(
                "Gateway devolveu estado de conexao sem o campo state para {Instancia}.",
                _opcoes.NomeDaInstancia);

            throw CanalWhatsAppServiceException.GatewayIndisponivel();
        }

        return new EstadoDaInstancia(atual, null);
    }

    public async Task<EstadoDaInstancia> IniciarConexaoAsync(CancellationToken ct)
    {
        GarantirConfigurado();

        var atual = await ObterEstadoAsync(ct);

        // Consultas e polling nunca reiniciam uma sessao. Mesmo o comando
        // explicito respeita "connecting": chamar connect de novo enquanto o
        // celular conclui o pareamento substitui a propria sessao (conflict:
        // replaced) e impede que ela estabilize em open.
        if (!atual.Estado.Equals("close", StringComparison.OrdinalIgnoreCase))
        {
            return atual;
        }

        var conexao = await EnviarComStatusAsync(
            HttpMethod.Get, $"instance/connect/{_opcoes.NomeDaInstancia}", null, ct);

        if (!conexao.Sucesso)
        {
            throw CanalWhatsAppServiceException.GatewayIndisponivel();
        }

        return new EstadoDaInstancia(
            "connecting",
            conexao.Corpo is { } corpo ? LerQrCode(corpo) : null);
    }

    public async Task DesconectarAsync(CancellationToken ct)
    {
        GarantirConfigurado();

        // Recusa (instancia ja fora do ar) nao e erro: o alvo — sessao
        // desconectada — ja esta atingido. So falha de rede vira excecao, dentro
        // de EnviarAsync.
        await EnviarAsync(HttpMethod.Delete, $"instance/logout/{_opcoes.NomeDaInstancia}", null, ct);
    }

    public async Task<PerfilDaInstancia?> ObterPerfilDaInstanciaAsync(CancellationToken ct)
    {
        GarantirConfigurado();

        var resposta = await EnviarAsync(
            HttpMethod.Get,
            $"instance/fetchInstances?instanceName={Uri.EscapeDataString(_opcoes.NomeDaInstancia)}",
            null,
            ct);

        if (resposta is not { } r)
        {
            return null;
        }

        // A resposta vem em ARRAY (fetchInstances lista instancias, mesmo
        // filtrando por nome em algumas versoes) ou em OBJETO solto, dependendo
        // da versao do gateway — aceitar os dois em vez de travar num so.
        var alvo = r.ValueKind == JsonValueKind.Array
            ? r.EnumerateArray().FirstOrDefault()
            : r;

        if (alvo.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // O "dono" da sessao aparece em campos diferentes entre versoes:
        // "ownerJid" no formato "5538999000045@s.whatsapp.net", ou "number" solto.
        var jid = LerTexto(alvo, "ownerJid") ?? LerTexto(alvo, "owner");
        var numero = LerTexto(alvo, "number") ?? jid?.Split('@')[0];
        var nome = LerTexto(alvo, "profileName") ?? LerTexto(alvo, "name");
        var foto = LerTexto(alvo, "profilePicUrl") ?? LerTexto(alvo, "profilePictureUrl");

        return new PerfilDaInstancia(numero, nome, foto);
    }

    public async Task<string> EnviarTextoAsync(
        string telefoneE164,
        string conteudo,
        CancellationToken ct)
    {
        GarantirConfigurado();

        var resposta = await EnviarAsync(
            HttpMethod.Post,
            $"message/sendText/{_opcoes.NomeDaInstancia}",
            new { number = telefoneE164, text = conteudo },
            ct)
            ?? throw CanalWhatsAppServiceException.GatewayIndisponivel();

        // O id vem em key.id. Sem ele o envio ate funciona, mas o webhook de
        // status nao acha a linha depois — entao vale o erro alto.
        if (resposta.TryGetProperty("key", out var chave)
            && LerTexto(chave, "id") is { Length: > 0 } id)
        {
            return id;
        }

        log.LogWarning("Envio aceito pelo gateway sem id de mensagem na resposta.");
        throw CanalWhatsAppServiceException.GatewayIndisponivel();
    }

    public async Task<string> EnviarMidiaAsync(
        string telefoneE164,
        byte[] conteudo,
        string tipoMime,
        string nomeDoArquivo,
        string? legenda,
        CancellationToken ct)
    {
        GarantirConfigurado();

        var resposta = await EnviarAsync(
            HttpMethod.Post,
            $"message/sendMedia/{_opcoes.NomeDaInstancia}",
            new
            {
                number = telefoneE164,
                mediatype = CategoriaDaMidia(tipoMime),
                mimetype = tipoMime,
                caption = legenda ?? string.Empty,
                media = Convert.ToBase64String(conteudo),
                fileName = nomeDoArquivo
            },
            ct)
            ?? throw CanalWhatsAppServiceException.GatewayIndisponivel();

        if (resposta.TryGetProperty("key", out var chave)
            && LerTexto(chave, "id") is { Length: > 0 } id)
        {
            return id;
        }

        log.LogWarning("Envio de midia aceito pelo gateway sem id de mensagem na resposta.");
        throw CanalWhatsAppServiceException.GatewayIndisponivel();
    }

    /// <summary>
    /// O `mediatype` que a Evolution espera, derivado do MIME.
    /// </summary>
    /// <remarks>
    /// Cai em `document` no desconhecido de proposito: documento e o unico tipo
    /// que o WhatsApp entrega sem tentar interpretar o conteudo. Chutar `image`
    /// num arquivo que nao e imagem produz mensagem quebrada no aparelho do
    /// cliente.
    /// </remarks>
    private static string CategoriaDaMidia(string tipoMime)
    {
        var mime = tipoMime.Split(';')[0].Trim().ToLowerInvariant();

        if (mime.StartsWith("image/", StringComparison.Ordinal)) return "image";
        if (mime.StartsWith("video/", StringComparison.Ordinal)) return "video";
        if (mime.StartsWith("audio/", StringComparison.Ordinal)) return "audio";

        return "document";
    }

    public async Task<byte[]?> BaixarMidiaAsync(string idExternoWhatsapp, CancellationToken ct)
    {
        GarantirConfigurado();

        var resposta = await EnviarAsync(
            HttpMethod.Post,
            $"chat/getBase64FromMediaMessage/{_opcoes.NomeDaInstancia}",
            new { message = new { key = new { id = idExternoWhatsapp } }, convertToMp4 = false },
            ct);

        if (resposta is not { } r || LerTexto(r, "base64") is not { Length: > 0 } base64)
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException falha)
        {
            log.LogWarning(falha, "Midia {Id} veio em base64 invalido.", idExternoWhatsapp);
            return null;
        }
    }

    public async Task<string?> ObterFotoPerfilUrlAsync(string telefoneE164, CancellationToken ct)
    {
        if (!_opcoes.EstaConfigurado)
        {
            return null;
        }

        // Timeout curto: foto de perfil e opcional. Em ambientes sem Evolution API
        // (ex: desenvolvimento local) nao queremos travar a UI por varios segundos.
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TimeSpan.FromSeconds(3));

        try
        {
            var resposta = await EnviarAsync(
                HttpMethod.Post,
                $"chat/fetchProfilePictureUrl/{_opcoes.NomeDaInstancia}",
                new { number = telefoneE164 },
                limite.Token);

            if (resposta is not { } r)
            {
                return null;
            }

            var url = LerTexto(r, "profilePictureUrl")
                ?? LerTexto(r, "picture")
                ?? LerTexto(r, "url");

            return string.IsNullOrWhiteSpace(url) ? null : url;
        }
        catch (Exception falha) when (falha is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogDebug(falha, "Nao foi possivel obter foto de perfil para {Telefone}.", telefoneE164);
            return null;
        }
    }


    private async Task RegistrarWebhookAsync(string urlDoWebhook, CancellationToken ct)
    {
        var registro = await EnviarComStatusAsync(
            HttpMethod.Post,
            $"webhook/set/{_opcoes.NomeDaInstancia}",
            new
            {
                webhook = new
                {
                    enabled = true,
                    url = urlDoWebhook,
                    byEvents = false,
                    base64 = false,
                    events = new[] { "MESSAGES_UPSERT", "MESSAGES_UPDATE", "CONNECTION_UPDATE" }
                }
            },
            ct);

        if (!registro.Sucesso)
        {
            throw CanalWhatsAppServiceException.GatewayIndisponivel();
        }

        // Nao basta o POST ter sido aceito: um gateway pode ignorar campos de um
        // payload incompatível. Confirmar enabled+URL impede mostrar um QR que
        // conectaria o numero sem entregar evento algum ao CRM.
        var confirmacao = await EnviarComStatusAsync(
            HttpMethod.Get,
            $"webhook/find/{_opcoes.NomeDaInstancia}",
            null,
            ct);

        if (!confirmacao.Sucesso
            || confirmacao.Corpo is not { } webhook
            || !WebhookConfere(webhook, urlDoWebhook))
        {
            log.LogWarning(
                "Gateway nao confirmou o webhook da instancia {Instancia}.",
                _opcoes.NomeDaInstancia);

            throw CanalWhatsAppServiceException.GatewayIndisponivel();
        }
    }

    /// <summary>
    /// Faz a chamada e devolve o corpo, ou <c>null</c> quando o gateway recusou.
    /// </summary>
    /// <remarks>
    /// Falha de rede vira <c>gateway_indisponivel</c> (503) porque e acionavel:
    /// o conteiner caiu. Recusa do gateway devolve null e quem chamou decide —
    /// "instancia ja existe" e recusa, mas nao e problema.
    /// </remarks>
    private async Task<JsonElement?> EnviarAsync(
        HttpMethod metodo,
        string caminho,
        object? corpo,
        CancellationToken ct)
    {
        var resposta = await EnviarComStatusAsync(metodo, caminho, corpo, ct);
        return resposta.Sucesso ? resposta.Corpo : null;
    }

    private async Task<RespostaDoGateway> EnviarComStatusAsync(
        HttpMethod metodo,
        string caminho,
        object? corpo,
        CancellationToken ct)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _opcoes.TimeoutSegundos)));

        using var requisicao = new HttpRequestMessage(
            metodo, $"{_opcoes.BaseUrl.TrimEnd('/')}/{caminho}");

        requisicao.Headers.Add("apikey", _opcoes.ApiKey);

        if (corpo is not null)
        {
            requisicao.Content = JsonContent.Create(corpo);
        }

        try
        {
            using var resposta = await http.SendAsync(requisicao, limite.Token);
            var texto = await resposta.Content.ReadAsStringAsync(limite.Token);

            if (!resposta.IsSuccessStatusCode)
            {
                log.LogWarning(
                    "Gateway devolveu {Status} em {Caminho}.",
                    resposta.StatusCode, caminho);

                return new RespostaDoGateway(resposta.StatusCode, false, null);
            }

            JsonElement? elemento = string.IsNullOrWhiteSpace(texto)
                ? null
                : JsonDocument.Parse(texto).RootElement.Clone();

            return new RespostaDoGateway(resposta.StatusCode, true, elemento);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Quem desistiu foi a requisicao original, nao o gateway.
            throw;
        }
        catch (Exception falha) when (falha is HttpRequestException or OperationCanceledException or JsonException)
        {
            log.LogError(falha, "Gateway de WhatsApp inacessivel em {Caminho}.", caminho);
            throw CanalWhatsAppServiceException.GatewayIndisponivel();
        }
    }

    private void GarantirConfigurado()
    {
        if (!_opcoes.EstaConfigurado)
        {
            throw CanalWhatsAppServiceException.CanalDesligado();
        }
    }

    private static bool WebhookConfere(JsonElement resposta, string urlEsperada)
    {
        if (resposta.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var webhook = resposta.TryGetProperty("webhook", out var aninhado)
            ? aninhado
            : resposta;

        return webhook.ValueKind == JsonValueKind.Object
            && webhook.TryGetProperty("enabled", out var habilitado)
            && habilitado.ValueKind == JsonValueKind.True
            && LerTexto(webhook, "url") is { } url
            && url.Equals(urlEsperada, StringComparison.Ordinal);
    }

    /// <summary>O QR aparece em `qrcode.base64` ou solto em `base64`.</summary>
    private static string? LerQrCode(JsonElement resposta)
    {
        if (resposta.TryGetProperty("qrcode", out var qrcode)
            && LerTexto(qrcode, "base64") is { Length: > 0 } aninhado)
        {
            return aninhado;
        }

        return LerTexto(resposta, "base64");
    }

    private static string? LerTexto(JsonElement elemento, string propriedade) =>
        elemento.ValueKind == JsonValueKind.Object
        && elemento.TryGetProperty(propriedade, out var valor)
        && valor.ValueKind == JsonValueKind.String
            ? valor.GetString()
            : null;

    private sealed record RespostaDoGateway(
        HttpStatusCode StatusCode,
        bool Sucesso,
        JsonElement? Corpo);
}
