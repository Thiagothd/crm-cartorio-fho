using CrmCartorio.Api.Domain.Common;

namespace CrmCartorio.Api.Modules.Configuracoes.Domain.Entities;

/// <summary>
/// Estagio da conversa dentro do atendimento — eixo de PROGRESSAO (funil).
/// Nao confundir com <see cref="Setor"/>, que e o eixo de ROTEAMENTO.
/// Tabela configuravel: os nomes sugeridos vieram de pesquisa de mercado e
/// aguardam validacao do cliente, por isso nunca foram para o codigo como enum.
/// </summary>
public class EtapaFunil : EntidadeBase, IEntidadeDeTenant
{
    public Guid TenantId { get; set; }

    /// <summary>Unico por tenant. Ex.: "Captacao do servico".</summary>
    public required string Nome { get; set; }

    public string? Descricao { get; set; }

    /// <summary>Define a sequencia do funil na interface.</summary>
    public int Ordem { get; set; }

    /// <summary>Hex no formato #RRGGBB, usado no quadro e nas etiquetas.</summary>
    public string Cor { get; set; } = "#64748B";

    /// <summary>Marca a etapa que representa conclusao do atendimento.</summary>
    public bool EhFinal { get; set; }

    public bool Ativo { get; set; } = true;
}
