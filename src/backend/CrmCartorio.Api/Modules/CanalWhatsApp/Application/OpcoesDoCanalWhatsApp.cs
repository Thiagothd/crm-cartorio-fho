namespace CrmCartorio.Api.Modules.CanalWhatsApp.Application;

public class OpcoesDoCanalWhatsApp
{
    public const string Secao = "CanalWhatsApp";
    public const string InstanciaDoAmbienteDeTeste = "cartorio-teste";

    private static readonly HashSet<int> DddsBrasileiros =
    [
        11, 12, 13, 14, 15, 16, 17, 18, 19,
        21, 22, 24, 27, 28,
        31, 32, 33, 34, 35, 37, 38,
        41, 42, 43, 44, 45, 46, 47, 48, 49,
        51, 53, 54, 55,
        61, 62, 63, 64, 65, 66, 67, 68, 69,
        71, 73, 74, 75, 77, 79,
        81, 82, 83, 84, 85, 86, 87, 88, 89,
        91, 92, 93, 94, 95, 96, 97, 98, 99
    ];

    /// <summary>
    /// Liga o canal real. Desligado, o sistema volta ao comportamento da Fase 1:
    /// conversa simulada pelo painel, mensagem que nao sai para lugar nenhum.
    /// </summary>
    /// <remarks>
    /// Existe porque o canal e um PILOTO com numero dedicado. Se o numero cair, ou
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
    /// URL publica desta API, usada para montar o endereco do webhook que o
    /// gateway vai chamar. Vazio = deduz da requisicao de pareamento.
    /// </summary>
    /// <remarks>
    /// Existe porque deduzir de <c>Request.Scheme/Host</c> so funciona quando o
    /// navegador fala direto com a API. Em producao nao e o caso: o Cloudflare
    /// Tunnel termina o TLS na borda e entrega HTTP simples ao IIS, entao a
    /// deducao produz <c>http://crm.crm-cartorio.example</c> — e o webhook fica
    /// registrado num endereco que redireciona ou recusa.
    ///
    /// Tambem resolve o outro lado: em desenvolvimento a deducao reescreve
    /// <c>localhost</c> para <c>host.docker.internal</c>, que e comportamento do
    /// Docker DESKTOP. Com Docker CE dentro do WSL2 (o servidor) esse nome nao
    /// existe por padrao, e a rota de volta ate a maquina hospedeira muda.
    ///
    /// Apontar para a URL publica tem uma vantagem que nao e obvia: o webhook
    /// sai do conteiner, vai ate a Cloudflare e volta pelo tunel. Da uma volta,
    /// mas e o unico caminho que nao depende de descobrir o IP da maquina
    /// hospedeira visto de dentro do WSL — e chega cifrado.
    ///
    /// Ex.: <c>https://crm.crm-cartorio.example</c>. Sem barra no fim.
    /// </remarks>
    public string UrlPublica { get; set; } = string.Empty;

    /// <summary>
    /// Piloto restrito a estes números, quando a lista não estiver vazia. Mensagem
    /// de qualquer outro número é descartada no webhook — nem vira <c>Cliente</c>,
    /// nem <c>Conversa</c> — e nenhum envio sai para um destino fora da lista.
    /// </summary>
    /// <remarks>
    /// Existe para isolar números usados em demonstração e teste (ver
    /// docs/DEPLOY-WHATSAPP.md): sem isso, qualquer pessoa que mande mensagem para
    /// o canal cairia na triagem, e uma conversa antiga do banco ainda poderia
    /// receber mensagem pelo gateway. Lista vazia = sem restrição — é o estado
    /// final para um canal público de produção.
    ///
    /// Só dígitos, com código do país, sem "+" e sem espaço/traço — o mesmo formato
    /// que <see cref="TradutorDePayload"/> já produz em
    /// <c>MensagemRecebidaDoCanal.TelefoneE164</c> (ex.: "5538991234567").
    /// </remarks>
    public string[] NumerosDeTeste { get; set; } = [];

    /// <summary>
    /// Números que nunca podem entrar nem sair por este canal, mesmo quando
    /// <see cref="NumerosDeTeste"/> estiver vazia.
    /// </summary>
    /// <remarks>
    /// É a proteção entre ambientes que compartilham o mesmo gateway. Se o número
    /// do canal de teste escrever para o número pareado em produção, deixar os dois
    /// lados responderem automaticamente cria um ciclo entre os CRMs. A lista de
    /// bloqueio interrompe esse caminho sem transformar o canal público de produção
    /// numa allowlist fechada.
    /// </remarks>
    public string[] NumerosBloqueados { get; set; } = [];

