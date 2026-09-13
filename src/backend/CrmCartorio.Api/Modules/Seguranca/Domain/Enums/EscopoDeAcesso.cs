namespace CrmCartorio.Api.Modules.Seguranca.Domain.Enums;

/// <summary>O que uma <c>AutorizacaoDeAcessoTecnico</c> libera enxergar.</summary>
public enum EscopoDeAcesso : byte
{
    /// <summary>
    /// Acesso total, temporário — todas as telas e funções do sistema
    /// (decisão do Thiago em 2026-09-13; ver <c>ValidadorDeSessao</c>).
    /// </summary>
    Geral = 1,

    /// <summary>Só a conversa nomeada em <c>ConversaId</c> — o pedido de suporte mais comum.</summary>
    Conversa = 2
}
