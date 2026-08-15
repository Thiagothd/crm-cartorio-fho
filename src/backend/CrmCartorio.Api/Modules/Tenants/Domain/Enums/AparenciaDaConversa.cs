namespace CrmCartorio.Api.Modules.Tenants.Domain.Enums;

/// <summary>
/// Textura de fundo do fio de mensagens, escolhida pela serventia.
/// </summary>
/// <remarks>
/// Enum, e nao tabela, porque cada valor corresponde a uma classe de estilo que
/// precisa existir no CSS. Textura nova exige release — e isso e proposital: um
/// campo livre aqui deixaria alguem colar uma imagem de papel de parede no fundo
/// de um livro de registro.
///
/// As tres usam apenas tokens da paleta (docs/DESIGN.md). Nenhuma delas troca a
/// cor de fundo: todas continuam sobre `papel`, e a textura e sempre mais
/// discreta que o texto que ela sustenta.
/// </remarks>
public enum AparenciaDaConversa : byte
{
    /// <summary>Papel liso. Só a marca d'água do selo no canto. É o padrão.</summary>
    PapelLiso = 1,

    /// <summary>O anel do selo repetido em grade larga, a 3% de opacidade.</summary>
    SeloRepetido = 2,

    /// <summary>Linhas horizontais finas em `linha`, como papel pautado de livro.</summary>
    PautaFina = 3
}
