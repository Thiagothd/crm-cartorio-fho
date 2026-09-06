using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.RespostaInteligente.Application.Cota;
using CrmCartorio.Api.Modules.RespostaInteligente.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CrmCartorio.Api.Modules.RespostaInteligente.Application;

public class SituacaoDaIa(
    CrmDbContext db,
    IContextoDoUsuario ctx,
    IOptions<OpcoesDaIA> opcoes,
    DisjuntorDoGemini disjuntor,
    ContadorDeCotaDoGemini cotaDoGemini,
    ContadorDeLimiteDiario limiteDiario) : ISituacaoDaIa
{
    public async Task<SituacaoDaIaResponse> ObterAsync(DateTime? de, DateTime? ate, CancellationToken ct = default)
    {
        if (!ctx.PodeGerirCadastros)
        {
            throw RespostaInteligenteServiceException.SemPermissao();
        }

        var valor = opcoes.Value;
        // Meia-noite no fuso da serventia, nao no fuso UTC do servidor — mesmo
        // limite que ContadorDeLimiteDiario usa para os dois tetos abaixo. Os
        // dois precisam mudar juntos: um card "hoje" em UTC ao lado de um teto
        // "hoje" em fuso local diverge visivelmente entre 21h e meia-noite.
        var inicioDoDiaUtc = DiaLocal.InicioDoDia(DateTime.UtcNow);
        var periodoDe = de ?? inicioDoDiaUtc;
        var periodoAte = ate ?? DateTime.UtcNow;

        var usadoHojeGeral = await limiteDiario.ConsultarUsadoAsync(ctx.TenantId, CategoriaDeLimiteDiario.Geral, ct);
        var usadoHojeHaiku = await limiteDiario.ConsultarUsadoAsync(ctx.TenantId, CategoriaDeLimiteDiario.Haiku, ct);

        var chaveDeProjeto = valor.GeminiEstaConfigurado ? FingerprintDeChave.Calcular(valor.Gemini.ApiKey) : null;

        var gemini = await MontarEstadoDoGeminiAsync(valor, chaveDeProjeto, inicioDoDiaUtc, ct);
        var anthropic = MontarEstadoDoAnthropic(valor, usadoHojeHaiku);
        var franquia = await MontarFranquiaAsync(valor, chaveDeProjeto, ct);

        var chamadasDoPeriodo = await db.ChamadasDeIa.AsNoTracking()
            .Where(c => c.TenantId == ctx.TenantId && c.IniciadaEm >= periodoDe && c.IniciadaEm <= periodoAte)
            .Select(c => new
            {
                c.Provedor,
                c.Sucesso,
                c.FoiFallback,
                c.TipoDeFallback,
                c.TipoDeFalha,
                c.TokensEntrada,
                c.TokensSaida,
                c.TokensPensamento,
                c.CustoEstimadoUsd
            })
            .ToListAsync(ct);

        var respostasEntregues = chamadasDoPeriodo.Where(c => c.Sucesso).ToList();
        var geminiSucessos = respostasEntregues.Count(c => c.Provedor == ProvedorDeIa.Gemini);
        var anthropicSucessos = respostasEntregues.Where(c => c.Provedor == ProvedorDeIa.Anthropic).ToList();
        var totalDeChamadasBemSucedidas = respostasEntregues.Count;

        var custoRealHaikuUsd = anthropicSucessos.Sum(c => c.CustoEstimadoUsd ?? 0m);
        var custoMedioHaikuUsd = anthropicSucessos.Count > 0
            ? custoRealHaikuUsd / anthropicSucessos.Count
            : 0.002m; // ~US$0,002/resposta — mesma estimativa historica do Haiku 4.5, usada so quando ainda nao ha amostra propria no periodo.

        var custoSeFosseTudoHaikuUsd = custoRealHaikuUsd + geminiSucessos * custoMedioHaikuUsd;
        var economiaUsd = custoSeFosseTudoHaikuUsd - custoRealHaikuUsd;
        var percentualDeEconomia = custoSeFosseTudoHaikuUsd > 0 ? economiaUsd / custoSeFosseTudoHaikuUsd * 100m : 0m;

        var falhas = chamadasDoPeriodo.Where(c => !c.Sucesso && c.Provedor == ProvedorDeIa.Gemini).ToList();

        // TipoDeFallback, nao "!Sucesso && Provedor==Gemini": o Gemini pode
        // nunca ter sido discado (desligado, disjuntor aberto, margem de cota) e
        // o Haiku responder sozinho — nesse caso nao existe NENHUMA linha de
        // falha do Gemini para contar em `falhas` acima, mas o fallback
        // aconteceu. Achado ao vivo: a tela mostrava "Caidas no Haiku: 1" e
        // "Nenhum fallback no periodo" ao mesmo tempo, porque a primeira frase
        // contava sucessos do Anthropic e a segunda contava so falhas do Gemini.
        var fallbackExecutado = chamadasDoPeriodo.Count(c => c.TipoDeFallback == TipoDeFallbackDeIa.Executado);
        var fallbackPreventivo = chamadasDoPeriodo.Count(c =>
            c.TipoDeFallback is TipoDeFallbackDeIa.PreventivoDesligado
                or TipoDeFallbackDeIa.PreventivoDisjuntor
                or TipoDeFallbackDeIa.PreventivoMargemDeCota
                or TipoDeFallbackDeIa.Indeterminado);

        var hoje = new HojeDaIaResponse(
            TotalDeChamadas: totalDeChamadasBemSucedidas,
            AtendidasPeloGemini: geminiSucessos,
            CaidasNoHaiku: anthropicSucessos.Count,
            PercentualGratuito: totalDeChamadasBemSucedidas > 0
                ? Math.Round((decimal)geminiSucessos / totalDeChamadasBemSucedidas * 100m, 1)
                : 0m,
            CustoEstimadoHaikuReais: Math.Round(custoRealHaikuUsd * valor.Anthropic.CotacaoDoDolar, 2),
            TokensDoGemini: respostasEntregues
                .Where(c => c.Provedor == ProvedorDeIa.Gemini)
                .Sum(c => c.TokensEntrada + c.TokensSaida + c.TokensPensamento));

        var motivos = new MotivosDeFallbackResponse(
            Quota: falhas.Count(c => c.TipoDeFalha == TipoDeFalhaDeIa.QuotaEsgotada),
            RateLimit: falhas.Count(c => c.TipoDeFalha == TipoDeFalhaDeIa.RateLimit),
            Indisponibilidade: falhas.Count(c => c.TipoDeFalha is TipoDeFalhaDeIa.Indisponibilidade or TipoDeFalhaDeIa.Timeout or TipoDeFalhaDeIa.Rede),
            Configuracao: falhas.Count(c => c.TipoDeFalha == TipoDeFalhaDeIa.Configuracao),
            Outros: falhas.Count(c => c.TipoDeFalha == TipoDeFalhaDeIa.RespostaInvalida),
            FallbackExecutado: fallbackExecutado,
            FallbackPreventivo: fallbackPreventivo);

        var economia = new EconomiaResponse(
            CustoRealHaikuReais: Math.Round(custoRealHaikuUsd * valor.Anthropic.CotacaoDoDolar, 2),
            CustoSeFosseTudoHaikuReais: Math.Round(custoSeFosseTudoHaikuUsd * valor.Anthropic.CotacaoDoDolar, 2),
            EconomiaEstimadaReais: Math.Round(economiaUsd * valor.Anthropic.CotacaoDoDolar, 2),
            PercentualDeEconomia: Math.Round(percentualDeEconomia, 1));

        return new SituacaoDaIaResponse(
            Habilitada: valor.Habilitada,
            EstaConfigurada: valor.EstaConfigurada,
            Gemini: gemini,
            Anthropic: anthropic,
            LimiteDiario: valor.LimiteDiario,
            UsadoHojeGeral: usadoHojeGeral,
            LimiteDiarioHaiku: valor.Anthropic.LimiteDiarioHaiku,
            UsadoHojeHaiku: usadoHojeHaiku,
            Franquia: franquia,
            Hoje: hoje,
            MotivosDeFallback: motivos,
            Economia: economia,
            PeriodoDe: periodoDe,
            PeriodoAte: periodoAte);
    }

    public async Task<ResumoDaIaResponse> ObterResumoAsync(CancellationToken ct = default)
    {
        var valor = opcoes.Value;
        var usadoHoje = await limiteDiario.ConsultarUsadoAsync(ctx.TenantId, CategoriaDeLimiteDiario.Geral, ct);

        var modelo = valor.GeminiEstaConfigurado ? valor.Gemini.Modelo : valor.ModeloDoAnthropicResolvido;

        return new ResumoDaIaResponse(valor.Habilitada, valor.EstaConfigurada, modelo, valor.LimiteDiario, usadoHoje);
    }

    private async Task<EstadoDoProvedorResponse> MontarEstadoDoGeminiAsync(
        OpcoesDaIA valor, string? chaveDeProjeto, DateTime inicioDoDiaUtc, CancellationToken ct)
    {
        if (!valor.GeminiEstaConfigurado || chaveDeProjeto is null)
        {
            return new EstadoDoProvedorResponse("Gemini 3.7 Flash", valor.Gemini.Modelo, false, "SemChave", null, null);
        }

        var situacaoDoDisjuntor = disjuntor.Consultar(chaveDeProjeto);

        if (situacaoDoDisjuntor.Estado != EstadoDoDisjuntor.Fechado)
        {
            var ultimaFalha = await db.ChamadasDeIa.AsNoTracking()
                .Where(c => c.TenantId == ctx.TenantId && c.Provedor == ProvedorDeIa.Gemini
                         && !c.Sucesso && c.IniciadaEm >= inicioDoDiaUtc)
                .OrderByDescending(c => c.IniciadaEm)
                .Select(c => c.MotivoDoFallback ?? c.TipoDeFalha.ToString())
                .FirstOrDefaultAsync(ct);

            return new EstadoDoProvedorResponse(
                "Gemini 3.7 Flash", valor.Gemini.Modelo, true, "Contingencia",
                situacaoDoDisjuntor.VoltaATentarEm, ultimaFalha);
        }

        return new EstadoDoProvedorResponse("Gemini 3.7 Flash", valor.Gemini.Modelo, true, "Disponivel", null, null);
    }

    private static EstadoDoProvedorResponse MontarEstadoDoAnthropic(OpcoesDaIA valor, int usadoHojeHaiku)
    {
        if (!valor.AnthropicEstaConfigurado)
        {
            return new EstadoDoProvedorResponse(
                "Claude Haiku 4.5", valor.ModeloDoAnthropicResolvido, false, "SemChave", null, null);
        }

        if (usadoHojeHaiku >= valor.Anthropic.LimiteDiarioHaiku)
        {
            return new EstadoDoProvedorResponse(
                "Claude Haiku 4.5", valor.ModeloDoAnthropicResolvido, true, "TetoDiarioAtingido", null,
                $"{usadoHojeHaiku}/{valor.Anthropic.LimiteDiarioHaiku} respostas hoje.");
        }

        return new EstadoDoProvedorResponse(
            "Claude Haiku 4.5", valor.ModeloDoAnthropicResolvido, true, "Disponivel", null, null);
    }

    private async Task<FranquiaDoGeminiResponse> MontarFranquiaAsync(
        OpcoesDaIA valor, string? chaveDeProjeto, CancellationToken ct)
    {
        if (chaveDeProjeto is null)
        {
            return new FranquiaDoGeminiResponse(false, null, 0, null, 0, null, 0, valor.Gemini.PercentualDeSeguranca, null);
        }

        var situacao = await cotaDoGemini.ConsultarAsync(chaveDeProjeto, valor.Gemini, ct);

        return new FranquiaDoGeminiResponse(
            LimitesCadastrados: situacao.LimitesCadastrados,
            LimiteRpd: valor.Gemini.LimiteRpd > 0 ? valor.Gemini.LimiteRpd : null,
            RequisicoesHoje: situacao.RequisicoesHoje,
            LimiteRpm: valor.Gemini.LimiteRpm > 0 ? valor.Gemini.LimiteRpm : null,
            RequisicoesNaJanela: situacao.RequisicoesNaJanela,
            LimiteTpm: valor.Gemini.LimiteTpm > 0 ? valor.Gemini.LimiteTpm : null,
            TokensNaJanela: situacao.TokensNaJanela,
            PercentualDeSeguranca: valor.Gemini.PercentualDeSeguranca,
            SugestaoDeLimiteRpd: situacao.SugestaoDeLimiteRpd);
    }
}
