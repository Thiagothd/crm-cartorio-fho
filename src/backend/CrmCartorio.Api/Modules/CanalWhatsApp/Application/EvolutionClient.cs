using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

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

        var criacao = await EnviarAsync(
            HttpMethod.Post,
            "instance/create",
            new
            {
                instanceName = _opcoes.NomeDaInstancia,
                integration = "WHATSAPP-BAILEYS",
                qrcode = true
            },
            ct);

        // 403/409 aqui significa "ja existe", que e o caminho normal do segundo
        // clique em diante. So falha de verdade interrompe.
        if (criacao is null)
        {
            log.LogInformation(
                "Instancia {Instancia} ja existia; seguindo para o webhook.", _opcoes.NomeDaInstancia);
        }

        await RegistrarWebhookAsync(urlDoWebhook, ct);

        return criacao is { } resposta && LerEstado(resposta) is { } estado
            ? estado
            : await ObterEstadoAsync(ct);
    }

    public async Task<EstadoDaInstancia> ObterEstadoAsync(CancellationToken ct)
    {
        GarantirConfigurado();

        var estado = await EnviarAsync(
            HttpMethod.Get, $"instance/connectionState/{_opcoes.NomeDaInstancia}", null, ct);

        // O campo vem ANINHADO — {"instance":{"instanceName":...,"state":"open"}}
        // — e nao solto na raiz. Ler a raiz direto (bug encontrado ao vivo,
        // 2026-08-17: instancia pareada de verdade e o endpoint teimava em
        // devolver "close") faz ESTA leitura sempre cair no fallback "close",
        // que por sua vez faz esta funcao chamar instance/connect em toda
        // consulta — inclusive com a sessao ja aberta, o que arrisca perturbar
        // uma conexao que nao precisava de QR nenhum.
        var atual = estado is { } e
            && e.TryGetProperty("instance", out var instanciaAtual)
            && LerTexto(instanciaAtual, "state") is { Length: > 0 } estadoLido
                ? estadoLido
                : "close";

        if (atual.Equals("open", StringComparison.OrdinalIgnoreCase))
        {
            return new EstadoDaInstancia("open", null);
        }

        // Fora do ar: pedir o QR e o que a tela precisa mostrar.
        var conexao = await EnviarAsync(
            HttpMethod.Get, $"instance/connect/{_opcoes.NomeDaInstancia}", null, ct);

        return new EstadoDaInstancia(atual, conexao is { } c ? LerQrCode(c) : null);
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

    private async Task RegistrarWebhookAsync(string urlDoWebhook, CancellationToken ct)
    {
        await EnviarAsync(
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
                    "Gateway devolveu {Status} em {Caminho}: {Corpo}",
                    resposta.StatusCode, caminho, texto);

                return null;
            }

            return string.IsNullOrWhiteSpace(texto)
                ? null
                : JsonDocument.Parse(texto).RootElement.Clone();
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

    private static EstadoDaInstancia? LerEstado(JsonElement resposta)
    {
        var qr = LerQrCode(resposta);

        if (resposta.TryGetProperty("instance", out var instancia)
            && LerTexto(instancia, "status") is { Length: > 0 } status)
        {
            return new EstadoDaInstancia(status, qr);
        }

        return qr is null ? null : new EstadoDaInstancia("connecting", qr);
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
}
