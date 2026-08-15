namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <summary>
/// Porta de saida do slice para o tempo real. A implementacao (SignalR) fica em
/// Infrastructure/Realtime: o slice descreve o que precisa avisar, nao como.
/// </summary>
public interface INotificadorDeAtendimento
{
    Task ConversaAtribuidaAsync(Guid tenantId, ConversaResumoResponse conversa, CancellationToken ct = default);

    Task ConversaLiberadaAsync(Guid tenantId, ConversaResumoResponse conversa, CancellationToken ct = default);

    Task ConversaTransferidaAsync(Guid tenantId, ConversaResumoResponse conversa, CancellationToken ct = default);

    Task ConversaAtualizadaAsync(Guid tenantId, ConversaResumoResponse conversa, CancellationToken ct = default);

    Task ConversaEncerradaAsync(Guid tenantId, ConversaResumoResponse conversa, CancellationToken ct = default);

    Task ConversaReabertaAsync(Guid tenantId, ConversaResumoResponse conversa, CancellationToken ct = default);

    Task MensagemRecebidaAsync(Guid tenantId, MensagemResponse mensagem, ConversaResumoResponse conversa, CancellationToken ct = default);
}
