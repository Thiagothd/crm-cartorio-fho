namespace CrmCartorio.Api.Modules.Seguranca.Application;

/// <summary>
/// Blindagem LGPD: "o suporte técnico pede, o cartório aprova, a faixa avisa, a
/// trilha registra". Ver docs/PROTECAO-DE-DADOS.md e
/// Modules/Seguranca/README.md.
/// </summary>
public interface IAcessoTecnicoService
{
    /// <summary>Exige <c>suporte.solicitar_acesso</c> — só a conta técnica tem essa chave.</summary>
    Task<AutorizacaoDeAcessoTecnicoResponse> SolicitarAsync(
        SolicitarAcessoTecnicoRequest request, CancellationToken ct = default);

    /// <summary>Exige <c>seguranca.acesso_tecnico.aprovar</c>. O ator já está autenticado na sessão do painel — decisão do Thiago em 2026-09-12 (ver histórico do commit) de não pedir um segundo passo de TOTP em cima de uma sessão já aberta.</summary>
    Task<AutorizacaoDeAcessoTecnicoResponse> AprovarAsync(Guid id, CancellationToken ct = default);

    /// <summary>Exige <c>seguranca.acesso_tecnico.aprovar</c>.</summary>
    Task<AutorizacaoDeAcessoTecnicoResponse> NegarAsync(Guid id, CancellationToken ct = default);

    /// <summary>Exige <c>seguranca.acesso_tecnico.aprovar</c>.</summary>
    Task<AutorizacaoDeAcessoTecnicoResponse> RevogarAsync(Guid id, CancellationToken ct = default);

    /// <summary>Qualquer autenticado do tenant — é o que alimenta a faixa global. Nulo quando não há nada ativo.</summary>
    Task<AcessoTecnicoAtivoResponse?> ObterAtivaAsync(CancellationToken ct = default);

    /// <summary>Exige <c>seguranca.eventos.ver</c> — histórico completo para a tela "Acesso aos dados".</summary>
    Task<PaginaDeSeguranca<AutorizacaoDeAcessoTecnicoResponse>> ListarAsync(
        FiltroDeAcessoTecnico filtro, CancellationToken ct = default);
}
