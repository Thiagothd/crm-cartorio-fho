using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.RespostaInteligente.Domain.Entities;

namespace CrmCartorio.Api.Modules.RespostaInteligente.Application.Cota;

/// <summary>
/// Grava <see cref="ChamadaDeIa"/> num escopo de <c>DbContext</c> próprio,
/// independente da transação de negócio que originou a chamada.
/// </summary>
/// <remarks>
/// É o que garante que uma tentativa fique registrada mesmo quando os dois
/// provedores falham e nenhuma <c>Mensagem</c> chega a ser criada, e mesmo quando
/// a transação de negócio da requisição sofre rollback — o log de observabilidade
/// não pode depender do sucesso do fluxo que está observando. Pelo mesmo motivo,
/// uma falha ao gravar aqui nunca propaga: perder uma linha de log é aceitável,
/// derrubar o atendimento por causa dela não é.
/// </remarks>
public class RegistradorDeChamadas(IServiceScopeFactory scopeFactory, ILogger<RegistradorDeChamadas> log)
{
    public async Task RegistrarAsync(ChamadaDeIa chamada)
    {
        try
        {
            using var escopo = scopeFactory.CreateScope();
            var db = escopo.ServiceProvider.GetRequiredService<CrmDbContext>();
            db.ChamadasDeIa.Add(chamada);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception falha)
        {
            log.LogError(
                falha, "Falha ao registrar ChamadaDeIa (Provedor={Provedor}, Tenant={TenantId}).",
                chamada.Provedor, chamada.TenantId);
        }
    }
}
