namespace CrmCartorio.Api.Modules.Atendimento.Domain.Enums;

public enum CanalOrigem : byte
{
    /// <summary>Cloud API da Meta. Passa a ser usado na Fase 2.</summary>
    WhatsApp = 1,

    /// <summary>Conversa criada pelo painel para demonstracao. Unico canal da Fase 1.</summary>
    Simulado = 9
}
