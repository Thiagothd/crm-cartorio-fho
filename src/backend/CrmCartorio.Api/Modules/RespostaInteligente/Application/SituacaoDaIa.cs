using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CrmCartorio.Api.Modules.RespostaInteligente.Application;

public class SituacaoDaIa(CrmDbContext db, IContextoDoUsuario ctx, IOptions<OpcoesDaIA> opcoes) : ISituacaoDaIa
{
    public async Task<SituacaoDaIaResponse> ObterAsync(CancellationToken ct = default)
    {
        if (!ctx.PodeGerirCadastros)
        {
            throw RespostaInteligenteServiceException.SemPermissao();
        }

        var valor = opcoes.Value;

        // Mesmo limite de dia (UTC) que RespostaInteligenteService usa para
        // conferir o teto — senao "usado hoje" mostraria um numero diferente do
        // que de fato decide se a IA ainda responde.
        var inicioDoDia = DateTime.UtcNow.Date;

        var usadoHoje = await db.Mensagens.AsNoTracking()
            .CountAsync(m => m.TenantId == ctx.TenantId && m.GeradaPorIa && m.EnviadaEm >= inicioDoDia, ct);

        return new SituacaoDaIaResponse(valor.Habilitada, valor.EstaConfigurada, valor.Modelo, valor.LimiteDiario, usadoHoje);
    }
}
