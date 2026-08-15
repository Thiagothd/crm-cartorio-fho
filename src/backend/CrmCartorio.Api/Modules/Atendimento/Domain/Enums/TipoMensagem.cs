namespace CrmCartorio.Api.Modules.Atendimento.Domain.Enums;

/// <summary>
/// Na Fase 1 so existe <see cref="Texto"/>. Os demais valores ja estao reservados
/// porque a Cloud API entrega midia na Fase 2 e mudar enum persistido depois e caro.
/// </summary>
public enum TipoMensagem : byte
{
    Texto = 1,
    Imagem = 2,
    Documento = 3,
    Audio = 4,
    Video = 5
}
