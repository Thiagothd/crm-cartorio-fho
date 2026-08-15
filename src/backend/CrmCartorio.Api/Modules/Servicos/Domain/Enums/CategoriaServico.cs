namespace CrmCartorio.Api.Modules.Servicos.Domain.Enums;

/// <summary>
/// Agrupamento do ato cartorario. Diferente de Setor: a categoria classifica o
/// SERVICO no catalogo; o setor diz QUEM atende, e e configuravel pelo cartorio.
/// </summary>
public enum CategoriaServico : byte
{
    Notas = 1,
    RegistroCivil = 2,
    RegistroImoveis = 3,
    TitulosDocumentos = 4,
    Protesto = 5,
    Outros = 9
}
