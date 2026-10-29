namespace CrmCartorio.Api.Modules.Devedores.Domain.Enums;

/// <summary>
/// Ciclo de vida de uma pendência financeira no cartório.
/// </summary>
public enum StatusPendencia : byte
{
    Aberta = 1,
    Quitada = 2,
    Cancelada = 3,
}
