using CrmCartorio.Api.Domain.Common;

namespace CrmCartorio.Api.Modules.Usuarios.Domain.Entities;

/// <summary>
/// SEC-03: um dos 10 codigos de recuperacao de MFA de um atendente. 16
/// caracteres Crockford Base32 (80 bits) gerados de uma vez na ativacao ou na
/// regeneracao; o texto claro so existe na resposta daquele momento.
/// </summary>
public class CodigoDeRecuperacaoMfa : EntidadeBase
{
    public required Guid TenantId { get; set; }

    public required Guid AtendenteId { get; set; }

    /// <summary>SHA-256 do codigo normalizado (maiusculas, sem hifen). Entrada de alta entropia — PBKDF2 aqui so criaria custo de DoS sem ganho.</summary>
    public required string HashDoCodigo { get; set; }

    /// <summary>Nulo enquanto disponivel. Consumo e atomico e definitivo — nunca reutilizavel.</summary>
    public DateTime? UsadoEm { get; set; }
}
