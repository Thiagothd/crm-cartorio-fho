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

/// <summary>Quem está do outro lado da sessão pareada, para a tela mostrar em vez do nome da instância.</summary>
public record PerfilDaInstancia(string? Numero, string? Nome, string? FotoUrl);

/// <summary>
/// Um contato como o gateway persiste, cru — sem filtro de grupo/`@lid` e sem
/// validação de telefone. Quem chama decide o que fazer com o <see cref="Jid"/>.
/// </summary>
public record ContatoDoWhatsApp(string Jid, string? Nome);

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
    /// Desliga a sessão pareada no gateway (dispositivo desvinculado, como em
    /// WhatsApp → Dispositivos conectados → sair).
    /// </summary>
    /// <remarks>
    /// Recusa do gateway (instância já desconectada) não é erro — mesma postura
    /// de <see cref="GarantirInstanciaAsync"/> com "já existe": o objetivo final
    /// (sessão fora do ar) já está atingido.
    /// </remarks>
    Task DesconectarAsync(CancellationToken ct);

    /// <summary>Número, nome e foto do perfil pareado agora — para a tela mostrar quem está conectado.</summary>
    /// <remarks>Devolve nulo nos campos que o gateway não informar. Nunca lança por formato inesperado.</remarks>
    Task<PerfilDaInstancia?> ObterPerfilDaInstanciaAsync(CancellationToken ct);

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

    /// <summary>
    /// Troca o texto de uma mensagem JA ENTREGUE ao cliente. O WhatsApp marca a
    /// mensagem como "editada" no aparelho dele.
    /// </summary>
    /// <remarks>
    /// <b>Devolve false em vez de lancar quando o WhatsApp recusa</b> — e a unica
    /// forma honesta de tratar o motivo mais comum de recusa, que e a janela de
    /// edicao ter fechado do lado deles. O servico ja checa a janela antes de
    /// chamar; este false cobre a discordancia de relogio entre nos e o WhatsApp,
    /// e "nao consegui" e resposta, nao falha de sistema. Excecao continua
    /// reservada para gateway fora do ar.
    ///
    /// O <paramref name="conteudo"/> chega JA DECORADO com a assinatura de
    /// autoria, como no envio — sem isso a correcao apagaria do aparelho do
    /// cliente o nome de quem estava falando com ele.
    /// </remarks>
    Task<bool> EditarTextoAsync(
        string telefoneE164,
        string idExternoWhatsapp,
        string conteudo,
        CancellationToken ct);

    /// <summary>
    /// Apaga para todos uma mensagem que NOS enviamos. O cliente passa a ver
    /// "Esta mensagem foi apagada".
    /// </summary>
    /// <remarks>
    /// Mesma disciplina de <see cref="EditarTextoAsync"/>: false quando o WhatsApp
    /// recusa (janela vencida do lado deles), excecao so quando o gateway nao
    /// responde. Vale apenas para mensagem nossa — <c>fromMe</c> e sempre
    /// verdadeiro aqui, e apagar mensagem do cliente nao e coisa que o WhatsApp
    /// permita a ninguem.
    /// </remarks>
    Task<bool> ApagarParaTodosAsync(
        string telefoneE164,
        string idExternoWhatsapp,
        CancellationToken ct);

    /// <summary>Baixa a midia de uma mensagem recebida, em base64.</summary>
    Task<byte[]?> BaixarMidiaAsync(string idExternoWhatsapp, CancellationToken ct);

    /// <summary>
    /// Consulta a URL da foto de perfil do contato no WhatsApp.
    /// Devolve nulo se o contato nao tiver foto, se a privacidade bloquear ou se o canal estiver desligado.
    /// </summary>
    Task<string?> ObterFotoPerfilUrlAsync(string telefoneE164, CancellationToken ct);

    /// <summary>
    /// Agenda de contatos que o gateway persistiu para a instância pareada —
    /// para a importação de contatos (Modules/Clientes).
    /// </summary>
    /// <remarks>
    /// Pode vir vazia mesmo com contatos reais no aparelho: a Evolution só
    /// devolve o que sincronizou no pareamento, não uma leitura ao vivo do
    /// telefone. Vazio não é erro — só falha de rede/gateway lança.
    /// </remarks>
    Task<IReadOnlyList<ContatoDoWhatsApp>> ObterContatosAsync(CancellationToken ct);
}
