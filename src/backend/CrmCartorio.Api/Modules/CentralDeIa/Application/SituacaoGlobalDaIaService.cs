using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.RespostaInteligente.Application;
using CrmCartorio.Api.Modules.RespostaInteligente.Application.Cota;
using CrmCartorio.Api.Modules.RespostaInteligente.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CrmCartorio.Api.Modules.CentralDeIa.Application;

/// <summary>
/// Único serviço do sistema que lê <c>ChamadasDeIa</c> SEM filtro de
/// <c>TenantId</c> — deliberado, e não um esquecimento. A Central existe
/// exatamente para consolidar saúde/custo/falha ENTRE tenants, um papel que
/// nenhuma tela do cartório pode ter: todo <c>Atendente</c> está preso ao
/// próprio <c>TenantId</c> (ver <c>IContextoDoUsuario</c>), e mesmo com um
/// tenant só hoje, esta consulta já nasce pensando em vários. Ver
/// Modules/CentralDeIa/README.md.
/// </summary>
public class SituacaoGlobalDaIaService(
    CrmDbContext db,
    IOptions<OpcoesDaIA> opcoes,
    DisjuntorDoGemini disjuntor) : ISituacaoGlobalDaIa
{
    public async Task<SituacaoGlobalDaIaResponse> ObterAsync(DateTime? de, DateTime? ate, CancellationToken ct = default)
    {
        var valor = opcoes.Value;
        // Meia-noite no fuso da serventia — mesmo motivo de SituacaoDaIa.cs. A
        // Central roda num servidor só (America/Sao_Paulo), então não há um
        // "fuso da serventia" diferente por tenant ainda (isso é Fase 3).
        var inicioDoDiaUtc = DiaLocal.InicioDoDia(DateTime.UtcNow);
        var periodoDe = de ?? inicioDoDiaUtc;
        var periodoAte = ate ?? DateTime.UtcNow;

        var gemini = await MontarEstadoDoGeminiAsync(valor, inicioDoDiaUtc, ct);

        // Sem `Where(c => c.TenantId == ...)` — ver o comentário da classe.
        var chamadasDoPeriodo = await db.ChamadasDeIa.AsNoTracking()
            .Where(c => c.IniciadaEm >= periodoDe && c.IniciadaEm <= periodoAte)
            .Select(c => new
            {
                c.TenantId,
                c.Provedor,
                c.Sucesso,
                c.TipoDeFallback,
                c.TipoDeFalha,
                c.CustoEstimadoUsd,
            })
            .ToListAsync(ct);

        var nomesDosTenants = await db.Tenants.AsNoTracking()
            .Select(t => new { t.Id, t.NomeFantasia, t.RazaoSocial })
            .ToDictionaryAsync(t => t.Id, ct);

        var porTenant = chamadasDoPeriodo
            .GroupBy(c => c.TenantId)
            .Select(grupo =>
            {
                var sucessos = grupo.Where(c => c.Sucesso).ToList();
                // Motivo de fallback so faz sentido para o Gemini (o Anthropic e a
                // contingencia — falha dele nao "cai" para mais ninguem) — mesmo
                // recorte de SituacaoDaIa.ObterAsync.
                var falhasDoGemini = grupo.Where(c => !c.Sucesso && c.Provedor == ProvedorDeIa.Gemini).ToList();
                var custoUsd = sucessos.Sum(c => c.CustoEstimadoUsd ?? 0m);

                // TipoDeFallback, nao "!Sucesso && Provedor==Gemini" — mesmo
                // motivo de SituacaoDaIa.cs: o Gemini pode ter sido pulado sem
                // nunca ser discado, e falhasDoGemini fica vazio mesmo com
                // fallback tendo acontecido.
                var fallbackExecutado = grupo.Count(c => c.TipoDeFallback == TipoDeFallbackDeIa.Executado);
                var fallbackPreventivo = grupo.Count(c =>
                    c.TipoDeFallback is TipoDeFallbackDeIa.PreventivoDesligado
                        or TipoDeFallbackDeIa.PreventivoDisjuntor
                        or TipoDeFallbackDeIa.PreventivoMargemDeCota
                        or TipoDeFallbackDeIa.Indeterminado);

                var nome = nomesDosTenants.TryGetValue(grupo.Key, out var tenant)
                    ? tenant.NomeFantasia ?? tenant.RazaoSocial
                    : "(tenant removido)";

                return new SituacaoPorTenantResponse(
                    TenantId: grupo.Key,
                    NomeTenant: nome,
                    TotalDeChamadas: grupo.Count(),
                    Sucessos: sucessos.Count,
                    Falhas: grupo.Count() - sucessos.Count,
                    AtendidasPeloGemini: sucessos.Count(c => c.Provedor == ProvedorDeIa.Gemini),
                    AtendidasPeloHaiku: sucessos.Count(c => c.Provedor == ProvedorDeIa.Anthropic),
                    CustoEstimadoUsd: custoUsd,
                    CustoEstimadoReais: Math.Round(custoUsd * valor.Anthropic.CotacaoDoDolar, 2),
                    MotivosDeFallback: new MotivosDeFallbackResponse(
                        Quota: falhasDoGemini.Count(c => c.TipoDeFalha == TipoDeFalhaDeIa.QuotaEsgotada),
                        RateLimit: falhasDoGemini.Count(c => c.TipoDeFalha == TipoDeFalhaDeIa.RateLimit),
                        Indisponibilidade: falhasDoGemini.Count(c =>
                            c.TipoDeFalha is TipoDeFalhaDeIa.Indisponibilidade or TipoDeFalhaDeIa.Timeout or TipoDeFalhaDeIa.Rede),
                        Configuracao: falhasDoGemini.Count(c => c.TipoDeFalha == TipoDeFalhaDeIa.Configuracao),
                        Outros: falhasDoGemini.Count(c => c.TipoDeFalha == TipoDeFalhaDeIa.RespostaInvalida),
                        FallbackExecutado: fallbackExecutado,
                        FallbackPreventivo: fallbackPreventivo));
            })
            .OrderByDescending(item => item.TotalDeChamadas)
            .ToList();

        return new SituacaoGlobalDaIaResponse(gemini, porTenant, periodoDe, periodoAte);
    }

    /// <remarks>
    /// O disjuntor é singleton particionado só por fingerprint de chave
    /// (<see cref="FingerprintDeChave"/>), global ao PROCESSO — não por tenant
    /// (mesmo particionamento de <see cref="Application.Cota.ContadorDeCotaDoGemini"/>
    /// em RespostaInteligente). Por isso o motivo da última falha também é lido
    /// SEM filtro de tenant: é a mesma falha, de qualquer serventia, que abriu o
    /// circuito para todo mundo.
    /// </remarks>
    private async Task<EstadoGlobalDoGeminiResponse> MontarEstadoDoGeminiAsync(
        OpcoesDaIA valor, DateTime inicioDoDiaUtc, CancellationToken ct)
    {
        if (!valor.GeminiEstaConfigurado)
        {
            return new EstadoGlobalDoGeminiResponse("SemChave", null, null);
        }

        var chaveDeProjeto = FingerprintDeChave.Calcular(valor.Gemini.ApiKey);
        var situacaoDoDisjuntor = disjuntor.Consultar(chaveDeProjeto);

        if (situacaoDoDisjuntor.Estado != EstadoDoDisjuntor.Fechado)
        {
            var ultimaFalha = await db.ChamadasDeIa.AsNoTracking()
                .Where(c => c.Provedor == ProvedorDeIa.Gemini && !c.Sucesso && c.IniciadaEm >= inicioDoDiaUtc)
                .OrderByDescending(c => c.IniciadaEm)
                .Select(c => c.MotivoDoFallback ?? c.TipoDeFalha.ToString())
                .FirstOrDefaultAsync(ct);

            return new EstadoGlobalDoGeminiResponse("Contingencia", situacaoDoDisjuntor.VoltaATentarEm, ultimaFalha);
        }

        return new EstadoGlobalDoGeminiResponse("Disponivel", null, null);
    }
}
