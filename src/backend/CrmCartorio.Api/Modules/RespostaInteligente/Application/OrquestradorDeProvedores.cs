using CrmCartorio.Api.Modules.RespostaInteligente.Application.Cota;
using CrmCartorio.Api.Modules.RespostaInteligente.Application.Provedores;
using CrmCartorio.Api.Modules.RespostaInteligente.Domain.Entities;
using CrmCartorio.Api.Modules.RespostaInteligente.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrmCartorio.Api.Modules.RespostaInteligente.Application;

public interface IOrquestradorDeProvedores
{
    Task<RespostaDaIA> ResponderAsync(Guid tenantId, ContextoDaChamada? contexto, PedidoDeIa pedido, CancellationToken ct);
}

/// <summary>
/// Escolhe o provedor, tenta, registra a tentativa e cai para o outro quando
/// necessário. Estruturalmente sempre tenta o Gemini primeiro e o Haiku por
/// trás — a troca é transparente para quem chama: <see cref="ResponderAsync"/>
/// devolve no máximo uma <see cref="RespostaDaIA"/>, nunca duas, e nunca lança
/// (exceto o cancelamento de quem chamou).
/// </summary>
/// <remarks>
/// ACHADO AO VIVO, 2026-09-03: com <c>Gemini.Habilitado=false</c> (decisão
/// temporária, ver <c>Program.cs</c>), <see cref="PoliticaDeSelecao.DecidirAsync"/>
/// devolve <c>TentarOGemini=false</c> antes de qualquer I/O — o Gemini nunca é
/// discado, nenhum tempo é gasto nele, e o Haiku vira o único provedor de fato,
/// sem que este orquestrador precise saber disso.
/// </remarks>
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
        Guid tenantId, ContextoDaChamada? contexto, PedidoDeIa pedido, CancellationToken ct)
    {
        var opcoesDaIA = opcoes.Value;

        // Mesmo valor nas duas tentativas desta pergunta (a que falhar e a que
        // resolver) — é o que permite contar "perguntas" em vez de "tentativas"
        // e somar a latência total que o cliente esperou de verdade.
        var rodadaId = Guid.CreateVersion7();

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

        // Rastreia se a reserva do teto do Haiku chegou a ser CONCEDIDA (nao so
        // tentada) — o finally usa as duas flags juntas para decidir o que
        // devolver: reservou+sucesso = fica consumida; reservou+(falha OU
        // cancelamento em QUALQUER ponto, inclusive durante a propria chamada ao
        // Haiku) = devolve; nunca reservou = nao ha nada para devolver. Um so
        // lugar de liberacao evita tanto o vazamento (visto ao vivo,
        // 2026-08-26: cancelamento durante a chamada ao Haiku pulava a liberacao
        // que antes ficava inline logo apos aquela chamada) quanto a liberacao
        // dupla (a mesma reserva nunca e' devolvida em dois lugares diferentes).
        var reservouHaiku = false;

        try
        {
            var decisao = await politica.DecidirAsync(opcoesDaIA.Gemini, ct);
            var motivoDoFallback = decisao.MotivoParaPular;

            if (decisao.TentarOGemini)
            {
                var resultado = await TentarProvedorAsync(
                    gemini, pedido, tenantId, contexto, rodadaId, decisao.ChaveDeProjeto,
                    tipoDeFallback: TipoDeFallbackDeIa.NaoEhFallback, motivoDoFallback: null, ct);

                if (resultado.Sucesso)
                {
                    disjuntor.RegistrarSucesso(decisao.ChaveDeProjeto!, decisao.EhSonda);

                    // ReservaDeCota (nao ChaveDeProjeto) corrige o slot EXATO que
                    // esta chamada abriu na janela de 1 minuto — nulo para a sonda
                    // (que pula a reserva preventiva de proposito) e' um no-op
                    // seguro dentro do proprio metodo.
                    await cotaDoGemini.RegistrarTokensReaisAsync(
                        decisao.ReservaDeCota,
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

            reservouHaiku = await limiteDiario.TentarReservarAsync(
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
                anthropic, pedido, tenantId, contexto, rodadaId, chaveDeProjeto: null,
                tipoDeFallback: decisao.TentarOGemini ? TipoDeFallbackDeIa.Executado : decisao.TipoSePulou,
                motivoDoFallback, ct);

            if (resultadoHaiku.Sucesso)
            {
                respondeuComSucesso = true;
                return resultadoHaiku.Resposta!;
            }

            // A liberacao da reserva do Haiku NAO acontece aqui — foi movida para
            // o finally externo (junto com a do teto Geral), justamente para
            // cobrir tambem o caminho de cancelamento: se `ct` for cancelado
            // DURANTE TentarProvedorAsync (a chamada ao Haiku acima), a excecao
            // pula direto para o finally sem passar por este ponto. Uma unica
            // liberacao central evita tanto o vazamento (a reserva ficava presa
            // no cancelamento) quanto a liberacao dupla (nunca dois releases da
            // mesma reserva).
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
                // CancellationToken.None: limpeza de reserva tem que acontecer
                // mesmo que quem chamou (o request HTTP) ja tenha cancelado —
                // senao a vaga vaza a cada cancelamento. Cobre os tres jeitos de
                // chegar aqui sem uma resposta final entregue: Gemini e Haiku
                // falharam normalmente; Haiku sem vaga (reservouHaiku fica falso,
                // nada a liberar do lado do Haiku); ou a requisicao foi cancelada
                // em QUALQUER ponto — durante o Gemini, durante a sonda, ou
                // durante a propria chamada ao Haiku (achado ao vivo, 2026-08-26).
                await limiteDiario.LiberarAsync(tenantId, CategoriaDeLimiteDiario.Geral, CancellationToken.None);

                if (reservouHaiku)
                {
                    await limiteDiario.LiberarAsync(tenantId, CategoriaDeLimiteDiario.Haiku, CancellationToken.None);
                }
            }
        }
    }

    private async Task<ResultadoDoProvedor> TentarProvedorAsync(
        IProvedorDeIa provedor,
        PedidoDeIa pedido,
        Guid tenantId,
        ContextoDaChamada? contexto,
        Guid rodadaId,
        string? chaveDeProjeto,
        TipoDeFallbackDeIa tipoDeFallback,
        string? motivoDoFallback,
        CancellationToken ct)
    {
        // Capturado ANTES da chamada ao provedor: IniciadaEm precisa marcar o
        // INICIO da tentativa, nao o fim. Achado ao vivo — o carimbo antigo era
        // `DateTime.UtcNow` avaliado so depois do await abaixo, entao a coluna
        // guardava o instante em que a resposta chegou, nao em que a pergunta
        // comecou a ser processada.
        var comecouEm = DateTime.UtcNow;

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

        var foiFallback = tipoDeFallback != TipoDeFallbackDeIa.NaoEhFallback;

        // Categoria fechada de "por que a IA nao soube" — so significativa
        // quando a tentativa teve sucesso E o modelo disse que nao sabia.
        // Nunca inferida de texto: o campo ja vem estruturado do provedor,
        // com NaoClassificado como piso quando ele nao devolveu nada.
        var motivoDaNaoResposta = resultado.Sucesso && resultado.Resposta!.PodeResponder == false
            ? resultado.Resposta!.CategoriaDaNaoResposta
            : (MotivoDaNaoRespostaDeIa?)null;

        var chamada = new ChamadaDeIa
        {
            TenantId = tenantId,
            ConversaId = contexto?.ConversaId,
            ChaveDeProjeto = chaveDeProjeto,
            Provedor = provedor.Identidade,
            Modelo = provedor.Modelo,
            IniciadaEm = comecouEm,
            LatenciaMs = resultado.LatenciaMs,
            Sucesso = resultado.Sucesso,
            PodeResponder = resultado.Sucesso ? resultado.Resposta!.PodeResponder : null,
            TipoDeFalha = resultado.Falha,
            CodigoHttp = resultado.CodigoHttp,
            MotivoDoFallback = foiFallback ? TruncarParaColuna(motivoDoFallback) : null,
            FoiFallback = foiFallback,
            TipoDeFallback = tipoDeFallback,
            RodadaId = rodadaId,
            MotivoDaNaoResposta = motivoDaNaoResposta,
            TipoServicoId = contexto?.TipoServicoId,
            SetorId = contexto?.SetorId,
            MensagemId = contexto?.MensagemId,
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

    // dbo.ChamadasDeIa.MotivoDoFallback e' NVARCHAR(200) (ver
    // ChamadaDeIaConfiguration.HasMaxLength(200)). A mensagem real do 429 do
    // Gemini free tier, sozinha, ja chega perto do limite — achado ao vivo,
    // 2026-08-26: sem truncar aqui, o SaveChanges falha por tamanho e a linha
    // inteira (a que mais importa para o painel) se perde, engolida pelo
    // try/catch do RegistradorDeChamadas. O log completo continua indo para o
    // ILogger, sem perda — so a coluna do banco e' que precisa caber.
    private const int TamanhoMaximoDoMotivo = 200;

    private static string? TruncarParaColuna(string? motivo) =>
        motivo is { Length: > TamanhoMaximoDoMotivo }
            ? motivo[..TamanhoMaximoDoMotivo]
            : motivo;

    private static RespostaDaIA NaoRespondeu(string motivo) => new(false, string.Empty, motivo);
}
