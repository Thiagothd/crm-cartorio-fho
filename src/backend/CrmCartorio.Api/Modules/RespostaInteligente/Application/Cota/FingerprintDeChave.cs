using System.Security.Cryptography;
using System.Text;

namespace CrmCartorio.Api.Modules.RespostaInteligente.Application.Cota;

/// <summary>
/// Identifica um projeto Google sem guardar a chave. A franquia do Gemini é por
/// projeto, não por tenant nem por chave individual dentro do mesmo projeto — mas
/// como o sistema só conhece a chave, este fingerprint é o que permite ao menos
/// não misturar contagem entre chaves diferentes (ex.: depois de uma rotação) sem
/// nunca persistir ou logar a chave em si.
/// </summary>
public static class FingerprintDeChave
{
    public static string Calcular(string chave)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(chave));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
