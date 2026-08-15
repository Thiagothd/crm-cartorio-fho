namespace CrmCartorio.Api.Modules.Clientes.Domain.Enums;

/// <summary>
/// Regime de bens do casamento. So faz sentido quando o estado civil e Casado —
/// e nesse caso e o que decide se o conjuge precisa assinar a escritura.
/// </summary>
public enum RegimeDeBens : byte
{
    NaoInformado = 0,
    ComunhaoParcial = 1,
    ComunhaoUniversal = 2,
    SeparacaoTotal = 3,

    /// <summary>Separacao obrigatoria por lei (art. 1.641 do Codigo Civil).</summary>
    SeparacaoObrigatoria = 4,

    ParticipacaoFinalNosAquestos = 5
}
