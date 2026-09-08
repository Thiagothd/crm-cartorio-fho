namespace CrmCartorio.Api.Modules.RespostaInteligente.Application;

/// <summary>Estado de um provedor para a faixa superior do painel — nunca expõe a chave.</summary>
public record EstadoDoProvedorResponse(
    string Nome,
    string Modelo,
    bool Configurado,
    /// <summary><c>Disponivel</c>, <c>Contingencia</c>, <c>SemChave</c> ou <c>TetoDiarioAtingido</c>.</summary>
    string Status,
    DateTime? VoltaATentarEm,
    string? Motivo);

/// <summary>Consumo observado da franquia do Gemini — sempre rotulado como estimativa local, nunca leitura oficial do Google.</summary>
public record FranquiaDoGeminiResponse(
    bool LimitesCadastrados,
    int? LimiteRpd,
    int RequisicoesHoje,
    int? LimiteRpm,
    int RequisicoesNaJanela,
    int? LimiteTpm,
    int TokensNaJanela,
    int PercentualDeSeguranca,
    int? SugestaoDeLimiteRpd);

public record HojeDaIaResponse(
    int TotalDeChamadas,
    int AtendidasPeloGemini,
    int CaidasNoHaiku,
    decimal PercentualGratuito,
    decimal CustoEstimadoHaikuReais,
    int TokensDoGemini);

/// <param name="FallbackExecutado">
/// Tentativas em que o Gemini foi discado e falhou, e o Haiku assumiu — a soma
/// de <see cref="Quota"/> + <see cref="RateLimit"/> + <see cref="Indisponibilidade"/>
/// + <see cref="Configuracao"/> + <see cref="Outros"/> abaixo.
/// </param>
/// <param name="FallbackPreventivo">
/// Tentativas em que o Gemini NUNCA foi discado (desligado por config, disjuntor
/// aberto, margem de cota atingida) e o Haiku respondeu sozinho. Sem esta
/// contagem, uma tela pode mostrar "caídas no Haiku" ao lado de "nenhum
/// fallback" ao mesmo tempo — os motivos abaixo só cobrem o Gemini que FOI
/// discado, não o que foi pulado.
/// </param>
public record MotivosDeFallbackResponse(
    int Quota,
    int RateLimit,
    int Indisponibilidade,
    int Configuracao,
    int Outros,
    int FallbackExecutado,
    int FallbackPreventivo);

public record EconomiaResponse(
    decimal CustoRealHaikuReais,
    decimal CustoSeFosseTudoHaikuReais,
    decimal EconomiaEstimadaReais,
    decimal PercentualDeEconomia);

/// <summary>Estado completo da Resposta Inteligente para o painel <c>/automacao/ia</c>.</summary>
public record SituacaoDaIaResponse(
    bool Habilitada,
    bool EstaConfigurada,
    EstadoDoProvedorResponse Gemini,
    EstadoDoProvedorResponse Anthropic,
    int LimiteDiario,
    int UsadoHojeGeral,
    int LimiteDiarioHaiku,
    int UsadoHojeHaiku,
    FranquiaDoGeminiResponse Franquia,
    HojeDaIaResponse Hoje,
    MotivosDeFallbackResponse MotivosDeFallback,
    EconomiaResponse Economia,
    DateTime PeriodoDe,
    DateTime PeriodoAte);

/// <summary>Resumo leve, sem consultas de período — usado pelo card "Saúde da operação" da Visão Geral e pelo alerta do sino, para não duplicar o cálculo do teto em mais de um lugar.</summary>
public record ResumoDaIaResponse(
    bool Habilitada,
    bool EstaConfigurada,
    string Modelo,
    int LimiteDiario,
    int UsadoHoje);

public interface ISituacaoDaIa
{
    /// <summary>
    /// <paramref name="de"/>/<paramref name="ate"/> nulos caem no dia UTC corrente
    /// — o mesmo recorte de "Hoje" que sempre existiu no teto.
    /// </summary>
    Task<SituacaoDaIaResponse> ObterAsync(DateTime? de, DateTime? ate, CancellationToken ct = default);

    Task<ResumoDaIaResponse> ObterResumoAsync(CancellationToken ct = default);
}
