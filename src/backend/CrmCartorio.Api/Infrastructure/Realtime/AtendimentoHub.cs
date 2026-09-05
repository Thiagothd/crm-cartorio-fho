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
/// SEC-01 — dois mecanismos DIFERENTES, nao confundir:
///
/// 1. REVOGACAO POR TokenVersion (troca de senha, desativacao, mudanca de
///    Perfil). <c>[Authorize]</c> usa a policy padrao (Program.cs), que inclui
///    <see cref="SessaoAtivaRequirement"/> — um handshake NOVO com JWT
///    revogado e recusado antes mesmo de <see cref="OnConnectedAsync"/>
///    rodar. A checagem em <see cref="IValidadorDeSessao"/> feita aqui dentro
///    e defesa em profundidade, nao a unica linha de defesa. Numa chamada
///    REST o mesmo evento derruba o token no proximo request (401).
///
///    SEC-01B — limitacao que continua aberta: o que este mecanismo NAO
///    cobre e uma conexao SignalR JA ABERTA antes da revogacao. Uma aba com
///    WebSocket aberto continua recebendo eventos do grupo do tenant depois
///    que TokenVersion muda — a checagem acima so roda no handshake de uma
///    conexao NOVA, nao a cada mensagem de uma conexao existente. Encerrar a
///    conexao ativa no instante da revogacao exigiria rastrear ConnectionId
///    por AtendenteId e abortar a conexao explicitamente — infraestrutura
///    fora do escopo deste PR (ver Modules/Usuarios/README.md).
///
/// 2. EXPIRACAO DO JWT (`exp`, nada a ver com TokenVersion). Program.cs
///    configura <c>CloseOnAuthenticationExpiration = true</c> no
///    <c>MapHub</c> deste hub — com isso, o ASP.NET Core fecha a conexao
///    quando o token chega ao `exp`, mesmo uma conexao ja aberta. Isto NAO
///    resolve SEC-01B: um token revogado por TokenVersion mas ainda dentro
///    da validade (`exp` no futuro) nao e afetado por
///    CloseOnAuthenticationExpiration — a conexao so cai quando o relogio
///    chega no `exp` original do token, nao quando a versao muda no banco.
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
