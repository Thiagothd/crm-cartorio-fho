using CrmCartorio.Api.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CrmCartorio.Api.Infrastructure.Realtime;

/// <summary>
/// Canal em tempo real do painel de atendimento. Cada conexao entra no grupo do
/// proprio tenant, o que garante que nenhum evento de um cartorio chegue a outro.
/// </summary>
[Authorize]
public class AtendimentoHub : Hub
{
    public static string GrupoDoTenant(Guid tenantId) => $"tenant:{tenantId}";

    public override async Task OnConnectedAsync()
    {
        var tenant = Context.User?.FindFirst(ClaimsDoAtendente.TenantId)?.Value;

        if (Guid.TryParse(tenant, out var tenantId) && tenantId != Guid.Empty)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GrupoDoTenant(tenantId));
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? excecao)
    {
        var tenant = Context.User?.FindFirst(ClaimsDoAtendente.TenantId)?.Value;

        if (Guid.TryParse(tenant, out var tenantId) && tenantId != Guid.Empty)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GrupoDoTenant(tenantId));
        }

        await base.OnDisconnectedAsync(excecao);
    }
}
