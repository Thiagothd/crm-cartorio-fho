using CrmCartorio.Api.Modules.RespostaInteligente.Domain.Enums;

namespace CrmCartorio.Api.Modules.RespostaInteligente.Application.Provedores;

/// <summary>
/// Chaves do registro keyed no DI (<c>AddKeyedScoped</c> / <c>[FromKeyedServices]</c>)
/// — permite que <see cref="OrquestradorDeProvedores"/> dependa só de
/// <see cref="IProvedorDeIa"/> (testável com fakes) em vez dos tipos concretos
/// <c>ProvedorGemini</c>/<c>ProvedorAnthropic</c>, que carregam <c>HttpClient</c> e
/// <c>AnthropicClient</c> reais.
/// </summary>
public static class ChaveDoProvedor
{
    public const string Gemini = "gemini";
    public const string Anthropic = "anthropic";
}

/// <summary>
/// Tudo que um provedor de IA precisa para responder — montado uma vez por
/// <see cref="RespostaInteligenteService"/> e entregue idêntico ao Gemini e ao
/// Anthropic: mesmo catálogo, mesmas respostas rápidas, mesmo histórico, mesma
/// pergunta. O prompt de sistema NÃO vai aqui de propósito — cada provedor
/// resolve o próprio via <see cref="PromptDeSistema.ParaGemini"/> /
/// <see cref="PromptDeSistema.ParaAnthropic"/>, porque as 7 regras são as mesmas
/// para os dois, mas o ajuste fino por modelo (quando existir) é decisão de cada
/// implementação, não do pedido.
/// </summary>
public sealed record PedidoDeIa(
    string Contexto,
    IReadOnlyList<TrocaAnterior> Historico,
    string MensagemDoCliente);

/// <summary>
/// Resultado de uma tentativa de chamada a um provedor. <see cref="Sucesso"/> só é
/// verdadeiro quando o modelo devolveu uma resposta estruturada válida — inclui o
/// caso <c>podeResponder=false</c>, que é resposta válida, não falha.
/// </summary>
public sealed record ResultadoDoProvedor(
    bool Sucesso,
    RespostaDaIA? Resposta,
    TipoDeFalhaDeIa Falha,
    int? CodigoHttp,
    int TokensEntrada,
    int TokensSaida,
    int TokensPensamento,
    int LatenciaMs,
    string? Detalhe,
    TimeSpan? RetryApos = null)
{
    public static ResultadoDoProvedor DeSucesso(
        RespostaDaIA resposta, int tokensEntrada, int tokensSaida, int tokensPensamento, int latenciaMs) =>
        new(true, resposta, TipoDeFalhaDeIa.Nenhuma, null, tokensEntrada, tokensSaida, tokensPensamento, latenciaMs, null);

    public static ResultadoDoProvedor DeFalha(
        TipoDeFalhaDeIa falha, int? codigoHttp, int latenciaMs, string? detalhe, TimeSpan? retryApos = null) =>
        new(false, null, falha, codigoHttp, 0, 0, 0, latenciaMs, detalhe, retryApos);
}

/// <summary>
/// Uma implementação de provedor de IA (Gemini, Anthropic, ...). Ao contrário de
/// <see cref="IRespostaInteligenteService"/>, PODE lançar — é o
/// <see cref="OrquestradorDeProvedores"/> que absorve a falha e decide o que fazer.
/// </summary>
public interface IProvedorDeIa
{
    ProvedorDeIa Identidade { get; }

    string Modelo { get; }

    /// <summary>Provedor ligado e com chave — quando falso, o orquestrador nem tenta.</summary>
    bool EstaConfigurado { get; }

    Task<ResultadoDoProvedor> GerarAsync(PedidoDeIa pedido, CancellationToken ct);
}
