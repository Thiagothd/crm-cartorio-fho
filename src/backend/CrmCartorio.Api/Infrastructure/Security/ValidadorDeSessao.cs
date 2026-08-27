using CrmCartorio.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Infrastructure.Security;

/// <summary>
/// SEC-01: confere se a sessao carregada nas claims de um JWT ja validado
/// (assinatura, issuer/audience, expiracao) ainda corresponde ao estado atual
/// do atendente no banco. E o mecanismo de revogacao — sem ele, um token so
/// deixa de funcionar quando expira, mesmo depois de troca de senha,
/// desativacao ou mudanca de Perfil.
/// </summary>
/// <remarks>
/// Usado nos dois pontos de entrada autenticados do sistema: a autorizacao de
/// controller (<see cref="SessaoAtivaHandler"/>, via a policy padrao) e o
/// handshake do SignalR (<see cref="Realtime.AtendimentoHub.OnConnectedAsync"/>,
/// em defesa de profundidade). Uma conexao SignalR JA aberta antes de uma
/// revogacao nao e reavaliada por este componente — ver SEC-01B no README do
/// modulo Usuarios.
/// </remarks>
public interface IValidadorDeSessao
{
    Task<bool> SessaoValidaAsync(
        Guid atendenteId,
        Guid tenantId,
        int tokenVersionDaClaim,
        CancellationToken ct = default);
}

public class ValidadorDeSessao(CrmDbContext db) : IValidadorDeSessao
{
    public async Task<bool> SessaoValidaAsync(
        Guid atendenteId,
        Guid tenantId,
        int tokenVersionDaClaim,
        CancellationToken ct = default)
    {
        if (atendenteId == Guid.Empty || tenantId == Guid.Empty)
        {
            return false;
        }

        // Consulta minima, por PK + TenantId, sem rastreamento: e o unico
        // custo extra que este mecanismo adiciona a cada request autenticado.
        var estado = await db.Atendentes.AsNoTracking()
            .Where(a => a.Id == atendenteId && a.TenantId == tenantId)
            .Select(a => new { a.Ativo, a.TokenVersion })
            .FirstOrDefaultAsync(ct);

        return estado is not null && estado.Ativo && estado.TokenVersion == tokenVersionDaClaim;
    }
}
