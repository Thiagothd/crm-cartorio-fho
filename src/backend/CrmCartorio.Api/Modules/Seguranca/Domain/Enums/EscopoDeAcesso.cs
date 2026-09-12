namespace CrmCartorio.Api.Modules.Seguranca.Domain.Enums;

/// <summary>O que uma <c>AutorizacaoDeAcessoTecnico</c> libera enxergar.</summary>
public enum EscopoDeAcesso : byte
{
    /// <summary>Qualquer conversa/ficha do tenant — mesmo alcance de um atendente comum.</summary>
    Geral = 1,

    /// <summary>Só a conversa nomeada em <c>ConversaId</c> — o pedido de suporte mais comum.</summary>
    Conversa = 2
}
