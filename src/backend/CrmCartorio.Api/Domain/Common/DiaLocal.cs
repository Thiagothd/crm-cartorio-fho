namespace CrmCartorio.Api.Domain.Common;

/// <summary>
/// "Hoje" no fuso da serventia, nunca no fuso UTC do servidor.
/// </summary>
/// <remarks>
/// As duas coisas divergem entre 21h e meia-noite no horario de Brasilia. Existe
/// aqui, em <c>Domain/Common</c>, porque mais de um slice precisa do MESMO limite
/// de dia — hoje <c>ConversaService</c> (contador "encerradas hoje" e filtro da
/// lista) e o slice de Indicadores; se cada consumidor calculasse o proprio, o
/// numero do indicador nao bateria com a lista ou o grafico que ele abre.
/// </remarks>
public static class DiaLocal
{
    /// <summary>Meia-noite de hoje no fuso local, devolvida em UTC.</summary>
    public static DateTime InicioDoDia(DateTime agoraUtc) =>
        TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(
                TimeZoneInfo.ConvertTimeFromUtc(agoraUtc, TimeZoneInfo.Local).Date,
                DateTimeKind.Unspecified),
            TimeZoneInfo.Local);

    /// <summary>Um instante UTC, convertido para hora local da serventia.</summary>
    public static DateTime ParaLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.Local);
}
