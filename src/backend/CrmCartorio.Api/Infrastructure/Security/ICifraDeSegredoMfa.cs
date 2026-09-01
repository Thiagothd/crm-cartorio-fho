namespace CrmCartorio.Api.Infrastructure.Security;

/// <summary>
/// SEC-03: cifra/decifra o segredo TOTP em repouso. AES-256-GCM — autenticado,
/// nunca so confidencial: um ciphertext adulterado falha ao decifrar em vez de
/// devolver lixo que passaria a ser tratado como segredo valido.
/// </summary>
public interface ICifraDeSegredoMfa
{
    /// <summary>
    /// Cifra <paramref name="segredoBase32"/> associando o resultado a
    /// <paramref name="tenantId"/> e <paramref name="atendenteId"/> como AAD —
    /// mover o ciphertext para outra conta (mesmo com a chave certa) falha ao
    /// decifrar. Nonce aleatorio a cada chamada: duas ativacoes do mesmo
    /// segredo (nunca deveria acontecer, mas nada impede) produzem ciphertexts
    /// diferentes.
    /// </summary>
    byte[] Cifrar(string segredoBase32, Guid tenantId, Guid atendenteId);

    /// <summary>
    /// Decifra. Falha lancando <see cref="System.Security.Cryptography.CryptographicException"/>
    /// se a tag de autenticacao nao bater (adulteracao, AAD errado, chave
    /// errada) — fail-closed, sem detalhe do motivo no erro.
    /// </summary>
    string Decifrar(byte[] cifrado, Guid tenantId, Guid atendenteId);
}
