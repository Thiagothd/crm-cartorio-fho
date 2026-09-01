namespace CrmCartorio.Api.Modules.Usuarios.Domain.Enums;

/// <summary>SEC-03: para que serve um desafio de MFA aberto.</summary>
public enum TipoDeDesafioMfa : byte
{
    /// <summary>Segunda etapa de um login normal, apos a senha conferir.</summary>
    SegundoFator = 1,

    /// <summary>
    /// Sessao de matricula para conta PRIVILEGIADA (Titular ou Administrador) —
    /// aberta so depois de confirmado o ticket fora de banda. Conta NAO
    /// privilegiada nao precisa de desafio para matricular: JWT + senha atual
    /// bastam, chamando <c>IMfaService.IniciarAsync</c>/<c>ConfirmarAsync</c>
    /// direto.
    /// </summary>
    MatriculaPrivilegiada = 2,

    /// <summary>
    /// O ticket em si: emitido fora de banda por quem ja tem MFA (ou pela
    /// ferramenta de bootstrap), consumido para abrir um desafio
    /// <see cref="MatriculaPrivilegiada"/>.
    /// </summary>
    Ticket = 3
}
