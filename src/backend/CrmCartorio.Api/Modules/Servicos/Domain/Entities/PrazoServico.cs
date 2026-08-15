using CrmCartorio.Api.Domain.Common;

namespace CrmCartorio.Api.Modules.Servicos.Domain.Entities;

/// <summary>
/// SLA de um tipo de servico. Relacao 1:1 com <see cref="TipoServico"/>.
/// </summary>
/// <remarks>
/// Prazo em HORAS, nao em dias, porque a faixa e larga: reconhecimento de firma
/// sai em minutos e registro de imovel leva dias. Hora e a menor unidade que
/// cobre os dois extremos sem virar dois campos.
/// </remarks>
public class PrazoServico : EntidadeBase, IEntidadeDeTenant
{
    public Guid TenantId { get; set; }

    public Guid TipoServicoId { get; set; }

    public int PrazoHoras { get; set; }

    /// <summary>
    /// Percentual do prazo consumido a partir do qual a conversa entra em alerta.
    /// Faixa aceita: 1 a 99.
    /// </summary>
    public int PercentualAlerta { get; set; } = 80;

    /// <summary>Se verdadeiro, o calculo do prazo-limite pula sabado e domingo.</summary>
    public bool ApenasDiasUteis { get; set; } = true;

    public bool Ativo { get; set; } = true;

    public TipoServico? TipoServico { get; set; }
}
