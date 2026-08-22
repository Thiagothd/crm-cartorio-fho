using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Modules.CanalWhatsApp.Domain.Enums;

namespace CrmCartorio.Api.Modules.CanalWhatsApp.Domain.Entities;

/// <summary>
/// Uma mensagem recebida que foi descartada pelo filtro de números, antes de
/// virar <c>Cliente</c> ou <c>Conversa</c>.
/// </summary>
/// <remarks>
/// Não é dado de atendimento — é telefone de quem não está autorizado a falar
/// com o canal — por isso <c>IRegrasDoCanal</c> poda linhas com mais de 7 dias
/// antes de cada leitura (ver <c>DescartesRecentesAsync</c>). Existe só para a
/// tela responder "por que a mensagem de fulano não chegou?" sem precisar
/// caçar no log da API.
/// </remarks>
public class DescarteDoCanal : EntidadeBase
{
    public required Guid TenantId { get; set; }

    /// <summary>Só dígitos, com o código do país.</summary>
    public required string Telefone { get; set; }

    public required MotivoDoDescarte Motivo { get; set; }
}