    /// <summary>
    /// Aplica a mesma política de números na entrada e na saída do canal.
    /// </summary>
    /// <remarks>
    /// Os dois lados são normalizados para somente dígitos. A normalização não
    /// acrescenta DDI: <c>19 99000-1488</c> vira <c>19990001488</c>, e não casa com
    /// o E.164 <c>5519990001488</c>. Configuração deve sempre trazer o <c>55</c>.
    /// Bloqueio tem precedência sobre permissão; valor sem dígito nunca casa.
    /// </remarks>
    public bool NumeroPermitido(string? telefone)
    {
        var normalizado = SomenteDigitos(telefone);
        var bloqueados = NumerosBloqueados ?? [];
        var permitidos = NumerosDeTeste ?? [];

        if (normalizado.Length == 0)
        {
            return false;
        }

        if (bloqueados.Any(numero => NumerosIguais(numero, normalizado)))
        {
            return false;
        }

        return permitidos.Length == 0
            || permitidos.Any(numero => NumerosIguais(numero, normalizado));
    }

    /// <summary>
    /// Confere se o evento pertence exatamente à instância deste ambiente.
    /// </summary>
    /// <remarks>
    /// É uma segunda barreira além do token do webhook. Uma base clonada pode
    /// conservar no Tenant o nome da instância de outro ambiente; aceitar esse
    /// payload faria teste e produção cruzarem atendimentos.
    /// </remarks>
    public bool InstanciaConfere(string? instancia) =>
        !string.IsNullOrWhiteSpace(instancia)
        && instancia.Equals(NomeDaInstancia, StringComparison.Ordinal);

    /// <summary>Impede o ambiente Teste de apontar para a sessão de produção.</summary>
    public bool InstanciaDeTesteValida =>
        string.Equals(NomeDaInstancia, InstanciaDoAmbienteDeTeste, StringComparison.Ordinal);

    /// <summary>
    /// Confere a trava obrigatória do ambiente de teste. A lista precisa ter
    /// exatamente um celular brasileiro canonico (55 + DDD + 9 + oito digitos).
    /// </summary>
    public bool RestricaoDeTesteValida
    {
        get
        {
            var permitidos = NumerosDeTeste ?? [];

            return permitidos.Length == 1
                && permitidos.All(numero => EhMovelBrasileiroCanonico(SomenteDigitos(numero)))
                && permitidos.Any(NumeroPermitido);
        }
    }

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

    private static bool NumerosIguais(string? configurado, string normalizado)
    {
        var numero = SomenteDigitos(configurado);

        if (numero.Length == 0 || normalizado.Length == 0)
        {
            return false;
        }

        if (numero.Equals(normalizado, StringComparison.Ordinal))
        {
            return true;
        }

        // O WhatsApp ainda pode identificar um celular brasileiro pelo JID
        // legado, sem o nono digito. A equivalencia e estrita: mesmo DDI, DDD e
        // assinante; nunca compara por sufixo nem altera numeros internacionais.
        return SaoFormasDoMesmoMovelBrasileiro(numero, normalizado);
    }

    private static bool SaoFormasDoMesmoMovelBrasileiro(string primeiro, string segundo)
    {
        var comNono = primeiro.Length == 13 ? primeiro : segundo;
        var semNono = primeiro.Length == 12 ? primeiro : segundo;

        return EhMovelBrasileiroCanonico(comNono)
            && comNono[5] is >= '6' and <= '9'
            && semNono.Length == 12
            && semNono.StartsWith("55", StringComparison.Ordinal)
            && DddValido(semNono)
            && comNono.Remove(4, 1).Equals(semNono, StringComparison.Ordinal);
    }

    private static bool EhMovelBrasileiroCanonico(string numero) =>
        numero.Length == 13
        && numero.StartsWith("55", StringComparison.Ordinal)
        && DddValido(numero)
        && numero[4] == '9';

    private static bool DddValido(string numero) =>
        numero.Length >= 4
        && int.TryParse(numero.AsSpan(2, 2), out var ddd)
        && DddsBrasileiros.Contains(ddd);

    private static string SomenteDigitos(string? valor) =>
        string.IsNullOrWhiteSpace(valor)
            ? string.Empty
            : new string(valor.Where(char.IsDigit).ToArray());
}
