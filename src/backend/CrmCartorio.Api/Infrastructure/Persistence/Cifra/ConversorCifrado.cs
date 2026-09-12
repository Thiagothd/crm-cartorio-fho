using CrmCartorio.Api.Infrastructure.Security;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CrmCartorio.Api.Infrastructure.Persistence.Cifra;

/// <summary>
/// Converter de coluna de texto cifrada. Um so serve propriedade obrigatoria
/// (<c>string</c>) e opcional (<c>string?</c>) — o CLR nao distingue as duas
/// em runtime (nulidade de referencia e so anotacao de compilacao), e o EF
/// trata valor nulo automaticamente (nao chama a funcao de conversao),
/// entao a coluna permanecer NOT NULL ou aceitar NULL e regra do
/// <c>IEntityTypeConfiguration</c>, nao deste converter.
/// </summary>
public class ConversorCifrado : ValueConverter<string, string>
{
    public ConversorCifrado(ICifraDeDados cifra, string rotulo, int tamanhoMaximoDoClaro)
        : base(
            claro => CifrarComValidacao(cifra, rotulo, tamanhoMaximoDoClaro, claro),
            armazenado => cifra.Decifrar(armazenado, rotulo)!)
    {
    }

    private static string CifrarComValidacao(ICifraDeDados cifra, string rotulo, int tamanhoMaximoDoClaro, string claro)
    {
        if (claro.Length > tamanhoMaximoDoClaro)
        {
            throw new InvalidOperationException(
                $"'{rotulo}' excede o tamanho maximo do texto em claro ({tamanhoMaximoDoClaro} caracteres).");
        }

        return cifra.Cifrar(claro, rotulo)!;
    }
}
