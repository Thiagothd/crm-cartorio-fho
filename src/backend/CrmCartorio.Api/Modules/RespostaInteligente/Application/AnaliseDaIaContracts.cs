namespace CrmCartorio.Api.Modules.RespostaInteligente.Application;

/// <summary>Recorte de tempo da análise, já resolvido — mesma forma de <c>PeriodoResponse</c> de Indicadores.</summary>
public record PeriodoDaAnaliseResponse(DateTime De, DateTime Ate, string Granularidade);

/// <summary>
/// "A IA está dando conta?", numa frase e um selo — nunca nome de provedor ou
/// modelo. <see cref="Status"/> é <c>Atendendo</c>, <c>Contingencia</c> ou
/// <c>Desligada</c>.
/// </summary>
/// <remarks>
/// "Contingência" é reservado para quando um provedor de fato tropeçou AGORA
/// (disjuntor aberto por falha real, ou teto diário do Haiku atingido) — nunca
/// para o Gemini estar desligado por decisão de configuração, que hoje é o
/// estado normal do produto, não um incidente. Ver <see cref="AnaliseDaIa"/>.
/// </remarks>
public record SituacaoAgoraDaIaResponse(string Status, string? Detalhe);

/// <param name="PerguntasAtendidas">
/// <c>null</c> quando <paramref name="PerguntasAtendidas"/> não pode ser medido
/// com confiança porque o período começa antes de <c>RodadasMedidasDesde</c> —
/// nesse caso a tela mostra "—", nunca um número que sub-conta silenciosamente.
/// </param>
public record ValorEntregueResponse(
    int? PerguntasAtendidas,
    decimal? PerguntasAtendidasDeltaPercentual,
    decimal TaxaDeResolucao,
    decimal? TaxaDeResolucaoDeltaPontos,
    int NaoSoubeResponder,
    decimal? NaoSoubeResponderDeltaPercentual,
    double? TempoTipicoDeRespostaSegundos,
    decimal? TempoTipicoDeRespostaDeltaPercentual);

/// <summary>Um balde da série temporal — mesmo vocabulário dos KPIs, nunca "chamadas" ou "fallback".</summary>
public record PontoDeVolumeDeIaResponse(DateTime Inicio, int Entregues, int NaoSoubeResponder, int Falhas);

public record ResultadoDasTentativasResponse(int Entregues, int NaoSoubeResponder, int Falhas);

public record AssuntoMaisPerguntadoResponse(Guid? TipoServicoId, string Nome, int Total, decimal Percentual);

/// <summary><see cref="Categoria"/> é o rótulo em português de <c>MotivoDaNaoRespostaDeIa</c> — nunca o nome cru do enum.</summary>
public record LacunaDeCatalogoResponse(string Categoria, Guid? TipoServicoId, string ServicoNome, int Total);

/// <summary>
/// Um insight determinístico, derivado dos MESMOS agregados que os cards e
/// gráficos usam — nunca prosa gerada por LLM. <see cref="Severidade"/> é
/// <c>atencao</c> ou <c>informativo</c>, só para o ícone.
/// </summary>
public record InsightDaIaResponse(string Severidade, string Texto);

/// <summary>Estado completo da Resposta Inteligente para a nova tela <c>/automacao/ia</c> — dono/gestor do cartório.</summary>
public record AnaliseDaIaResponse(
    PeriodoDaAnaliseResponse Periodo,
    SituacaoAgoraDaIaResponse SituacaoAgora,
    ValorEntregueResponse ValorEntregue,
    IReadOnlyList<PontoDeVolumeDeIaResponse> VolumeAoLongoDoTempo,
    ResultadoDasTentativasResponse ResultadoDasTentativas,
    IReadOnlyList<AssuntoMaisPerguntadoResponse> AssuntosMaisPerguntados,
    IReadOnlyList<int> HorariosDeMaiorUso,
    IReadOnlyList<LacunaDeCatalogoResponse> LacunasDeCatalogo,
    IReadOnlyList<InsightDaIaResponse> Insights,
    DateTime? RodadasMedidasDesde);

public interface IAnaliseDaIa
{
    Task<AnaliseDaIaResponse> ObterAsync(DateTime? de, DateTime? ate, CancellationToken ct = default);
}
