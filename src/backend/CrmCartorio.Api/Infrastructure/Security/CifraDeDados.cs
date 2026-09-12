using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace CrmCartorio.Api.Infrastructure.Security;

/// <inheritdoc cref="ICifraDeDados"/>
/// <remarks>
/// Mesmo formato auto-descritivo de <see cref="CifraDeSegredoMfa"/>:
/// <c>[formatoVersao:1][tamanhoDaVersaoDaChave:1][versaoDaChave:N][nonce:12][tag:16][ciphertext:resto]</c>.
/// Para coluna de texto, esse blob vira base64 com o prefixo <c>$c1$</c> —
/// mantem a coluna NVARCHAR e a ausencia do prefixo identifica dado legado em
/// claro (janela de migracao / cifra dormente). Para anexo, o blob e gravado
/// direto em bytes (o arquivo em disco carrega seu proprio marcador "CRMA" na
/// frente — ver <see cref="Anexos.ArmazenamentoDeAnexosEmDisco"/>).
/// </remarks>
public class CifraDeDados(IProvedorDeChaveDeDados provedor, IOptions<OpcoesDeProtecaoDeDados> opcoes) : ICifraDeDados
{
    private const string Prefixo = "$c1$";
    private const int FormatoVersaoAtual = 1;
    private const int TamanhoDoNonce = 12;
    private const int TamanhoDaTag = 16;

    private readonly OpcoesDeProtecaoDeDados _opcoes = opcoes.Value;

    public string? Cifrar(string? claro, string rotulo)
    {
        if (claro is null)
        {
            return null;
        }

        if (!provedor.CifraAtiva)
        {
            return claro;
        }

        var blob = CifrarBytes(Encoding.UTF8.GetBytes(claro), MontarAad(rotulo));
        return Prefixo + Convert.ToBase64String(blob);
    }

    public string? Decifrar(string? armazenado, string rotulo)
    {
        if (armazenado is null)
        {
            return null;
        }

        if (!armazenado.StartsWith(Prefixo, StringComparison.Ordinal))
        {
            if (_opcoes.ExigirCifrado && provedor.CifraAtiva)
            {
                throw new DadoNaoCifradoException(rotulo);
            }

            return armazenado;
        }

        var blob = Convert.FromBase64String(armazenado[Prefixo.Length..]);
        var textoClaro = DecifrarBytes(blob, MontarAad(rotulo));
        return Encoding.UTF8.GetString(textoClaro);
    }

    public byte[] CifrarBytes(byte[] claro, byte[] aad)
    {
        if (!provedor.CifraAtiva)
        {
            return claro;
        }

        var versaoDaChave = provedor.VersaoCorrente;
        var chave = provedor.ObterChave(versaoDaChave);

        var nonce = RandomNumberGenerator.GetBytes(TamanhoDoNonce);
        var tag = new byte[TamanhoDaTag];
        var ciphertext = new byte[claro.Length];

        using (var aes = new AesGcm(chave, TamanhoDaTag))
        {
            aes.Encrypt(nonce, claro, ciphertext, tag, aad);
        }

        var versaoDaChaveBytes = Encoding.UTF8.GetBytes(versaoDaChave);
        if (versaoDaChaveBytes.Length > byte.MaxValue)
        {
            throw new InvalidOperationException("Versao de chave de dados nao pode exceder 255 bytes.");
        }

        var saida = new byte[2 + versaoDaChaveBytes.Length + TamanhoDoNonce + TamanhoDaTag + ciphertext.Length];
        var pos = 0;

        saida[pos++] = FormatoVersaoAtual;
        saida[pos++] = (byte)versaoDaChaveBytes.Length;
        versaoDaChaveBytes.CopyTo(saida, pos); pos += versaoDaChaveBytes.Length;
        nonce.CopyTo(saida, pos); pos += TamanhoDoNonce;
        tag.CopyTo(saida, pos); pos += TamanhoDaTag;
        ciphertext.CopyTo(saida, pos);

        return saida;
    }

    public byte[] DecifrarBytes(byte[] cifrado, byte[] aad)
    {
        if (cifrado.Length < 2 || cifrado[0] != FormatoVersaoAtual)
        {
            throw new CryptographicException("Formato de dado cifrado desconhecido.");
        }

        var pos = 1;
        var tamanhoVersao = cifrado[pos++];

        if (cifrado.Length < pos + tamanhoVersao + TamanhoDoNonce + TamanhoDaTag)
        {
            throw new CryptographicException("Dado cifrado truncado.");
        }

        var versaoDaChave = Encoding.UTF8.GetString(cifrado, pos, tamanhoVersao); pos += tamanhoVersao;
        var nonce = cifrado.AsSpan(pos, TamanhoDoNonce); pos += TamanhoDoNonce;
        var tag = cifrado.AsSpan(pos, TamanhoDaTag); pos += TamanhoDaTag;
        var ciphertext = cifrado.AsSpan(pos);

        var chave = provedor.ObterChave(versaoDaChave);
        var textoClaro = new byte[ciphertext.Length];

        using (var aes = new AesGcm(chave, TamanhoDaTag))
        {
            // Falha (tag/AAD/chave errados) lanca CryptographicException — fail
            // closed, sem devolver bytes parciais nem detalhe do motivo.
            aes.Decrypt(nonce, ciphertext, tag, textoClaro, aad);
        }

        return textoClaro;
    }

    /// <summary>AAD = rotulo da coluna/anexo. O converter/armazenamento nao enxerga a linha (tenant/id) — ver Anexos, que monta AAD proprio com tenant+mensagem.</summary>
    private static byte[] MontarAad(string rotulo) => Encoding.UTF8.GetBytes(rotulo);
}
