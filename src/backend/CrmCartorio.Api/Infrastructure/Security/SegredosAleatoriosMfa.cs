using System.Security.Cryptography;
using System.Text;

namespace CrmCartorio.Api.Infrastructure.Security;

/// <summary>
/// SEC-03: geracao e hash dos valores aleatorios de alta entropia usados pelo
/// MFA — desafios, tickets e codigos de recuperacao. Nada aqui e persistido em
/// texto claro; o banco so guarda o hash devolvido por <see cref="Sha256Hex"/>.
/// </summary>
public static class SegredosAleatoriosMfa
{
    /// <summary>Alfabeto Crockford Base32 — sem I, L, O, U: elimina ambiguidade visual e palavras acidentais.</summary>
    private const string AlfabetoCrockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>
    /// Token opaco para desafio/ticket — 32 bytes (256 bits) em Base64Url. Nao
    /// precisa ser digitavel a mao: viaja em JSON, nunca lido em voz alta.
    /// </summary>
    public static string NovoToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>
    /// Codigo de recuperacao — 16 caracteres Crockford Base32 = 80 bits de
    /// entropia (5 bits por caractere; 32 divide 256, sem vies de modulo).
    /// Exibido como <c>XXXX-XXXX-XXXX-XXXX</c>; os hifens nao entram no hash.
    /// </summary>
    public static string NovoCodigoDeRecuperacao()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        var caracteres = new char[16];

        for (var i = 0; i < 16; i++)
        {
            caracteres[i] = AlfabetoCrockford[bytes[i] & 0b0001_1111]; // 5 bits — mascara sem vies, 32 = tamanho do alfabeto
        }

        return new string(caracteres);
    }

    /// <summary>Formata para exibicao: <c>XXXX-XXXX-XXXX-XXXX</c>.</summary>
    public static string FormatarCodigoDeRecuperacao(string codigo) =>
        string.Join('-', Enumerable.Range(0, 4).Select(i => codigo.Substring(i * 4, 4)));

    /// <summary>Maiusculas, sem hifen nem espaco — a mesma forma canonica em que o hash foi calculado.</summary>
    public static string NormalizarCodigoDeRecuperacao(string codigo) =>
        new(codigo.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public static string Sha256Hex(string valor) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(valor)));
}
