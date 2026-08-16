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
public interface IEnviadorDeCanal
{
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
}
