namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <summary>
/// Porta de SAIDA do atendimento: entrega ao cliente o que foi escrito no painel.
/// </summary>
/// <remarks>
/// Porta neste slice, adaptador fora — mesma disciplina de
/// <see cref="INotificadorDeAtendimento"/>. O Atendimento nao sabe que existe uma
/// Evolution API do outro lado; trocar para a Cloud API oficial da Meta troca o
/// adaptador e nada mais.
///
/// <b>O gate por canal e responsabilidade do adaptador, nao de quem chama.</b>
/// Conversa <c>Simulado</c> (as 11 da base de demonstracao) nao pode sair para
/// WhatsApp nenhum: ligar o canal com o gate no chamador faria a primeira demo
/// tentar mandar mensagem para telefones ficticios.
/// </remarks>
/// <summary>
/// O que o canal conseguiu fazer com uma mensagem que ja tinha saido.
/// </summary>
/// <remarks>
/// Tres estados, e nao um booleano, porque as tres respostas levam a lugares
/// diferentes na tela: <see cref="NaoSeAplica"/> nao e erro nenhum (conversa de
/// demonstracao, canal desligado) e a correcao vale so no fio;
/// <see cref="Recusado"/> e o WhatsApp dizendo nao — a mensagem continua como
/// estava no aparelho do cliente, e o painel nao pode fingir o contrario.
/// </remarks>
public enum ResultadoNoCanal
{
    /// <summary>Conversa que nao sai por WhatsApp, ou canal desligado.</summary>
    NaoSeAplica = 0,

    /// <summary>O WhatsApp aceitou.</summary>
    Feito = 1,

    /// <summary>O WhatsApp recusou — tipicamente a janela dele ja tinha fechado.</summary>
    Recusado = 2
}

public interface IEnviadorDeCanal
{
    /// <summary>
    /// O canal real esta ligado e vinculado a esta serventia?
    /// </summary>
    /// <remarks>
    /// E o que decide o canal do atendimento aberto pelo painel ("Novo
    /// atendimento"). Ate 2026-10-28 ele nascia sempre <c>Simulado</c>, resto da
    /// Fase 1: o cartório parceiro abriu atendimentos para clientes reais, escreveu, mandou
    /// PDF, e nada saiu — o fio mostrava um tique so, sem erro nenhum.
    ///
    /// O padrao <c>false</c> mantem o comportamento antigo em quem nao tem canal
    /// (dev, demonstracao, os doubles dos verificadores): sem canal, a conversa
    /// continua <c>Simulado</c> e nada tenta sair.
    /// </remarks>
    Task<bool> AtendeOTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(false);

    /// <summary>
    /// Entrega a mensagem pelo canal da conversa.
    /// </summary>
    /// <returns>
    /// O id externo da mensagem quando ela saiu de verdade; <c>null</c> quando o
    /// canal nao se aplica (conversa simulada, canal desligado).
    /// </returns>
    Task<string?> EnviarAsync(Guid mensagemId, CancellationToken ct = default);

    /// <summary>
    /// Entrega as mensagens automaticas que ainda estao <c>Pendente</c> na conversa.
    /// </summary>
    /// <remarks>
    /// Mensagem automatica nasce <c>Pendente</c> desde a Fase 1 — era "o unico
    /// estado honesto sem canal conectado". Agora ha canal, e este metodo e o que
    /// as tira do papel.
    ///
    /// Existe como varredura por conversa, e nao como envio por mensagem, porque o
    /// disparador ENFILEIRA sem gravar: quem chama decide o <c>SaveChanges</c>. So
    /// depois do commit da para entregar, e nesse ponto o que se tem em maos e a
    /// conversa, nao a lista de ids.
    /// </remarks>
    Task EnviarPendentesAsync(Guid conversaId, CancellationToken ct = default);

    /// <summary>
    /// Troca, no aparelho do cliente, o texto de uma mensagem ja entregue.
    /// </summary>
    /// <remarks>
    /// Recebe o texto LIMPO, como fica gravado; a assinatura de autoria e
    /// remontada pelo adaptador, exatamente como no envio. Quem chama nao sabe
    /// (nem deve saber) que existe cabecalho em negrito do outro lado.
    /// </remarks>
    Task<ResultadoNoCanal> CorrigirAsync(
        Guid mensagemId,
        string novoConteudo,
        CancellationToken ct = default);

    /// <summary>
    /// Apaga a mensagem no aparelho do cliente. O registro no fio continua.
    /// </summary>
    Task<ResultadoNoCanal> ApagarParaTodosAsync(
        Guid mensagemId,
        CancellationToken ct = default);
}
