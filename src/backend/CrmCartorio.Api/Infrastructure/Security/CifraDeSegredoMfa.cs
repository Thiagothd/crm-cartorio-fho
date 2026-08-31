using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace CrmCartorio.Api.Infrastructure.Security;

/// <inheritdoc cref="ICifraDeSegredoMfa"/>
/// <remarks>
/// Formato do <c>byte[]</c> gravado (auto-descritivo, para suportar rotacao de
/// chave sem migrar dado):
/// <code>
/// [formatoVersao:1][tamanhoDaVersaoDaChave:1][versaoDaChave:N][nonce:12][tag:16][ciphertext:resto]
/// </code>
/// Cifra-se sempre com <see cref="OpcoesDeMfa.VersaoCorrente"/>; decifra-se pela
/// versao GRAVADA no proprio registro — rotacionar a chave e adicionar uma
/// entrada nova em <see cref="OpcoesDeMfa.Chaves"/> e trocar
/// <see cref="OpcoesDeMfa.VersaoCorrente"/>, sem tocar em segredo ja cifrado.
/// </remarks>
public class CifraDeSegredoMfa(IOptions<OpcoesDeMfa> opcoes) : ICifraDeSegredoMfa
{
    private const int FormatoVersaoAtual = 1;
    private const int TamanhoDoNonce = 12;
    private const int TamanhoDaTag = 16;

    private readonly OpcoesDeMfa _opcoes = opcoes.Value;

    public byte[] Cifrar(string segredoBase32, Guid tenantId, Guid atendenteId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segredoBase32);

        var versaoDaChave = _opcoes.VersaoCorrente;
        var chave = ObterChave(versaoDaChave);

        var nonce = RandomNumberGenerator.GetBytes(TamanhoDoNonce);
        var tag = new byte[TamanhoDaTag];
        var textoClaro = Encoding.UTF8.GetBytes(segredoBase32);
        var ciphertext = new byte[textoClaro.Length];
        var aad = MontarAad(tenantId, atendenteId);

        using (var aes = new AesGcm(chave, TamanhoDaTag))
        {
            aes.Encrypt(nonce, textoClaro, ciphertext, tag, aad);
        }

        var versaoDaChaveBytes = Encoding.UTF8.GetBytes(versaoDaChave);
        if (versaoDaChaveBytes.Length > byte.MaxValue)
        {
            throw new InvalidOperationException("Versao de chave de MFA nao pode exceder 255 bytes.");
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

    public string Decifrar(byte[] cifrado, Guid tenantId, Guid atendenteId)
    {
        ArgumentNullException.ThrowIfNull(cifrado);

        if (cifrado.Length < 2 || cifrado[0] != FormatoVersaoAtual)
        {
            throw new CryptographicException("Formato de segredo MFA desconhecido.");
        }

        var pos = 1;
        var tamanhoVersao = cifrado[pos++];

        if (cifrado.Length < pos + tamanhoVersao + TamanhoDoNonce + TamanhoDaTag)
        {
            throw new CryptographicException("Segredo MFA truncado.");
        }

        var versaoDaChave = Encoding.UTF8.GetString(cifrado, pos, tamanhoVersao); pos += tamanhoVersao;
        var nonce = cifrado.AsSpan(pos, TamanhoDoNonce); pos += TamanhoDoNonce;
        var tag = cifrado.AsSpan(pos, TamanhoDaTag); pos += TamanhoDaTag;
        var ciphertext = cifrado.AsSpan(pos);

        var chave = ObterChave(versaoDaChave);
        var aad = MontarAad(tenantId, atendenteId);
        var textoClaro = new byte[ciphertext.Length];

        using (var aes = new AesGcm(chave, TamanhoDaTag))
        {
            // Falha (tag/AAD/chave errados) lanca CryptographicException — fail
            // closed, sem devolver bytes parciais nem detalhe do motivo.
            aes.Decrypt(nonce, ciphertext, tag, textoClaro, aad);
        }

        return Encoding.UTF8.GetString(textoClaro);
    }

    /// <summary>AAD = TenantId ‖ AtendenteId: move o ciphertext para outra conta, mesmo com a chave certa, e a tag deixa de bater.</summary>
    private static byte[] MontarAad(Guid tenantId, Guid atendenteId)
    {
        var aad = new byte[32];
        tenantId.TryWriteBytes(aad.AsSpan(0, 16));
        atendenteId.TryWriteBytes(aad.AsSpan(16, 16));
        return aad;
    }

    private byte[] ObterChave(string versao)
    {
        if (!_opcoes.Chaves.TryGetValue(versao, out var chaveBase64) || string.IsNullOrWhiteSpace(chaveBase64))
        {
            throw new InvalidOperationException(
                $"Nenhuma chave de MFA configurada para a versao '{versao}' (Mfa:Chaves:{versao}).");
        }

        var chave = Convert.FromBase64String(chaveBase64);

        if (chave.Length != 32)
        {
            throw new InvalidOperationException(
                $"Chave de MFA versao '{versao}' precisa ter 32 bytes (AES-256); tem {chave.Length}.");
        }

        return chave;
    }
}
