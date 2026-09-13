namespace CrmCartorio.Api.Modules.Usuarios.Application;

/// <summary>
/// SEC-03: gestao de MFA — ativacao, confirmacao, desativacao, regeneracao de
/// codigos, matricula privilegiada por ticket, reset administrativo e a
/// politica de obrigatoriedade. Login (etapas 1 e 2) fica em
/// <see cref="IAutenticacaoService"/> — a fronteira e "autenticar" vs. "gerir a
/// conta ja autenticada".
/// </summary>
public interface IMfaService
{
    // ---------------------------------------------------- sessao JWT normal

    /// <summary>
    /// 1a ativacao: exige so <c>SenhaAtual</c>. Trocar autenticador com MFA ja
    /// ativo: exige tambem <c>CodigoAtual</c> valido — um JWT roubado sozinho
    /// nunca substitui o segredo.
    /// </summary>
    Task<IniciarMfaResponse> IniciarAsync(IdentidadeParaMfa identidade, IniciarMfaRequest request, string? ip, CancellationToken ct = default);

    /// <summary>Confirma o segredo PENDENTE. So aqui <c>MfaAtivo</c> vira true.</summary>
    Task<ConfirmarMfaResponse> ConfirmarAsync(IdentidadeParaMfa identidade, ConfirmarMfaRequest request, string? ip, CancellationToken ct = default);

    /// <summary>Recusa se a politica da serventia exige MFA para esta conta.</summary>
    Task DesativarAsync(IdentidadeParaMfa identidade, DesativarMfaRequest request, string? ip, CancellationToken ct = default);

    Task<RegenerarCodigosResponse> RegenerarCodigosAsync(IdentidadeParaMfa identidade, RegenerarCodigosRequest request, string? ip, CancellationToken ct = default);

    Task<MfaSituacaoResponse> ObterSituacaoAsync(IdentidadeParaMfa identidade, CancellationToken ct = default);

    /// <summary>
    /// SEC-03 — revoga TODOS os "confiar neste dispositivo por 30 dias" da
    /// propria conta; o proximo login de qualquer navegador volta a exigir o
    /// segundo fator. Devolve quantos foram revogados.
    /// </summary>
    /// <remarks>
    /// Sem step-up de proposito, diferente das demais acoes deste servico:
    /// desativar MFA, trocar autenticador ou regenerar codigos podem ENFRAQUECER
    /// a conta e por isso exigem senha + fator atual; revogar confianca so
    /// pode fortalece-la.
    /// </remarks>
    Task<int> EsquecerDispositivosConfiaveisAsync(IdentidadeParaMfa identidade, CancellationToken ct = default);

    // -------------------------------------------- matricula privilegiada (ticket)

    /// <summary>
    /// Valida e-mail + senha + ticket fora de banda; devolve um desafio de
    /// matricula (nunca JWT — a conta ainda nao tem MFA, entao nao ha sessao
    /// completa a emitir ainda).
    /// </summary>
    Task<DesafioMfaResponse> IniciarMatriculaPrivilegiadaAsync(MatricularPrivilegiadoRequest request, string? ip, CancellationToken ct = default);

    Task<IniciarMfaResponse> IniciarViaDesafioAsync(IniciarViaDesafioRequest request, CancellationToken ct = default);

    /// <summary>Confirma e SO ENTAO emite a primeira sessao completa da conta.</summary>
    Task<ConfirmarMfaResponse> ConfirmarViaDesafioAsync(ConfirmarViaDesafioRequest request, string? ip, CancellationToken ct = default);

    // ---------------------------------------------------------- administrativo

    /// <summary>Ator ja matriculado emite ticket para outra conta privilegiada. Exige step-up.</summary>
    Task<TicketResponse> EmitirTicketAsync(IdentidadeParaMfa ator, Guid atendenteIdAlvo, EmitirTicketRequest request, string? ip, CancellationToken ct = default);

    /// <summary>Reset administrativo. Alvo Titular exige ator Administrador+Titular.</summary>
    Task ResetarAsync(IdentidadeParaMfa ator, Guid atendenteIdAlvo, ResetarMfaRequest request, string? ip, CancellationToken ct = default);

    /// <summary>
    /// Blindagem LGPD — step-up de segundo fator para uma ação sensível de
    /// OUTRO módulo (hoje: aprovar acesso técnico autorizado,
    /// <c>Modules/Seguranca</c>). Mesma disciplina de porteiro único do resto
    /// do SEC-03: valida e CONSOME o código na mesma transação — nunca
    /// reaproveitável. Lança <see cref="UsuariosServiceException"/> com os
    /// mesmos códigos do restante deste serviço
    /// (<c>mfa_obrigatorio_nao_matriculado</c> se o ator não tem MFA ativo,
    /// <c>codigo_mfa_invalido</c>, <c>mfa_bloqueado</c>) — o chamador não
    /// precisa reinterpretar um vocabulário de erro novo.
    /// </summary>
    Task ConferirSegundoFatorAsync(IdentidadeParaMfa ator, string? codigo, CancellationToken ct = default);

    /// <summary>
    /// Liga <c>Tenants.MfaObrigatorioParaAdministradores</c>. Aborta se houver
    /// privilegiado sem MFA. Devolve sessao NOVA para o proprio ator — o flip
    /// tambem sobe o TokenVersion dele.
    /// </summary>
    Task<LoginResponse> AtivarObrigatoriedadeAsync(IdentidadeParaMfa ator, AtivarObrigatoriedadeRequest request, string? ip, CancellationToken ct = default);
}
