namespace CrmCartorio.Api.Modules.RespostaInteligente.Application;

public class OpcoesDaIA
{
    public const string Secao = "RespostaInteligente";

    // Nao existe mais "Habilitada" aqui (2026-09-22). Ligar/desligar a IA virou
    // escolha do cartorio, pela tela: Tenant.RespostaInteligenteHabilitada, lida
    // em RespostaInteligenteService.ResponderAsync. Uma chave "Habilitada" que
    // ainda esteja no appsettings do servidor e ignorada pelo binding. O que
    // continua aqui e infraestrutura: sem provedor com chave, nao ha IA — ver
    // EstaConfigurada.

    public int TimeoutSegundos { get; set; } = 20;

    /// <summary>
    /// Teto de RESPOSTAS ENTREGUES ao cliente por dia (UTC), somado no tenant
    /// inteiro — uma por mensagem, qualquer que seja o provedor que a produziu ou
    /// quantos foram tentados. Continua sendo a trava contra loop malformado; com
    /// o Gemini gratuito, o teto que controla gasto de verdade é
    /// <see cref="OpcoesDoAnthropic.LimiteDiarioHaiku"/>.
    /// </summary>
    public int LimiteDiario { get; set; } = 300;

    public OpcoesDoGemini Gemini { get; set; } = new();

    public OpcoesDoAnthropic Anthropic { get; set; } = new();

    /// <summary>
    /// Legado — chave da Anthropic da configuração anterior a dois provedores.
    /// Usada como fallback só quando <see cref="OpcoesDoAnthropic.ApiKey"/> está
    /// vazio, para que produção (que não recebe <c>appsettings.Production.json</c>
    /// no deploy) não suba sem IA no primeiro release desta mudança.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Legado — modelo da Anthropic. Mesmo motivo de fallback de <see cref="ApiKey"/>.</summary>
    public string? Modelo { get; set; }

    public string ApiKeyDoAnthropicResolvida =>
        string.IsNullOrWhiteSpace(Anthropic.ApiKey) ? ApiKey ?? string.Empty : Anthropic.ApiKey;

    public string ModeloDoAnthropicResolvido =>
        !string.IsNullOrWhiteSpace(Anthropic.Modelo) ? Anthropic.Modelo
        : !string.IsNullOrWhiteSpace(Modelo) ? Modelo!
        : "claude-haiku-4-5";

    public bool GeminiEstaConfigurado => Gemini.Habilitado && !string.IsNullOrWhiteSpace(Gemini.ApiKey);

    public bool AnthropicEstaConfigurado => Anthropic.Habilitado && !string.IsNullOrWhiteSpace(ApiKeyDoAnthropicResolvida);

    /// <summary>
    /// Falta de configuração cai para o comportamento humano de sempre — nunca trava
    /// o cliente esperando uma IA mal configurada. Diz só se HÁ provedor; se o
    /// cartório quer a IA ligada é <c>Tenant.RespostaInteligenteHabilitada</c>.
    /// </summary>
    public bool EstaConfigurada => GeminiEstaConfigurado || AnthropicEstaConfigurado;
}

public class OpcoesDoGemini
{
    public bool Habilitado { get; set; } = true;

    /// <summary>
    /// Chave do Google AI Studio (console.cloud.google.com / aistudio.google.com).
    /// Vive em appsettings.Development.json, gitignored — nunca versionada.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    public string Modelo { get; set; } = "gemini-3.7-flash";

    /// <summary><c>low</c>, <c>medium</c> ou <c>high</c>. Baixa latência é prioridade num chat de WhatsApp.</summary>
    public string NivelDeRaciocinio { get; set; } = "low";

    /// <summary>
    /// Interactions API estável. Só troca para <c>v1beta</c> se algum recurso usado
    /// (response_format, store, thinking_level) não estiver disponível em v1 — ver
    /// README do módulo para o motivo técnico registrado, se isso acontecer.
    /// </summary>
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1/";

