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
    TriagemSemCorrespondencia = 5,

    /// <summary>
    /// A triagem reconheceu o setor. Pergunta qual servico dentro dele, com
    /// {MenuServicos}.
    /// </summary>
    /// <remarks>
    /// Mesmo motivo do valor 5: o convite para escolher o servico e frase que o
    /// cliente le, entao e cadastro, nao constante em C#.
    /// </remarks>
    TriagemComCorrespondencia = 6,

    /// <summary>
    /// O cliente escolheu o servico no menu de 2o nivel. Confirma com
    /// {DocumentosNecessarios} e {PrazoServico}.
    /// </summary>
    /// <remarks>
    /// Nao usa <see cref="EtapaAlterada"/> de proposito: EtapaFunil e cadastro sem
    /// papel fixo nenhum (nome e ordem editaveis pelo cartório parceiro, sem enum), entao nao ha
    /// GUID estavel de "etapa de captacao" para o codigo depender. Gatilho proprio
    /// evita acoplar este passo a uma linha de configuracao que pode ser renomeada
    /// ou reordenada a qualquer momento.
    /// </remarks>
    ServicoEscolhido = 7
}
