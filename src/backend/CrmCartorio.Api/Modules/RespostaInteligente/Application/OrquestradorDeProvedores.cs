using CrmCartorio.Api.Modules.RespostaInteligente.Application.Cota;
using CrmCartorio.Api.Modules.RespostaInteligente.Application.Provedores;
using CrmCartorio.Api.Modules.RespostaInteligente.Domain.Entities;
using CrmCartorio.Api.Modules.RespostaInteligente.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrmCartorio.Api.Modules.RespostaInteligente.Application;

public interface IOrquestradorDeProvedores
{
    Task<RespostaDaIA> ResponderAsync(Guid tenantId, Guid? conversaId, PedidoDeIa pedido, CancellationToken ct);
}

/// <summary>
/// Escolhe o provedor, tenta, registra a tentativa e cai para o outro quando
/// necessário. Gemini primeiro, Haiku por trás — a troca é transparente para
/// quem chama: <see cref="ResponderAsync"/> devolve no máximo uma
/// <see cref="RespostaDaIA"/>, nunca duas, e nunca lança (exceto o cancelamento
/// de quem chamou).
/// </summary>
/// <remarks>
/// Os dois tetos diários (<see cref="OpcoesDaIA.LimiteDiario"/> e
/// <see cref="OpcoesDoAnthropic.LimiteDiarioHaiku"/>) são reservados ANTES da
/// tentativa e confirmados só no sucesso — nunca dois debitados por uma única
/// resposta entregue, mesmo quando o Gemini falha e o Haiku assume.
/// </remarks>
public class OrquestradorDeProvedores(
    [FromKeyedServices(ChaveDoProvedor.Gemini)] IProvedorDeIa gemini,
    [FromKeyedServices(ChaveDoProvedor.Anthropic)] IProvedorDeIa anthropic,
    PoliticaDeSelecao politica,
    DisjuntorDoGemini disjuntor,
    ContadorDeCotaDoGemini cotaDoGemini,
    ContadorDeLimiteDiario limiteDiario,
    RegistradorDeChamadas registrador,
    IOptions<OpcoesDaIA> opcoes,
    ILogger<OrquestradorDeProvedores> log) : IOrquestradorDeProvedores
{
    public async Task<RespostaDaIA> ResponderAsync(
        Guid tenantId, Guid? conversaId, PedidoDeIa pedido, CancellationToken ct)
    {
        var opcoesDaIA = opcoes.Value;

        var reservouGeral = await limiteDiario.TentarReservarAsync(
            tenantId, CategoriaDeLimiteDiario.Geral, opcoesDaIA.LimiteDiario, ct);

        if (!reservouGeral)
        {
            log.LogWarning(
                "Limite diario geral de {Limite} respostas da IA atingido para o tenant {TenantId}.",
                opcoesDaIA.LimiteDiario, tenantId);

            return NaoRespondeu("Limite diário de respostas da IA atingido por hoje.");
        }

        var respondeuComSucesso = false;

        try
        {
            var decisao = await politica.DecidirAsync(opcoesDaIA.Gemini, ct);
            var motivoDoFallback = decisao.MotivoParaPular;

            if (decisao.TentarOGemini)
            {
                var resultado = await TentarProvedorAsync(
                    gemini, pedido, tenantId, conversaId, decisao.ChaveDeProjeto,
                    foiFallback: false, motivoDoFallback: null, ct);

                if (resultado.Sucesso)
                {
                    disjuntor.RegistrarSucesso(decisao.ChaveDeProjeto!, decisao.EhSonda);

                    await cotaDoGemini.RegistrarTokensReaisAsync(
                        decisao.ChaveDeProjeto!,
                        resultado.TokensEntrada + resultado.TokensSaida + resultado.TokensPensamento,
                        ct);

                    respondeuComSucesso = true;
                    return resultado.Resposta!;
                }

                disjuntor.RegistrarFalha(
                    decisao.ChaveDeProjeto!, resultado.Falha, resultado.CodigoHttp, resultado.RetryApos,
                    decisao.EhSonda, opcoesDaIA.Gemini);

                if (resultado.Falha == TipoDeFalhaDeIa.QuotaEsgotada)
                {
                    await cotaDoGemini.RegistrarEstouroDeQuotaDiariaAsync(decisao.ChaveDeProjeto!, ct);
                }

                motivoDoFallback = DescreverFalha("Gemini", resultado);
            }

            // Chega aqui tanto quando o Gemini foi tentado e falhou quanto quando
            // foi pulado preventivamente (sem chave, disjuntor aberto, margem de
            // cota atingida) — os dois casos usam o Haiku do mesmo jeito.
            if (!anthropic.EstaConfigurado)
            {
                return NaoRespondeu(motivoDoFallback ?? "Gemini indisponível e Haiku sem chave configurada.");
            }

            var reservouHaiku = await limiteDiario.TentarReservarAsync(
                tenantId, CategoriaDeLimiteDiario.Haiku, opcoesDaIA.Anthropic.LimiteDiarioHaiku, ct);

            if (!reservouHaiku)
            {
                log.LogWarning(
                    "Limite diario do Haiku ({Limite}) atingido para o tenant {TenantId}.",
                    opcoesDaIA.Anthropic.LimiteDiarioHaiku, tenantId);

                var motivo = motivoDoFallback is null
                    ? "Teto diário do Haiku atingido."
                    : $"{motivoDoFallback} Teto diário do Haiku também atingido.";

                return NaoRespondeu(motivo);
            }

            var resultadoHaiku = await TentarProvedorAsync(
                anthropic, pedido, tenantId, conversaId, chaveDeProjeto: null,
                foiFallback: true, motivoDoFallback, ct);

            if (resultadoHaiku.Sucesso)
            {
                respondeuComSucesso = true;
                return resultadoHaiku.Resposta!;
            }

            // A reserva do teto do Haiku foi feita para uma chamada que acabou
            // falhando — devolve a vaga, senao um dia inteiro de Haiku fora do ar
            // esgotaria o teto sem produzir nenhuma resposta de verdade.
            await limiteDiario.LiberarAsync(tenantId, CategoriaDeLimiteDiario.Haiku, ct);

            // Motivo final cita as DUAS falhas quando o Gemini chegou a ser
            // tentado — só a falha do Haiku, sozinha, esconderia por que o
            // Gemini não assumiu primeiro (achado pelo verificador, cenário 7).
            var motivoDaFalhaDoHaiku = DescreverFalha("Haiku", resultadoHaiku);
            var motivoFinal = motivoDoFallback is null
                ? motivoDaFalhaDoHaiku
                : $"{motivoDoFallback} | {motivoDaFalhaDoHaiku}";

            return NaoRespondeu(motivoFinal);
        }
        finally
        {
            if (!respondeuComSucesso)
            {
                await limiteDiario.LiberarAsync(tenantId, CategoriaDeLimiteDiario.Geral, ct);
            }
        }
    }

    private async Task<ResultadoDoProvedor> TentarProvedorAsync(
        IProvedorDeIa provedor,
        PedidoDeIa pedido,
        Guid tenantId,
        Guid? conversaId,
        string? chaveDeProjeto,
        bool foiFallback,
        string? motivoDoFallback,
        CancellationToken ct)
    {
        ResultadoDoProvedor resultado;

        try
        {
            resultado = await provedor.GerarAsync(pedido, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception falha)
        {
            // Rede de seguranca final: os provedores ja classificam a propria
            // falha, mas nada impede uma excecao nao prevista escapar (bug de
            // desserializacao, etc.) — mesmo assim isto nunca pode propagar.
            log.LogWarning(falha, "{Provedor}: falha nao capturada pelo proprio provedor.", provedor.Identidade);
            resultado = ResultadoDoProvedor.DeFalha(TipoDeFalhaDeIa.Indisponibilidade, null, 0, falha.Message);
        }

        var chamada = new ChamadaDeIa
        {
            TenantId = tenantId,
            ConversaId = conversaId,
            ChaveDeProjeto = chaveDeProjeto,
            Provedor = provedor.Identidade,
            Modelo = provedor.Modelo,
            IniciadaEm = DateTime.UtcNow,
            LatenciaMs = resultado.LatenciaMs,
            Sucesso = resultado.Sucesso,
            PodeResponder = resultado.Sucesso ? resultado.Resposta!.PodeResponder : null,
            TipoDeFalha = resultado.Falha,
            CodigoHttp = resultado.CodigoHttp,
            MotivoDoFallback = foiFallback ? motivoDoFallback : null,
            FoiFallback = foiFallback,
            TokensEntrada = resultado.TokensEntrada,
            TokensSaida = resultado.TokensSaida,
            TokensPensamento = resultado.TokensPensamento,
            CustoEstimadoUsd = CalcularCustoEstimado(provedor.Identidade, resultado),
            NivelDeRaciocinio = provedor.Identidade == ProvedorDeIa.Gemini ? opcoes.Value.Gemini.NivelDeRaciocinio : null
        };

        await registrador.RegistrarAsync(chamada);

        return resultado;
    }

    private decimal? CalcularCustoEstimado(ProvedorDeIa provedor, ResultadoDoProvedor resultado)
    {
        if (provedor != ProvedorDeIa.Anthropic || !resultado.Sucesso)
        {
            // Gemini e sempre nulo (gratuito dentro da franquia); Anthropic so
            // tem custo quando de fato produziu tokens de saida.
            return null;
        }

        var opcoesAnthropic = opcoes.Value.Anthropic;
        var custoEntrada = resultado.TokensEntrada / 1_000_000m * opcoesAnthropic.CustoPorMilhaoEntradaUsd;
        var custoSaida = resultado.TokensSaida / 1_000_000m * opcoesAnthropic.CustoPorMilhaoSaidaUsd;
        return custoEntrada + custoSaida;
    }

    private static string DescreverFalha(string provedor, ResultadoDoProvedor resultado) =>
        $"{provedor}: {resultado.Falha} ({(resultado.CodigoHttp is { } codigo ? codigo.ToString() : "sem codigo")}) — {resultado.Detalhe}";

    private static RespostaDaIA NaoRespondeu(string motivo) => new(false, string.Empty, motivo);
}
