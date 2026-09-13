namespace CrmCartorio.Api.Modules.Usuarios.Domain.Enums;

/// <summary>
/// SEC-03: fatos de seguranca registrados em <c>EventosDeSeguranca</c> —
/// append-only, nunca carrega segredo, codigo, ticket ou desafio, so o FATO.
/// </summary>
public enum TipoDeEventoDeSeguranca : byte
{
    MfaAtivado = 1,
    MfaDesativado = 2,
    CodigoDeRecuperacaoUsado = 3,
    CodigosDeRecuperacaoRegenerados = 4,
    ResetAdministrativo = 5,
    ResetDeEmergencia = 6,
    BloqueioDeMfaPorFalhas = 7,
    TicketDeMatriculaEmitido = 8,
    TicketDeBootstrapEmitido = 9,
    PoliticaDeMfaAtivada = 10,

    /// <summary>
    /// SEC-03 (bloqueador B20, sétima revisão independente) — reemissão de
    /// ticket de rematrícula DURANTE uma recuperação de emergência já
    /// aberta (o ticket original do break-glass foi perdido, expirou ou
    /// ficou inutilizável antes da rematrícula). Distinto de
    /// <see cref="ResetDeEmergencia"/> de propósito: nada sobre o estado de
    /// MFA do alvo muda aqui (continua desativado, a rematrícula continua
    /// obrigatória) — só um ticket novo é emitido. Conflar os dois no mesmo
    /// tipo esconderia, de um auditor, quantas vezes uma emergência precisou
    /// de reemissão (sinal de TTL curto demais ou processo lento).
    /// </summary>
    TicketDeRematriculaReemitido = 11,

    /// <summary>
    /// Permissões granulares: o conjunto de <c>PermissaoDoSistema</c> de
    /// <see cref="Entities.Atendente.Permissoes"/> foi alterado por outro
    /// atendente. Registra o FATO (quem mexeu, em quem, quando, de onde) —
    /// nunca as chaves em si, que não são segredo mas também não são o que
    /// este log existe para guardar; o estado atual já está na própria
    /// coluna. Ver Modules/Usuarios/README.md, "Permissões granulares".
    /// </summary>
    PermissoesAlteradas = 12,

    /// <summary>
    /// Blindagem LGPD (2026-09): login concluído com sucesso — sem MFA, por
    /// dispositivo confiável, ou pelo segundo fator completo. Antes desta
    /// entrega, login nunca virava evento de segurança nem gravava IP. Falha
    /// de senha/código continua NÃO virando evento aqui — o backoff por
    /// conta já existe, e uma tabela alimentada por tentativa errada seria
    /// alvo fácil de enchimento por quem só quer forçar o rodízio dela.
    /// </summary>
    LoginRealizado = 13
}
