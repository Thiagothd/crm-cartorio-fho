using CrmCartorio.Api.Modules.RespostaInteligente.Domain.Enums;

namespace CrmCartorio.Api.Modules.CentralDeIa.Application;

/// <summary>O que os três blocos (KPIs/tendências, tenants, histórico) filtram em comum — sempre aplicado server-side.</summary>
public enum ResultadoDaChamadaDeIa
{
    Entregue,
    NaoSoubeResponder,
    Falha,
}

public sealed record FiltroDeObservabilidadeDaIa(
    DateTime De,
    DateTime Ate,
    Guid? TenantId,
    ProvedorDeIa? Provedor,
    string? Modelo,
    ResultadoDaChamadaDeIa? Resultado,
    TipoDeFallbackDeIa? TipoDeFallback,
    TipoDeFalhaDeIa? TipoDeFalha,
    int? CodigoHttp);

public record SaudeDoProvedorResponse(
    string Provedor,
    bool Configurado,
    string? Modelo,
    DateTime? UltimaChamadaEm,
    DateTime? UltimaChamadaComSucessoEm,
    string? UltimoErroTipo,
    int? UltimoErroCodigoHttp,
    DateTime? UltimoErroEm,
    int Total429Hoje,
    DateTime? Ultimo429Em,
    double? LatenciaMediaMsHoje,
    decimal? TaxaDeSucessoHoje,
    string EstadoDoCircuito,
    DateTime? CircuitoVoltaATentarEm,
    int FallbacksOriginadosHoje,
    int TokensEntradaHoje,
    int TokensSaidaHoje,
    decimal? CustoEstimadoUsdHoje,
    int? LimiteRpdInformado,
    int RequisicoesHoje);

public record SaudeAgoraResponse(SaudeDoProvedorResponse Gemini, SaudeDoProvedorResponse Anthropic);

public record KpisDaObservabilidadeResponse(
    int Tentativas,
    int Rodadas,
    int Entregues,
    int NaoSoubeResponder,
    int Falhas,
    decimal TaxaDeSucesso,
    decimal TaxaDeFalha,
    int FallbackExecutado,
    int FallbackPreventivoDesligado,
    int FallbackPreventivoDisjuntor,
    int FallbackPreventivoMargemDeCota,
    int TokensEntrada,
    int TokensSaida,
    int TokensPensamento,
    decimal CustoEstimadoUsdTotal,
    decimal CustoEstimadoReaisTotal,
    decimal? CustoMedioUsdPorRodada,
    int TenantsAtivos,
    int Total429,
    int TotalTimeouts,
    int LarguraDoBaldeDeLatenciaMs,
    double? P50LatenciaMs,
    double? P95LatenciaMs,
    double? P99LatenciaMs);

public record PontoDaLinhaDoTempoResponse(
    DateTime Inicio,
    int TentativasGemini,
    int TentativasAnthropic,
    int Entregues,
    int NaoSoubeResponder,
    int Falhas,
    int FallbackExecutado,
    decimal CustoEstimadoUsd,
    int TokensEntrada,
    int TokensSaida);

public record BaldeDeLatenciaResponse(int InicioMs, int Total);

public record HistogramaDeLatenciaResponse(string Provedor, int LarguraDoBaldeMs, IReadOnlyList<BaldeDeLatenciaResponse> Baldes);

public record ErroPorCategoriaResponse(string Provedor, string TipoDeFalha, int Total);

public record FallbackPorTipoResponse(string Tipo, int Total);

public record DistribuicaoPorModeloResponse(string Provedor, string Modelo, int Total, decimal CustoEstimadoUsd);

public record TenantDaObservabilidadeResponse(
    Guid TenantId,
    string Nome,
    int Tentativas,
    int Falhas,
    int FallbackExecutado,
    decimal CustoEstimadoUsd,
    decimal CustoEstimadoReais,
    double? LatenciaMediaMs);

public record PaginaDeTenantsResponse(
    IReadOnlyList<TenantDaObservabilidadeResponse> Itens, int Total, int Pagina, int TamanhoDaPagina);

public enum OrdenarTenantsPor
{
    Tentativas,
    Custo,
    Falhas,
    FallbackExecutado,
    Latencia,
}

/// <summary>
/// Uma linha de <c>ChamadasDeIa</c>, já traduzida para exibição. Nunca carrega
/// <c>ConversaId</c>, <c>MensagemId</c> nem <c>MotivoDoFallback</c> (texto
/// livre) — ver a nota de privacidade em <c>ChamadaDeIa</c>.
/// </summary>
public record LinhaDoHistoricoResponse(
    Guid Id,
    DateTime IniciadaEmUtc,
    Guid TenantId,
    string TenantNome,
    string Provedor,
    string Modelo,
    string Resultado,
    string TipoDeFallback,
    string TipoDeFalha,
    int? CodigoHttp,
    int LatenciaMs,
    int TokensEntrada,
    int TokensSaida,
    decimal? CustoEstimadoUsd,
    Guid? RodadaId);

public record PaginaDeHistoricoResponse(
    IReadOnlyList<LinhaDoHistoricoResponse> Itens, int Total, int Pagina, int TamanhoDaPagina);

public record ObservabilidadeDaIaResponse(
    DateTime PeriodoDe,
    DateTime PeriodoAte,
    SaudeAgoraResponse SaudeAgora,
    KpisDaObservabilidadeResponse Kpis,
    IReadOnlyList<PontoDaLinhaDoTempoResponse> LinhaDoTempo,
    IReadOnlyList<HistogramaDeLatenciaResponse> HistogramaDeLatencia,
    IReadOnlyList<ErroPorCategoriaResponse> ErrosPorCategoria,
    IReadOnlyList<FallbackPorTipoResponse> FallbacksPorTipo,
    IReadOnlyList<DistribuicaoPorModeloResponse> DistribuicaoPorModelo,
    IReadOnlyList<TenantDaObservabilidadeResponse> TopTenantsPorCusto);