    /// <summary>
    /// Orçamento de latência do Gemini antes de cair para a contingência — NÃO é
    /// "quanto o Gemini costuma demorar", é "quanto o cliente aceita esperar por ele".
    /// </summary>
    /// <remarks>
    /// ACHADO AO VIVO, 2026-09-03 (telemetria de <c>ChamadaDeIa</c> no ambiente de
    /// teste): quando o Gemini responde, responde em ~2,4s — mais rápido que o Haiku.
    /// Mas na primeira chamada depois de um período ocioso ele estoura qualquer
    /// limite, e o valor antigo (20s) fazia o cliente esperar 20 segundos em silêncio
    /// antes de o Haiku (que responde em 1,5–3,7s) sequer ser consultado. Total
    /// observado: 22–25s para uma pergunta trivial.
    ///
    /// 8s é deliberadamente maior que a latência normal (2,4s) e menor que a
    /// paciência de quem está no WhatsApp: dá margem de sobra para uma resposta
    /// legítima um pouco lenta, sem transformar um tropeço do Gemini em espera
    /// absurda. O piso de 5s em <c>ProvedorGemini</c> continua valendo.
    ///
    /// O Anthropic mantém orçamento maior de propósito: ele é o último recurso, e
    /// desistir dele cedo demais deixaria o cliente sem resposta nenhuma.
    /// </remarks>
    public int TimeoutSegundos { get; set; } = 8;

    /// <summary>
    /// Requisições por minuto do free tier. <c>0</c> = não cadastrado — o Google
    /// não publica mais este número na documentação, só no painel do AI Studio
    /// (aistudio.google.com/rate-limit). Enquanto for 0, o controle preventivo por
    /// percentual fica desligado e o disjuntor reativo (429) vira a única
    /// autoridade — ver <see cref="Cota.PoliticaDeSelecao"/>.
    /// </summary>
    public int LimiteRpm { get; set; }

    /// <summary>Tokens por minuto do free tier. <c>0</c> = não cadastrado. Mesmo comportamento de <see cref="LimiteRpm"/>.</summary>
    public int LimiteTpm { get; set; }

    /// <summary>Requisições por dia do free tier (reseta à meia-noite do Pacífico). <c>0</c> = não cadastrado.</summary>
    public int LimiteRpd { get; set; }

    /// <summary>
    /// Freio local aplicado SÓ quando os limites acima não estão cadastrados —
    /// não é uma alegação sobre a franquia real do Google, é só para não martelar
    /// a API às cegas enquanto o disjuntor reativo aprende os limites de fato.
    /// </summary>
    public int LimiteRpmDeDescoberta { get; set; } = 5;

    /// <summary>Margem de segurança sobre os limites cadastrados — o Gemini para de ser escolhido ao atingir este percentual.</summary>
    public int PercentualDeSeguranca { get; set; } = 85;

    public int FalhasParaAbrirDisjuntor { get; set; } = 3;

    public int CooldownSegundos { get; set; } = 120;

    public int CooldownMaximoSegundos { get; set; } = 900;
}

public class OpcoesDoAnthropic
{
    public bool Habilitado { get; set; } = true;

    /// <summary>Vazio cai para o legado <see cref="OpcoesDaIA.ApiKey"/> — ver <see cref="OpcoesDaIA.ApiKeyDoAnthropicResolvida"/>.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Vazio cai para o legado <see cref="OpcoesDaIA.Modelo"/> — ver <see cref="OpcoesDaIA.ModeloDoAnthropicResolvido"/>.</summary>
    public string Modelo { get; set; } = string.Empty;

    /// <summary>
    /// Teto de respostas produzidas PELO ANTHROPIC por dia (UTC), por tenant — o
    /// que de fato controla o gasto em dinheiro, já que o Gemini é gratuito dentro
    /// da franquia. Ao estourar, o Gemini continua respondendo normalmente até o
    /// <see cref="OpcoesDaIA.LimiteDiario"/> geral; só o Haiku para de ser usado.
    /// </summary>
    public int LimiteDiarioHaiku { get; set; } = 60;

    public int TimeoutSegundos { get; set; } = 20;

    public decimal CustoPorMilhaoEntradaUsd { get; set; } = 1.00m;

    public decimal CustoPorMilhaoSaidaUsd { get; set; } = 5.00m;

    /// <summary>Cotação usada só para exibir o custo estimado do Haiku em R$ no painel — preço muda, fica configurável.</summary>
    public decimal CotacaoDoDolar { get; set; } = 5.40m;
}
