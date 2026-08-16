namespace CrmCartorio.Api.Modules.CanalWhatsApp.Application;

/// <summary>Estado do canal para a tela de Configuracoes.</summary>
/// <param name="Habilitado">O canal esta ligado na configuracao do servidor.</param>
/// <param name="Conectado">A instancia esta pareada e operante.</param>
/// <param name="Estado">Texto cru do gateway (`open`, `connecting`, `close`).</param>
/// <param name="QrCodeBase64">PNG em data-URI enquanto espera a leitura; nulo se pareado.</param>
/// <param name="Instancia">Nome da instancia vinculada a esta serventia.</param>
public record SituacaoDoCanal(
    bool Habilitado,
    bool Conectado,
    string Estado,
    string? QrCodeBase64,
    string? Instancia);

/// <summary>
/// Prepara e inspeciona o pareamento do numero, para a tela de Configuracoes.
/// </summary>
public interface IPareamentoDoCanal
{
    /// <summary>
    /// Cria a instancia se preciso, registra o webhook, vincula ao tenant e
    /// devolve o QR.
    /// </summary>
    /// <remarks>
    /// Idempotente de ponta a ponta: e o mesmo botao para conectar pela primeira
    /// vez e para repareamento depois de a sessao cair.
    /// </remarks>
    Task<SituacaoDoCanal> PrepararAsync(Guid tenantId, string urlBaseDaApi, CancellationToken ct = default);

    /// <summary>Situacao atual, sem mexer em nada.</summary>
    Task<SituacaoDoCanal> ConsultarAsync(Guid tenantId, CancellationToken ct = default);
}
