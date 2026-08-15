using CrmCartorio.Api.Domain.Common;

namespace CrmCartorio.Api.Modules.Configuracoes.Domain.Entities;

/// <summary>
/// Texto pronto que o atendente insere no campo de resposta digitando "/atalho".
/// Cadastro editavel pela tela.
/// </summary>
/// <remarks>
/// NAO e mensagem automatica: nada sai daqui sozinho. O atalho preenche o campo e
/// o atendente le, edita se quiser e envia. A distincao importa porque as duas
/// coisas parecem iguais no banco (um texto com marcadores) e sao opostas na
/// responsabilidade: <see cref="MensagemAutomatica"/> o sistema assina, esta o
/// atendente assina.
/// </remarks>
public class RespostaRapida : EntidadeBase, IEntidadeDeTenant
{
    public Guid TenantId { get; set; }

    /// <summary>
    /// Palavra digitada depois da barra. Guardada sem a barra, em minusculas e sem
    /// espaco — unica por tenant.
    /// </summary>
    public required string Atalho { get; set; }

    /// <summary>Rotulo curto que aparece na lista do seletor.</summary>
    public required string Titulo { get; set; }

    /// <summary>Texto inserido no campo. Aceita os marcadores de <see cref="PlaceholdersDeMensagem"/>.</summary>
    public required string Conteudo { get; set; }

    public bool Ativo { get; set; } = true;
}
