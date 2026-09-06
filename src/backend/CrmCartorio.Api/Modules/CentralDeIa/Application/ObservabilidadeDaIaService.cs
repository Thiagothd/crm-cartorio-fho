using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.RespostaInteligente.Application;
using CrmCartorio.Api.Modules.RespostaInteligente.Application.Cota;
using CrmCartorio.Api.Modules.RespostaInteligente.Domain.Entities;
using CrmCartorio.Api.Modules.RespostaInteligente.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CrmCartorio.Api.Modules.CentralDeIa.Application;

/// <summary>
/// Observabilidade cross-tenant da IA para o desenvolvedor — sucessor de
/// <c>SituacaoGlobalDaIaService</c> (Etapa C da reformulação de IA). Único
/// serviço do sistema que lê <c>ChamadasDeIa</c> sem filtro de
/// <c>TenantId</c> — deliberado, ver Modules/CentralDeIa/README.md.
/// </summary>
/// <remarks>
/// A agregação pesada (contagem, soma, histograma de latência) sempre roda em
/// SQL via <c>GroupBy</c> traduzido — nunca materializa a tabela inteira. A
/// única exceção é <see cref="MontarLinhaDoTempoAsync"/>, que precisa de
/// bucket por dia/hora LOCAL (a serventia, não UTC) para o "hoje" bater com o
/// resto do sistema — SQL não sabe fazer esse corte sem uma coluna de fuso
/// dedicada, então ali a projeção estreita (poucos campos) vai para memória e
/// o bucket é feito em C#, mesmo padrão de <c>AnaliseDaIa.MontarVolumeAoLongoDoTempoAsync</c>.
/// </remarks>
public class ObservabilidadeDaIaService(
    CrmDbContext db,
    IOptions<OpcoesDaIA> opcoes,
    DisjuntorDoGemini disjuntor) : IObservabilidadeDaIa
{
    private const int LarguraDoBaldeMs = 250;

    public async Task<ObservabilidadeDaIaResponse> ObterAsync(FiltroDeObservabilidadeDaIa filtro, CancellationToken ct = default)
    {
        var saudeAgora = await MontarSaudeAgoraAsync(ct);
        var kpis = await MontarKpisAsync(filtro, ct);
        var linhaDoTempo = await MontarLinhaDoTempoAsync(filtro, ct);
        var histograma = await MontarHistogramaDeLatenciaAsync(filtro, ct);
        var erros = await MontarErrosPorCategoriaAsync(filtro, ct);
        var fallbacks = await MontarFallbacksPorTipoAsync(filtro, ct);
        var modelos = await MontarDistribuicaoPorModeloAsync(filtro, ct);
        var topTenants = await ObterTenantsAsync(filtro, null, OrdenarTenantsPor.Custo, decrescente: true, pagina: 1, tamanhoDaPagina: 8, ct);

        return new ObservabilidadeDaIaResponse(
            filtro.De, filtro.Ate, saudeAgora, kpis, linhaDoTempo, histograma, erros, fallbacks, modelos, topTenants.Itens);
    }

    private static IQueryable<ChamadaDeIa> AplicarFiltros(IQueryable<ChamadaDeIa> query, FiltroDeObservabilidadeDaIa f)
    {
        query = query.Where(c => c.IniciadaEm >= f.De && c.IniciadaEm < f.Ate);

        if (f.TenantId is { } tenantId)
        {
            query = query.Where(c => c.TenantId == tenantId);
        }

        if (f.Provedor is { } provedor)
        {
            query = query.Where(c => c.Provedor == provedor);
        }

        if (!string.IsNullOrWhiteSpace(f.Modelo))
        {
            query = query.Where(c => c.Modelo == f.Modelo);
        }

        query = f.Resultado switch
        {
            ResultadoDaChamadaDeIa.Entregue => query.Where(c => c.Sucesso && c.PodeResponder == true),
            ResultadoDaChamadaDeIa.NaoSoubeResponder => query.Where(c => c.Sucesso && c.PodeResponder == false),
            ResultadoDaChamadaDeIa.Falha => query.Where(c => !c.Sucesso),
            _ => query,
        };

        if (f.TipoDeFallback is { } tipoDeFallback)
        {
            query = query.Where(c => c.TipoDeFallback == tipoDeFallback);
        }

        if (f.TipoDeFalha is { } tipoDeFalha)
        {
            query = query.Where(c => c.TipoDeFalha == tipoDeFalha);
        }

        if (f.CodigoHttp is { } codigoHttp)
        {
            query = query.Where(c => c.CodigoHttp == codigoHttp);
        }

        return query;
    }

    /// <remarks>
    /// Sempre o dia LOCAL corrente, independente do filtro de período recebido
    /// — "saúde agora" responde "está funcionando neste instante?", não "como
    /// foi durante a janela histórica escolhida". Mesmo recorte de
    /// <c>ISituacaoDaIa</c>/<c>SituacaoGlobalDaIaService</c> (o antecessor).
    /// </remarks>
    private async Task<SaudeAgoraResponse> MontarSaudeAgoraAsync(CancellationToken ct)
    {
        var valor = opcoes.Value;
        var inicioDoDia = DiaLocal.InicioDoDia(DateTime.UtcNow);

        var gemini = await MontarSaudeDoProvedorAsync(
            ProvedorDeIa.Gemini, valor.GeminiEstaConfigurado, valor.GeminiEstaConfigurado ? valor.Gemini.Modelo : null, inicioDoDia, ct);
        var anthropic = await MontarSaudeDoProvedorAsync(
            ProvedorDeIa.Anthropic, valor.AnthropicEstaConfigurado, valor.AnthropicEstaConfigurado ? valor.ModeloDoAnthropicResolvido : null, inicioDoDia, ct);

        // "Fallback originado pelo Gemini" mora fisicamente na linha do Anthropic
        // (é ela que carrega TipoDeFallback=Executado — ver o enum) — por isso
        // não dá para calcular dentro de MontarSaudeDoProvedorAsync olhando só
        // as próprias linhas do provedor.
        var fallbacksOriginadosPeloGeminiHoje = await db.ChamadasDeIa.AsNoTracking()
            .Where(c => c.Provedor == ProvedorDeIa.Anthropic && c.TipoDeFallback == TipoDeFallbackDeIa.Executado && c.IniciadaEm >= inicioDoDia)
            .CountAsync(ct);

        string estadoDoCircuito;
        DateTime? voltaATentarEm = null;

        if (!valor.GeminiEstaConfigurado)
        {
            estadoDoCircuito = "SemChave";
        }
        else
        {
            var chaveDeProjeto = FingerprintDeChave.Calcular(valor.Gemini.ApiKey);
            var situacaoDoDisjuntor = disjuntor.Consultar(chaveDeProjeto);
            estadoDoCircuito = situacaoDoDisjuntor.Estado.ToString();
            voltaATentarEm = situacaoDoDisjuntor.VoltaATentarEm;
        }

        gemini = gemini with
        {
            EstadoDoCircuito = estadoDoCircuito,
            CircuitoVoltaATentarEm = voltaATentarEm,
            FallbacksOriginadosHoje = fallbacksOriginadosPeloGeminiHoje,
            // Fato observado (RPM/RPD) nunca vira percentual sobre isto — só
            // aparece separado, rotulado como "informado por você" (ver plano,
            // C.7). 0 = não cadastrado, mesmo sentinela de OpcoesDoGemini.
            LimiteRpdInformado = valor.Gemini.LimiteRpd > 0 ? valor.Gemini.LimiteRpd : null,
        };

        anthropic = anthropic with
        {
            // O Anthropic não tem disjuntor nem franquia diária cadastrada — só o
            // teto de respostas (OpcoesDoAnthropic.LimiteDiarioHaiku), que já é
            // assunto da tela do cartório, não desta.
            EstadoDoCircuito = valor.AnthropicEstaConfigurado ? "NaoSeAplica" : "SemChave",
        };

        return new SaudeAgoraResponse(gemini, anthropic);
    }

    private async Task<SaudeDoProvedorResponse> MontarSaudeDoProvedorAsync(
        ProvedorDeIa provedor, bool configurado, string? modelo, DateTime inicioDoDia, CancellationToken ct)
    {
        var chamadasDeHoje = await db.ChamadasDeIa.AsNoTracking()
            .Where(c => c.Provedor == provedor && c.IniciadaEm >= inicioDoDia)
            .Select(c => new
            {
                c.IniciadaEm,
                c.Sucesso,
                c.CodigoHttp,
                c.TipoDeFalha,
                c.LatenciaMs,
                c.TokensEntrada,
                c.TokensSaida,
                c.CustoEstimadoUsd,
            })
            .ToListAsync(ct);

        var ultimaChamada = chamadasDeHoje.OrderByDescending(c => c.IniciadaEm).FirstOrDefault();
        var ultimaComSucesso = chamadasDeHoje.Where(c => c.Sucesso).OrderByDescending(c => c.IniciadaEm).FirstOrDefault();
        var ultimaComFalha = chamadasDeHoje.Where(c => !c.Sucesso).OrderByDescending(c => c.IniciadaEm).FirstOrDefault();
        var ultimoComRateLimit = chamadasDeHoje.Where(c => c.CodigoHttp == 429).OrderByDescending(c => c.IniciadaEm).FirstOrDefault();
        var sucessos = chamadasDeHoje.Where(c => c.Sucesso).ToList();

        return new SaudeDoProvedorResponse(
            Provedor: provedor.ToString(),
            Configurado: configurado,
            Modelo: modelo,
            UltimaChamadaEm: ultimaChamada?.IniciadaEm,
            UltimaChamadaComSucessoEm: ultimaComSucesso?.IniciadaEm,
            UltimoErroTipo: ultimaComFalha?.TipoDeFalha.ToString(),
            UltimoErroCodigoHttp: ultimaComFalha?.CodigoHttp,
            UltimoErroEm: ultimaComFalha?.IniciadaEm,
            Total429Hoje: chamadasDeHoje.Count(c => c.CodigoHttp == 429),
            Ultimo429Em: ultimoComRateLimit?.IniciadaEm,
            LatenciaMediaMsHoje: sucessos.Count == 0 ? null : sucessos.Average(c => (double)c.LatenciaMs),
            TaxaDeSucessoHoje: chamadasDeHoje.Count == 0 ? null : Math.Round(100m * sucessos.Count / chamadasDeHoje.Count, 1),
            EstadoDoCircuito: "NaoSeAplica",
            CircuitoVoltaATentarEm: null,
            FallbacksOriginadosHoje: 0,
            TokensEntradaHoje: chamadasDeHoje.Sum(c => c.TokensEntrada),
            TokensSaidaHoje: chamadasDeHoje.Sum(c => c.TokensSaida),
            CustoEstimadoUsdHoje: sucessos.Count == 0 ? null : sucessos.Sum(c => c.CustoEstimadoUsd ?? 0m),
            LimiteRpdInformado: null,
            RequisicoesHoje: chamadasDeHoje.Count);
    }

    private async Task<KpisDaObservabilidadeResponse> MontarKpisAsync(FiltroDeObservabilidadeDaIa filtro, CancellationToken ct)
    {
        var cotacaoDoDolar = opcoes.Value.Anthropic.CotacaoDoDolar;
        var baseQuery = AplicarFiltros(db.ChamadasDeIa.AsNoTracking(), filtro);

        var agregado = await baseQuery
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Tentativas = g.Count(),
                Entregues = g.Count(c => c.Sucesso && c.PodeResponder == true),
                NaoSoube = g.Count(c => c.Sucesso && c.PodeResponder == false),
                Falhas = g.Count(c => !c.Sucesso),
                FallbackExecutado = g.Count(c => c.TipoDeFallback == TipoDeFallbackDeIa.Executado),
                FallbackPreventivoDesligado = g.Count(c => c.TipoDeFallback == TipoDeFallbackDeIa.PreventivoDesligado),
                FallbackPreventivoDisjuntor = g.Count(c => c.TipoDeFallback == TipoDeFallbackDeIa.PreventivoDisjuntor),
                FallbackPreventivoMargemDeCota = g.Count(c => c.TipoDeFallback == TipoDeFallbackDeIa.PreventivoMargemDeCota),
                TokensEntrada = g.Sum(c => c.TokensEntrada),
                TokensSaida = g.Sum(c => c.TokensSaida),
                TokensPensamento = g.Sum(c => c.TokensPensamento),
                CustoTotalUsd = g.Sum(c => c.CustoEstimadoUsd ?? 0m),
                Total429 = g.Count(c => c.CodigoHttp == 429),
                TotalTimeouts = g.Count(c => c.TipoDeFalha == TipoDeFalhaDeIa.Timeout),
            })
            .FirstOrDefaultAsync(ct);

        var totalRodadas = await baseQuery
            .Where(c => c.RodadaId != null)
            .Select(c => c.RodadaId!.Value)
            .Distinct()
            .CountAsync(ct);

        var tenantsAtivos = await baseQuery
            .Select(c => c.TenantId)
            .Distinct()
            .CountAsync(ct);

        var (p50, p95, p99) = await CalcularPercentisGeralAsync(filtro, ct);

        if (agregado is null)
        {
            return new KpisDaObservabilidadeResponse(
                0, totalRodadas, 0, 0, 0, 0m, 0m, 0, 0, 0, 0, 0, 0, 0, 0m, 0m, null,
                tenantsAtivos, 0, 0, LarguraDoBaldeMs, p50, p95, p99);
        }

        var taxaDeSucesso = agregado.Tentativas == 0 ? 0m
            : Math.Round(100m * (agregado.Entregues + agregado.NaoSoube) / agregado.Tentativas, 1);
        var taxaDeFalha = agregado.Tentativas == 0 ? 0m : Math.Round(100m * agregado.Falhas / agregado.Tentativas, 1);
        var custoMedioPorRodada = totalRodadas == 0 ? (decimal?)null : Math.Round(agregado.CustoTotalUsd / totalRodadas, 4);

        return new KpisDaObservabilidadeResponse(
            agregado.Tentativas,
            totalRodadas,
            agregado.Entregues,
            agregado.NaoSoube,
            agregado.Falhas,
            taxaDeSucesso,
            taxaDeFalha,
            agregado.FallbackExecutado,
            agregado.FallbackPreventivoDesligado,
            agregado.FallbackPreventivoDisjuntor,
            agregado.FallbackPreventivoMargemDeCota,
            agregado.TokensEntrada,
            agregado.TokensSaida,
            agregado.TokensPensamento,
            agregado.CustoTotalUsd,
            Math.Round(agregado.CustoTotalUsd * cotacaoDoDolar, 2),
            custoMedioPorRodada,
            tenantsAtivos,
            agregado.Total429,
            agregado.TotalTimeouts,
            LarguraDoBaldeMs,
            p50,
            p95,
            p99);
    }

    /// <summary>
    /// Percentil interpolado sobre o histograma acumulado (balde de 250ms) —
    /// nunca <c>PERCENTILE_CONT</c>, que exigiria <c>FromSqlRaw</c> e escaparia
    /// de <c>verificar-traducao.cs</c> (ver plano, C.3). Precisão
    /// ±<see cref="LarguraDoBaldeMs"/>/2 — o ponto médio do balde onde o
    /// acumulado cruza o percentil pedido.
    /// </summary>
    private async Task<(double? P50, double? P95, double? P99)> CalcularPercentisGeralAsync(
        FiltroDeObservabilidadeDaIa filtro, CancellationToken ct)
    {
        var baldes = await AplicarFiltros(db.ChamadasDeIa.AsNoTracking(), filtro)
            .Where(c => c.Sucesso)
            .GroupBy(c => c.LatenciaMs / LarguraDoBaldeMs)
            .Select(g => new { Balde = g.Key, Total = g.Count() })
            .ToListAsync(ct);

        if (baldes.Count == 0)
        {
            return (null, null, null);
        }

        var total = baldes.Sum(b => b.Total);
        var ordenados = baldes.OrderBy(b => b.Balde).ToList();

        double Percentil(double fracao)
        {
            var alvo = fracao * total;
            var acumulado = 0;

            foreach (var balde in ordenados)
            {
                acumulado += balde.Total;
                if (acumulado >= alvo)
                {
                    return balde.Balde * LarguraDoBaldeMs + LarguraDoBaldeMs / 2.0;
                }
            }

            return ordenados[^1].Balde * LarguraDoBaldeMs + LarguraDoBaldeMs / 2.0;
        }

        return (Percentil(0.5), Percentil(0.95), Percentil(0.99));
    }

    private async Task<IReadOnlyList<PontoDaLinhaDoTempoResponse>> MontarLinhaDoTempoAsync(
        FiltroDeObservabilidadeDaIa filtro, CancellationToken ct)
    {
        var linhas = await AplicarFiltros(db.ChamadasDeIa.AsNoTracking(), filtro)
            .Select(c => new
            {
                c.IniciadaEm,
                c.Provedor,
                c.Sucesso,
                c.PodeResponder,
                c.TipoDeFallback,
                c.CustoEstimadoUsd,
                c.TokensEntrada,
                c.TokensSaida,
            })
            .ToListAsync(ct);

        var granularidadeHora = filtro.Ate - filtro.De <= TimeSpan.FromHours(25);

        DateTime Chave(DateTime instanteUtc)
        {
            var local = DiaLocal.ParaLocal(instanteUtc);
            return granularidadeHora
                ? new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0, DateTimeKind.Unspecified)
                : local.Date;
        }

        var baldes = new Dictionary<DateTime, (int Gemini, int Anthropic, int Entregues, int NaoSoube, int Falhas, int FallbackExecutado, decimal Custo, int TokensEntrada, int TokensSaida)>();

        foreach (var linha in linhas)
        {
            var chave = Chave(linha.IniciadaEm);
            baldes.TryGetValue(chave, out var atual);

            atual.Gemini += linha.Provedor == ProvedorDeIa.Gemini ? 1 : 0;
            atual.Anthropic += linha.Provedor == ProvedorDeIa.Anthropic ? 1 : 0;
            atual.Entregues += linha.Sucesso && linha.PodeResponder == true ? 1 : 0;
            atual.NaoSoube += linha.Sucesso && linha.PodeResponder == false ? 1 : 0;
            atual.Falhas += !linha.Sucesso ? 1 : 0;
            atual.FallbackExecutado += linha.TipoDeFallback == TipoDeFallbackDeIa.Executado ? 1 : 0;
            atual.Custo += linha.CustoEstimadoUsd ?? 0m;
            atual.TokensEntrada += linha.TokensEntrada;
            atual.TokensSaida += linha.TokensSaida;

            baldes[chave] = atual;
        }

        var passo = granularidadeHora ? TimeSpan.FromHours(1) : TimeSpan.FromDays(1);
        var inicioLocal = Chave(filtro.De);
        var fimLocal = Chave(filtro.Ate);

        var pontos = new List<PontoDaLinhaDoTempoResponse>();

        for (var atualBalde = inicioLocal; atualBalde <= fimLocal; atualBalde = atualBalde.Add(passo))
        {
            baldes.TryGetValue(atualBalde, out var v);
            pontos.Add(new PontoDaLinhaDoTempoResponse(
                atualBalde, v.Gemini, v.Anthropic, v.Entregues, v.NaoSoube, v.Falhas, v.FallbackExecutado, v.Custo, v.TokensEntrada, v.TokensSaida));
        }

        return pontos;
    }

    private async Task<IReadOnlyList<HistogramaDeLatenciaResponse>> MontarHistogramaDeLatenciaAsync(
        FiltroDeObservabilidadeDaIa filtro, CancellationToken ct)
    {
        var linhas = await AplicarFiltros(db.ChamadasDeIa.AsNoTracking(), filtro)
            .Where(c => c.Sucesso)
            .GroupBy(c => new { c.Provedor, Balde = c.LatenciaMs / LarguraDoBaldeMs })
            .Select(g => new { g.Key.Provedor, g.Key.Balde, Total = g.Count() })
            .ToListAsync(ct);

        return linhas
            .GroupBy(l => l.Provedor)
            .Select(g => new HistogramaDeLatenciaResponse(
                g.Key.ToString(),
                LarguraDoBaldeMs,
                g.OrderBy(x => x.Balde).Select(x => new BaldeDeLatenciaResponse(x.Balde * LarguraDoBaldeMs, x.Total)).ToList()))
            .ToList();
    }

    private async Task<IReadOnlyList<ErroPorCategoriaResponse>> MontarErrosPorCategoriaAsync(
        FiltroDeObservabilidadeDaIa filtro, CancellationToken ct)
    {
        var linhas = await AplicarFiltros(db.ChamadasDeIa.AsNoTracking(), filtro)
            .Where(c => !c.Sucesso)
            .GroupBy(c => new { c.Provedor, c.TipoDeFalha })
            .Select(g => new { g.Key.Provedor, g.Key.TipoDeFalha, Total = g.Count() })
            .ToListAsync(ct);

        return linhas
            .Select(l => new ErroPorCategoriaResponse(l.Provedor.ToString(), l.TipoDeFalha.ToString(), l.Total))
            .OrderByDescending(l => l.Total)
            .ToList();
    }

    private async Task<IReadOnlyList<FallbackPorTipoResponse>> MontarFallbacksPorTipoAsync(
        FiltroDeObservabilidadeDaIa filtro, CancellationToken ct)
    {
        var linhas = await AplicarFiltros(db.ChamadasDeIa.AsNoTracking(), filtro)
            .Where(c => c.TipoDeFallback != TipoDeFallbackDeIa.NaoEhFallback)
            .GroupBy(c => c.TipoDeFallback)
            .Select(g => new { Tipo = g.Key, Total = g.Count() })
            .ToListAsync(ct);

        return linhas
            .Select(l => new FallbackPorTipoResponse(l.Tipo.ToString(), l.Total))
            .OrderByDescending(l => l.Total)
            .ToList();
    }

    private async Task<IReadOnlyList<DistribuicaoPorModeloResponse>> MontarDistribuicaoPorModeloAsync(
        FiltroDeObservabilidadeDaIa filtro, CancellationToken ct)
    {
        var linhas = await AplicarFiltros(db.ChamadasDeIa.AsNoTracking(), filtro)
            .GroupBy(c => new { c.Provedor, c.Modelo })
            .Select(g => new { g.Key.Provedor, g.Key.Modelo, Total = g.Count(), Custo = g.Sum(c => c.CustoEstimadoUsd ?? 0m) })
            .ToListAsync(ct);

        return linhas
            .Select(l => new DistribuicaoPorModeloResponse(l.Provedor.ToString(), l.Modelo, l.Total, l.Custo))
            .OrderByDescending(l => l.Total)
            .ToList();
    }

    /// <remarks>
    /// A agregação por tenant (a parte que pode envolver centenas de milhares
    /// de linhas) roda em SQL via <c>GroupBy</c>. Ordenar e paginar o resultado
    /// já agregado acontece em memória — número de TENANTS é sempre pequeno
    /// (dezenas, não milhões), então isso nunca é o gargalo; só a linha bruta
    /// de <c>ChamadasDeIa</c> seria.
    /// </remarks>
    public async Task<PaginaDeTenantsResponse> ObterTenantsAsync(
        FiltroDeObservabilidadeDaIa filtro,
        string? busca,
        OrdenarTenantsPor ordenarPor,
        bool decrescente,
        int pagina,
        int tamanhoDaPagina,
        CancellationToken ct = default)
    {
        var query = AplicarFiltros(db.ChamadasDeIa.AsNoTracking(), filtro);

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var idsQueBatemNaBusca = await db.Tenants.AsNoTracking()
                .Where(t => EF.Functions.Like(t.NomeFantasia, $"%{busca}%") || EF.Functions.Like(t.RazaoSocial, $"%{busca}%"))
                .Select(t => t.Id)
                .ToListAsync(ct);

            query = query.Where(c => idsQueBatemNaBusca.Contains(c.TenantId));
        }

        var agregados = await query
            .GroupBy(c => c.TenantId)
            .Select(g => new
            {
                TenantId = g.Key,
                Tentativas = g.Count(),
                Falhas = g.Count(c => !c.Sucesso),
                FallbackExecutado = g.Count(c => c.TipoDeFallback == TipoDeFallbackDeIa.Executado),
                CustoEstimadoUsd = g.Sum(c => c.CustoEstimadoUsd ?? 0m),
                LatenciaMediaMs = g.Where(c => c.Sucesso).Average(c => (double?)c.LatenciaMs),
            })
            .ToListAsync(ct);

        var total = agregados.Count;

        var tenantIds = agregados.Select(a => a.TenantId).ToList();
        var nomes = await db.Tenants.AsNoTracking()
            .Where(t => tenantIds.Contains(t.Id))
            .Select(t => new { t.Id, t.NomeFantasia, t.RazaoSocial })
            .ToDictionaryAsync(t => t.Id, ct);

        var cotacaoDoDolar = opcoes.Value.Anthropic.CotacaoDoDolar;

        var itens = agregados
            .Select(a => new TenantDaObservabilidadeResponse(
                a.TenantId,
                nomes.TryGetValue(a.TenantId, out var t) ? (t.NomeFantasia ?? t.RazaoSocial) : "(tenant removido)",
                a.Tentativas,
                a.Falhas,
                a.FallbackExecutado,
                a.CustoEstimadoUsd,
                Math.Round(a.CustoEstimadoUsd * cotacaoDoDolar, 2),
                a.LatenciaMediaMs))
            .ToList();

        Func<TenantDaObservabilidadeResponse, IComparable> chave = ordenarPor switch
        {
            OrdenarTenantsPor.Custo => t => t.CustoEstimadoUsd,
            OrdenarTenantsPor.Falhas => t => t.Falhas,
            OrdenarTenantsPor.FallbackExecutado => t => t.FallbackExecutado,
            OrdenarTenantsPor.Latencia => t => t.LatenciaMediaMs ?? -1,
            _ => t => t.Tentativas,
        };

        var ordenados = decrescente
            ? itens.OrderByDescending(chave).ThenBy(t => t.TenantId).ToList()
            : itens.OrderBy(chave).ThenBy(t => t.TenantId).ToList();

        var paginaValida = Math.Max(1, pagina);
        var pagados = ordenados.Skip((paginaValida - 1) * tamanhoDaPagina).Take(tamanhoDaPagina).ToList();

        return new PaginaDeTenantsResponse(pagados, total, paginaValida, tamanhoDaPagina);
    }

    /// <remarks>
    /// Nunca projeta <c>ConversaId</c>, <c>MensagemId</c> nem
    /// <c>MotivoDoFallback</c> — ver a nota de privacidade em
    /// <see cref="ChamadaDeIa"/>. Ordenado por <c>IniciadaEm</c> desc com
    /// desempate por <c>Id</c> (UUID v7, ordenado no tempo) — paginação
    /// estável mesmo com timestamps empatados.
    /// </remarks>
    public async Task<PaginaDeHistoricoResponse> ObterHistoricoAsync(
        FiltroDeObservabilidadeDaIa filtro,
        Guid? rodadaId,
        int pagina,
        int tamanhoDaPagina,
        CancellationToken ct = default)
    {
        var query = AplicarFiltros(db.ChamadasDeIa.AsNoTracking(), filtro);

        if (rodadaId is { } rid)
        {
            query = query.Where(c => c.RodadaId == rid);
        }

        var total = await query.CountAsync(ct);
        var paginaValida = Math.Max(1, pagina);

        var linhas = await query
            .OrderByDescending(c => c.IniciadaEm)
            .ThenBy(c => c.Id)
            .Skip((paginaValida - 1) * tamanhoDaPagina)
            .Take(tamanhoDaPagina)
            .Select(c => new
            {
                c.Id,
                c.IniciadaEm,
                c.TenantId,
                c.Provedor,
                c.Modelo,
                c.Sucesso,
                c.PodeResponder,
                c.TipoDeFallback,
                c.TipoDeFalha,
                c.CodigoHttp,
                c.LatenciaMs,
                c.TokensEntrada,
                c.TokensSaida,
                c.CustoEstimadoUsd,
                c.RodadaId,
            })
            .ToListAsync(ct);

        var tenantIds = linhas.Select(l => l.TenantId).Distinct().ToList();
        var nomes = await db.Tenants.AsNoTracking()
            .Where(t => tenantIds.Contains(t.Id))
            .Select(t => new { t.Id, t.NomeFantasia, t.RazaoSocial })
            .ToDictionaryAsync(t => t.Id, ct);

        var itens = linhas
            .Select(l => new LinhaDoHistoricoResponse(
                l.Id,
                l.IniciadaEm,
                l.TenantId,
                nomes.TryGetValue(l.TenantId, out var t) ? (t.NomeFantasia ?? t.RazaoSocial) : "(tenant removido)",
                l.Provedor.ToString(),
                l.Modelo,
                ResultadoLegivel(l.Sucesso, l.PodeResponder),
                l.TipoDeFallback.ToString(),
                l.TipoDeFalha.ToString(),
                l.CodigoHttp,
                l.LatenciaMs,
                l.TokensEntrada,
                l.TokensSaida,
                l.CustoEstimadoUsd,
                l.RodadaId))
            .ToList();

        return new PaginaDeHistoricoResponse(itens, total, paginaValida, tamanhoDaPagina);
    }

    private static string ResultadoLegivel(bool sucesso, bool? podeResponder) => (sucesso, podeResponder) switch
    {
        (true, true) => "Entregue",
        (true, false) => "NaoSoubeResponder",
        _ => "Falha",
    };
}
