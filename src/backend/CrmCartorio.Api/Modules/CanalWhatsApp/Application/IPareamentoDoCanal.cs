namespace CrmCartorio.Api.Modules.CanalWhatsApp.Application;

/// <summary>Estado do canal para a tela de Configuracoes.</summary>
/// <param name="Habilitado">O canal esta ligado na configuracao do servidor.</param>
/// <param name="Conectado">A instancia esta pareada e operante.</param>
/// <param name="Estado">Texto cru do gateway (`open`, `connecting`, `close`).</param>
/// <param name="QrCodeBase64">PNG em data-URI enquanto espera a leitura; nulo se pareado.</param>
/// <param name="Instancia">Nome da instancia vinculada a esta serventia.</param>
/// <param name="Numero">Numero pareado (E.164, so digitos), para a tela mostrar em vez do nome da instancia.</param>
/// <param name="PerfilNome">Nome do perfil de WhatsApp da sessao pareada.</param>
/// <param name="PerfilFotoUrl">Foto de perfil da sessao pareada.</param>
/// <param name="ConectadoEm">Quando a sessao atual pareou. Nulo enquanto desconectado.</param>
public record SituacaoDoCanal(
    bool Habilitado,
    bool Conectado,
    string Estado,
    string? QrCodeBase64,
    string? Instancia,
    string? Numero = null,
    string? PerfilNome = null,
    string? PerfilFotoUrl = null,
    DateTime? ConectadoEm = null);

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

    /// <summary>Situacao atual, sem mexer em nada — exceto persistir numero/perfil na primeira vez que os ve.</summary>
    Task<SituacaoDoCanal> ConsultarAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Fecha a sessao atual, limpa numero/perfil do tenant e devolve um QR novo
    /// para o proximo numero. A instancia e o webhook nao mudam — nenhuma
    /// conversa existente e tocada.
    /// </summary>
    Task<SituacaoDoCanal> TrocarNumeroAsync(
        Guid tenantId, Guid atendenteId, string urlBaseDaApi, CancellationToken ct = default);

    /// <summary>Fecha a sessao atual sem parear um numero novo.</summary>
    Task<SituacaoDoCanal> DesconectarAsync(Guid tenantId, Guid atendenteId, CancellationToken ct = default);

    /// <summary>
    /// Manda um texto fixo para um número, direto pelo gateway — sem criar
    /// <c>Conversa</c>, <c>Mensagem</c> nem protocolo. Prova que o canal está
    /// funcionando sem precisar de outro celular na mão.
    /// </summary>
    /// <remarks>Respeita a mesma regra de quem fala com o canal (<c>IRegrasDoCanal</c>).</remarks>
    Task<TesteDeEnvioResponse> TestarEnvioAsync(Guid tenantId, string telefone, CancellationToken ct = default);

    /// <summary>Últimas sessões do canal, mais recente primeiro — para a tela responder "quando caiu, e quem trocou".</summary>
    Task<IReadOnlyList<ConexaoResponse>> HistoricoDeConexoesAsync(
        Guid tenantId, int limite = 20, CancellationToken ct = default);
}

/// <param name="Enviado">Se a mensagem de teste saiu.</param>
/// <param name="Motivo">Por que não saiu, quando <see cref="Enviado"/> é falso.</param>
public record TesteDeEnvioResponse(bool Enviado, string? Motivo);

/// <param name="AtendenteNome">Quem pediu a troca ou desconexão. Nulo em pareamento e queda do gateway.</param>
public record ConexaoResponse(
    string? Numero,
    string? PerfilNome,
    DateTime ConectadoEm,
    DateTime? DesconectadoEm,
    string? AtendenteNome,
    Domain.Enums.MotivoDaConexao Motivo);
