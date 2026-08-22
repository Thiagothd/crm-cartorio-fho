namespace CrmCartorio.Api.Modules.CanalWhatsApp.Domain.Enums;

/// <summary>Papel de uma linha em <c>CanalWhatsappNumeros</c>.</summary>
public enum TipoDeNumero : byte
{
    /// <summary>Número autorizado, usado quando o modo é <see cref="ModoDeRecebimento.SomenteLiberados"/>.</summary>
    Liberado = 1,

    /// <summary>Número calado, usado quando o modo é <see cref="ModoDeRecebimento.TodosMenosBloqueados"/>.</summary>
    Bloqueado = 2
}
