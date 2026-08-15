using CrmCartorio.Api.Modules.Atendimento.Application;
using Microsoft.AspNetCore.SignalR;

namespace CrmCartorio.Api.Infrastructure.Realtime;

/// <summary>
/// Adaptador SignalR da porta <see cref="INotificadorDeAtendimento"/>.
/// Todo evento vai para o grupo do tenant — nunca em broadcast global.
/// </summary>
public class NotificadorDeAtendimento(IHubContext<AtendimentoHub> hub) : INotificadorDeAtendimento
{
    public Task ConversaAtribuidaAsync(Guid tenantId, ConversaResumoResponse conversa, CancellationToken ct = default) =>
        Enviar(tenantId, "ConversaAtribuida", conversa, ct);

    public Task ConversaLiberadaAsync(Guid tenantId, ConversaResumoResponse conversa, CancellationToken ct = default) =>
        Enviar(tenantId, "ConversaLiberada", conversa, ct);

    public Task ConversaTransferidaAsync(Guid tenantId, ConversaResumoResponse conversa, CancellationToken ct = default) =>
        Enviar(tenantId, "ConversaTransferida", conversa, ct);

    public Task ConversaAtualizadaAsync(Guid tenantId, ConversaResumoResponse conversa, CancellationToken ct = default) =>
        Enviar(tenantId, "ConversaAtualizada", conversa, ct);

    public Task ConversaEncerradaAsync(Guid tenantId, ConversaResumoResponse conversa, CancellationToken ct = default) =>
        Enviar(tenantId, "ConversaEncerrada", conversa, ct);

    public Task ConversaReabertaAsync(Guid tenantId, ConversaResumoResponse conversa, CancellationToken ct = default) =>
        Enviar(tenantId, "ConversaReaberta", conversa, ct);

    public Task MensagemRecebidaAsync(
        Guid tenantId,
        MensagemResponse mensagem,
        ConversaResumoResponse conversa,
        CancellationToken ct = default) =>
        hub.Clients
            .Group(AtendimentoHub.GrupoDoTenant(tenantId))
            .SendAsync("MensagemRecebida", new { mensagem, conversa }, ct);

    private Task Enviar(Guid tenantId, string evento, ConversaResumoResponse conversa, CancellationToken ct) =>
        hub.Clients
            .Group(AtendimentoHub.GrupoDoTenant(tenantId))
            .SendAsync(evento, conversa, ct);
}
