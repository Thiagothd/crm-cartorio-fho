using CrmCartorio.Api.Modules.Atendimento.Application;

namespace CrmCartorio.Api.Infrastructure.Automacao;

/// <summary>
/// Acorda processadores por sinal local e por varredura persistente. O canal em
/// memoria reduz latencia; a fila real continua sendo a tabela Mensagens.
/// </summary>
public sealed class HospedeDoProcessamentoDeEntradaWhatsapp(
    IServiceScopeFactory escopos,
    SinalizadorDeEntradaWhatsapp sinalizador,
    ILogger<HospedeDoProcessamentoDeEntradaWhatsapp> log) : BackgroundService
{
    private const int QuantidadeDeWorkers = 4;
    private static readonly TimeSpan IntervaloDeRecuperacao = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken pararEm)
    {
        var workers = Enumerable.Range(0, QuantidadeDeWorkers)
            .Select(_ => ConsumirAsync(pararEm));

        await Task.WhenAll([VarrerAsync(pararEm), .. workers]);
    }

    private async Task ConsumirAsync(CancellationToken ct)
    {
        await foreach (var conversaId in sinalizador.Leitor.ReadAllAsync(ct))
        {
            try
            {
                using var escopo = escopos.CreateScope();
                var processador = escopo.ServiceProvider.GetRequiredService<IProcessadorDeEntradaWhatsapp>();
                await processador.ProcessarConversaAsync(conversaId, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogError(
                    ex,
                    "Falha no worker de entrada WhatsApp da conversa {ConversaId}; a varredura retomara.",
                    conversaId);
            }
        }
    }

    private async Task VarrerAsync(CancellationToken ct)
    {
        // A primeira passada acontece no boot: pode haver entrada aceita antes da
        // reciclagem do IIS que ainda nao foi processada.
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var escopo = escopos.CreateScope();
                var processador = escopo.ServiceProvider.GetRequiredService<IProcessadorDeEntradaWhatsapp>();
                var conversas = await processador.ListarConversasPendentesAsync(ct: ct);

                foreach (var conversaId in conversas)
                {
                    sinalizador.Sinalizar(conversaId);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Falha ao varrer entradas WhatsApp pendentes; a proxima passada tentara novamente.");
            }

            await Task.Delay(IntervaloDeRecuperacao, ct);
        }
    }
}
