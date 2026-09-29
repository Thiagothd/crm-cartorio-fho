using CrmCartorio.Api.Infrastructure.Tenancy;
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
    ICartoriosDeSegundoPlano cartorios,
    ILogger<HospedeDaEsperaDeDados> log) : BackgroundService
{
    // Abaixo do intervalo: um cartorio preso nao pode empurrar a rodada seguinte.
    private readonly RodadaDosCartorios _rodada = new(
        "Espera de dados do servico", log, paralelismo: 4, tempoLimite: TimeSpan.FromSeconds(8));

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
            await Task.WhenAll(PassadaDoPilotoAsync(silencio, pararEm), PassadaDosCartoriosAsync(silencio, pararEm));
        }
    }

    private async Task PassadaDoPilotoAsync(TimeSpan silencio, CancellationToken pararEm)
    {
        try
        {
            await PassadaAsync(null, silencio, pararEm);
        }
        catch (OperationCanceledException) when (pararEm.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // Uma passada com erro nao pode matar o servico de fundo: sem este catch,
            // quem mandou os dados ficaria sem a instrucao ate alguem reiniciar a API.
            log.LogError(ex, "Falha na espera de dados do servico. A proxima passada tentara de novo.");
        }
    }

    /// <summary>Uma passada num banco. Escopo nulo e' o piloto.</summary>
    private async Task PassadaAsync(CartorioDaRequisicao? cartorio, TimeSpan silencio, CancellationToken pararEm)
    {
        using var escopoDoCartorio = cartorio is null ? null : EscopoDeCartorio.Entrar(cartorio);
        using var escopo = escopos.CreateScope();
        var varredor = escopo.ServiceProvider.GetRequiredService<IVarredorDeDadosDoServico>();

        var tratadas = await varredor.ExecutarAsync(silencio, pararEm);

        if (tratadas > 0)
        {
            log.LogInformation(
                "Espera de dados do servico respondeu {Total} conversa(s) em '{Banco}'.",
                tratadas, cartorio?.Slug ?? "piloto");
        }
    }

    private async Task PassadaDosCartoriosAsync(TimeSpan silencio, CancellationToken pararEm)
    {
        IReadOnlyList<CartorioDaRequisicao> lista;
        try
        {
            lista = await cartorios.ListarAsync(pararEm);
        }
        catch (OperationCanceledException) when (pararEm.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao listar cartorios para a espera de dados do servico.");
            return;
        }

        try
        {
            await _rodada.ExecutarAsync(lista, (cartorio, ct) => PassadaAsync(cartorio, silencio, ct), pararEm);
        }
        catch (OperationCanceledException) when (pararEm.IsCancellationRequested)
        {
        }
    }
}
