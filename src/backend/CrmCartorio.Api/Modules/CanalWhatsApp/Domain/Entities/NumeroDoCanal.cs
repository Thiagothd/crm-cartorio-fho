using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Modules.CanalWhatsApp.Domain.Enums;

namespace CrmCartorio.Api.Modules.CanalWhatsApp.Domain.Entities;

/// <summary>
/// Um número liberado ou bloqueado para falar com o canal desta serventia.
/// </summary>
/// <remarks>
/// Qual lista este registro pertence é o próprio <see cref="Tipo"/> — não há
/// duas tabelas, porque o índice único por telefone (ver
/// <c>NumeroDoCanalConfiguration</c>) já impede o mesmo número de estar nas
/// duas ao mesmo tempo, e ter tabelas separadas permitiria exatamente essa
/// contradição.
/// </remarks>
public class NumeroDoCanal : EntidadeBase
{
    public required Guid TenantId { get; set; }

    /// <summary>Só dígitos, com o código do país (ex.: "5538991234567").</summary>
    public required string Telefone { get; set; }

    /// <summary>Nome livre para quem vai reconhecer o número na lista.</summary>
    public string? Rotulo { get; set; }

    public required TipoDeNumero Tipo { get; set; }

    /// <summary>Quem adicionou. Nulo quando o registro veio de uma migração retroativa.</summary>
    public Guid? CriadoPorAtendenteId { get; set; }
}
