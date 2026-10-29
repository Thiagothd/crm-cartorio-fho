using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Modules.Devedores.Domain.Enums;

namespace CrmCartorio.Api.Modules.Devedores.Domain.Entities;

/// <summary>
/// Registro de um valor em aberto de um cliente junto ao cartório (emolumentos,
/// certidão, escritura, procuração, custas complementares ou serviço deixado em
/// confiança pelo tabelião/substituto).
/// </summary>
public class PendenciaFinanceira : EntidadeBase, IEntidadeDeTenant
{
    public Guid TenantId { get; set; }

    public Guid ClienteId { get; set; }

    /// <summary>Conversa onde a pendência nasceu, quando aplicável.</summary>
    public Guid? ConversaOrigemId { get; set; }

    /// <summary>Serviço ou ato do catálogo, quando aplicável.</summary>
    public Guid? TipoServicoId { get; set; }

    public Guid RegistradoPorAtendenteId { get; set; }

    public Guid? QuitadoPorAtendenteId { get; set; }

    /// <summary>
    /// Resumo curto do motivo/ato (ex.: "2ª via de certidão", "Reconhecimento de firma").
    /// Fica em claro para permitir busca parcial em SQL.
    /// </summary>
    public required string Titulo { get; set; }

    /// <summary>Detalhes livres sobre o acordo. Cifrado em repouso (PR3).</summary>
    public string? Observacoes { get; set; }

    /// <summary>
    /// Valor combinado originalmente, em reais. Nulável quando o responsável quer
    /// apenas marcar o contato como devedor antes de fechar a conta dos emolumentos.
    /// </summary>
    public decimal? ValorOriginal { get; set; }

    /// <summary>Soma acumulada dos pagamentos parciais ou totais registrados.</summary>
    public decimal ValorPago { get; set; }

    /// <summary>Data em que o serviço foi prestado ou a pendência surgiu.</summary>
    public DateOnly DataReferencia { get; set; }

    /// <summary>Data combinada para o cliente acertar o valor, quando houver.</summary>
    public DateOnly? DataPromessaPagamento { get; set; }

    public StatusPendencia Status { get; set; } = StatusPendencia.Aberta;

    /// <summary>Data/hora UTC do último lembrete ou cobrança enviado pelo WhatsApp.</summary>
    public DateTime? UltimaCobrancaEm { get; set; }

    /// <summary>Quantidade de cobranças registradas/enviadas para esta pendência.</summary>
    public int TotalCobrancas { get; set; }

    public DateTime? QuitadaEm { get; set; }

    public DateTime? CanceladaEm { get; set; }

    public ICollection<PagamentoDePendencia> Pagamentos { get; set; } = [];
}
