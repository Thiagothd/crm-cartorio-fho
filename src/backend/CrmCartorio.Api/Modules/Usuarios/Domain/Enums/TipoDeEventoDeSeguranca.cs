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
    TicketDeRematriculaReemitido = 11
}
