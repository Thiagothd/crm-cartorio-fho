namespace CrmCartorio.Api.Modules.Atendimento.Domain.Enums;

public enum AutorTipo : byte
{
    Cliente = 1,

    /// <summary>Exige AtendenteId preenchido — e o requisito RF-007.</summary>
    Atendente = 2,

    /// <summary>Evento registrado no fio da conversa (assumida, etapa alterada, encerrada).</summary>
    Sistema = 3
}
