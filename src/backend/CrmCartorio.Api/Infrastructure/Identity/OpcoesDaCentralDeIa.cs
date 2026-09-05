namespace CrmCartorio.Api.Infrastructure.Identity;

/// <summary>
/// Configuração da Central de IA — área do desenvolvedor da equipe do projeto, fora do
/// modelo Atendente/Tenant. Ver Modules/CentralDeIa/README.md.
/// </summary>
public class OpcoesDaCentralDeIa
{
    public const string Secao = "CentralDeIa";

    /// <summary>
    /// Segredo compartilhado, comparado em tempo fixo
    /// (<c>System.Security.Cryptography.CryptographicOperations.FixedTimeEquals</c>,
    /// mesmo padrão de <c>CanalWhatsApp:TokenDoWebhook</c>). Nunca versionado com
    /// valor real — só nos <c>.example.json</c> e no <c>appsettings.&lt;Ambiente&gt;.json</c>
    /// real do servidor.
    /// </summary>
    /// <remarks>
    /// Vazio (padrão) faz <c>CentralDeIaAutenticador</c> nunca aceitar nenhum
    /// segredo — comparação contra string vazia falha sempre. A API sobe
    /// normalmente mesmo sem esta chave configurada; só a Central fica
    /// inacessível, mesmo princípio de "nunca travar por config ausente" já
    /// usado em <c>OpcoesDaIA</c>/<c>OpcoesDoCanalWhatsApp</c>.
    /// </remarks>
    public string Chave { get; set; } = string.Empty;

    /// <summary>
    /// O token da Central não carrega <c>TokenVersion</c> nem qualquer outro
    /// mecanismo de revogação (não existe Atendente por trás dele) — a
    /// expiração curta é a única mitigação. Configurável por ambiente, nunca
    /// pensado para ficar próximo das 8h do token de Atendente.
    /// </summary>
    public int ExpiracaoEmMinutos { get; set; } = 60;
}
