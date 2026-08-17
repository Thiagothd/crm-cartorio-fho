using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using CrmCartorio.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CrmCartorio.Api.Modules.RespostaInteligente.Application;

/// <inheritdoc cref="IRespostaInteligenteService"/>
public class RespostaInteligenteService(
    CrmDbContext db,
    AnthropicClient anthropic,
    IOptions<OpcoesDaIA> opcoes,
    ILogger<RespostaInteligenteService> log) : IRespostaInteligenteService
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
            }
        }),
        ["required"] = JsonSerializer.SerializeToElement(new[] { "podeResponder", "resposta", "motivo" }),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false)
    };

    public async Task<RespostaDaIA> ResponderAsync(
        Guid tenantId,
        IReadOnlyList<TrocaAnterior> historico,
        string mensagemDoCliente,
        CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
        {
            throw RespostaInteligenteServiceException.TenantObrigatorio();
        }

        if (!_opcoes.EstaConfigurada)
        {
            return NaoRespondeu("IA desligada ou sem chave configurada.");
        }

        try
        {
            // Teto diario ANTES de gastar qualquer coisa com a IA. Fica dentro do
            // try de proposito: se a propria contagem falhar (banco fora do ar,
            // etc.), cai no catch generico abaixo e degrada do mesmo jeito — nunca
            // deixa uma falha aqui abrir uma brecha no teto.
            var inicioDoDia = DateTime.UtcNow.Date;
            var respostasHoje = await db.Mensagens.AsNoTracking()
                .Where(m => m.TenantId == tenantId && m.GeradaPorIa && m.EnviadaEm >= inicioDoDia)
                .CountAsync(ct);

            if (respostasHoje >= _opcoes.LimiteDiario)
            {
                log.LogWarning(
                    "Limite diario de {Limite} respostas da IA atingido para o tenant {TenantId}.",
                    _opcoes.LimiteDiario, tenantId);

                return NaoRespondeu("Limite diário de respostas da IA atingido por hoje.");
            }

            var contexto = await MontadorDeContexto.MontarAsync(db, tenantId, ct);

            var mensagens = new List<MessageParam>();

            foreach (var troca in historico)
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
                Content = $"CONTEXTO:\n{contexto}\nPERGUNTA DO CLIENTE:\n{mensagemDoCliente}"
            });

            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _opcoes.TimeoutSegundos)));

            var resposta = await anthropic.Messages.Create(
                new MessageCreateParams
                {
                    Model = _opcoes.Modelo,
                    MaxTokens = 1024,
                    System = PromptDeSistema.Texto,
                    Messages = mensagens,
                    OutputConfig = new OutputConfig
                    {
                        Format = new JsonOutputFormat { Schema = EsquemaDaResposta }
                    }
                },
                cancellationToken: limite.Token);

            var textoJson = resposta.Content
                .Select(bloco => bloco.Value)
                .OfType<TextBlock>()
                .FirstOrDefault()?.Text;

            if (string.IsNullOrWhiteSpace(textoJson))
            {
                log.LogWarning(
                    "IA nao devolveu texto (stop_reason: {StopReason}).", resposta.StopReason);

                return NaoRespondeu("Resposta da IA veio vazia.");
            }

            var lida = JsonSerializer.Deserialize<RespostaWire>(textoJson, OpcoesDeDeserializacao)
                ?? throw new JsonException("Deserializacao da resposta da IA devolveu nulo.");

            return new RespostaDaIA(lida.PodeResponder, lida.Resposta, lida.Motivo);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Quem desistiu foi quem chamou, nao a IA — o cancelamento sobe, como em
            // qualquer outro ponto do sistema.
            throw;
        }
        catch (Exception falha)
        {
            // Chave invalida, rede fora do ar, timeout nosso (o CancelAfter acima
            // tambem cai aqui), JSON que nao bateu com o schema — tudo isso e' o
            // mesmo resultado pratico: a IA nao respondeu agora. Nunca propaga: quem
            // chamou so olha PodeResponder e cai para o atendimento humano de sempre.
            log.LogWarning(falha, "Falha ao consultar a IA; caindo para atendimento humano.");
            return NaoRespondeu("Falha ao consultar a IA.");
        }
    }

    private static RespostaDaIA NaoRespondeu(string motivo) => new(false, string.Empty, motivo);

    private sealed record RespostaWire(bool PodeResponder, string Resposta, string Motivo);
}
