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
    ServicoEscolhido = 7,

    /// <summary>
    /// O atendimento estruturado terminou (checklist entregue). Oferece continuar
    /// (outro servico, outro setor), falar com atendente ou encerrar.
    /// </summary>
    /// <remarks>
    /// E o que fecha o buraco achado ao vivo 2026-08-17: sem isto, qualquer coisa
    /// que o cliente escrevesse depois do checklist (ate um "obrigada") caia fora
    /// de toda flag de estado e nao gerava resposta nenhuma.
    /// </remarks>
    MenuDeContinuacao = 8,

    /// <summary>
    /// Encaminha para atendente NO MEIO ou DEPOIS do atendimento — nao no primeiro
    /// contato, que e <see cref="TriagemSemCorrespondencia"/>.
    /// </summary>
    /// <remarks>
    /// Existe porque o texto de <see cref="TriagemSemCorrespondencia"/> foi escrito
    /// para o primeiro "obrigado pelo contato, protocolo X" — sair com essa frase no
    /// meio de um atendimento ja identificado (achado ao vivo) soa como se o sistema
    /// tivesse esquecido o que ja sabe do cliente.
    /// </remarks>
    PedidoDeAtendente = 9,

    /// <summary>O cliente escolheu encerrar pelo <see cref="MenuDeContinuacao"/>.</summary>
    /// <remarks>
    /// Alem da despedida, fecha a Conversa de verdade (Status vira Encerrada) —
    /// ver <see cref="Atendimento.Application.AplicadorDePosAtendimento"/>.
    /// Ate 2026-08-17 so a mensagem saia e a conversa ficava Ativa para sempre;
    /// achado ao vivo e corrigido no mesmo dia.
    /// </remarks>
    Despedida = 10,

    /// <summary>
    /// A IA nao encontrou, no catalogo cadastrado, como responder — e o cliente
    /// NAO pediu atendente explicitamente.
    /// </summary>
    /// <remarks>
    /// Achado ao vivo em 2026-08-18: ate entao, `podeResponder=false` da IA
    /// escalava direto para <see cref="TriagemSemCorrespondencia"/> ou
    /// <see cref="PedidoDeAtendente"/> — e dois clientes reais, no mesmo dia,
    /// receberam "um atendente vai continuar" por terem dito soh "somente" ou
    /// perguntado algo que a IA nao sabia, sem nunca ter pedido atendente. A
    /// regra agora e literal: essas duas mensagens SO disparam por
    /// <see cref="Atendimento.Domain.TriagemAutomatica.PediuHumano"/> explicito.
    /// Este gatilho e o que a IA usa no lugar disso — nao promete atendente,
    /// convida a pedir um se quiser, e mantem
    /// <see cref="Atendimento.Domain.Entities.Conversa.AguardandoRespostaInteligente"/>
    /// ligada: a IA continua ouvindo, nao desiste da conversa.
    /// </remarks>
    RespostaSemCorrespondencia = 11
}
