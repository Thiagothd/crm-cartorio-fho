namespace CrmCartorio.Api.Modules.Atendimento.Domain.Enums;

/// <summary>
/// Nao existe valor "Excluida". Encerrar arquiva; o conteudo permanece integro
/// e consultavel. Exigencia explicita do cliente (RF-015).
/// </summary>
public enum StatusConversa : byte
{
    Ativa = 1,
    Encerrada = 2
}
