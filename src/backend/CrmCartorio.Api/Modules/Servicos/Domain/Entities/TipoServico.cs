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

    public bool Ativo { get; set; } = true;

    /// <summary>SLA deste servico. Relacao 1:1, opcional.</summary>
    public PrazoServico? Prazo { get; set; }
}
