namespace CrmCartorio.Api.Infrastructure.Security;

/// <summary>
/// SEC-03: chaves de cifra do segredo TOTP. Mesma disciplina de
/// <c>OpcoesDeJwt.Chave</c> — vive em <c>appsettings.&lt;Ambiente&gt;.json</c>,
/// nunca versionada, nunca junto do backup do banco (ver docs/INSTALACAO.md).
/// </summary>
/// <remarks>
/// <see cref="Chaves"/> e um mapa {versao (string) → chave AES-256 em base64,
/// 32 bytes}. Cifra-se sempre com <see cref="VersaoCorrente"/>; decifra-se pela
/// versao GRAVADA no proprio registro (ver
/// <see cref="ICifraDeSegredoMfa"/>) — o que permite rotacionar a chave sem
/// invalidar segredos ja cifrados com a anterior: basta manter a chave antiga
/// no mapa e trocar <see cref="VersaoCorrente"/>.
/// </remarks>
public class OpcoesDeMfa
{
    public const string Secao = "Mfa";

    public string VersaoCorrente { get; set; } = "1";

    public Dictionary<string, string> Chaves { get; set; } = [];
}
