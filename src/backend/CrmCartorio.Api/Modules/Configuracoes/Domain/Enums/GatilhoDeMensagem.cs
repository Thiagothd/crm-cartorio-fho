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
    RespostaSemCorrespondencia = 11,

    /// <summary>
    /// A conversa ficou em silencio pelo tempo configurado na serventia. Pergunta
    /// ao cliente se pode encerrar, antes de encerrar sozinho.
    /// </summary>
    /// <remarks>
    /// Nao reusa <see cref="Despedida"/> de proposito: aquele texto e o "obrigado
    /// por escolher o cartorio" de quem JA escolheu fechar. Este e uma pergunta, e
    /// precisa oferecer as duas saidas — o texto cadastrado deve trazer o menu
    /// numerado (1 encerra, 2 continua), que e o que
    /// <see cref="Atendimento.Domain.TriagemAutomatica.LerRespostaDeEncerramento"/>
    /// le do outro lado.
    ///
    /// Sem regra ativa para este gatilho a serventia simplesmente nao pergunta —
    /// e, por consequencia, nao encerra por inatividade. Perguntar nada e fechar
    /// em silencio seria pior do que nao fazer nada.
    /// </remarks>
    ConfirmacaoDeEncerramento = 12,

    /// <summary>
    /// O cliente nao respondeu a <see cref="ConfirmacaoDeEncerramento"/> dentro do
    /// tempo configurado, e a conversa foi encerrada sozinha.
    /// </summary>
    /// <remarks>
    /// Diferente dos outros gatilhos, este NAO e condicao para o efeito acontecer:
    /// sem regra cadastrada a conversa encerra do mesmo jeito, so sem a cortesia
    /// de avisar. Ver <see cref="Atendimento.Application.VarredorDeInatividade"/>,
    /// que documenta por que esta e uma divergencia deliberada do
    /// <see cref="Atendimento.Application.VarredorDePrazos"/>.
    /// </remarks>
    EncerramentoPorInatividade = 13,

    /// <summary>
    /// O contato chegou sem um nome utilizavel no perfil do WhatsApp (".", emoji,
    /// vazio). Pergunta como pode chamar a pessoa ANTES de mandar o menu.
    /// </summary>
    /// <remarks>
    /// Nao reusa <see cref="NovoAtendimento"/> porque nao substitui as boas-vindas:
    /// e um passo a MAIS, que acontece antes delas e so para quem cai neste caso.
    /// Quem tem nome no perfil nunca ve esta mensagem, e o fluxo dele fica
    /// exatamente como sempre foi.
    ///
    /// <b>O texto cadastrado nao deve usar <c>{NomeCliente}</c>.</b> E a unica
    /// mensagem do sistema que sai justamente porque nao ha nome utilizavel —
    /// o marcador resolveria para vazio e a frase sairia sem o vocativo, o que
    /// funciona, mas escrever "Ola, {NomeCliente}!" aqui e pedir a mensagem que o
    /// gatilho existe para evitar. <c>{Protocolo}</c> e <c>{NomeCartorio}</c> sao
    /// bem-vindos.
    ///
    /// Sem regra ativa a serventia simplesmente nao pergunta: o recebimento cai
    /// para <see cref="NovoAtendimento"/> na mesma passada, e o cliente recebe o
    /// menu de sempre. Conversa muda seria pior que "Ola!".
    /// </remarks>
    PedidoDeNome = 14,

    /// <summary>
    /// Um atendente encerrou o atendimento pelo painel. Avisa o cliente de que a
    /// conversa foi fechada.
    /// </summary>
    /// <remarks>
    /// <b>Nao reusa <see cref="Despedida"/> de proposito.</b> Aquele texto e a voz
    /// de quem foi atendido ate o fim e DECIDIU fechar ("obrigado por escolher o
    /// cartorio"). Aqui quem fechou foi a serventia — pode ter sido resolvido no
    /// balcao, pode ter sido fim de expediente. A mesma frase nos dois casos soa
    /// como se o sistema nao soubesse a diferenca.
    ///
    /// Ate 2026-09-04 este era o UNICO dos quatro caminhos de encerramento que
    /// fechava em silencio: o cliente parava de ser respondido sem saber que o
    /// atendimento tinha acabado. Os outros tres ja avisavam
    /// (<see cref="Despedida"/> quando e o cliente que escolhe encerrar,
    /// <see cref="EncerramentoPorInatividade"/> na varredura automatica).
    ///
    /// Como <see cref="EncerramentoPorInatividade"/>, e cortesia e NAO condicao
    /// para o efeito: sem regra cadastrada a conversa encerra do mesmo jeito, so
    /// sem avisar. Prender o encerramento manual do atendente a existencia de um
    /// texto seria dar a um cadastro poder de veto sobre uma acao humana.
    ///
    /// <b>O texto nao tem como nomear quem encerrou nem repetir o motivo:</b> nao
    /// existe marcador de atendente em
    /// <see cref="Domain.PlaceholdersDeMensagem"/>, e o motivo digitado no
    /// encerramento e registro interno. O par natural existe do outro lado — a
    /// mensagem que o atendente escreve sai assinada com o nome dele
    /// (ver <c>CanalWhatsApp.Application.AssinaturaDeAutoria</c>).
    ///
    /// Quem provoca: <see cref="Atendimento.Application.ConversaService"/>
    /// (<c>EncerrarAsync</c>), e so ele.
    /// </remarks>
    EncerramentoPeloAtendente = 15,

    /// <summary>
    /// Depois do texto de um serviço que só orienta (ex.: registro de nascimento),
    /// pergunta se ficou dúvida.
    /// </summary>
    /// <remarks>
    /// Pedido do Thiago em 2026-09-30: sem isto o cliente lia a orientação, recebia o
    /// menu de continuação e a conversa acabava encerrada por inatividade sem ninguém
    /// saber se ele entendeu. A resposta é lida por
    /// <see cref="Atendimento.Domain.TriagemAutomatica.LerRespostaDeDuvida"/> — o texto
    /// cadastrado deve oferecer "1" para encerrar e "2" para falar com atendente; o
    /// que não for isso é a dúvida, e a IA responde. A pergunta volta depois de cada
    /// resposta da IA, então o texto precisa funcionar nas duas situações.
    ///
    /// Sem regra ativa, o serviço volta a terminar no <see cref="MenuDeContinuacao"/>,
    /// como antes. Ver <see cref="Atendimento.Application.AplicadorDeDuvidaDoServico"/>.
    /// </remarks>
    PerguntaDeDuvida = 16
}
