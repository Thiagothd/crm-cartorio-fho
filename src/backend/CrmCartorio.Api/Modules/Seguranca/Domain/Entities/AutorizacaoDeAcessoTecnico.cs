using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Modules.Seguranca.Domain.Enums;

namespace CrmCartorio.Api.Modules.Seguranca.Domain.Entities;

/// <summary>
/// Blindagem LGPD: o pedido, aprovado ou não, para a conta técnica da
/// equipe do projeto ler dado de cliente por um tempo limitado. "A equipe do projeto pede, o
/// cartório aprova, a faixa avisa, a trilha registra" —
/// docs/PROTECAO-DE-DADOS.md.
/// </summary>
/// <remarks>
/// O estado (Pendente/Ativa/Expirada/Negada/Revogada) é sempre DERIVADO na
/// leitura a partir dos carimbos abaixo — nunca uma coluna própria, para não
/// existir uma segunda fonte de verdade que possa divergir do que os
/// carimbos realmente dizem. Ver <c>AcessoTecnicoService</c> para a função
/// que deriva isso.
/// </remarks>
public class AutorizacaoDeAcessoTecnico : EntidadeBase
{
    public required Guid TenantId { get; set; }

    /// <summary>O <c>Atendente</c> com <see cref="Usuarios.Domain.Entities.Atendente.EhContaTecnica"/> que pediu.</summary>
    public required Guid ContaTecnicaId { get; set; }

    public required string Motivo { get; set; }

    public required EscopoDeAcesso Escopo { get; set; }

    /// <summary>Preenchida só quando <see cref="Escopo"/> é <see cref="EscopoDeAcesso.Conversa"/>.</summary>
    public Guid? ConversaId { get; set; }

    /// <summary>Teto de 480 min (8h) — reforçado no serviço, nunca confiado só à entrada.</summary>
    public required int DuracaoSolicitadaMinutos { get; set; }

    public required DateTime SolicitadaEm { get; set; }

    public string? IpDeSolicitacao { get; set; }

    public Guid? AprovadaPorAtendenteId { get; set; }

    public DateTime? AprovadaEm { get; set; }

    /// <summary>Nula até aprovar. Uma vez aprovada, nunca estendida — revogar e pedir de novo é o caminho.</summary>
    public DateTime? ValidaAte { get; set; }

    public Guid? NegadaPorAtendenteId { get; set; }

    public DateTime? NegadaEm { get; set; }

    public Guid? RevogadaPorAtendenteId { get; set; }

    public DateTime? RevogadaEm { get; set; }
}
