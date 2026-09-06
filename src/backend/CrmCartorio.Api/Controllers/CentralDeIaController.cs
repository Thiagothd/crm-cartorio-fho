using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.CentralDeIa.Application;
using CrmCartorio.Api.Modules.RespostaInteligente.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CrmCartorio.Api.Controllers;

/// <summary>
/// Área privada do desenvolvedor da equipe do projeto — fora do modelo Atendente/Tenant.
/// Ver Modules/CentralDeIa/README.md.
/// </summary>
[ApiController]
[Route("api/central-ia")]
public class CentralDeIaController(
    ICentralDeIaAutenticador autenticador,
    IObservabilidadeDaIa observabilidade) : ControllerBase
{
    private const int TamanhoDaPaginaPadrao = 20;
    private const int TamanhoDaPaginaMaximo = 100;

    /// <summary>
    /// Único endpoint anônimo do módulo. Confere o segredo compartilhado
    /// (<c>CentralDeIa:Chave</c>) em tempo fixo e, se bater, emite um JWT com a
    /// claim própria <c>central_ia_operador</c> — nunca as claims de Atendente.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting(PoliticaDeLimiteDeLogin.NomeCentralDeIa)]
    [HttpPost("entrar")]
    public async Task<ActionResult<EntrarNaCentralDeIaResponse>> Entrar(
        [FromBody] EntrarNaCentralDeIaRequest requisicao, CancellationToken ct) =>
        Ok(await autenticador.EntrarAsync(requisicao.Segredo, ct));

    /// <summary>
    /// Saúde agora, KPIs, linha do tempo, histograma de latência, erros,
    /// fallbacks, distribuição por modelo e o top 8 de tenants por custo —
    /// tudo sob os mesmos filtros globais. <paramref name="de"/>/<paramref name="ate"/>
    /// omitidos caem nos últimos 7 dias (mesmo padrão de <c>GET /api/ia/analise</c>).
    /// </summary>
    [Authorize(Policy = "CentralDeIa")]
    [HttpGet("observabilidade")]
    public async Task<ActionResult<ObservabilidadeDaIaResponse>> Observabilidade(
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        [FromQuery] Guid? tenantId,
        [FromQuery] ProvedorDeIa? provedor,
        [FromQuery] string? modelo,
        [FromQuery] ResultadoDaChamadaDeIa? resultado,
        [FromQuery] TipoDeFallbackDeIa? tipoDeFallback,
        [FromQuery] TipoDeFalhaDeIa? tipoDeFalha,
        [FromQuery] int? codigoHttp,
        CancellationToken ct) =>
        Ok(await observabilidade.ObterAsync(MontarFiltro(de, ate, tenantId, provedor, modelo, resultado, tipoDeFallback, tipoDeFalha, codigoHttp), ct));

    /// <summary>Tabela de tenants ordenável, buscável por nome e paginada — o drill-down de C.5.</summary>
    [Authorize(Policy = "CentralDeIa")]
    [HttpGet("tenants")]
    public async Task<ActionResult<PaginaDeTenantsResponse>> Tenants(
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        [FromQuery] Guid? tenantId,
        [FromQuery] ProvedorDeIa? provedor,
        [FromQuery] string? modelo,
        [FromQuery] ResultadoDaChamadaDeIa? resultado,
        [FromQuery] TipoDeFallbackDeIa? tipoDeFallback,
        [FromQuery] TipoDeFalhaDeIa? tipoDeFalha,
        [FromQuery] int? codigoHttp,
        [FromQuery] string? busca,
        [FromQuery] OrdenarTenantsPor ordenarPor = OrdenarTenantsPor.Tentativas,
        [FromQuery] bool decrescente = true,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanhoDaPagina = TamanhoDaPaginaPadrao,
        CancellationToken ct = default) =>
        Ok(await observabilidade.ObterTenantsAsync(
            MontarFiltro(de, ate, tenantId, provedor, modelo, resultado, tipoDeFallback, tipoDeFalha, codigoHttp),
            busca,
            ordenarPor,
            decrescente,
            pagina,
            Math.Clamp(tamanhoDaPagina, 1, TamanhoDaPaginaMaximo),
            ct));

    /// <summary>
    /// Histórico paginado, uma linha por tentativa — a investigação de C.6.
    /// Buscar por <paramref name="rodadaId"/> traz as duas tentativas da mesma
    /// pergunta lado a lado, quando houve fallback.
    /// </summary>
    [Authorize(Policy = "CentralDeIa")]
    [HttpGet("historico")]
    public async Task<ActionResult<PaginaDeHistoricoResponse>> Historico(
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        [FromQuery] Guid? tenantId,
        [FromQuery] ProvedorDeIa? provedor,
        [FromQuery] string? modelo,
        [FromQuery] ResultadoDaChamadaDeIa? resultado,
        [FromQuery] TipoDeFallbackDeIa? tipoDeFallback,
        [FromQuery] TipoDeFalhaDeIa? tipoDeFalha,
        [FromQuery] int? codigoHttp,
        [FromQuery] Guid? rodadaId,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanhoDaPagina = TamanhoDaPaginaPadrao,
        CancellationToken ct = default) =>
        Ok(await observabilidade.ObterHistoricoAsync(
            MontarFiltro(de, ate, tenantId, provedor, modelo, resultado, tipoDeFallback, tipoDeFalha, codigoHttp),
            rodadaId,
            pagina,
            Math.Clamp(tamanhoDaPagina, 1, TamanhoDaPaginaMaximo),
            ct));

    private static FiltroDeObservabilidadeDaIa MontarFiltro(
        DateTime? de, DateTime? ate, Guid? tenantId, ProvedorDeIa? provedor, string? modelo,
        ResultadoDaChamadaDeIa? resultado, TipoDeFallbackDeIa? tipoDeFallback, TipoDeFalhaDeIa? tipoDeFalha, int? codigoHttp)
    {
        var (periodoDe, periodoAte, _) = PeriodoDeAnalise.Resolver(de, ate, DateTime.UtcNow);
        return new FiltroDeObservabilidadeDaIa(
            periodoDe, periodoAte, tenantId, provedor, modelo, resultado, tipoDeFallback, tipoDeFalha, codigoHttp);
    }
}
