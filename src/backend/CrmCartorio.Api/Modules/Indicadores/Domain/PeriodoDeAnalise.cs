namespace CrmCartorio.Api.Modules.Indicadores.Domain;

/// <summary>
/// Resolve o intervalo [De, Ate) pedido pela Visão Geral e pelos Relatórios,
/// sempre em UTC, e decide a granularidade do gráfico de fluxo.
/// </summary>
/// <remarks>
/// Período "Hoje" mostraria um único ponto se o gráfico continuasse agrupando
/// por dia — por isso qualquer intervalo que caiba num dia vira granularidade
/// de hora. O corte de 25h (e não 24h) absorve o dia com horário de verão sem
/// precisar de regra especial.
/// </remarks>
public static class PeriodoDeAnalise
{
    public const int DiasPadrao = 7;

    public static (DateTime De, DateTime Ate, string Granularidade) Resolver(
        DateTime? deSolicitado,
        DateTime? ateSolicitado,
        DateTime agoraUtc)
    {
        // O parametro de query chega como Kind=Unspecified na maioria dos casos —
        // tratamos sempre como UTC (é o que o frontend envia) em vez de deixar
        // ToUniversalTime() supor o fuso do servidor.
        var ate = ComoUtc(ateSolicitado ?? agoraUtc);
        var de = ComoUtc(deSolicitado ?? ate.AddDays(-DiasPadrao));

        if (de > ate)
        {
            (de, ate) = (ate, de);
        }

        var granularidade = ate - de <= TimeSpan.FromHours(25) ? "hora" : "dia";

        return (de, ate, granularidade);
    }

    private static DateTime ComoUtc(DateTime valor) =>
        valor.Kind switch
        {
            DateTimeKind.Utc => valor,
            DateTimeKind.Local => valor.ToUniversalTime(),
            _ => DateTime.SpecifyKind(valor, DateTimeKind.Utc)
        };
}
