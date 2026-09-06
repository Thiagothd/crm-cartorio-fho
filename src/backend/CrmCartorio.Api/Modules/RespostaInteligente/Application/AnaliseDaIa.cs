using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.RespostaInteligente.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.RespostaInteligente.Application;

/// <summary>
/// Analytics da Resposta Inteligente para o dono/gestor do cartório —
/// <c>/automacao/ia</c>. Linguagem de produto: nenhum nome de provedor ou
/// modelo, nenhum valor em dinheiro (isso fica só na Central de IA, área do
/// desenvolvedor). Todo agregado usa o vocabulário único documentado no plano
/// da reformulação: tentativa, rodada, resposta entregue, não soube responder,
/// falha terminal, fallback executado/preventivo.
/// </summary>
/// <remarks>
/// <see cref="ISituacaoDaIa"/> continua sendo a fonte do estado "agora" dos
/// provedores (disjuntor, teto do Haiku) — este serviço reaproveita aquele
/// cálculo em vez de duplicar a leitura do disjuntor, e traduz o resultado
/// para uma frase sem jargão.
/// </remarks>
public class AnaliseDaIa(
    CrmDbContext db,
    IContextoDoUsuario ctx,
    ISituacaoDaIa situacaoDaIa) : IAnaliseDaIa
{
    public async Task<AnaliseDaIaResponse> ObterAsync(DateTime? de, DateTime? ate, CancellationToken ct = default)
    {
        if (!ctx.PodeGerirCadastros)
        {
            throw RespostaInteligenteServiceException.SemPermissao();
        }

        var tenantId = ctx.TenantId;
        var agora = DateTime.UtcNow;
        var (periodoDe, periodoAte, granularidade) = PeriodoDeAnalise.Resolver(de, ate, agora);

        // Período anterior, MESMO tamanho, sem sobreposição — é a base de
        // comparação dos 4 cards. `[anteriorDe, periodoDe)` encosta exatamente
        // onde o período atual começa.
        var duracao = periodoAte - periodoDe;
        var anteriorDe = periodoDe - duracao;
        var anteriorAte = periodoDe;

        var rodadasMedidasDesde = await db.ChamadasDeIa.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.RodadaId != null)
            .OrderBy(c => c.IniciadaEm)
            .Select(c => (DateTime?)c.IniciadaEm)
            .FirstOrDefaultAsync(ct);

        var atual = await ObterAgregadosAsync(tenantId, periodoDe, periodoAte, ct);
        var anterior = await ObterAgregadosAsync(tenantId, anteriorDe, anteriorAte, ct);

        var situacaoAgora = await MontarSituacaoAgoraAsync(ct);
        var valorEntregue = MontarValorEntregue(atual, anterior, rodadasMedidasDesde, periodoDe);
        var volumeAoLongoDoTempo = await MontarVolumeAoLongoDoTempoAsync(tenantId, periodoDe, periodoAte, granularidade, ct);
        var assuntos = await MontarAssuntosMaisPerguntadosAsync(tenantId, periodoDe, periodoAte, ct);
        var horarios = await MontarHorariosDeMaiorUsoAsync(tenantId, periodoDe, periodoAte, ct);
        var lacunas = await MontarLacunasDeCatalogoAsync(tenantId, periodoDe, periodoAte, ct);
        var insights = MontarInsights(atual, anterior, assuntos, lacunas);

        return new AnaliseDaIaResponse(
            new PeriodoDaAnaliseResponse(periodoDe, periodoAte, granularidade),
            situacaoAgora,
            valorEntregue,
            volumeAoLongoDoTempo,
            new ResultadoDasTentativasResponse(atual.TotalEntregues, atual.TotalNaoSoube, atual.TotalFalhas),
            assuntos,
            horarios,
            lacunas,
            insights,
            rodadasMedidasDesde);
    }

    /// <summary>
    /// "Contingência" é reservado para um provedor tropeçando DE VERDADE agora
    /// — disjuntor aberto por falha real, ou teto diário do Haiku batido.
    /// Gemini desligado por configuração (o estado normal do produto hoje) NÃO
    /// entra aqui: do ponto de vista do cartório a IA está funcionando, e
    /// chamar isso de "contingência" alarmaria por um detalhe de
    /// infraestrutura que não afeta ninguém.
    /// </summary>
    private async Task<SituacaoAgoraDaIaResponse> MontarSituacaoAgoraAsync(CancellationToken ct)
    {
        var situacao = await situacaoDaIa.ObterAsync(null, null, ct);

        if (!situacao.Habilitada || !situacao.EstaConfigurada)
        {
            return new SituacaoAgoraDaIaResponse("Desligada", null);
        }

        if (situacao.Gemini.Status == "Contingencia")
        {
            var volta = situacao.Gemini.VoltaATentarEm;
            var detalhe = volta is { } quando
                ? $"Provedor principal em recuperação — nova tentativa por volta de {quando:HH:mm}."
                : "Provedor principal em recuperação.";
            return new SituacaoAgoraDaIaResponse("Contingencia", detalhe);
        }

        if (situacao.Anthropic.Status == "TetoDiarioAtingido")
        {
            return new SituacaoAgoraDaIaResponse(
                "Contingencia", "Teto diário de respostas atingido — atendimento humano assume o resto do dia.");
        }

        return new SituacaoAgoraDaIaResponse("Atendendo", null);
    }

    private sealed record AgregadosDoPeriodo(
        int TotalTentativas,
        int TotalEntregues,
        int TotalNaoSoube,
        int TotalFalhas,
        int TotalRodadas,
        int RodadasResolvidas,
        double? MedianaDeLatenciaMs);

    /// <summary>
    /// Uma ida ao banco, agrupada por <c>RodadaId</c>, alimenta tentativas,
    /// entregas, "não soube" e falhas — e também rodadas totais/resolvidas, sem
    /// varrer a tabela de novo. Linha com <c>RodadaId</c> nulo (anterior à
    /// migração 042) cai num único grupo à parte: entra nos totais de
    /// tentativa/resultado, nunca em "rodadas" (contá-la como uma única rodada
    /// gigante infacionaria a taxa de resolução).
    /// </summary>
    private async Task<AgregadosDoPeriodo> ObterAgregadosAsync(
        Guid tenantId, DateTime de, DateTime ate, CancellationToken ct)
    {
        var porRodada = await db.ChamadasDeIa.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.IniciadaEm >= de && c.IniciadaEm < ate)
            .GroupBy(c => c.RodadaId)
            .Select(g => new
            {
                RodadaId = g.Key,
                Tentativas = g.Count(),
                Entregues = g.Count(c => c.Sucesso && c.PodeResponder == true),
                NaoSoube = g.Count(c => c.Sucesso && c.PodeResponder == false),
                Falhas = g.Count(c => !c.Sucesso)
            })
            .ToListAsync(ct);

        var comRodada = porRodada.Where(r => r.RodadaId != null).ToList();

        var latencias = await db.ChamadasDeIa.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.IniciadaEm >= de && c.IniciadaEm < ate && c.Sucesso)
            .Select(c => c.LatenciaMs)
            .ToListAsync(ct);

        latencias.Sort();

        return new AgregadosDoPeriodo(
            TotalTentativas: porRodada.Sum(r => r.Tentativas),
            TotalEntregues: porRodada.Sum(r => r.Entregues),
            TotalNaoSoube: porRodada.Sum(r => r.NaoSoube),
            TotalFalhas: porRodada.Sum(r => r.Falhas),
            TotalRodadas: comRodada.Count,
            RodadasResolvidas: comRodada.Count(r => r.Entregues > 0),
            MedianaDeLatenciaMs: latencias.Count == 0 ? null : Mediana(latencias));
    }

    private static ValorEntregueResponse MontarValorEntregue(
        AgregadosDoPeriodo atual, AgregadosDoPeriodo anterior, DateTime? rodadasMedidasDesde, DateTime periodoDe)
    {
        var medicaoConfiavel = rodadasMedidasDesde is null || rodadasMedidasDesde <= periodoDe;

        var taxaAtual = atual.TotalRodadas == 0 ? 0m : Math.Round(100m * atual.RodadasResolvidas / atual.TotalRodadas, 1);
        var taxaAnterior = anterior.TotalRodadas == 0 ? (decimal?)null
            : Math.Round(100m * anterior.RodadasResolvidas / anterior.TotalRodadas, 1);

        return new ValorEntregueResponse(
            PerguntasAtendidas: medicaoConfiavel ? atual.TotalRodadas : null,
            PerguntasAtendidasDeltaPercentual: medicaoConfiavel ? DeltaPercentual(atual.TotalRodadas, anterior.TotalRodadas) : null,
            TaxaDeResolucao: taxaAtual,
            TaxaDeResolucaoDeltaPontos: taxaAnterior is { } t ? Math.Round(taxaAtual - t, 1) : null,
            NaoSoubeResponder: atual.TotalNaoSoube,
            NaoSoubeResponderDeltaPercentual: DeltaPercentual(atual.TotalNaoSoube, anterior.TotalNaoSoube),
            TempoTipicoDeRespostaSegundos: atual.MedianaDeLatenciaMs is { } m ? Math.Round(m / 1000, 1) : null,
            TempoTipicoDeRespostaDeltaPercentual: atual.MedianaDeLatenciaMs is { } a && anterior.MedianaDeLatenciaMs is { } b && b > 0
                ? Math.Round(100m * ((decimal)a - (decimal)b) / (decimal)b, 1)
                : null);
    }

    private static decimal? DeltaPercentual(int atual, int anterior)
    {
        if (anterior == 0)
        {
            // Sem amostra anterior para comparar — "de 0 para N" não é uma
            // variação percentual honesta (seria sempre +infinito ou +100%
            // arbitrário). A tela mostra "novo" em vez de um número inventado.
            return null;
        }

        return Math.Round(100m * (atual - anterior) / anterior, 1);
    }

    /// <summary>
    /// Bucket em memória por <see cref="DiaLocal"/> — mesma disciplina de
    /// <c>IndicadoresService.MontarFluxo</c>: agrupar por data em SQL daria o
    /// balde em UTC, e "hoje" ficaria errado entre 21h e meia-noite em
    /// Brasília. Eixo inteiro preenchido, balde vazio vira zero, nunca some.
    /// </summary>
    private async Task<IReadOnlyList<PontoDeVolumeDeIaResponse>> MontarVolumeAoLongoDoTempoAsync(
        Guid tenantId, DateTime de, DateTime ate, string granularidade, CancellationToken ct)
    {
        var linhas = await db.ChamadasDeIa.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.IniciadaEm >= de && c.IniciadaEm < ate)
            .Select(c => new { c.IniciadaEm, c.Sucesso, c.PodeResponder })
            .ToListAsync(ct);

        DateTime Chave(DateTime instanteUtc)
        {
            var local = DiaLocal.ParaLocal(instanteUtc);
            return granularidade == "hora"
                ? new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0, DateTimeKind.Unspecified)
                : local.Date;
        }

        var baldes = new Dictionary<DateTime, (int Entregues, int NaoSoube, int Falhas)>();

        foreach (var linha in linhas)
        {
            var chave = Chave(linha.IniciadaEm);
            baldes.TryGetValue(chave, out var atual);

            baldes[chave] = linha switch
            {
                { Sucesso: true, PodeResponder: true } => (atual.Entregues + 1, atual.NaoSoube, atual.Falhas),
                { Sucesso: true, PodeResponder: false } => (atual.Entregues, atual.NaoSoube + 1, atual.Falhas),
                _ => (atual.Entregues, atual.NaoSoube, atual.Falhas + 1)
            };
        }

        var passo = granularidade == "hora" ? TimeSpan.FromHours(1) : TimeSpan.FromDays(1);
        var inicioLocal = Chave(de);
        var fimLocal = Chave(ate);

        var pontos = new List<PontoDeVolumeDeIaResponse>();

        for (var atualBalde = inicioLocal; atualBalde <= fimLocal; atualBalde = atualBalde.Add(passo))
        {
            baldes.TryGetValue(atualBalde, out var valores);
            pontos.Add(new PontoDeVolumeDeIaResponse(atualBalde, valores.Entregues, valores.NaoSoube, valores.Falhas));
        }

        return pontos;
    }

    /// <summary>
    /// Top 8 serviços por tentativa, mais o balde "Não identificado" para
    /// <c>TipoServicoId</c> nulo (linha anterior à migração 042, ou pergunta
    /// fora de qualquer conversa com serviço definido) — sempre visível quando
    /// existe, nunca escondido dentro do top 8.
    /// </summary>
    private async Task<IReadOnlyList<AssuntoMaisPerguntadoResponse>> MontarAssuntosMaisPerguntadosAsync(
        Guid tenantId, DateTime de, DateTime ate, CancellationToken ct)
    {
        var contagem = await db.ChamadasDeIa.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.IniciadaEm >= de && c.IniciadaEm < ate)
            .GroupBy(c => c.TipoServicoId)
            .Select(g => new { TipoServicoId = g.Key, Total = g.Count() })
            .ToListAsync(ct);

        var totalGeral = contagem.Sum(c => c.Total);

        if (totalGeral == 0)
        {
            return [];
        }

        var comServico = contagem.Where(c => c.TipoServicoId != null).OrderByDescending(c => c.Total).Take(8).ToList();
        var semServico = contagem.FirstOrDefault(c => c.TipoServicoId == null)?.Total ?? 0;

        var idsServicos = comServico.Select(c => c.TipoServicoId!.Value).ToList();

        var nomes = await db.TiposServico.AsNoTracking()
            .Where(t => idsServicos.Contains(t.Id))
            .Select(t => new { t.Id, t.Nome })
            .ToDictionaryAsync(t => t.Id, t => t.Nome, ct);

        var resultado = comServico
            .Select(c => new AssuntoMaisPerguntadoResponse(
                c.TipoServicoId,
                nomes.TryGetValue(c.TipoServicoId!.Value, out var nome) ? nome : "Serviço removido",
                c.Total,
                Math.Round(100m * c.Total / totalGeral, 1)))
            .ToList();

        if (semServico > 0)
        {
            resultado.Add(new AssuntoMaisPerguntadoResponse(
                null, "Não identificado", semServico, Math.Round(100m * semServico / totalGeral, 1)));
        }

        return resultado;
    }

    /// <summary>Mesmo padrão de <c>IndicadoresService.MontarHorariosDeMaiorDemandaAsync</c>: um <c>int[24]</c>, hora local.</summary>
    private async Task<IReadOnlyList<int>> MontarHorariosDeMaiorUsoAsync(
        Guid tenantId, DateTime de, DateTime ate, CancellationToken ct)
    {
        var instantes = await db.ChamadasDeIa.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.IniciadaEm >= de && c.IniciadaEm < ate)
            .Select(c => c.IniciadaEm)
            .ToListAsync(ct);

        var horas = new int[24];

        foreach (var instante in instantes)
        {
            horas[DiaLocal.ParaLocal(instante).Hour]++;
        }

        return horas;
    }

    /// <summary>
    /// Top 10 pares (categoria × serviço) de "não soube responder" — é o
    /// insight que justifica o produto: "12 pessoas perguntaram preço de
    /// reconhecimento de firma e a IA não soube".
    /// </summary>
    private async Task<IReadOnlyList<LacunaDeCatalogoResponse>> MontarLacunasDeCatalogoAsync(
        Guid tenantId, DateTime de, DateTime ate, CancellationToken ct)
    {
        var pares = await db.ChamadasDeIa.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.IniciadaEm >= de && c.IniciadaEm < ate && c.PodeResponder == false)
            .GroupBy(c => new { c.MotivoDaNaoResposta, c.TipoServicoId })
            .Select(g => new { g.Key.MotivoDaNaoResposta, g.Key.TipoServicoId, Total = g.Count() })
            .OrderByDescending(g => g.Total)
            .Take(10)
            .ToListAsync(ct);

        if (pares.Count == 0)
        {
            return [];
        }

        var idsServicos = pares.Where(p => p.TipoServicoId != null).Select(p => p.TipoServicoId!.Value).Distinct().ToList();

        var nomes = await db.TiposServico.AsNoTracking()
            .Where(t => idsServicos.Contains(t.Id))
            .Select(t => new { t.Id, t.Nome })
            .ToDictionaryAsync(t => t.Id, t => t.Nome, ct);

        return pares
            .Select(p => new LacunaDeCatalogoResponse(
                RotuloDaCategoria(p.MotivoDaNaoResposta),
                p.TipoServicoId,
                p.TipoServicoId is { } id && nomes.TryGetValue(id, out var nome) ? nome : "Não identificado",
                p.Total))
            .ToList();
    }

    private static string RotuloDaCategoria(MotivoDaNaoRespostaDeIa? categoria) => categoria switch
    {
        MotivoDaNaoRespostaDeIa.PrecoNaoCadastrado => "Preço não cadastrado",
        MotivoDaNaoRespostaDeIa.PrazoNaoCadastrado => "Prazo não cadastrado",
        MotivoDaNaoRespostaDeIa.DocumentoNaoCadastrado => "Documento não cadastrado",
        MotivoDaNaoRespostaDeIa.ServicoForaDoCatalogo => "Serviço fora do catálogo",
        MotivoDaNaoRespostaDeIa.OrientacaoJuridicaEspecifica => "Pediu orientação jurídica específica",
        MotivoDaNaoRespostaDeIa.ForaDoEscopoDaServentia => "Fora do escopo do cartório",
        MotivoDaNaoRespostaDeIa.PerguntaAmbigua => "Pergunta ambígua",
        MotivoDaNaoRespostaDeIa.Outro => "Outro motivo",
        _ => "Não classificado"
    };

    /// <summary>
    /// Regra determinística, nunca prosa de LLM: cada insight tem gatilho e
    /// amostra mínima fixos em código, e é derivado dos MESMOS agregados que
    /// os cards já calcularam — por construção não pode divergir deles. Sem
    /// amostra suficiente, a lista fica vazia (a tela explica isso, nunca
    /// força um insight fraco a aparecer).
    /// </summary>
    private static IReadOnlyList<InsightDaIaResponse> MontarInsights(
        AgregadosDoPeriodo atual,
        AgregadosDoPeriodo anterior,
        IReadOnlyList<AssuntoMaisPerguntadoResponse> assuntos,
        IReadOnlyList<LacunaDeCatalogoResponse> lacunas)
    {
        var insights = new List<InsightDaIaResponse>();

        // 1. Concentração de assunto.
        var topAssunto = assuntos.Where(a => a.TipoServicoId != null).OrderByDescending(a => a.Total).FirstOrDefault();
        if (topAssunto is not null && atual.TotalTentativas >= 10 && topAssunto.Percentual >= 20m)
        {
            insights.Add(new InsightDaIaResponse(
                "informativo",
                $"\"{topAssunto.Nome}\" respondeu por {topAssunto.Percentual:0.#}% das perguntas no período ({topAssunto.Total} de {atual.TotalTentativas})."));
        }

        // 2. Maior lacuna de catálogo.
        var maiorLacuna = lacunas.OrderByDescending(l => l.Total).FirstOrDefault();
        if (maiorLacuna is not null && maiorLacuna.Total >= 5)
        {
            insights.Add(new InsightDaIaResponse(
                "atencao",
                $"{maiorLacuna.Total} pessoas perguntaram sobre \"{maiorLacuna.ServicoNome}\" ({maiorLacuna.Categoria.ToLowerInvariant()}) e a IA não soube responder — considere cadastrar essa informação."));
        }

        // 3. Aumento da taxa de falha.
        if (atual.TotalTentativas >= 5)
        {
            var taxaDeFalha = 100m * atual.TotalFalhas / atual.TotalTentativas;
            if (taxaDeFalha >= 5m)
            {
                insights.Add(new InsightDaIaResponse(
                    "atencao",
                    $"{atual.TotalFalhas} de {atual.TotalTentativas} tentativas falharam neste período ({taxaDeFalha:0.#}%) — acima do normal."));
            }
        }

        // 4. Crescimento ou queda de volume de rodadas vs. período anterior.
        if (atual.TotalRodadas >= 10 && anterior.TotalRodadas >= 10)
        {
            var variacao = 100m * (atual.TotalRodadas - anterior.TotalRodadas) / anterior.TotalRodadas;
            if (variacao >= 30m)
            {
                insights.Add(new InsightDaIaResponse(
                    "informativo",
                    $"O volume de perguntas cresceu {variacao:0.#}% em relação ao período anterior ({anterior.TotalRodadas} → {atual.TotalRodadas})."));
            }
            else if (variacao <= -40m)
            {
                insights.Add(new InsightDaIaResponse(
                    "informativo",
                    $"O volume de perguntas caiu {Math.Abs(variacao):0.#}% em relação ao período anterior ({anterior.TotalRodadas} → {atual.TotalRodadas})."));
            }
        }

        return insights.Take(4).ToList();
    }

    private static double Mediana(IReadOnlyList<int> valoresOrdenados)
    {
        var n = valoresOrdenados.Count;

        return n % 2 == 1
            ? valoresOrdenados[n / 2]
            : (valoresOrdenados[n / 2 - 1] + valoresOrdenados[n / 2]) / 2.0;
    }
}
