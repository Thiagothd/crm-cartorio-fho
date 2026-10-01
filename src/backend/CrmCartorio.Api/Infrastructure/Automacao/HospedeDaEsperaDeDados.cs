using CrmCartorio.Api.Modules.Atendimento.Application;
using Microsoft.Extensions.Options;

namespace CrmCartorio.Api.Infrastructure.Automacao;

/// <summary>
/// Chama <see cref="IVarredorDeDadosDoServico"/> em intervalo fixo, de poucos segundos.
/// </summary>
/// <remarks>
/// Casca de hospedagem, sem regra de negocio — mesma divisao de
/// <see cref="HospedeDaVarreduraDeInatividade"/>.
///
/// <b>Relogio proprio, e nao mais uma fase da varredura de inatividade.</b> Aquela roda
/// de minuto em minuto; aqui o cliente esta olhando o celular esperando a chave Pix, e
/// o atraso maximo da resposta e o silencio configurado mais um intervalo. Com 45 s de
/// silencio e 10 s de intervalo, a instrucao sai entre 45 e 55 s depois da ultima
/// mensagem de dados.
/// </remarks>
public class HospedeDaEsperaDeDados(
    IServiceScopeFactory escopos,
    IOptions<OpcoesDeAutomacao> opcoes,
    ILogger<HospedeDaEsperaDeDados> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken pararEm)
    {
        var configuracao = opcoes.Value;

        if (!configuracao.EsperarDadosDoServico)
        {
            log.LogInformation(
                "Espera de dados do servico desligada por configuracao (Automacao:EsperarDadosDoServico).");
            return;
        }

        var intervalo = TimeSpan.FromSeconds(Math.Clamp(configuracao.IntervaloDaEsperaDeDadosEmSegundos, 5, 300));
        var silencio = TimeSpan.FromSeconds(Math.Clamp(configuracao.SegundosDeSilencioAntesDaInstrucao, 10, 600));

        log.LogInformation(
            "Espera de dados do servico ativa, a cada {Intervalo}, respondendo apos {Silencio} de silencio.",
            intervalo, silencio);

        using var relogio = new PeriodicTimer(intervalo);

        while (await relogio.WaitForNextTickAsync(pararEm))
        {
            try
            {
                using var escopo = escopos.CreateScope();
                var varredor = escopo.ServiceProvider.GetRequiredService<IVarredorDeDadosDoServico>();

                var tratadas = await varredor.ExecutarAsync(silencio, pararEm);

                if (tratadas > 0)
                {
                    log.LogInformation("Espera de dados do servico respondeu {Total} conversa(s).", tratadas);
                }
            }
            catch (OperationCanceledException) when (pararEm.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Uma passada com erro nao pode matar o servico de fundo: sem este catch,
                // quem mandou os dados ficaria sem a instrucao ate alguem reiniciar a API.
                log.LogError(ex, "Falha na espera de dados do servico. A proxima passada tentara de novo.");
            }
        }
    }
}
