using CrmCartorio.Api.Domain.Common;

namespace CrmCartorio.Api.Modules.RespostaInteligente.Domain.Entities;

/// <summary>
/// Snapshot de um <see cref="ConhecimentoDeIa"/> tirado imediatamente ANTES de
/// cada edição. Só cresce: nunca é editada nem apagada — mesmo espírito de
/// <see cref="Atendimento.Domain.Entities.HistoricoEtapa"/>.
/// </summary>
/// <remarks>
/// É o que torna o conhecimento "inspecionável e corrigível": qualquer edição em
/// <see cref="ConhecimentoDeIa"/> primeiro grava aqui o estado que estava valendo,
/// depois aplica a mudança. Consultar por <see cref="ConhecimentoDeIaId"/> reconstrói
/// a linha do tempo completa de um item.
/// </remarks>
public class HistoricoDeConhecimentoDeIa : EntidadeBase, IEntidadeDeTenant
{
    public Guid TenantId { get; set; }

    public Guid ConhecimentoDeIaId { get; set; }

    /// <summary>Número da versão que este snapshot representa.</summary>
    public int Versao { get; set; }

    public required string Titulo { get; set; }

    public required string Conteudo { get; set; }

    public required string Tema { get; set; }

    public Guid? TipoServicoId { get; set; }

    public DateOnly? VigenteDesde { get; set; }

    public DateOnly? VigenteAte { get; set; }

    public bool Ativo { get; set; }

    /// <summary>Nulo quando o registro veio de uma migração retroativa.</summary>
    public Guid? AlteradoPorAtendenteId { get; set; }

    public DateTime OcorridoEm { get; set; } = DateTime.UtcNow;
}
