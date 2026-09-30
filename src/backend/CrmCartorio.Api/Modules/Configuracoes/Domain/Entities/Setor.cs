using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Enums;

namespace CrmCartorio.Api.Modules.Configuracoes.Domain.Entities;

/// <summary>
/// Area da serventia que atende — eixo de ROTEAMENTO da conversa.
/// Nao confundir com <see cref="EtapaFunil"/>, que e o eixo de PROGRESSAO.
/// Tabela configuravel: o cartorio renomeia e reordena pela tela, sem tocar em codigo.
/// </summary>
public class Setor : EntidadeBase, IEntidadeDeTenant
{
    public Guid TenantId { get; set; }

    /// <summary>Unico por tenant. Ex.: "Tabelionato de Notas".</summary>
    public required string Nome { get; set; }

    public string? Descricao { get; set; }

    public int Ordem { get; set; }

    /// <summary>Desativar nao afeta conversa historica que aponta para este setor.</summary>
    public bool Ativo { get; set; } = true;

    /// <summary>
    /// Como o robô trata este setor: ligado, só equipe (quem escolhe vai direto para
    /// uma pessoa) ou fora do menu. Ver <see cref="ModoDoRoboNoSetor"/>.
    /// </summary>
    public ModoDoRoboNoSetor ModoDoRobo { get; set; } = ModoDoRoboNoSetor.Ligado;
}
