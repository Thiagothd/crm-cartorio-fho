using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Modules.Devedores.Domain.Enums;

namespace CrmCartorio.Api.Modules.Devedores.Domain.Entities;

/// <summary>
/// Baixa parcial ou total realizada sobre uma <see cref="PendenciaFinanceira"/>.
/// </summary>
public class PagamentoDePendencia : EntidadeBase, IEntidadeDeTenant
{
    public Guid TenantId { get; set; }

    public Guid PendenciaFinanceiraId { get; set; }

    public Guid RegistradoPorAtendenteId { get; set; }

    public decimal Valor { get; set; }

    public MeioDePagamento MeioPagamento { get; set; } = MeioDePagamento.Pix;

    /// <summary>Observação do recebimento (ex.: comprovante, portador). Cifrada em repouso (PR3).</summary>
    public string? Observacao { get; set; }

    public DateTime PagoEm { get; set; }
}
