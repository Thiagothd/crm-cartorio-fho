namespace CrmCartorio.Api.Modules.Atendimento.Domain.Enums;

/// <summary>
/// Calculada na leitura, nunca persistida. Persistir exigiria um job periodico
/// e o valor mentiria entre duas execucoes.
/// </summary>
public enum SituacaoPrazo : byte
{
    SemPrazo = 0,
    EmDia = 1,
    EmAlerta = 2,
    Atrasada = 3
}
