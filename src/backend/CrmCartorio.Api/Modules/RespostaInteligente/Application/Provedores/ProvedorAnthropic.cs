using System.Diagnostics;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using CrmCartorio.Api.Modules.RespostaInteligente.Domain.Enums;
using Microsoft.Extensions.Options;

namespace CrmCartorio.Api.Modules.RespostaInteligente.Application.Provedores;

/// <summary>
/// Provedor de contingência — mesma chamada estruturada que o serviço original
/// fazia antes de existirem dois provedores, agora atrás de <see cref="IProvedorDeIa"/>.
/// </summary>
/// <remarks>
/// Ao contrário do antigo <c>RespostaInteligenteService</c>, este método PODE
/// lançar — é <see cref="OrquestradorDeProvedores"/> quem absorve a exceção e
/// decide o que fazer. A classificação de falha usa a hierarquia real do SDK
/// (<see cref="AnthropicRateLimitException"/>, <see cref="Anthropic5xxException"/>,
/// etc.), inspecionada via reflection no pacote 12.40.0 — não existe documentação
/// pública detalhada dessas exceções.
/// </remarks>
public class ProvedorAnthropic(
    AnthropicClient anthropic,
    IOptions<OpcoesDaIA> opcoes,
    ILogger<ProvedorAnthropic> log) : IProvedorDeIa
{
    private readonly OpcoesDaIA _opcoes = opcoes.Value;

    // O esquema pede podeResponder/resposta/motivo em camelCase (e o modelo
    // devolve exatamente isso), mas RespostaWire e' PascalCase — sem
    // PropertyNameCaseInsensitive, o Deserialize abaixo NAO lanca erro nenhum: ele
    // silenciosamente devolve tudo no valor padrao (false/null/null). Bug
    // encontrado ao vivo, 2026-08-17: a IA respondia certo, e todo pedido virava
    // "nao respondeu" porque a leitura da resposta descartava tudo.
    private static readonly JsonSerializerOptions OpcoesDeDeserializacao =
        new() { PropertyNameCaseInsensitive = true };

    private static readonly Dictionary<string, JsonElement> EsquemaDaResposta = new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["properties"] = JsonSerializer.SerializeToElement(new
        {
            podeResponder = new
            {
                type = "boolean",
                description = "Se a pergunta pode ser respondida só com o CONTEXTO fornecido."
            },
            resposta = new
            {
                type = "string",
                description = "A resposta ao cliente. Vazia quando podeResponder é falso."
            },
            motivo = new
            {
                type = "string",
                description = "Por que não pode responder, quando podeResponder é falso."
            },
            categoria = new
            {
                type = "string",
                @enum = Enum.GetNames<MotivoDaNaoRespostaDeIa>(),
                description =
                    "Categoria fechada de por que não pode responder. Use NaoClassificado quando " +
                    "podeResponder for verdadeiro, ou quando nenhuma categoria específica descrever o caso."
            }
        }),
        ["required"] = JsonSerializer.SerializeToElement(new[] { "podeResponder", "resposta", "motivo", "categoria" }),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false)
    };

    public ProvedorDeIa Identidade => ProvedorDeIa.Anthropic;

    public string Modelo => _opcoes.ModeloDoAnthropicResolvido;

    public bool EstaConfigurado => _opcoes.AnthropicEstaConfigurado;

    /// <summary>
    /// O Haiku 4.5 rejeita <c>effort</c> com 400; do Haiku 5.5 em diante ele e o
    /// controle de quanto o modelo pensa. Decidido pelo modelo configurado para
    /// que voltar a configuracao para o 4.5 nunca derrube a IA.
    /// </summary>
    private bool ModeloAceitaEsforco =>
        !Modelo.StartsWith("claude-haiku-4", StringComparison.OrdinalIgnoreCase);

    public async Task<ResultadoDoProvedor> GerarAsync(PedidoDeIa pedido, CancellationToken ct)
    {
        var cronometro = Stopwatch.StartNew();

        var mensagens = new List<MessageParam>();

        foreach (var troca in pedido.Historico)
        {
            mensagens.Add(new MessageParam
            {
                Role = troca.DoCliente ? Role.User : Role.Assistant,
                Content = troca.Conteudo
            });
        }

        mensagens.Add(new MessageParam
        {
            Role = Role.User,
            Content = $"CONTEXTO:\n{pedido.Contexto}\nPERGUNTA DO CLIENTE:\n{pedido.MensagemDoCliente}"
        });

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _opcoes.Anthropic.TimeoutSegundos)));

        try
        {
            var resposta = await anthropic.Messages.Create(
                new MessageCreateParams
                {
                    Model = Modelo,
                    // 4096, e nao 1024: a partir do Haiku 5.5 o modelo pensa antes de
                    // responder, e o pensamento conta dentro deste teto. Com 1024 uma
                    // resposta longa podia parar no meio (stop_reason max_tokens).
                    MaxTokens = 4096,
                    System = PromptDeSistema.ParaAnthropic(),
                    Messages = mensagens,
                    OutputConfig = ModeloAceitaEsforco
                        ? new OutputConfig
                        {
                            Format = new JsonOutputFormat { Schema = EsquemaDaResposta },
                            // Resposta curta de balcao, grounded no catalogo: esforco
                            // baixo basta e mantem a latencia dentro do timeout. O
                            // padrao do Haiku 5.5 e medium.
                            Effort = Effort.Low
                        }
                        : new OutputConfig
                        {
                            Format = new JsonOutputFormat { Schema = EsquemaDaResposta }
                        }
                },
                cancellationToken: limite.Token);

            cronometro.Stop();

            var textoJson = resposta.Content
                .Select(bloco => bloco.Value)
                .OfType<TextBlock>()
                .FirstOrDefault()?.Text;

            if (string.IsNullOrWhiteSpace(textoJson))
            {
                log.LogWarning(
                    "Anthropic nao devolveu texto (stop_reason: {StopReason}).", resposta.StopReason);

                return ResultadoDoProvedor.DeFalha(
                    TipoDeFalhaDeIa.RespostaInvalida, null, (int)cronometro.ElapsedMilliseconds, "Resposta vazia.");
            }

            var lida = JsonSerializer.Deserialize<RespostaWire>(textoJson, OpcoesDeDeserializacao)
                ?? throw new JsonException("Deserializacao da resposta da IA devolveu nulo.");

            return ResultadoDoProvedor.DeSucesso(
                new RespostaDaIA(lida.PodeResponder, lida.Resposta, lida.Motivo, InterpretarCategoria(lida.Categoria)),
                tokensEntrada: (int)resposta.Usage.InputTokens,
                tokensSaida: (int)resposta.Usage.OutputTokens,
                tokensPensamento: 0, // a API nao separa: no Haiku 5.5 o pensamento ja vem somado em OutputTokens
                latenciaMs: (int)cronometro.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Quem desistiu foi quem chamou, nao a IA — sobe, como em qualquer
            // outro ponto do sistema. Nao e falha do provedor.
            throw;
        }
        catch (OperationCanceledException)
        {
            cronometro.Stop();
            return ResultadoDoProvedor.DeFalha(
                TipoDeFalhaDeIa.Timeout, null, (int)cronometro.ElapsedMilliseconds, "Timeout da chamada ao Anthropic.");
        }
        catch (AnthropicRateLimitException falha)
        {
            cronometro.Stop();
            log.LogWarning(falha, "Anthropic: rate limit (429).");
            return ResultadoDoProvedor.DeFalha(
                TipoDeFalhaDeIa.RateLimit, (int)falha.StatusCode, (int)cronometro.ElapsedMilliseconds, falha.Message);
        }
        catch (Anthropic4xxException falha)
        {
            // BadRequest, Unauthorized, Forbidden, NotFound, UnprocessableEntity —
            // sempre erro nosso (chave, modelo, corpo da requisicao), nunca
            // transitorio. Cai para o Haiku... espera, isto E o Haiku: cai para
            // "nao respondeu" e o log em Error torna o problema visivel, porque
            // mascarar erro de configuracao e pior do que deixar sem resposta.
            cronometro.Stop();
            log.LogError(falha, "Anthropic: erro de configuracao ({StatusCode}).", falha.StatusCode);
            return ResultadoDoProvedor.DeFalha(
                TipoDeFalhaDeIa.Configuracao, (int)falha.StatusCode, (int)cronometro.ElapsedMilliseconds, falha.Message);
        }
        catch (Anthropic5xxException falha)
        {
            cronometro.Stop();
            log.LogWarning(falha, "Anthropic: indisponibilidade ({StatusCode}).", falha.StatusCode);
            return ResultadoDoProvedor.DeFalha(
                TipoDeFalhaDeIa.Indisponibilidade, (int)falha.StatusCode, (int)cronometro.ElapsedMilliseconds, falha.Message);
        }
        catch (AnthropicApiException falha)
        {
            cronometro.Stop();
            log.LogWarning(falha, "Anthropic: erro de API nao classificado.");
            return ResultadoDoProvedor.DeFalha(
                TipoDeFalhaDeIa.Indisponibilidade, (int)falha.StatusCode, (int)cronometro.ElapsedMilliseconds, falha.Message);
        }
        catch (AnthropicIOException falha)
        {
            cronometro.Stop();
            log.LogWarning(falha, "Anthropic: falha de rede.");
            return ResultadoDoProvedor.DeFalha(
                TipoDeFalhaDeIa.Rede, null, (int)cronometro.ElapsedMilliseconds, falha.Message);
        }
        catch (JsonException falha)
        {
            cronometro.Stop();
            log.LogWarning(falha, "Anthropic: resposta fora do esquema.");
            return ResultadoDoProvedor.DeFalha(
                TipoDeFalhaDeIa.RespostaInvalida, null, (int)cronometro.ElapsedMilliseconds, falha.Message);
        }
        catch (Exception falha)
        {
            // Rede de seguranca final — qualquer coisa nao prevista acima ainda
            // vira degradacao, nunca propaga. Mesmo principio de contingencia do
            // resto do sistema.
            cronometro.Stop();
            log.LogWarning(falha, "Anthropic: falha nao classificada.");
            return ResultadoDoProvedor.DeFalha(
                TipoDeFalhaDeIa.Indisponibilidade, null, (int)cronometro.ElapsedMilliseconds, falha.Message);
        }
    }

    private sealed record RespostaWire(bool PodeResponder, string Resposta, string Motivo, string? Categoria);

    // Duplicado identico em ProvedorGemini.cs — mesmo espirito da duplicacao ja
    // assumida no esquema da resposta (comentario no topo do arquivo): nunca
    // inventa categoria fora da lista fechada. Enum.TryParse com nome invalido,
    // vazio ou nulo cai no piso seguro.
    private static MotivoDaNaoRespostaDeIa InterpretarCategoria(string? valor) =>
        Enum.TryParse<MotivoDaNaoRespostaDeIa>(valor, ignoreCase: true, out var categoria)
            ? categoria
            : MotivoDaNaoRespostaDeIa.NaoClassificado;
}
