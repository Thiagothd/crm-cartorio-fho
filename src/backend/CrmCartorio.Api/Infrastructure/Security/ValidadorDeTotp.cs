using OtpNet;

namespace CrmCartorio.Api.Infrastructure.Security;

/// <summary>
/// SEC-03: RFC 6238 via <c>Otp.NET</c> — biblioteca madura, nao implementacao
/// propria. SHA1 / 6 digitos / passo de 30s: nao e o conjunto mais forte
/// possivel, e o unico que o Google Authenticator (o app mais comum entre os
/// atendentes) suporta de forma confiavel.
/// </summary>
public static class ValidadorDeTotp
{
    public const int TamanhoDoSegredoEmBytes = 20; // 160 bits — recomendado pela RFC 4226 para HMAC-SHA1
    private const int PassoEmSegundos = 30;
    private const int Digitos = 6;

    /// <summary>Gera um segredo novo, ja em Base32 (formato que o otpauth:// e os apps de autenticacao esperam).</summary>
    public static string GerarSegredoBase32() =>
        Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(TamanhoDoSegredoEmBytes));

    public static string MontarOtpauthUri(string segredoBase32, string emissor, string conta) =>
        $"otpauth://totp/{Uri.EscapeDataString(emissor)}:{Uri.EscapeDataString(conta)}" +
        $"?secret={segredoBase32}&issuer={Uri.EscapeDataString(emissor)}" +
        $"&algorithm=SHA1&digits={Digitos}&period={PassoEmSegundos}";

    /// <summary>
    /// Valida <paramref name="codigo"/> contra <paramref name="segredoBase32"/>
    /// numa janela de +-1 passo (90s de tolerancia a deriva de relogio).
    /// Devolve o NUMERO DO PASSO aceito — e o valor gravado em
    /// <c>Atendente.MfaUltimoPassoUsado</c> para o compare-and-set anti-replay;
    /// nunca aceitar o mesmo numero de passo duas vezes.
    /// </summary>
    public static bool Validar(string segredoBase32, string codigo, out long passoValidado)
    {
        passoValidado = 0;

        if (string.IsNullOrWhiteSpace(codigo))
        {
            return false;
        }

        var totp = new Totp(Base32Encoding.ToBytes(segredoBase32), PassoEmSegundos, OtpHashMode.Sha1, Digitos);

        return totp.VerifyTotp(codigo.Trim(), out passoValidado, new VerificationWindow(previous: 1, future: 1));
    }
}
