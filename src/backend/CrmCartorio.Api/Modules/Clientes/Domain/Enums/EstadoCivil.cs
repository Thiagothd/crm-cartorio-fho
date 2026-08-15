namespace CrmCartorio.Api.Modules.Clientes.Domain.Enums;

/// <summary>
/// Estado civil da pessoa fisica. Nao e campo decorativo: entra na qualificacao
/// das partes em escritura e procuracao, e determina se o conjuge precisa
/// comparecer ou anuir ao ato.
/// </summary>
public enum EstadoCivil : byte
{
    NaoInformado = 0,
    Solteiro = 1,
    Casado = 2,
    Divorciado = 3,
    Viuvo = 4,
    Separado = 5,

    /// <summary>Uniao estavel reconhecida. Nao e casamento, mas repercute em ato.</summary>
    UniaoEstavel = 6
}
