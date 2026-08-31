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
    PoliticaDeMfaAtivada = 10
}
