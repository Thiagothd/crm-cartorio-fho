namespace CrmCartorio.Api.Modules.CanalWhatsApp.Application;

/// <summary>
/// Única fonte da resposta "este número pode falar com o nosso canal?".
/// </summary>
/// <remarks>
/// Consumida pelos dois sentidos do canal — <c>ProcessadorDeWebhook</c> (o que
/// entra) e <c>EnviadorPeloWhatsApp</c> (o que sai) —, para que a regra nunca
/// divirja entre os dois lados. Enquanto o tenant não tiver um modo salvo pela
/// tela (<c>Tenant.WhatsappModoDeRecebimento</c> nulo), a fonte é
/// <c>CanalWhatsApp:NumerosDeTeste</c> do appsettings, para preservar o
/// comportamento que já está em produção.
/// </remarks>
public interface IRegrasDoCanal
{
    /// <summary>Modo atual e as duas listas, já resolvidas para o tenant da requisição.</summary>
    Task<RegrasDoCanalResponse> ObterAsync(CancellationToken ct = default);

    Task<RegrasDoCanalResponse> DefinirModoAsync(DefinirModoRequest request, CancellationToken ct = default);

    Task<NumeroDoCanalResponse> AdicionarAsync(AdicionarNumeroRequest request, CancellationToken ct = default);

    Task RemoverAsync(Guid numeroId, CancellationToken ct = default);

    /// <summary>
    /// Se o telefone (E.164, só dígitos) pode falar com o canal deste tenant
    /// agora — chamado tanto na entrada quanto na saída da mensagem.
    /// </summary>
    Task<VeredictoDoNumero> PodeFalarComAsync(Guid tenantId, string telefoneE164, CancellationToken ct = default);

    /// <summary>
    /// Registra um descarte (mensagem que não entrou por causa do filtro).
    /// Poda linhas com mais de 7 dias antes de gravar — não é dado de
    /// atendimento, não deve virar arquivo permanente.
    /// </summary>
    Task RegistrarDescarteAsync(
        Guid tenantId,
        string telefoneE164,
        Domain.Enums.MotivoDoDescarte motivo,
        CancellationToken ct = default);

    Task<DescartesResponse> DescartesRecentesAsync(CancellationToken ct = default);
}
