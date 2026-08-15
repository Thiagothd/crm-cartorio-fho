using CrmCartorio.Api.Modules.Atendimento.Application;
using Microsoft.Extensions.Options;

namespace CrmCartorio.Api.Infrastructure.Automacao;

/// <summary>
/// Chama <see cref="IVarredorDePrazos"/> em intervalo fixo.
/// </summary>
/// <remarks>
/// Casca de hospedagem, sem regra de negocio: quem decide o que e alerta, o que e
/// atraso e que texto sai e o varredor, dentro do modulo Atendimento. Aqui so mora o
/// relogio e o escopo de injecao — que e o que <c>Infrastructure/</c> na raiz pode
/// conter (CLAUDE.md, 7.1).
/// </remarks>
public class HospedeDaVarreduraDePrazos(
    IServiceScopeFactory escopos,
    IOptions<OpcoesDeAutomacao> opcoes,
    ILogger<HospedeDaVarreduraDePrazos> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken pararEm)
    {
        var configuracao = opcoes.Value;

        if (!configuracao.VarrerPrazos)
        {
            log.LogInformation("Varredura de prazo desligada por configuracao (Automacao:VarrerPrazos).");
            return;
        }

        var intervalo = TimeSpan.FromMinutes(Math.Clamp(configuracao.IntervaloEmMinutos, 1, 1440));

        log.LogInformation("Varredura de prazo ativa, a cada {Intervalo}.", intervalo);

        using var relogio = new PeriodicTimer(intervalo);

        // Espera antes da primeira passada: subir a API nao e evento de prazo, e
        // varrer durante a inicializacao competiria com o semeador de demonstracao
        // pelas mesmas tabelas.
        while (await relogio.WaitForNextTickAsync(pararEm))
        {
            try
            {
                using var escopo = escopos.CreateScope();
                var varredor = escopo.ServiceProvider.GetRequiredService<IVarredorDePrazos>();

                var avisadas = await varredor.ExecutarAsync(pararEm);

                if (avisadas > 0)
                {
                    log.LogInformation("Varredura de prazo avisou {Total} conversa(s).", avisadas);
                }
            }
            catch (OperationCanceledException) when (pararEm.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Uma passada com erro — banco fora do ar, por exemplo — nao pode
                // matar o servico de fundo: sem este catch, o BackgroundService
                // encerra e os avisos param de sair em silencio ate reiniciar a API.
                log.LogError(ex, "Falha na varredura de prazo. A proxima passada tentara de novo.");
            }
        }
    }
}
