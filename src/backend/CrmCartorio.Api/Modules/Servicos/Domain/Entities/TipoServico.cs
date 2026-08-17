using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Modules.Servicos.Domain.Enums;

namespace CrmCartorio.Api.Modules.Servicos.Domain.Entities;

/// <summary>
/// Catalogo de atos que a serventia presta. Configuravel por tenant.
/// </summary>
public class TipoServico : EntidadeBase, IEntidadeDeTenant
{
    public Guid TenantId { get; set; }

    /// <summary>Unico por tenant. Ex.: "Reconhecimento de firma".</summary>
    public required string Nome { get; set; }

    /// <summary>Codigo interno do cartorio, se houver.</summary>
    public string? Codigo { get; set; }

    public CategoriaServico Categoria { get; set; } = CategoriaServico.Outros;

    public string? Descricao { get; set; }

    /// <summary>
    /// Setor que oferece este servico no menu de 2o nivel da triagem automatica
    /// (depois que o cliente ja escolheu o setor). Nulo de proposito para servico
    /// ainda "aguardando confirmacao" de atribuicao (ver <see cref="Descricao"/>) —
    /// sem SetorId, o servico nao entra em nenhum menu automatico ate confirmado.
    /// </summary>
    public Guid? SetorId { get; set; }

    /// <summary>
    /// Checklist de documento em texto livre, cadastravel pela tela. Populado no
    /// marcador <c>{DocumentosNecessarios}</c> das mensagens automaticas.
    /// </summary>
    public string? DocumentosNecessarios { get; set; }

    public bool Ativo { get; set; } = true;

    /// <summary>SLA deste servico. Relacao 1:1, opcional.</summary>
    public PrazoServico? Prazo { get; set; }
}
