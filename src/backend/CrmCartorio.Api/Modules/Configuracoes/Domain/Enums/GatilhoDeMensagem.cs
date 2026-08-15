namespace CrmCartorio.Api.Modules.Configuracoes.Domain.Enums;

/// <summary>
/// Evento do atendimento que dispara uma mensagem automatica.
/// </summary>
/// <remarks>
/// Enum, e nao tabela, porque cada valor corresponde a um ponto de codigo que
/// precisa existir para disparar. Gatilho novo exige codigo novo — diferente do
/// TEXTO da mensagem e de QUAL etapa dispara, que sao cadastro.
/// </remarks>
public enum GatilhoDeMensagem : byte
{
    /// <summary>Conversa aberta. E a mensagem de boas-vindas com o menu de triagem.</summary>
    NovoAtendimento = 1,

    /// <summary>
    /// Conversa passou a uma etapa especifica. Exige
    /// <see cref="Entities.MensagemAutomatica.EtapaAlvoId"/> preenchido.
    /// </summary>
    EtapaAlterada = 2,

    /// <summary>
    /// A conversa consumiu o percentual de alerta do SLA do servico. O percentual
    /// nao e configurado aqui: sai de PrazoServico.PercentualAlerta, o mesmo campo
    /// que acende o alerta amarelo no painel.
    /// </summary>
    PrazoProximoVencimento = 3,

    /// <summary>O prazo-limite do servico passou.</summary>
    PrazoVencido = 4,

    /// <summary>
    /// O cliente respondeu ao menu de triagem com algo que nao corresponde a nenhum
    /// setor. Avisa que o atendimento segue com uma pessoa.
    /// </summary>
    /// <remarks>
    /// Este gatilho e um acrescimo aos quatro pedidos no escopo, e existe porque a
    /// alternativa seria pior: o texto de fallback e frase que o CLIENTE DO CARTORIO
    /// le, e deixar essa frase como constante em C# a tornaria a unica mensagem do
    /// sistema que o cartório parceiro nao pode reescrever sozinho.
    /// </remarks>
    TriagemSemCorrespondencia = 5
}
