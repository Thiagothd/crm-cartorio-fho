namespace CrmCartorio.Api.Modules.RespostaInteligente.Application;

public class OpcoesDaIA
{
    public const string Secao = "RespostaInteligente";

    /// <summary>
    /// Liga a camada de IA. Desligada, a triagem sem correspondência cai direto no
    /// encaminhamento humano de sempre — o comportamento da Fase 1.
    /// </summary>
    public bool Habilitada { get; set; }

    /// <summary>
    /// Chave própria do console.anthropic.com. NÃO é a assinatura pessoal do
    /// desenvolvedor — vive em appsettings.Development.json, gitignored.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Modelo usado para a resposta grounded. Configurável de propósito.</summary>
    public string Modelo { get; set; } = "claude-haiku-4-5";

    public int TimeoutSegundos { get; set; } = 20;

    /// <summary>
    /// Teto de respostas da IA por dia (UTC), somado no tenant inteiro.
    /// </summary>
    /// <remarks>
    /// Rede de segurança contra um loop malformado gastando crédito sem controle —
    /// não é o uso esperado, é o piso embaixo dele. Conta TROCAS, não dólar: o custo
    /// por troca do Haiku 4.5 é previsível o bastante (~US$0,002) para um teto de
    /// trocas equivaler, na prática, a um teto de gasto — e contar linha no banco é
    /// bem mais simples e confiável do que somar custo exato de cada resposta.
    /// </remarks>
    public int LimiteDiario { get; set; } = 300;

    /// <summary>
    /// Falta de configuração cai para o comportamento humano de sempre — nunca trava
    /// o cliente esperando uma IA mal configurada.
    /// </summary>
    public bool EstaConfigurada => Habilitada && !string.IsNullOrWhiteSpace(ApiKey);
}
