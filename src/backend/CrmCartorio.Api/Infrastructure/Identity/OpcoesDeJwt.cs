namespace CrmCartorio.Api.Infrastructure.Identity;

public class OpcoesDeJwt
{
    public const string Secao = "Jwt";

    public string Emissor { get; set; } = "CrmCartorio";

    public string Audiencia { get; set; } = "CrmCartorio";

    /// <summary>
    /// Minimo de 32 bytes para HMAC-SHA256. Em producao vem de variavel de ambiente
    /// ou user-secrets — nunca do appsettings versionado.
    /// </summary>
    public string Chave { get; set; } = string.Empty;

    public int ExpiracaoEmMinutos { get; set; } = 480;
}
