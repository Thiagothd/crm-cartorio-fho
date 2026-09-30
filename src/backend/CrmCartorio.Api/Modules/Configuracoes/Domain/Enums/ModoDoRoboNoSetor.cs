namespace CrmCartorio.Api.Modules.Configuracoes.Domain.Enums;

/// <summary>
/// Como o robô trata um setor. Pedido do cartório parceiro em 2026-09-30: poder tirar o robô de
/// um setor inteiro sem desligar o sistema todo.
/// </summary>
/// <remarks>
/// Três estados, e não dois interruptores (mensagens e IA) por setor: no meio do
/// expediente a pergunta que se faz é "quem atende este setor, o robô ou a equipe?",
/// e um seletor responde isso sem combinação que ninguém entende. O liga e desliga
/// separado de mensagens e IA continua existindo, mas no geral (chaves do Tenant).
/// </remarks>
public enum ModoDoRoboNoSetor : byte
{
    /// <summary>Como sempre: menu de serviços, textos, IA.</summary>
    Ligado = 1,

    /// <summary>
    /// O setor aparece no menu de setores, mas quem o escolhe vai direto para a
    /// equipe (aviso de pedido de atendente), sem menu de serviços nem IA.
    /// </summary>
    SoEquipe = 2,

    /// <summary>
    /// O setor some do menu de setores do robô. Continua valendo no painel: dá para
    /// mover conversa para ele e filtrar por ele.
    /// </summary>
    ForaDoMenu = 3
}
