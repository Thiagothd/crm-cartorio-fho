namespace CrmCartorio.Api.Modules.Atendimento.Domain.Enums;

/// <summary>O que acontece quando alguém liga o robô de novo numa conversa.</summary>
/// <remarks>
/// Quem escolhe é o atendente, na hora (decisão de 2026-09-30): ele sabe se o cliente
/// ainda está no meio de um assunto ou se precisa recomeçar.
/// </remarks>
public enum ModoDeRetomadaDoRobo : byte
{
    /// <summary>Manda as boas-vindas com o menu de setores, como um atendimento novo.</summary>
    MenuInicial = 1,

    /// <summary>Não manda nada: o robô responde a próxima mensagem do cliente.</summary>
    SoResponder = 2
}
