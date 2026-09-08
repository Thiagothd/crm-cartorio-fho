namespace CrmCartorio.Api.Modules.RespostaInteligente.Application.Cota;

/// <summary>
/// O RPD (requisições por dia) do free tier do Gemini reseta à meia-noite do
/// horário do Pacífico — não UTC, não horário de Brasília. Tenta o ID IANA
/// primeiro (resolve tanto em Linux quanto no Windows moderno via ICU); cai para
/// o ID do Windows só se o runtime não tiver o catálogo IANA disponível.
/// </summary>
internal static class RelogioPacifico
{
    private static readonly TimeZoneInfo Fuso = Resolver();

    private static TimeZoneInfo Resolver()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        }
    }

    public static DateOnly DiaAtual()
    {
        var agora = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Fuso);
        return DateOnly.FromDateTime(agora);
    }

    /// <summary>
    /// Início (meia-noite) do dia atual do Pacífico, convertido para UTC. NÃO é
    /// <c>DateTime.UtcNow.Date</c> — o Pacífico está 7-8h atrás de UTC, então o dia
    /// Pacífico corrente começou ontem em termos de data UTC na maior parte do dia
    /// brasileiro. Achado ao vivo, 2026-08-26: uma consulta de hidratação que
    /// filtrava por <c>UtcNow.Date</c> perdia praticamente todas as chamadas do
    /// dia Pacífico corrente, porque a maioria delas tem <c>IniciadaEm</c> no dia
    /// UTC ANTERIOR.
    /// </summary>
    public static DateTime InicioDoDiaAtualUtc()
    {
        var meiaNoitePacifico = DiaAtual().ToDateTime(TimeOnly.MinValue);
        return TimeZoneInfo.ConvertTimeToUtc(meiaNoitePacifico, Fuso);
    }

    public static DateTime ProximaMeiaNoiteUtc()
    {
        var agora = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Fuso);
        var proximaMeiaNoite = agora.Date.AddDays(1);
        return TimeZoneInfo.ConvertTimeToUtc(proximaMeiaNoite, Fuso);
    }

    public static DateOnly DiaDoInstante(DateTime instanteUtc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(instanteUtc, Fuso));
}
