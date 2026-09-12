using CrmCartorio.Api.Infrastructure.Security;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CrmCartorio.Api.Infrastructure.Persistence.Cifra;

/// <summary>
/// <c>Cliente.DataNascimento</c> cifrada: a coluna deixa de ser <c>date</c> e
/// passa a ser <c>nvarchar(max)</c> guardando a data em ISO
/// (<c>yyyy-MM-dd</c>), cifrada do mesmo jeito que qualquer outra coluna de
/// texto. Formato de texto (nao numerico) de proposito — reaproveita
/// <see cref="ICifraDeDados"/> sem precisar de uma variante para bytes fixos.
/// </summary>
public class ConversorDeDataCifrada : ValueConverter<DateOnly?, string?>
{
    private const string FormatoIso = "yyyy-MM-dd";

    public ConversorDeDataCifrada(ICifraDeDados cifra, string rotulo)
        : base(
            data => Cifrar(cifra, rotulo, data),
            armazenado => Decifrar(cifra, rotulo, armazenado))
    {
    }

    private static string? Cifrar(ICifraDeDados cifra, string rotulo, DateOnly? data) =>
        data is null ? null : cifra.Cifrar(data.Value.ToString(FormatoIso), rotulo);

    private static DateOnly? Decifrar(ICifraDeDados cifra, string rotulo, string? armazenado)
    {
        if (armazenado is null)
        {
            return null;
        }

        var iso = cifra.Decifrar(armazenado, rotulo)!;
        return DateOnly.ParseExact(iso, FormatoIso);
    }
}
