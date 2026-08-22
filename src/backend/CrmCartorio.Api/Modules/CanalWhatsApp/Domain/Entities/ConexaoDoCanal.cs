using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Modules.CanalWhatsApp.Domain.Enums;

namespace CrmCartorio.Api.Modules.CanalWhatsApp.Domain.Entities;

/// <summary>
/// Uma sessão do número pareado, do pareamento até a queda ou a troca.
/// </summary>
/// <remarks>
/// Existe para a tela responder "quando foi que caiu?" e "quem trocou o
/// número?" sem depender do log do servidor. <see cref="DesconectadoEm"/> nulo
/// significa que esta é a sessão em curso — no máximo uma por tenant fica
/// assim por vez.
/// </remarks>
public class ConexaoDoCanal : EntidadeBase
{
    public required Guid TenantId { get; set; }

    /// <summary>Número pareado nesta sessão. Nulo se a sessão caiu antes do gateway informar o número.</summary>
    public string? Numero { get; set; }

    public string? PerfilNome { get; set; }

    public DateTime ConectadoEm { get; set; }

    /// <summary>Nulo enquanto a sessão está em curso.</summary>
    public DateTime? DesconectadoEm { get; set; }

    /// <summary>Quem pediu a troca ou a desconexão pela tela. Nulo em pareamento e queda do gateway.</summary>
    public Guid? AtendenteId { get; set; }

    public required MotivoDaConexao Motivo { get; set; }
}
