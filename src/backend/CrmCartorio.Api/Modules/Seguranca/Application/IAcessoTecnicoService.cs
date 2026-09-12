namespace CrmCartorio.Api.Modules.Seguranca.Application;

/// <summary>
/// Blindagem LGPD: "a equipe do projeto pede, o cartório aprova, a faixa avisa, a
/// trilha registra". Ver docs/PROTECAO-DE-DADOS.md e
/// Modules/Seguranca/README.md.
/// </summary>
public interface IAcessoTecnicoService
{
    /// <summary>Exige <c>suporte.solicitar_acesso</c> — só a conta técnica tem essa chave.</summary>
    Task<AutorizacaoDeAcessoTecnicoResponse> SolicitarAsync(
        SolicitarAcessoTecnicoRequest request, CancellationToken ct = default);

    /// <summary>Exige <c>seguranca.acesso_tecnico.aprovar</c> + segundo fator do aprovador.</summary>
    Task<AutorizacaoDeAcessoTecnicoResponse> AprovarAsync(
        Guid id, AprovarAcessoTecnicoRequest request, CancellationToken ct = default);

    /// <summary>Exige <c>seguranca.acesso_tecnico.aprovar</c>. Sem segundo fator: negar nunca amplia acesso.</summary>
    Task<AutorizacaoDeAcessoTecnicoResponse> NegarAsync(Guid id, CancellationToken ct = default);

    /// <summary>Exige <c>seguranca.acesso_tecnico.aprovar</c>. Sem segundo fator: reduzir acesso nunca é ação sensível.</summary>
    Task<AutorizacaoDeAcessoTecnicoResponse> RevogarAsync(Guid id, CancellationToken ct = default);

    /// <summary>Qualquer autenticado do tenant — é o que alimenta a faixa global. Nulo quando não há nada ativo.</summary>
    Task<AcessoTecnicoAtivoResponse?> ObterAtivaAsync(CancellationToken ct = default);

    /// <summary>Exige <c>seguranca.eventos.ver</c> — histórico completo para a tela "Acesso aos dados".</summary>
    Task<PaginaDeSeguranca<AutorizacaoDeAcessoTecnicoResponse>> ListarAsync(
        FiltroDeAcessoTecnico filtro, CancellationToken ct = default);
}
