namespace CrmCartorio.Api.Modules.CanalWhatsApp.Application;

/// <summary>Estado do pareamento da instancia, para a tela de Configuracoes.</summary>
/// <param name="Estado">`open` (pareado), `connecting`, `close`.</param>
/// <param name="QrCodeBase64">
/// PNG em data-URI para exibir. Preenchido apenas enquanto o gateway espera a
/// leitura; nulo quando ja pareado.
/// </param>
public record EstadoDaInstancia(string Estado, string? QrCodeBase64)
{
    public bool Conectado => Estado.Equals("open", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Fala com o gateway Evolution. E o unico ponto do sistema que conhece o
/// formato do terceiro.
/// </summary>
public interface IEvolutionClient
{
    /// <summary>
    /// Cria a instancia sem QR, se necessario, registra e confirma o webhook.
    /// </summary>
    /// <remarks>
    /// Idempotente: instancia ja existente nao e erro, so segue para o registro
    /// do webhook. E o que permite a tela de pareamento ser um botao so.
    /// </remarks>
    Task<EstadoDaInstancia> GarantirInstanciaAsync(string urlDoWebhook, CancellationToken ct);

    /// <summary>
    /// Consulta o estado atual sem criar sessao, pedir QR ou alterar o gateway.
    /// </summary>
    Task<EstadoDaInstancia> ObterEstadoAsync(CancellationToken ct);

    /// <summary>
    /// Inicia uma tentativa de conexao somente quando a instancia estiver
    /// fechada. Se ja estiver aberta ou conectando, apenas devolve o estado.
    /// </summary>
    Task<EstadoDaInstancia> IniciarConexaoAsync(CancellationToken ct);

    /// <summary>
    /// Envia texto e devolve o id da mensagem no WhatsApp.
    /// </summary>
    /// <remarks>
    /// O id volta para ser gravado em <c>Mensagem.IdExternoWhatsapp</c> — e por
    /// ele que o webhook de status encontra a linha para promover a Entregue/Lida.
    /// </remarks>
    Task<string> EnviarTextoAsync(string telefoneE164, string conteudo, CancellationToken ct);

    /// <summary>
    /// Envia um arquivo e devolve o id da mensagem no WhatsApp.
    /// </summary>
    /// <param name="legenda">
    /// Texto que acompanha o arquivo. Vai vazio quando o conteudo da mensagem e
    /// so o rotulo do proprio anexo — mandar "[imagem]" como legenda de uma
    /// imagem seria ruido na tela do cliente.
    /// </param>
    Task<string> EnviarMidiaAsync(
        string telefoneE164,
        byte[] conteudo,
        string tipoMime,
        string nomeDoArquivo,
        string? legenda,
        CancellationToken ct);

    /// <summary>Baixa a midia de uma mensagem recebida, em base64.</summary>
    Task<byte[]?> BaixarMidiaAsync(string idExternoWhatsapp, CancellationToken ct);

    /// <summary>
    /// Consulta a URL da foto de perfil do contato no WhatsApp.
    /// Devolve nulo se o contato nao tiver foto, se a privacidade bloquear ou se o canal estiver desligado.
    /// </summary>
    Task<string?> ObterFotoPerfilUrlAsync(string telefoneE164, CancellationToken ct);
}
