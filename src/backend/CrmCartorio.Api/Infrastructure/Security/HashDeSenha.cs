using System.Security.Cryptography;

namespace CrmCartorio.Api.Infrastructure.Security;

/// <summary>
/// PBKDF2-HMAC-SHA256 com salt por usuario.
/// Formato armazenado: pbkdf2.sha256$&lt;iteracoes&gt;$&lt;salt b64&gt;$&lt;hash b64&gt;.
/// O numero de iteracoes vai no proprio hash para que aumenta-lo no futuro nao
/// invalide as senhas ja cadastradas.
/// </summary>
public class HashDeSenha : IHashDeSenha
{
    private const int Iteracoes = 210_000;
    private const int TamanhoSaltBytes = 16;
    private const int TamanhoHashBytes = 32;
    private const string Prefixo = "pbkdf2.sha256";

    public string Gerar(string senha)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(senha);

        var salt = RandomNumberGenerator.GetBytes(TamanhoSaltBytes);
        var hash = Derivar(senha, salt, Iteracoes);

        return $"{Prefixo}${Iteracoes}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verificar(string senha, string hashArmazenado)
    {
        if (string.IsNullOrWhiteSpace(senha) || string.IsNullOrWhiteSpace(hashArmazenado))
        {
            return false;
        }

        var partes = hashArmazenado.Split('$');
        if (partes.Length != 4 || partes[0] != Prefixo)
        {
            return false;
        }

        if (!int.TryParse(partes[1], out var iteracoes) || iteracoes <= 0)
        {
            return false;
        }

        byte[] salt;
        byte[] esperado;
        try
        {
            salt = Convert.FromBase64String(partes[2]);
            esperado = Convert.FromBase64String(partes[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var calculado = Derivar(senha, salt, iteracoes, esperado.Length);

        // Comparacao em tempo fixo: comparar com == vazaria informacao por timing.
        return CryptographicOperations.FixedTimeEquals(calculado, esperado);
    }

    private static byte[] Derivar(string senha, byte[] salt, int iteracoes, int tamanho = TamanhoHashBytes) =>
        Rfc2898DeriveBytes.Pbkdf2(senha, salt, iteracoes, HashAlgorithmName.SHA256, tamanho);
}
