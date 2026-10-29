namespace CrmCartorio.Api.Modules.Devedores.Domain.Enums;

/// <summary>
/// Meio pelo qual o cliente pagou parcial ou totalmente uma pendência financeira.
/// </summary>
public enum MeioDePagamento : byte
{
    Pix = 1,
    Dinheiro = 2,
    Cartao = 3,
    Transferencia = 4,
    Outro = 5,
}
