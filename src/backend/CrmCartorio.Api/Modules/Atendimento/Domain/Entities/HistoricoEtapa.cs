using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;

namespace CrmCartorio.Api.Modules.Atendimento.Domain.Entities;

/// <summary>
/// Trilha de mudancas de etapa e de setor de uma conversa. So cresce: nunca e
/// editada nem apagada. As duas dimensoes ficam na mesma tabela porque uma unica
/// acao do atendente pode mover as duas ao mesmo tempo, e separar em duas tabelas
/// quebraria a linha do tempo.
/// </summary>
public class HistoricoEtapa : EntidadeBase, IEntidadeDeTenant
{
    public Guid TenantId { get; set; }

    public Guid ConversaId { get; set; }

    public TipoMudancaHistorico TipoMudanca { get; set; }

    public Guid? EtapaAnteriorId { get; set; }

    public Guid? EtapaNovaId { get; set; }

    public Guid? SetorAnteriorId { get; set; }

    public Guid? SetorNovoId { get; set; }

    /// <summary>Nulo quando a mudanca foi automatica.</summary>
    public Guid? AtendenteId { get; set; }

    public DateTime OcorridoEm { get; set; } = DateTime.UtcNow;

    public string? Observacao { get; set; }

    public Conversa? Conversa { get; set; }
}
