using CrmCartorio.Api.Modules.Seguranca.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrmCartorio.Api.Controllers;

/// <summary>
/// A tela "Acesso aos dados" — quem abriu o quê, os eventos de segurança do
/// SEC-03 e o resumo de proteção ativa. Ver docs/PROTECAO-DE-DADOS.md.
/// </summary>
[ApiController]
[Authorize]
[Route("api/seguranca")]
public class SegurancaController(ISegurancaService servico, IAcessoTecnicoService acessoTecnico) : ControllerBase
{
    [HttpGet("acessos")]
    public async Task<ActionResult<PaginaDeSeguranca<RegistroDeAcessoResponse>>> Acessos(
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        [FromQuery] Guid? atendenteId,
        [FromQuery] Guid? conversaId,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanho = 50,
        CancellationToken ct = default) =>
        Ok(await servico.ListarAcessosAsync(
            new FiltroDeAcessos(de, ate, atendenteId, conversaId, pagina, tamanho), ct));

    [HttpGet("eventos")]
    public async Task<ActionResult<PaginaDeSeguranca<EventoDeSegurancaResponse>>> Eventos(
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanho = 50,
        CancellationToken ct = default) =>
        Ok(await servico.ListarEventosAsync(new FiltroDeEventos(de, ate, pagina, tamanho), ct));

    [HttpGet("protecao")]
    public async Task<ActionResult<ProtecaoAtivaResponse>> Protecao(CancellationToken ct) =>
        Ok(await servico.ObterProtecaoAtivaAsync(ct));

    // --------------------------------------------- Acesso técnico autorizado

    [HttpPost("acesso-tecnico/solicitacoes")]
    public async Task<ActionResult<AutorizacaoDeAcessoTecnicoResponse>> Solicitar(
        [FromBody] SolicitarAcessoTecnicoRequest request, CancellationToken ct) =>
        Ok(await acessoTecnico.SolicitarAsync(request, ct));

    [HttpPost("acesso-tecnico/solicitacoes/{id:guid}/aprovar")]
    public async Task<ActionResult<AutorizacaoDeAcessoTecnicoResponse>> Aprovar(
        Guid id, [FromBody] AprovarAcessoTecnicoRequest request, CancellationToken ct) =>
        Ok(await acessoTecnico.AprovarAsync(id, request, ct));

    [HttpPost("acesso-tecnico/solicitacoes/{id:guid}/negar")]
    public async Task<ActionResult<AutorizacaoDeAcessoTecnicoResponse>> Negar(Guid id, CancellationToken ct) =>
        Ok(await acessoTecnico.NegarAsync(id, ct));

    [HttpPost("acesso-tecnico/solicitacoes/{id:guid}/revogar")]
    public async Task<ActionResult<AutorizacaoDeAcessoTecnicoResponse>> Revogar(Guid id, CancellationToken ct) =>
        Ok(await acessoTecnico.RevogarAsync(id, ct));

    /// <summary>Qualquer autenticado do tenant — alimenta a faixa global. 204 quando não há nada ativo.</summary>
    [HttpGet("acesso-tecnico/ativa")]
    public async Task<ActionResult<AcessoTecnicoAtivoResponse>> Ativa(CancellationToken ct)
    {
        var ativa = await acessoTecnico.ObterAtivaAsync(ct);
        return ativa is null ? NoContent() : Ok(ativa);
    }

    [HttpGet("acesso-tecnico/solicitacoes")]
    public async Task<ActionResult<PaginaDeSeguranca<AutorizacaoDeAcessoTecnicoResponse>>> ListarSolicitacoes(
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanho = 50,
        CancellationToken ct = default) =>
        Ok(await acessoTecnico.ListarAsync(new FiltroDeAcessoTecnico(de, ate, pagina, tamanho), ct));
}
