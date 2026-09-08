using CrmCartorio.Api.Domain.Common;

namespace CrmCartorio.Api.Modules.RespostaInteligente.Domain.Entities;

/// <summary>
/// Um item de conhecimento aprovado, cadastrado por um humano, que
/// <see cref="Application.MontadorDeContexto"/> injeta no grounding da IA.
/// </summary>
/// <remarks>
/// Diferente de <see cref="Configuracoes.Domain.Entities.RespostaRapida"/> — que é
/// atalho de digitação do atendente e só de raspão alimenta a IA — este cadastro
/// existe para governar o que a IA sabe: quem criou, quem editou por último, em
/// que versão está, e por quanto tempo vale. A IA nunca escreve aqui; só um
/// atendente com <c>PermissaoDoSistema.IaConhecimentoEditar</c> pode.
///
/// Cada edição preserva o estado anterior em
/// <see cref="HistoricoDeConhecimentoDeIa"/> antes de sobrescrever — ver
/// <see cref="Application.ConhecimentoDeIaService"/>.
/// </remarks>
public class ConhecimentoDeIa : EntidadeBase, IEntidadeDeTenant
{
    public Guid TenantId { get; set; }

    /// <summary>Rótulo curto que identifica o conhecimento na lista.</summary>
    public required string Titulo { get; set; }

    /// <summary>Texto que entra no grounding da IA.</summary>
    public required string Conteudo { get; set; }

    /// <summary>Categoria livre — usada quando o conhecimento não é de um serviço específico.</summary>
    public required string Tema { get; set; }

    /// <summary>Vínculo opcional a um serviço do catálogo.</summary>
    public Guid? TipoServicoId { get; set; }

    /// <summary>Nulo = vale desde sempre.</summary>
    public DateOnly? VigenteDesde { get; set; }

    /// <summary>Nulo = vale para sempre.</summary>
    public DateOnly? VigenteAte { get; set; }

    /// <summary>Incrementada a cada edição. Começa em 1.</summary>
    public int Versao { get; set; } = 1;

    /// <summary>Nulo quando o registro veio de uma migração retroativa.</summary>
    public Guid? CriadoPorAtendenteId { get; set; }

    /// <summary>Quem fez a última edição. Nulo enquanto o registro nunca foi editado.</summary>
    public Guid? AtualizadoPorAtendenteId { get; set; }

    public bool Ativo { get; set; } = true;
}
