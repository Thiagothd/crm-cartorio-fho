using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Usuarios.Application;

/// <summary>
/// SEC-03: contador de falhas de SEGUNDO FATOR por conta — compartilhado entre
/// <see cref="AutenticacaoService"/> (etapa 2 do login) e <see cref="MfaService"/>
/// (step-up de operacoes sensiveis), porque as duas superficies precisam
/// alimentar o MESMO contador: quem ja tem a senha pode tentar o segundo fator
/// tanto logando quanto chamando uma acao de gestao, e a protecao so vale se
/// nenhum dos dois caminhos escapar dela.
/// </summary>
/// <remarks>
/// Mesma progressao de <c>FalhasDeLoginConsecutivas</c> (SEC-02): 5 falhas
/// fecham um ciclo e abrem 1, 2, 4, 8, 16 ou 30 minutos de bloqueio, teto no
/// sexto nivel. Contador PROPRIO — nunca somado ao de senha.
/// </remarks>
public static class BloqueioDeMfa
{
    public const int LimiteDeFalhas = 5;
    private const byte TetoDoNivel = 6;

    /// <remarks>
    /// O <c>WHERE</c> nao estende um bloqueio ja vigente (tentativa durante
    /// bloqueio nao conta, regra explicita de SEC-03) nem toca conta inativa.
    /// </remarks>
    public static async Task RegistrarFalhaAsync(CrmDbContext db, Guid atendenteId, DateTime agora, CancellationToken ct)
    {
        var afetadas = await db.Atendentes
            .Where(a => a.Id == atendenteId
                     && a.Ativo
                     && (a.MfaBloqueadoAte == null || a.MfaBloqueadoAte <= agora))
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.MfaFalhasConsecutivas,
                    a => a.MfaFalhasConsecutivas + 1 >= LimiteDeFalhas
                        ? 0
                        : a.MfaFalhasConsecutivas + 1)
                .SetProperty(a => a.MfaNivelDeBloqueio,
                    a => a.MfaFalhasConsecutivas + 1 < LimiteDeFalhas
                        ? a.MfaNivelDeBloqueio
                        : a.MfaNivelDeBloqueio >= TetoDoNivel
                            ? TetoDoNivel
                            : (byte)(a.MfaNivelDeBloqueio + 1))
                .SetProperty(a => a.MfaBloqueadoAte,
                    a => a.MfaFalhasConsecutivas + 1 < LimiteDeFalhas ? a.MfaBloqueadoAte
                       : a.MfaNivelDeBloqueio >= 5 ? agora.AddMinutes(30)
                       : a.MfaNivelDeBloqueio == 4 ? agora.AddMinutes(16)
                       : a.MfaNivelDeBloqueio == 3 ? agora.AddMinutes(8)
                       : a.MfaNivelDeBloqueio == 2 ? agora.AddMinutes(4)
                       : a.MfaNivelDeBloqueio == 1 ? agora.AddMinutes(2)
                       : agora.AddMinutes(1)), ct);

        if (afetadas != 1)
        {
            return;
        }

        var estado = await db.Atendentes.AsNoTracking()
            .Where(a => a.Id == atendenteId)
            .Select(a => new { a.TenantId, a.MfaBloqueadoAte })
            .FirstOrDefaultAsync(ct);

        if (estado?.MfaBloqueadoAte is { } bloqueadoAte && bloqueadoAte > agora)
        {
            await AutenticacaoService.RegistrarEventoAsync(
                db, estado.TenantId, atendenteId, atorAtendenteId: null, atorExterno: null,
                TipoDeEventoDeSeguranca.BloqueioDeMfaPorFalhas, ip: null, motivo: null, CancellationToken.None);
        }
    }
}
