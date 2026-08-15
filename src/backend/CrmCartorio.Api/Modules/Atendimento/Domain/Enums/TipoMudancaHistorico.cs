namespace CrmCartorio.Api.Modules.Atendimento.Domain.Enums;

public enum TipoMudancaHistorico : byte
{
    /// <summary>Registro inicial, gravado na criacao da conversa.</summary>
    Abertura = 1,
    Etapa = 2,
    Setor = 3,
    EtapaESetor = 4
}
