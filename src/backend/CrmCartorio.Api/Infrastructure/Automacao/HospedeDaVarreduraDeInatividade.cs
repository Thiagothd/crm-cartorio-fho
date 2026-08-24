using CrmCartorio.Api.Modules.Atendimento.Application;
using Microsoft.Extensions.Options;

namespace CrmCartorio.Api.Infrastructure.Automacao;

/// <summary>
/// Chama <see cref="IVarredorDeInatividade"/> em intervalo fixo.
/// </summary>
/// <remarks>
/// Casca de hospedagem, sem regra de negocio — mesma divisao de
/// <see cref="HospedeDaVarreduraDePrazos"/>: aqui moram o relogio e o escopo de
/// injecao, e o que conta como conversa parada fica no varredor, dentro do modulo
/// Atendimento.
///
/// <b>Servico proprio, e nao mais uma chamada dentro do hospede de prazos.</b> O
/// <c>catch</c> do laco protege a passada, nao o servico: juntas, uma varredura que
/// falhasse levaria a outra junto na mesma volta. Sao dois relogios independentes
/// porque sao duas responsabilidades independentes.
/// </remarks>
public class HospedeDaVarreduraDeInatividade(
    IServiceScopeFactory escopos,
    IOptions<OpcoesDeAutomacao> opcoes,
    ILogger<HospedeDaVarreduraDeInatividade> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken pararEm)
    {
        var configuracao = opcoes.Value;

        if (!configuracao.VarrerInatividade)
        {
            log.LogInformation(
                "Varredura de inatividade desligada por configuracao (Automacao:VarrerInatividade).");
            return;
        }

        var intervalo = TimeSpan.FromMinutes(
            Math.Clamp(configuracao.IntervaloDeInatividadeEmMinutos, 1, 1440));

        log.LogInformation("Varredura de inatividade ativa, a cada {Intervalo}.", intervalo);

        using var relogio = new PeriodicTimer(intervalo);

        // Espera antes da primeira passada, pelo mesmo motivo do hospede de prazos:
        // subir a API nao e evento de inatividade, e varrer durante a inicializacao
        // competiria com o semeador pelas mesmas tabelas.
        while (await relogio.WaitForNextTickAsync(pararEm))
        {
            try
            {
                using var escopo = escopos.CreateScope();
                var varredor = escopo.ServiceProvider.GetRequiredService<IVarredorDeInatividade>();

                var tratadas = await varredor.ExecutarAsync(pararEm);

                if (tratadas > 0)
                {
                    log.LogInformation(
                        "Varredura de inatividade tratou {Total} conversa(s).", tratadas);
                }
            }
            catch (OperationCanceledException) when (pararEm.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Uma passada com erro nao pode matar o servico de fundo: sem este
                // catch o BackgroundService encerra e as conversas param de ser
                // encerradas em silencio, ate alguem reiniciar a API.
                log.LogError(
                    ex, "Falha na varredura de inatividade. A proxima passada tentara de novo.");
            }
        }
    }
}
