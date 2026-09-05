using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Atendimento.Application;
using CrmCartorio.Api.Modules.CanalWhatsApp.Application;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Infrastructure.Automacao;

/// <summary>
/// Drena <see cref="FilaDeEnvioDoCanal"/> e entrega de fato pelo canal, fora do
/// tempo de resposta de quem agendou.
/// </summary>
/// <remarks>
/// Casca de hospedagem, sem regra de negocio: quem decide O QUE enviar e a
/// mensagem que ja foi gravada como <c>Pendente</c> por quem chamou
/// <see cref="IFilaDeEnvioDoCanal.Agendar"/>; aqui so mora o consumo da fila e
/// o escopo de injecao (CLAUDE.md, 7.1) — mesmo papel de
/// <see cref="HospedeDaVarreduraDePrazos"/>.
/// </remarks>
public class HospedeDaFilaDeEnvioDoCanal(
    FilaDeEnvioDoCanal fila,
    IServiceScopeFactory escopos,
    ILogger<HospedeDaFilaDeEnvioDoCanal> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken pararEm)
    {
        await foreach (var conversaId in fila.LerAsync(pararEm))
        {
            try
            {
                using var escopo = escopos.CreateScope();
                var provedor = escopo.ServiceProvider;

                await provedor.GetRequiredService<IEnviadorDeCanal>()
                    .EnviarPendentesAsync(conversaId, pararEm);

                await NotificarAsync(
                    provedor.GetRequiredService<CrmDbContext>(),
                    provedor.GetRequiredService<INotificadorDeAtendimento>(),
                    conversaId,
                    pararEm);
            }
            catch (OperationCanceledException) when (pararEm.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Uma falha aqui nao pode matar a fila inteira: a proxima conversa
                // agendada tem de continuar sendo processada mesmo que esta tenha
                // estourado (mesma postura de HospedeDaVarreduraDePrazos).
                log.LogError(
                    ex, "Falha ao entregar mensagens pendentes da conversa {ConversaId}.", conversaId);
            }
        }
    }

    /// <remarks>
    /// Mesmo formato de <c>RecebimentoDeWhatsApp.NotificarAsync</c>: falha de
    /// notificacao nao desfaz o envio, que ja aconteceu — o painel ve o status
    /// certo na proxima leitura de qualquer forma.
    /// </remarks>
    private async Task NotificarAsync(
        CrmDbContext db, INotificadorDeAtendimento notificador, Guid conversaId, CancellationToken ct)
    {
        try
        {
            var tenantId = await db.Conversas.AsNoTracking()
                .Where(c => c.Id == conversaId)
                .Select(c => (Guid?)c.TenantId)
                .FirstOrDefaultAsync(ct);

            if (tenantId is not { } id)
            {
                return;
            }

            var projecao = await ProjecaoDeConversa
                .De(db, db.Conversas.AsNoTracking().Where(c => c.Id == conversaId))
                .FirstAsync(ct);

            await notificador.ConversaAtualizadaAsync(id, projecao.ParaResposta(DateTime.UtcNow), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(
                ex,
                "Envio da conversa {ConversaId} concluido, mas a notificacao em tempo real falhou.",
                conversaId);
        }
    }
}
