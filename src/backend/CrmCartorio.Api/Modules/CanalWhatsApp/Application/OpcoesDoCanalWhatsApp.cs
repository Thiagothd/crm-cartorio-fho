namespace CrmCartorio.Api.Modules.CanalWhatsApp.Application;

public class OpcoesDoCanalWhatsApp
{
    public const string Secao = "CanalWhatsApp";

    /// <summary>
    /// Liga o canal real. Desligado, o sistema volta ao comportamento da Fase 1:
    /// conversa simulada pelo painel, mensagem que nao sai para lugar nenhum.
    /// </summary>
    /// <remarks>
    /// Existe porque o canal e um PILOTO com numero pessoal. Se o numero cair, ou
    /// se a demonstracao acabar, desligar tem de ser uma linha de configuracao —
    /// nao um deploy.
    /// </remarks>
    public bool Habilitado { get; set; }

    /// <summary>Endereco do gateway Evolution. So laco local em desenvolvimento.</summary>
    public string BaseUrl { get; set; } = "http://localhost:8080";

    /// <summary>
    /// Chave que autentica ESTA API perante o gateway (header `apikey`).
    /// </summary>
    /// <remarks>
    /// E a credencial que da controle total sobre o WhatsApp pareado. Vive em
    /// appsettings.Development.json, que e gitignored — nunca no codigo.
    /// </remarks>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Nome da instancia pareada no gateway. UMA INSTANCIA = UM TENANT.
    /// </summary>
    /// <remarks>
    /// E por este nome que o webhook descobre de qual cartorio e a mensagem, sem
    /// token nenhum: o payload traz a instancia, e Tenants.WhatsappInstancia diz
    /// a quem ela pertence. Na Fase 1 ha um tenant so, mas amarrar isso agora e o
    /// que evita reescrever o recebimento inteiro na Fase 3.
    /// </remarks>
    public string NomeDaInstancia { get; set; } = "cartorio-piloto";

    /// <summary>
    /// Segredo compartilhado que o gateway devolve na URL do webhook.
    /// </summary>
    /// <remarks>
    /// O endpoint do webhook e anonimo por necessidade — o gateway nao carrega
    /// JWT. Este token e a UNICA barreira entre o mundo e um endpoint que cria
    /// conversa: sem ele, qualquer um que descobrisse a URL escreveria no fio de
    /// atendimento do cartorio. Diferente do ApiKey de proposito, para que vazar
    /// um nao entregue o outro.
    /// </remarks>
    public string TokenDoWebhook { get; set; } = string.Empty;

    /// <summary>Teto de espera por chamada ao gateway, em segundos.</summary>
    public int TimeoutSegundos { get; set; } = 20;

    /// <summary>
    /// Piloto restrito a estes números, quando a lista não estiver vazia. Mensagem
    /// de qualquer outro número é descartada no webhook — nem vira <c>Cliente</c>,
    /// nem <c>Conversa</c>, nada entra no sistema.
    /// </summary>
    /// <remarks>
    /// Existe porque o piloto roda no número PESSOAL do Thiago (ver
    /// docs/DEPLOY-WHATSAPP.md): sem isso, qualquer pessoa que mande mensagem para
    /// esse número cai na triagem do cartório. Lista vazia = sem restrição — é o
    /// estado final, para quando o número oficial do cartório entrar.
    ///
    /// Só dígitos, com código do país, sem "+" e sem espaço/traço — o mesmo formato
    /// que <see cref="TradutorDePayload"/> já produz em
    /// <c>MensagemRecebidaDoCanal.TelefoneE164</c> (ex.: "5538991234567").
    /// </remarks>
    public string[] NumerosDeTeste { get; set; } = [];

    /// <summary>
    /// O canal so opera com as tres credenciais no lugar. Configuracao pela
    /// metade e pior que canal desligado: falha no meio do atendimento.
    /// </summary>
    public bool EstaConfigurado =>
        Habilitado
        && !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(NomeDaInstancia)
        && !string.IsNullOrWhiteSpace(TokenDoWebhook);
}
