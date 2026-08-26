namespace CrmCartorio.Api.Modules.RespostaInteligente.Domain.Enums;

/// <summary>
/// Qual provedor de IA atendeu (ou tentou atender) a mensagem. Persistido em
/// <see cref="Entities.ChamadaDeIa"/> como <c>tinyint</c> — a ordem destes valores
/// nunca muda depois de gravado no banco.
/// </summary>
public enum ProvedorDeIa : byte
{
    /// <summary>Google Gemini — provedor principal, gratuito dentro da franquia.</summary>
    Gemini = 1,

    /// <summary>Anthropic Claude Haiku — contingência paga.</summary>
    Anthropic = 2,
}
