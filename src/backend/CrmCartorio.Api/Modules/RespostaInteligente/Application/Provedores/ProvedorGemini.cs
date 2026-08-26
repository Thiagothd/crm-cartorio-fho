using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CrmCartorio.Api.Modules.RespostaInteligente.Domain.Enums;
using Microsoft.Extensions.Options;

namespace CrmCartorio.Api.Modules.RespostaInteligente.Application.Provedores;

/// <summary>
/// Provedor principal — Gemini 3.7 Flash via Interactions API
/// (<c>POST {BaseUrl}interactions</c>). Não existe SDK .NET oficial para esta API
/// (o pacote <c>Google.GenAI</c> só cobre o legado <c>generateContent</c>), então
/// a chamada é <see cref="HttpClient"/> tipado + <see cref="System.Text.Json"/>,
/// no mesmo padrão do <c>EvolutionClient</c> deste projeto.
/// </summary>
/// <remarks>
/// <c>store: false</c> de propósito: o histórico do cartório não fica retido na
/// nuvem do Google — coerente com o posicionamento LGPD do produto (CLAUDE.md
/// §4). Por isso o histórico é sempre reconstruído do nosso banco e enviado
/// inline em <c>input</c>, nunca via <c>previous_interaction_id</c> (que exige
/// <c>store: true</c>).
///
/// Sem <c>temperature</c>/<c>top_p</c>/<c>top_k</c> — Gemini 3.x não aceita mais
/// esses parâmetros (a documentação pede para removê-los explicitamente). O
/// controle de latência/qualidade é <c>generation_config.thinking_level</c>.
///
/// <c>input</c> vai como STRING ÚNICA com o histórico transcrito inline
/// (<see cref="MontarInput"/>), não como array de turnos com <c>role</c>/<c>parts</c>
/// — confirmado por chamada real em 2026-08-25: `/v1/interactions` devolveu
/// <c>400 "Unknown parameter 'turns' at 'input'"</c> para o array de turnos, e
/// aceitou normalmente uma string única. A doc de referência não fixa esse
/// formato (detalha só a aba `/v1beta`), por isso a decisão ficou registrada
/// aqui em vez de assumida da documentação.
/// </remarks>
public partial class ProvedorGemini(
    HttpClient http,
    IOptions<OpcoesDaIA> opcoes,
    ILogger<ProvedorGemini> log) : IProvedorDeIa
{
    private readonly OpcoesDoGemini _opcoes = opcoes.Value.Gemini;

    private static readonly JsonSerializerOptions OpcoesDeLeitura =
        new() { PropertyNameCaseInsensitive = true };

    private static readonly JsonSerializerOptions OpcoesDeEscrita =
        new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    // Mesmo esquema semântico do Anthropic (podeResponder/resposta/motivo) — só a
    // forma de declarar muda (dicionário simples em vez de JsonElement) porque
    // aqui quem serializa é System.Text.Json puro, sem o tipo do SDK da Anthropic.
    private static readonly Dictionary<string, object> EsquemaDaResposta = new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object>
        {
            ["podeResponder"] = new Dictionary<string, object>
            {
                ["type"] = "boolean",
                ["description"] = "Se a pergunta pode ser respondida só com o CONTEXTO fornecido."
            },
            ["resposta"] = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["description"] = "A resposta ao cliente. Vazia quando podeResponder é falso."
            },
            ["motivo"] = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["description"] = "Por que não pode responder, quando podeResponder é falso."
            }
        },
        ["required"] = new[] { "podeResponder", "resposta", "motivo" },
        ["additionalProperties"] = false
    };

    public ProvedorDeIa Identidade => ProvedorDeIa.Gemini;

    public string Modelo => _opcoes.Modelo;

    public bool EstaConfigurado => _opcoes.Habilitado && !string.IsNullOrWhiteSpace(_opcoes.ApiKey);

    public async Task<ResultadoDoProvedor> GerarAsync(PedidoDeIa pedido, CancellationToken ct)
    {
        var cronometro = Stopwatch.StartNew();

        var corpo = new InteractionRequestDto
        {
            Model = Modelo,
            SystemInstruction = PromptDeSistema.ParaGemini(),
            Input = MontarInput(pedido),
            Store = false,
            GenerationConfig = new GenerationConfigDto
            {
                ThinkingLevel = _opcoes.NivelDeRaciocinio,
                MaxOutputTokens = 1024
            },
            ResponseFormat = new ResponseFormatDto { Schema = EsquemaDaResposta }
        };

        using var requisicao = new HttpRequestMessage(HttpMethod.Post, "interactions")
        {
            Content = JsonContent.Create(corpo, options: OpcoesDeEscrita)
        };
        requisicao.Headers.Add("x-goog-api-key", _opcoes.ApiKey);

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _opcoes.TimeoutSegundos)));

        HttpResponseMessage resposta;

        try
        {
            resposta = await http.SendAsync(requisicao, limite.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            cronometro.Stop();
            return ResultadoDoProvedor.DeFalha(
                TipoDeFalhaDeIa.Timeout, null, (int)cronometro.ElapsedMilliseconds, "Timeout da chamada ao Gemini.");
        }
        catch (HttpRequestException falha)
        {
            cronometro.Stop();
            log.LogWarning(falha, "Gemini: falha de rede.");
            return ResultadoDoProvedor.DeFalha(
                TipoDeFalhaDeIa.Rede, null, (int)cronometro.ElapsedMilliseconds, falha.Message);
        }

        using (resposta)
        {
            var corpoTexto = await resposta.Content.ReadAsStringAsync(ct);
            cronometro.Stop();

            if (!resposta.IsSuccessStatusCode)
            {
                return ClassificarErro(resposta.StatusCode, corpoTexto, (int)cronometro.ElapsedMilliseconds);
            }

            InteractionResponseDto? corpoLido;

            try
            {
                corpoLido = JsonSerializer.Deserialize<InteractionResponseDto>(corpoTexto, OpcoesDeLeitura);
            }
            catch (JsonException falha)
            {
                log.LogWarning(falha, "Gemini: corpo de resposta ilegivel.");
                return ResultadoDoProvedor.DeFalha(
                    TipoDeFalhaDeIa.RespostaInvalida, (int)resposta.StatusCode, (int)cronometro.ElapsedMilliseconds,
                    "Corpo de resposta ilegivel.");
            }

            var texto = corpoLido?.Steps?
                .Where(passo => passo.Type == "model_output")
                .SelectMany(passo => passo.Content ?? [])
                .FirstOrDefault(parte => parte.Type == "text")?.Text;

            if (string.IsNullOrWhiteSpace(texto))
            {
                log.LogWarning("Gemini nao devolveu texto (status: {Status}).", corpoLido?.Status);
                return ResultadoDoProvedor.DeFalha(
                    TipoDeFalhaDeIa.RespostaInvalida, (int)resposta.StatusCode, (int)cronometro.ElapsedMilliseconds,
                    "Resposta vazia.");
            }

            RespostaWire? lida;

            try
            {
                lida = JsonSerializer.Deserialize<RespostaWire>(texto, OpcoesDeLeitura);
            }
            catch (JsonException falha)
            {
                log.LogWarning(falha, "Gemini: texto fora do esquema pedido.");
                return ResultadoDoProvedor.DeFalha(
                    TipoDeFalhaDeIa.RespostaInvalida, (int)resposta.StatusCode, (int)cronometro.ElapsedMilliseconds,
                    falha.Message);
            }

            if (lida is null)
            {
                return ResultadoDoProvedor.DeFalha(
                    TipoDeFalhaDeIa.RespostaInvalida, (int)resposta.StatusCode, (int)cronometro.ElapsedMilliseconds,
                    "JSON da resposta veio nulo.");
            }

            return ResultadoDoProvedor.DeSucesso(
                new RespostaDaIA(lida.PodeResponder, lida.Resposta, lida.Motivo),
                tokensEntrada: corpoLido?.Usage?.TotalInputTokens ?? 0,
                tokensSaida: corpoLido?.Usage?.TotalOutputTokens ?? 0,
                tokensPensamento: corpoLido?.Usage?.TotalThoughtTokens ?? 0,
                latenciaMs: (int)cronometro.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Classifica o corpo de erro no formato padrão de erro das APIs do Google
    /// (<c>{"error": {"code","message","status","details":[...]}}</c>). Leitura
    /// inteiramente defensiva — o Google pode mudar o formato entre versões, e
    /// isso nunca pode derrubar o atendimento.
    /// </summary>
    private ResultadoDoProvedor ClassificarErro(HttpStatusCode status, string corpo, int latenciaMs)
    {
        var codigo = (int)status;
        GoogleErrorEnvelopeDto? envelope = null;

        try
        {
            envelope = JsonSerializer.Deserialize<GoogleErrorEnvelopeDto>(corpo, OpcoesDeLeitura);
        }
        catch (JsonException)
        {
            // Corpo de erro que nao e JSON valido — segue com o texto cru.
        }

        var mensagem = envelope?.Error?.Message ?? corpo;
        var detalhes = envelope?.Error?.Details ?? [];

        if (status == HttpStatusCode.TooManyRequests)
        {
            // Confirmado por chamada real em 2026-08-25: o 429 do free tier NAO
            // vem no formato estruturado (details/QuotaFailure/RetryInfo) que a
            // documentacao generica do Google descreve para outras APIs — vem
            // como texto livre em "message", ex.:
            //   "Quota exceeded for metric: .../generate_content_free_tier_requests,
            //    limit: 20, model: gemini-3.7-flash. Please retry in 40.76s."
            // A leitura estruturada fica como tentativa primeiria (caso a API
            // mude), com o texto livre como fallback real.
            var violacoesDeQuota = detalhes
                .Where(d => d.Type != null && d.Type.Contains("QuotaFailure", StringComparison.OrdinalIgnoreCase))
                .SelectMany(d => d.Violations ?? [])
                .ToList();

            var ehQuotaDiaria = violacoesDeQuota.Any(v =>
                    v.QuotaId != null && v.QuotaId.Contains("PerDay", StringComparison.OrdinalIgnoreCase))
                || mensagem.Contains("PerDay", StringComparison.OrdinalIgnoreCase)
                || mensagem.Contains("per day", StringComparison.OrdinalIgnoreCase);

            var retryApos = ExtrairRetryDelay(detalhes) ?? ExtrairRetryDelayDoTexto(mensagem);

            log.LogWarning(
                "Gemini: 429 ({Tipo}). {Mensagem}", ehQuotaDiaria ? "quota diaria" : "rate limit", mensagem);

            return ResultadoDoProvedor.DeFalha(
                ehQuotaDiaria ? TipoDeFalhaDeIa.QuotaEsgotada : TipoDeFalhaDeIa.RateLimit,
                codigo, latenciaMs, mensagem, retryApos);
        }

        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest)
        {
            // Chave invalida, permissao negada, corpo da requisicao rejeitado —
            // sempre erro de configuracao nosso, nunca transitorio. Log em Error
            // de proposito: mascarar isto atras do fallback e pior do que deixar
            // visivel, mesmo que o Haiku assuma sem o cliente perceber.
            log.LogError("Gemini: erro de configuracao ({Status}). {Mensagem}", status, mensagem);
            return ResultadoDoProvedor.DeFalha(TipoDeFalhaDeIa.Configuracao, codigo, latenciaMs, mensagem);
        }

        // 500/502/503/504 e qualquer outro codigo nao mapeado explicitamente.
        log.LogWarning("Gemini: indisponibilidade ({Status}). {Mensagem}", status, mensagem);
        return ResultadoDoProvedor.DeFalha(TipoDeFalhaDeIa.Indisponibilidade, codigo, latenciaMs, mensagem);
    }

    [GeneratedRegex(@"^(\d+(?:\.\d+)?)s$")]
    private static partial Regex RegexDeRetryDelay();

    private static TimeSpan? ExtrairRetryDelay(IReadOnlyList<GoogleErrorDetailDto> detalhes)
    {
        var retryInfo = detalhes.FirstOrDefault(d => d.Type != null && d.Type.Contains("RetryInfo", StringComparison.OrdinalIgnoreCase));

        if (retryInfo?.RetryDelay is not { } bruto)
        {
            return null;
        }

        // Duracao no formato protobuf ("13s", "1.500s").
        var casado = RegexDeRetryDelay().Match(bruto);
        return casado.Success && double.TryParse(casado.Groups[1].Value, out var segundos)
            ? SanidadeDoRetryDelay(segundos)
            : null;
    }

    /// <summary>
    /// Nunca confia cegamente num numero vindo de fora — nem estruturado, nem
    /// de texto livre. Um valor absurdo (parse errado do nosso lado, ou o
    /// proprio Google mandando algo fora do padrao) deve virar "sem sugestao",
    /// nunca um TimeSpan gigantesco. O disjuntor tambem tem um teto proprio
    /// (CooldownMaximoSegundos) — esta e a primeira camada, mais perto da fonte.
    /// </summary>
    private static TimeSpan? SanidadeDoRetryDelay(double segundos) =>
        segundos is > 0 and <= 3600 ? TimeSpan.FromSeconds(segundos) : null;

    [GeneratedRegex(@"retry in (\d+(?:\.\d+)?)s", RegexOptions.IgnoreCase)]
    private static partial Regex RegexDeRetryNoTexto();

    /// <summary>
    /// Fallback confirmado por chamada real: o 429 do free tier embute o tempo
    /// de espera em texto livre dentro de "message" ("Please retry in 40.76s."),
    /// não no campo estruturado <c>RetryInfo</c> que a documentação genérica do
    /// Google descreve.
    /// </summary>
    private static TimeSpan? ExtrairRetryDelayDoTexto(string mensagem)
    {
        var casado = RegexDeRetryNoTexto().Match(mensagem);
        return casado.Success && double.TryParse(casado.Groups[1].Value, out var segundos)
            ? SanidadeDoRetryDelay(segundos)
            : null;
    }

    /// <summary>
    /// Transcreve o histórico em um único bloco de texto, com marcadores de
    /// turno — a forma que <c>/v1/interactions</c> aceita de fato (ver o
    /// <c>remarks</c> da classe). Semanticamente idêntico ao que o Anthropic
    /// recebe como mensagens separadas: mesmo conteúdo, outra serialização.
    /// </summary>
    private static string MontarInput(PedidoDeIa pedido)
    {
        if (pedido.Historico.Count == 0)
        {
            return $"CONTEXTO:\n{pedido.Contexto}\nPERGUNTA DO CLIENTE:\n{pedido.MensagemDoCliente}";
        }

        var historico = new System.Text.StringBuilder("HISTORICO DESTA CONVERSA:\n");

        foreach (var troca in pedido.Historico)
        {
            historico.Append(troca.DoCliente ? "Cliente: " : "Atendimento: ").AppendLine(troca.Conteudo);
        }

        return $"{historico}\nCONTEXTO:\n{pedido.Contexto}\nPERGUNTA DO CLIENTE:\n{pedido.MensagemDoCliente}";
    }

    private sealed record RespostaWire(bool PodeResponder, string Resposta, string Motivo);

    // --- DTOs da Interactions API — nomes de campo em snake_case, como a API exige.

    private sealed class InteractionRequestDto
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("system_instruction")] public string SystemInstruction { get; set; } = "";
        [JsonPropertyName("input")] public string Input { get; set; } = "";
        [JsonPropertyName("store")] public bool Store { get; set; }
        [JsonPropertyName("generation_config")] public GenerationConfigDto GenerationConfig { get; set; } = new();
        [JsonPropertyName("response_format")] public ResponseFormatDto ResponseFormat { get; set; } = new();
    }

    private sealed class GenerationConfigDto
    {
        [JsonPropertyName("thinking_level")] public string ThinkingLevel { get; set; } = "low";
        [JsonPropertyName("max_output_tokens")] public int MaxOutputTokens { get; set; } = 1024;
    }

    private sealed class ResponseFormatDto
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "text";
        [JsonPropertyName("mime_type")] public string MimeType { get; set; } = "application/json";
        [JsonPropertyName("schema")] public object Schema { get; set; } = new Dictionary<string, object>();
    }

    private sealed class InteractionResponseDto
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("usage")] public UsageDto? Usage { get; set; }
        [JsonPropertyName("steps")] public List<StepDto>? Steps { get; set; }
    }

    private sealed class UsageDto
    {
        [JsonPropertyName("total_input_tokens")] public int TotalInputTokens { get; set; }
        [JsonPropertyName("total_output_tokens")] public int TotalOutputTokens { get; set; }
        [JsonPropertyName("total_thought_tokens")] public int TotalThoughtTokens { get; set; }
    }

    private sealed class StepDto
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("content")] public List<StepContentDto>? Content { get; set; }
    }

    private sealed class StepContentDto
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("text")] public string? Text { get; set; }
    }

    private sealed class GoogleErrorEnvelopeDto
    {
        [JsonPropertyName("error")] public GoogleErrorDto? Error { get; set; }
    }

    private sealed class GoogleErrorDto
    {
        [JsonPropertyName("code")] public int? Code { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("details")] public List<GoogleErrorDetailDto>? Details { get; set; }
    }

    private sealed class GoogleErrorDetailDto
    {
        [JsonPropertyName("@type")] public string? Type { get; set; }

        // RetryInfo
        [JsonPropertyName("retryDelay")] public string? RetryDelay { get; set; }

        // QuotaFailure
        [JsonPropertyName("violations")] public List<GoogleQuotaViolationDto>? Violations { get; set; }
    }

    private sealed class GoogleQuotaViolationDto
    {
        [JsonPropertyName("quotaId")] public string? QuotaId { get; set; }
        [JsonPropertyName("quotaValue")] public string? QuotaValue { get; set; }
    }
}
