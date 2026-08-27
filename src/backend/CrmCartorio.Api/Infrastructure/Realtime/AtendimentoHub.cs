using CrmCartorio.Api.Infrastructure.Identity;
using CrmCartorio.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CrmCartorio.Api.Infrastructure.Realtime;

/// <summary>
/// Canal em tempo real do painel de atendimento. Cada conexao entra no grupo do
/// proprio tenant, o que garante que nenhum evento de um cartorio chegue a outro.
/// </summary>
/// <remarks>
/// SEC-01: <c>[Authorize]</c> ja usa a policy padrao (Program.cs), que inclui
/// <see cref="SessaoAtivaRequirement"/> — um handshake novo com JWT revogado e
/// recusado antes mesmo de <see cref="OnConnectedAsync"/> rodar. A checagem em
/// <see cref="IValidadorDeSessao"/> feita aqui dentro e defesa em profundidade,
/// nao a unica linha de defesa.
///
/// SEC-01B — limitacao que continua aberta: isto cobre HANDSHAKE NOVO, nao uma
/// conexao ja estabelecida antes da revogacao. Uma aba com WebSocket aberto
/// continua recebendo eventos do grupo do tenant ate cair por outro motivo
/// (queda de rede, fechar a aba, reciclagem do App Pool, ou o JWT expirar de
/// verdade — o ASP.NET Core fecha conexao de SignalR cujo token expira, mas
/// isso e sobre o `exp` do token, nao sobre TokenVersion). Encerrar a conexao
/// ativa no instante da revogacao exigiria rastrear ConnectionId por
/// AtendenteId e abortar a conexao explicitamente — infraestrutura fora do
/// escopo deste PR (ver Modules/Usuarios/README.md).
/// </remarks>
[Authorize]
public class AtendimentoHub(IValidadorDeSessao validador) : Hub
{
    public static string GrupoDoTenant(Guid tenantId) => $"tenant:{tenantId}";

    public override async Task OnConnectedAsync()
    {
        var atendenteIdClaim = Context.User?.FindFirst(ClaimsDoAtendente.AtendenteId)?.Value;
        var tenantClaim = Context.User?.FindFirst(ClaimsDoAtendente.TenantId)?.Value;
        var tokenVersionClaim = Context.User?.FindFirst(ClaimsDoAtendente.TokenVersion)?.Value;

        if (!Guid.TryParse(atendenteIdClaim, out var atendenteId) ||
            !Guid.TryParse(tenantClaim, out var tenantId) ||
            tenantId == Guid.Empty ||
            !int.TryParse(tokenVersionClaim, out var tokenVersion))
        {
            Context.Abort();
            return;
        }

        var valida = await validador.SessaoValidaAsync(atendenteId, tenantId, tokenVersion, Context.ConnectionAborted);

        if (!valida)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GrupoDoTenant(tenantId));

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
