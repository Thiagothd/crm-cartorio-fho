using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;

namespace CrmCartorio.Api.Modules.Usuarios.Domain.Entities;

/// <summary>
/// SEC-03: registro append-only de fatos de seguranca — nunca editado, nunca
/// apagado. Mesmo padrao de <c>CanalWhatsApp.Domain.Entities.ConexaoDoCanal</c>:
/// existe para a tela (e para uma investigacao) responderem "o que aconteceu e
/// quando" sem depender do log do servidor.
/// </summary>
/// <remarks>
/// <b>Nunca grava segredo, codigo, ticket, senha ou desafio</b> — so o FATO
/// (quem, o que, quando, de onde, por que). Isto vale tambem para
/// <see cref="Motivo"/>, que e texto livre digitado por um humano para uma
/// acao administrativa, nunca preenchido a partir de dado sensivel.
/// </remarks>
public class EventoDeSeguranca : EntidadeBase
{
    public required Guid TenantId { get; set; }

    /// <summary>De quem este evento fala — a conta afetada.</summary>
    public required Guid AtendenteId { get; set; }

    /// <summary>
    /// Quem executou a acao, quando foi outro atendente (reset administrativo,
    /// ativacao de politica). Nulo quando o proprio alvo agiu sobre si mesmo
    /// (ativou/desativou o proprio MFA, usou o proprio codigo de recuperacao).
    /// </summary>
    public Guid? AtorAtendenteId { get; set; }

    /// <summary>
    /// Identidade de quem agiu FORA do sistema — a pessoa com acesso SSH que
    /// rodou a ferramenta de bootstrap, ou quem conduziu um break-glass. Nunca
    /// usado para guardar motivo nem qualquer outro dado; e so identidade.
    /// </summary>
    public string? AtorExterno { get; set; }

    public required TipoDeEventoDeSeguranca Tipo { get; set; }

    public required DateTime OcorridoEm { get; set; }

    public string? IpDeOrigem { get; set; }

    /// <summary>Texto livre exigido nas acoes administrativas (reset, break-glass, flip de politica).</summary>
    public string? Motivo { get; set; }
}
