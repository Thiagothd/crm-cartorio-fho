using System.Text;
using System.Text.Json.Serialization;
using Anthropic;
using CrmCartorio.Api.Infrastructure.Automacao;
using CrmCartorio.Api.Infrastructure.Auditoria;
using CrmCartorio.Api.Infrastructure.Identity;
using CrmCartorio.Api.Infrastructure.Anexos;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Persistence.Seeding;
using CrmCartorio.Api.Infrastructure.Realtime;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Infrastructure.Web;
using CrmCartorio.Api.Modules.Atendimento.Application;
using CrmCartorio.Api.Modules.CanalWhatsApp.Application;
using CrmCartorio.Api.Modules.CentralDeIa.Application;
using CrmCartorio.Api.Modules.Clientes.Application;
using CrmCartorio.Api.Modules.Configuracoes.Application;
using CrmCartorio.Api.Modules.Consultas.Application;
using CrmCartorio.Api.Modules.Indicadores.Application;
using CrmCartorio.Api.Modules.RespostaInteligente.Application;
using CrmCartorio.Api.Modules.RespostaInteligente.Application.Cota;
using CrmCartorio.Api.Modules.RespostaInteligente.Application.Provedores;
using CrmCartorio.Api.Modules.RespostaInteligente.Infrastructure;
using CrmCartorio.Api.Modules.Seguranca.Application;
using CrmCartorio.Api.Modules.Servicos.Application;
using CrmCartorio.Api.Modules.Tenants.Application;
using CrmCartorio.Api.Modules.Usuarios.Application;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

// SEC-03 — bootstrap do MFA do primeiro privilegiado. Comando de execucao
// unica, NAO um modo do servidor: sai antes de montar qualquer servico web,
// hospedagem ou pipeline HTTP. Ver FerramentaDeBootstrapMfa para as regras
// (so interativo, nunca ILogger, fecha sozinho apos o primeiro uso).
if (args.Contains("--emitir-ticket-de-bootstrap"))
{
    var configuracaoDoBootstrap = new ConfigurationBuilder()
        .AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}.json", optional: true)
        .AddEnvironmentVariables()
        .Build();

    return await FerramentaDeBootstrapMfa.ExecutarAsync(configuracaoDoBootstrap);
}

// SEC-03 — break-glass: reset de emergencia para privilegiado que perdeu
// autenticador E codigos de recuperacao. Mesma disciplina do bootstrap
// acima (comando de execucao unica, so interativo, nunca ILogger) — ver
// FerramentaDeResetDeEmergencia para o porque de existir separado do reset
// administrativo normal e do bootstrap (os dois ficam bloqueados justamente
// no cenario que o break-glass existe para resolver).
if (args.Contains("--reset-de-emergencia"))
{
    var configuracaoDeEmergencia = new ConfigurationBuilder()
        .AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}.json", optional: true)
        .AddEnvironmentVariables()
        .Build();

    return await FerramentaDeResetDeEmergencia.ExecutarAsync(configuracaoDeEmergencia);
}

// SEC-03 — reemissao de ticket DURANTE uma recuperacao de emergencia ja
// aberta (bloqueador B20, setima revisao independente): o ticket que o
// break-glass emitiu foi perdido, expirou, ou ficou inutilizavel antes da
// rematricula. Mesma disciplina dos dois comandos acima; ver
// FerramentaDeReemissaoDeTicketDeEmergencia — nunca reativa o MFA, nunca
// libera login, so troca o ticket.
if (args.Contains("--reemitir-ticket-de-emergencia"))
{
    var configuracaoDeReemissao = new ConfigurationBuilder()
        .AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}.json", optional: true)
        .AddEnvironmentVariables()
        .Build();

    return await FerramentaDeReemissaoDeTicketDeEmergencia.ExecutarAsync(configuracaoDeReemissao);
}

// PR3 da blindagem LGPD — re-cifra em repouso toda linha legada depois que
// alguem com acesso ao servidor provisiona ProtecaoDeDados:Chaves. Mesma
// disciplina dos tres comandos acima (execucao unica, nunca modo do
// servidor). Ver FerramentaDeCifraDeDadosLegados.
if (args.Contains("--cifrar-dados-legados"))
{
    var configuracaoDeCifra = new ConfigurationBuilder()
        .AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}.json", optional: true)
        .AddEnvironmentVariables()
        .Build();

    return await FerramentaDeCifraDeDadosLegados.ExecutarAsync(configuracaoDeCifra);
}

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------- configuracao

builder.Services.Configure<OpcoesDeJwt>(builder.Configuration.GetSection(OpcoesDeJwt.Secao));
builder.Services.Configure<OpcoesDeAutomacao>(builder.Configuration.GetSection(OpcoesDeAutomacao.Secao));
builder.Services.Configure<OpcoesDeConsultaPublica>(
    builder.Configuration.GetSection(OpcoesDeConsultaPublica.Secao));
builder.Services.Configure<OpcoesDoCanalWhatsApp>(
    builder.Configuration.GetSection(OpcoesDoCanalWhatsApp.Secao));
builder.Services.Configure<OpcoesDaIA>(builder.Configuration.GetSection(OpcoesDaIA.Secao));
builder.Services.Configure<OpcoesDeAnexos>(builder.Configuration.GetSection("Anexos"));
builder.Services.Configure<OpcoesDeMfa>(builder.Configuration.GetSection(OpcoesDeMfa.Secao));
builder.Services.Configure<OpcoesDeProtecaoDeDados>(
    builder.Configuration.GetSection(OpcoesDeProtecaoDeDados.Secao));
builder.Services.Configure<OpcoesDaCentralDeIa>(builder.Configuration.GetSection(OpcoesDaCentralDeIa.Secao));

var opcoesJwt = builder.Configuration.GetSection(OpcoesDeJwt.Secao).Get<OpcoesDeJwt>() ?? new OpcoesDeJwt();
var opcoesCanalWhatsApp = builder.Configuration
    .GetSection(OpcoesDoCanalWhatsApp.Secao)
    .Get<OpcoesDoCanalWhatsApp>() ?? new OpcoesDoCanalWhatsApp();

if (string.IsNullOrWhiteSpace(opcoesJwt.Chave) || Encoding.UTF8.GetByteCount(opcoesJwt.Chave) < 32)
{
    // Falhar aqui e melhor do que subir com uma chave fraca e so descobrir depois
    // que qualquer um consegue forjar um token.
    throw new InvalidOperationException(
        "Jwt:Chave ausente ou com menos de 32 bytes. Defina em variavel de ambiente " +
        "ou user-secrets antes de iniciar a API.");
}

// Teste nunca pode subir com canal real aberto para qualquer contato. Em
// producao, lista vazia significa canal publico por decisao; no ambiente Teste,
// a mesma omissao e erro de configuracao e falha antes de aceitar trafego.
if (builder.Environment.IsEnvironment("Teste")
    && opcoesCanalWhatsApp.Habilitado
    && (!opcoesCanalWhatsApp.RestricaoDeTesteValida
        || !opcoesCanalWhatsApp.InstanciaDeTesteValida))
{
    throw new InvalidOperationException(
        "No ambiente Teste, CanalWhatsApp:NomeDaInstancia deve ser cartorio-teste " +
        "e CanalWhatsApp:NumerosDeTeste deve conter exatamente um telefone brasileiro " +
        "normalizavel para E.164 (iniciado por 55) e nao bloqueado.");
}

// ---------------------------------------------------------------- persistencia

// PR3 da blindagem LGPD (cifra em repouso). Registrado ANTES do DbContext:
// AddDbContext resolve parametros extras do construtor pelo container, e
// CrmDbContext pede ICifraDeDados. Fica seguro subir sem nenhuma chave
// configurada — ver OpcoesDeProtecaoDeDados para o porque disso ser
// deliberado, nao uma omissao.
builder.Services.AddSingleton<IProvedorDeChaveDeDados, ProvedorDeChaveEmConfiguracao>();
builder.Services.AddSingleton<ICifraDeDados, CifraDeDados>();
builder.Services.AddSingleton<IndiceCego>();

builder.Services.AddDbContext<CrmDbContext>(opcoes =>
    ConfiguracaoDoBanco.Configurar(opcoes, builder.Configuration.GetConnectionString("SqlServer")));

// -------------------------------------------------------------------- servicos

builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<IHashDeSenha, HashDeSenha>();
builder.Services.AddScoped<IContextoDoUsuario, ContextoDoUsuario>();
builder.Services.AddScoped<IGeradorDeToken, GeradorDeToken>();

// SEC-03: cookie HttpOnly de "confiar neste dispositivo por 30 dias". So o
// transporte mora aqui — quem decide conceder, aceitar ou recusar e o
// AutenticacaoService (ver ICookieDeDispositivoConfiavel).
builder.Services.AddScoped<ICookieDeDispositivoConfiavel, CookieDeDispositivoConfiavel>();

// SEC-01: revogacao de sessao. ValidadorDeSessao confere Ativo/TokenVersion no
// banco; SessaoAtivaHandler acopla essa checagem a policy padrao de
// autorizacao (registrada mais abaixo), cobrindo controllers e o Hub do
// SignalR com o mesmo componente. O mesmo round-trip agora tambem resolve as
// permissoes granulares e deposita em PermissoesDaRequisicao — ContextoDoUsuario
// le de la, nunca faz consulta propria.
builder.Services.AddScoped<IValidadorDeSessao, ValidadorDeSessao>();
builder.Services.AddScoped<PermissoesDaRequisicao>();
builder.Services.AddScoped<IAuthorizationHandler, SessaoAtivaHandler>();

builder.Services.AddScoped<IAutenticacaoService, AutenticacaoService>();
builder.Services.AddScoped<IAtendenteService, AtendenteService>();
builder.Services.AddSingleton<ICifraDeSegredoMfa, CifraDeSegredoMfa>();
builder.Services.AddScoped<IMfaService, MfaService>();
builder.Services.AddScoped<IClienteService, ClienteService>();
builder.Services.AddScoped<IImportacaoDeContatosService, ImportacaoDeContatosService>();
builder.Services.AddScoped<ISetorService, SetorService>();
builder.Services.AddScoped<IEtapaFunilService, EtapaFunilService>();
builder.Services.AddScoped<IMensagemAutomaticaService, MensagemAutomaticaService>();
builder.Services.AddScoped<IRespostaRapidaService, RespostaRapidaService>();
builder.Services.AddScoped<ITipoServicoService, TipoServicoService>();
builder.Services.AddScoped<ITenantService, TenantService>();
builder.Services.AddScoped<IGeradorDeProtocolo, GeradorDeProtocolo>();
builder.Services.AddScoped<IRegistradorDeAcesso, RegistradorDeAcesso>();
builder.Services.AddScoped<ISegurancaService, SegurancaService>();
builder.Services.AddScoped<IAcessoTecnicoService, AcessoTecnicoService>();
builder.Services.AddScoped<IConversaService, ConversaService>();
builder.Services.AddScoped<IMensagemService, MensagemService>();

// Mesma instancia serve as duas portas: as acoes do menu de contexto compartilham
// as regras de trava e de tenant com o envio, e separa-las em dois servicos faria
// as duas copias divergirem na primeira mudanca de regra.
builder.Services.AddScoped<IAcoesDeMensagem>(sp => (MensagemService)sp.GetRequiredService<IMensagemService>());
builder.Services.AddScoped<IDisparadorDeMensagemAutomatica, DisparadorDeMensagemAutomatica>();
builder.Services.AddScoped<IVarredorDePrazos, VarredorDePrazos>();
builder.Services.AddScoped<IVarredorDeInatividade, VarredorDeInatividade>();
builder.Services.AddScoped<INotificadorDeAtendimento, NotificadorDeAtendimento>();
builder.Services.AddScoped<SemeadorDeDemonstracao>();

// Canal real de WhatsApp (PILOTO, numeros dedicados — ver docs/DEPLOY-WHATSAPP.md).
// O recebimento fica no Atendimento porque e regra de atendimento; so o formato
// do gateway mora no slice CanalWhatsApp.
builder.Services.AddScoped<IRecebimentoDeWhatsApp, RecebimentoDeWhatsApp>();
builder.Services.AddScoped<IProcessadorDeEntradaWhatsapp, ProcessadorDeEntradaWhatsapp>();
builder.Services.AddSingleton<SinalizadorDeEntradaWhatsapp>();
builder.Services.AddScoped<IEnviadorDeCanal, EnviadorPeloWhatsApp>();
builder.Services.AddScoped<IProcessadorDeWebhook, ProcessadorDeWebhook>();
builder.Services.AddScoped<IIdentificadorDeEco, IdentificadorDeEco>();
builder.Services.AddScoped<IPareamentoDoCanal, PareamentoDoCanal>();
builder.Services.AddScoped<IRegrasDoCanal, RegrasDoCanal>();
builder.Services.AddSingleton<ControleDeTentativasDePareamento>();

// Fila em memoria para o envio pendente nao bloquear a acao do atendente (ex.:
// mudar etapa) esperando a chamada de rede ao gateway. Singleton unico: quem
// escreve (ConversaService, via IFilaDeEnvioDoCanal) e quem le
// (HospedeDaFilaDeEnvioDoCanal, pelo tipo concreto) precisam do mesmo Channel.
builder.Services.AddSingleton<FilaDeEnvioDoCanal>();
builder.Services.AddSingleton<IFilaDeEnvioDoCanal>(sp => sp.GetRequiredService<FilaDeEnvioDoCanal>());
builder.Services.AddHttpClient();

// Mesmo ajuste de pool de conexao aplicado ao ProvedorGemini (ver comentario
// abaixo, "ACHADO AO VIVO 2026-09-03"): o padrao do IHttpClientFactory recicla o
// handler a cada 2 minutos, forcando abertura de conexao do zero para quem
// chamar depois de um intervalo ocioso. Aqui o alvo e localhost (BaseUrl da
// Evolution API, sem DNS nem TLS de verdade), entao o ganho esperado e bem menor
// que o do Gemini — mas custa nada aplicar o mesmo padrao e elimina essa
// variavel por completo. Nao define BaseAddress aqui de proposito: EvolutionClient
// resolve a URL em tempo de chamada (o numero pode ser trocado pela tela sem
// reiniciar o processo — Fase 2F).
builder.Services.AddHttpClient<IEvolutionClient, EvolutionClient>()
    .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(5),
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
    });

// Resposta Inteligente (Fase 2B, dois provedores desde a Fase 2G): arquitetura
// pensada como Gemini 3.7 Flash principal + Haiku 4.5 de contingencia, mas
// ACHADO AO VIVO, 2026-09-03: Gemini.Habilitado esta FALSE por decisao temporaria
// do Thiago — free tier instavel (timeouts de 20-60s, 429/503) e o tier pago exige
// pre-pagamento que ele nao quis comprometer agora. Enquanto isso, Haiku e o UNICO
// provedor. Nada nesta orquestracao precisou mudar para isso: PoliticaDeSelecao
// pula o Gemini synchronously (sem HTTP, sem espera) quando desligado — ver
// Modules/RespostaInteligente/README.md.
// Sem chave configurada, os dois sobem do mesmo jeito; EstaConfigurado de cada
// provedor decide se ele e tentado, nunca o registro no DI.
//
// AnthropicClient continua singleton com a ApiKey capturada no boot (mesma
// ressalva de sempre: rotacionar a chave exige reciclar o processo).
// ApiKeyDoAnthropicResolvida cai para a chave legada (RespostaInteligente:ApiKey)
// quando RespostaInteligente:Anthropic:ApiKey esta vazio — sem isso, o primeiro
// deploy desta mudanca subiria producao sem IA (appsettings.Production.json nao
// e sobrescrito pelo deploy).
builder.Services.AddSingleton(sp =>
{
    var opcoesDaIA = sp.GetRequiredService<IOptions<OpcoesDaIA>>().Value;
    return new AnthropicClient { ApiKey = opcoesDaIA.ApiKeyDoAnthropicResolvida };
});
// Registrados via chave (AddKeyedScoped) e nao pelo tipo concreto: o
// OrquestradorDeProvedores so conhece IProvedorDeIa, o que permite substituir os
// dois por fakes nos verificadores sem tocar em HttpClient/AnthropicClient real.
builder.Services.AddScoped<ProvedorAnthropic>();
builder.Services.AddKeyedScoped<IProvedorDeIa>(
    ChaveDoProvedor.Anthropic, (sp, _) => sp.GetRequiredService<ProvedorAnthropic>());

// HttpClient tipado, mesmo padrao do EvolutionClient — nao existe SDK .NET
// oficial para a Interactions API do Gemini (o pacote Google.GenAI so cobre o
// generateContent legado).
// A conexao com o Google e mantida VIVA de proposito. O padrao do
// IHttpClientFactory recicla o handler a cada 2 minutos; num chat de cartorio, que
// passa longos periodos ocioso, isso garantia que quase toda conversa comecasse
// pagando DNS + TCP + TLS do zero. Ver ACHADO em OpcoesDoGemini.TimeoutSegundos:
// a primeira chamada apos ociosidade estourava 20s enquanto as seguintes
// respondiam em ~2,4s.
builder.Services.AddHttpClient<ProvedorGemini>((sp, http) =>
{
    var opcoesGemini = sp.GetRequiredService<IOptions<OpcoesDaIA>>().Value.Gemini;
    http.BaseAddress = new Uri(opcoesGemini.BaseUrl);
})
    // Infinito aqui NAO vaza conexao: quem controla a idade da conexao passa a ser
    // PooledConnectionLifetime abaixo, que e o mecanismo correto para isso e ainda
    // respeita troca de DNS. Sem esta linha, a rotacao de 2min do factory jogaria
    // fora justamente a conexao que acabamos de aquecer.
    .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        // Uma conexao travada falha rapido e libera o orcamento de latencia para a
        // contingencia, em vez de consumir a espera inteira do cliente.
        ConnectTimeout = TimeSpan.FromSeconds(5),

        // Renova a conexao periodicamente (acompanha mudanca de DNS do Google) sem
        // descartar a cada 2 minutos como o padrao do factory faria.
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),

        // Mantem a conexao ociosa no pool por mais tempo que o intervalo do
        // aquecedor, para que ele de fato encontre uma conexao viva para reusar.
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
    });
builder.Services.AddKeyedScoped<IProvedorDeIa>(
    ChaveDoProvedor.Gemini, (sp, _) => sp.GetRequiredService<ProvedorGemini>());

// Cota do Gemini e o disjuntor de fallback sao GLOBAIS do projeto Google (a
// franquia nao e por tenant), por isso singleton com particionamento interno por
// ChaveDeProjeto — ver ContadorDeCotaDoGemini e DisjuntorDoGemini. Os dois tetos
// diarios por tenant (LimiteDiario, LimiteDiarioHaiku) tambem viram reserva
// atomica em memoria, corrigindo a race condition que a contagem direta no banco
// tinha antes dos dois provedores.
builder.Services.AddSingleton<ContadorDeCotaDoGemini>();
builder.Services.AddSingleton<DisjuntorDoGemini>();
builder.Services.AddSingleton<ContadorDeLimiteDiario>();
builder.Services.AddScoped<PoliticaDeSelecao>();
builder.Services.AddScoped<RegistradorDeChamadas>();
builder.Services.AddScoped<IOrquestradorDeProvedores, OrquestradorDeProvedores>();

builder.Services.AddScoped<IRespostaInteligenteService, RespostaInteligenteService>();
builder.Services.AddScoped<ISituacaoDaIa, SituacaoDaIa>();
builder.Services.AddScoped<IAnaliseDaIa, AnaliseDaIa>();
builder.Services.AddScoped<IConhecimentoDeIaService, ConhecimentoDeIaService>();

// Central de IA privada do desenvolvedor — identidade propria, fora do modelo
// Atendente/Tenant. Ver Modules/CentralDeIa/README.md.
builder.Services.AddScoped<IGeradorDeTokenDaCentralDeIa, GeradorDeTokenDaCentralDeIa>();
builder.Services.AddScoped<ICentralDeIaAutenticador, CentralDeIaAutenticador>();
builder.Services.AddScoped<IObservabilidadeDaIa, ObservabilidadeDaIaService>();

// Visão Geral, Indicadores e Relatórios, alertas do sino — só consulta, lê o
// DbContext direto (permitido; chamar o Service de outro slice não seria).
builder.Services.AddScoped<IIndicadoresService, IndicadoresService>();

// Anexo vai para o disco do proprio servidor, nunca para o banco nem para
// storage de terceiro. Singleton porque nao guarda estado de requisicao —
// so le a configuracao e mexe em arquivo.
builder.Services.AddSingleton<IArmazenamentoDeAnexos, ArmazenamentoDeAnexosEmDisco>();

// Consulta de CEP e CNPJ. O cache e singleton de proposito: e da serventia
// inteira, nao da requisicao — cartorio atende a mesma cidade o dia todo.
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<IConsultaPublicaService, ConsultaPublicaService>(http =>
{
    // Sem User-Agent a BrasilAPI responde 429 e a consulta nunca funciona — ela
    // trata requisicao anonima como robo. Identificar-se tambem e boa educacao
    // com quem mantem servico publico de graca.
    http.DefaultRequestHeaders.UserAgent.ParseAdd("CrmCartorio/1.0 (self-hosted)");
});

// Os gatilhos de prazo sao os unicos que ninguem provoca — prazo simplesmente passa.
// Ver IVarredorDePrazos para o motivo de nao serem calculados na leitura da inbox.
builder.Services.AddHostedService<HospedeDaVarreduraDePrazos>();

// Silencio tambem nao e evento que alguem provoca. Sem esta varredura, conversa que
// ninguem encerra fica Ativa para sempre e o cliente que volta cai nela — sem
// boas-vindas, sem triagem. Ver IVarredorDeInatividade.
builder.Services.AddHostedService<HospedeDaVarreduraDeInatividade>();

// Drena a fila acima em segundo plano — ver FilaDeEnvioDoCanal e
// HospedeDaFilaDeEnvioDoCanal para o porque de existir. Complementar ao
// processamento de ENTRADA abaixo: aquele resolve o webhook (durável em SQL
// Server); este resolve o envio disparado por uma AÇÃO DO ATENDENTE (ex.:
// mudar etapa), que nunca passa pelo webhook.
builder.Services.AddHostedService<HospedeDaFilaDeEnvioDoCanal>();

// O webhook confirma depois do commit curto; automacao/IA/saida sao retomadas
// por workers coordenados no SQL Server, inclusive apos reciclagem do IIS.
builder.Services.AddHostedService<HospedeDoProcessamentoDeEntradaWhatsapp>();

// Canal de cartorio fica ocioso a maior parte do dia, entao quase toda conversa
// comecava com a conexao do Gemini fria — e a primeira chamada estourava o
// orcamento de latencia inteiro. Sondagem sem chave, custo zero de token.
builder.Services.AddHostedService<AquecedorDeConexaoDoGemini>();

// ---------------------------------------------------------------- autenticacao

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opcoes =>
    {
        opcoes.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = opcoesJwt.Emissor,
            ValidAudience = opcoesJwt.Audiencia,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opcoesJwt.Chave)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        // O WebSocket do SignalR nao carrega cabecalho Authorization, entao o
        // token chega pela query string. Restrito ao caminho dos hubs.
        opcoes.Events = new JwtBearerEvents
        {
            OnMessageReceived = contexto =>
            {
                var token = contexto.Request.Query["access_token"];

                if (!string.IsNullOrEmpty(token) &&
                    contexto.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    contexto.Token = token;
                }

                return Task.CompletedTask;
            },

            // SEC-01: SessaoAtivaRequirement falha com Forbid (403) por
            // convencao do framework quando o usuario esta autenticado mas a
            // policy nao passa. Para quem chama a API, token revogado deve
            // parecer "nao autenticado" — 401, igual a token expirado ou
            // ausente — nunca 403 (este projeto nunca usa 403 de policy do
            // framework; permissao fina e sempre erro de negocio dentro do
            // service, via IContextoDoUsuario).
            OnForbidden = contexto =>
            {
                contexto.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }
        };
    });

// SEC-02: amortecedor de forca bruta no login, por IP, em memoria. A politica
// mora em PoliticaDeLimiteDeLogin para que o verificador exercite exatamente
// esta configuracao, e nao uma copia dela.
builder.Services.AdicionarLimiteDeLogin();

// SEC-01: a policy padrao (usada por todo [Authorize] sem policy nomeada —
// controllers e o Hub do SignalR, nenhum deles nomeia policy) passa a exigir
// tambem SessaoAtivaRequirement: sessao ativa no banco, nao so JWT com
// assinatura e expiracao validas. Ver SessaoAtivaHandler. A unica excecao do
// projeto e a policy nomeada "CentralDeIa" logo abaixo — motivo no comentario
// dela.
builder.Services.AddAuthorization(opcoes =>
{
    opcoes.DefaultPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .AddRequirements(new SessaoAtivaRequirement())
        .Build();

    // Modules/CentralDeIa — a UNICA policy NOMEADA do projeto (todo o resto usa
    // a DefaultPolicy acima, sem nome). E o caso certo de fugir da regra: o
    // ator aqui nao e um Atendente, entao SessaoAtivaRequirement (que exige
    // TokenVersion de um Atendente) nao se aplica — a exigencia e so a claim
    // propria do token da Central. Ver Modules/CentralDeIa/README.md.
    opcoes.AddPolicy("CentralDeIa", politica => politica
        .RequireAuthenticatedUser()
        .RequireClaim(ClaimsDaCentralDeIa.Operador, "true"));
});

// ------------------------------------------------------------------- pipeline

builder.Services
    .AddControllers()
    .AddJsonOptions(opcoes =>
    {
        // Enum como texto: o frontend TypeScript trabalha com uniao de strings,
        // e log/payload ficam legiveis sem tabela de-para.
        opcoes.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddSignalR();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

const string PoliticaCors = "painel";

var origensPermitidas = builder.Configuration
    .GetSection("Cors:Origens")
    .Get<string[]>() ?? ["http://localhost:5173"];

builder.Services.AddCors(opcoes =>
    opcoes.AddPolicy(PoliticaCors, politica => politica
        .WithOrigins(origensPermitidas)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

var app = builder.Build();

// Checagem de sanidade, nao um Validate/ValidateOnStart: a IA segue a mesma
// politica de "nunca travar o sistema por configuracao ruim" do resto do
// modulo (OpcoesDaIA.EstaConfigurada) — um TimeoutSegundos alto demais vira
// aviso no log de boot, nunca impede o processo de subir. Ver a premissa
// documentada em DisjuntorDoGemini.TempoMaximoDeSondaPadrao.
{
    var opcoesGeminiNoBoot = app.Services.GetRequiredService<IOptions<OpcoesDaIA>>().Value.Gemini;
    var timeoutEfetivoSegundos = Math.Max(5, opcoesGeminiNoBoot.TimeoutSegundos);

    if (timeoutEfetivoSegundos >= DisjuntorDoGemini.TempoMaximoDeSondaPadrao.TotalSeconds)
    {
        app.Logger.LogWarning(
            "RespostaInteligente:Gemini:TimeoutSegundos ({TimeoutSegundos}s, efetivo {TimeoutEfetivo}s) " +
            "esta igual ou acima do teto de validade da sonda do disjuntor ({TempoMaximoDeSonda}s). " +
            "Uma sonda lenta (nao cancelada) pode ser tratada como perdida antes de terminar, abrindo uma " +
            "segunda sonda concorrente. Reduza o timeout ou aumente o teto da sonda (DisjuntorDoGemini).",
            opcoesGeminiNoBoot.TimeoutSegundos, timeoutEfetivoSegundos,
            DisjuntorDoGemini.TempoMaximoDeSondaPadrao.TotalSeconds);
    }
}

// PR3 da blindagem LGPD: nunca trava o boot (ver OpcoesDeProtecaoDeDados) —
// so avisa no log qual dos dois estados o binario subiu. "Dormente" e o
// estado normal ate alguem com acesso ao servidor provisionar a chave.
{
    var provedorDeChaveNoBoot = app.Services.GetRequiredService<IProvedorDeChaveDeDados>();

    if (provedorDeChaveNoBoot.CifraAtiva)
    {
        app.Logger.LogInformation(
            "Cifra em repouso ATIVA (ProtecaoDeDados:VersaoCorrente={VersaoCorrente}).",
            provedorDeChaveNoBoot.VersaoCorrente);
    }
    else
    {
        app.Logger.LogInformation(
            "Cifra em repouso DORMENTE — ProtecaoDeDados sem chave configurada. " +
            "Colunas marcadas .Cifrada() gravam em claro ate o provisionamento manual da chave.");
    }
}

// ------------------------------------------------------------------ execucao

app.UseMiddleware<TratamentoDeErroMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors(PoliticaCors);

// O painel React sai da MESMA origem que a API — o build do Vite e' copiado
// para wwwroot/ no deploy. Em desenvolvimento wwwroot esta vazio e quem serve o
// painel continua sendo o dev server do Vite na 5173, entao estas duas linhas
// nao mudam nada no dia a dia de quem desenvolve.
//
// Mesma origem resolve tres coisas de uma vez em producao: CORS deixa de
// existir, o IIS nao precisa de regra de rewrite para as rotas do react-router,
// e o WebSocket do SignalR nao e' cross-origin.
app.UseDefaultFiles();
app.UseStaticFiles();

// SEC-02: antes da autenticacao de proposito — a recusa por excesso de
// tentativas nao precisa (nem deve) custar validacao de token, consulta ao banco
// ou PBKDF2. Nao afeta rota alguma por si so: sem [EnableRateLimiting] a
// requisicao passa direto, entao /api/autenticacao/serventia, os webhooks e o
// resto da API seguem sem limite nenhum.
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// SEC-01: sem isto, CloseOnAuthenticationExpiration fica no padrao (false) e a
// conexao NUNCA cai sozinha quando o proprio JWT expira — so quando algo mais
// a derruba (rede, aba fechada, reciclagem do App Pool). Com true, o SignalR
// fecha a conexao no `exp` do token, do mesmo jeito que uma chamada REST com
// token expirado ja falha. Isto e SOBRE EXPIRACAO, nao sobre revogacao por
// TokenVersion — nao fecha uma conexao cujo token ainda esta dentro da
// validade mas foi revogado por troca de senha/desativacao/perfil (SEC-01B,
// ver AtendimentoHub.cs e Modules/Usuarios/README.md).
app.MapHub<AtendimentoHub>("/hubs/atendimento", opcoes =>
{
    opcoes.CloseOnAuthenticationExpiration = true;
});

// Vivo: o processo subiu e responde. NAO prova que o banco responde — de
// proposito, para servir de liveness barato (o IIS e o tunel batem aqui).
app.MapGet("/health", () => Results.Ok(new { status = "ok", fase = 1 }))
    .AllowAnonymous();

// Pronto: o banco responde de verdade.
//
// Existe porque /health mentia no unico momento em que a verdade importa: a
// connection string nao e' validada na subida (ao contrario de Jwt:Chave), entao
// um ambiente com connection string errada sobe, responde "ok" no /health, e so
// quebra quando o primeiro atendente tenta entrar. E' isto que o deploy confere.
app.MapGet("/health/pronto", async (CrmDbContext db, CancellationToken ct) =>
        await db.Database.CanConnectAsync(ct)
            ? Results.Ok(new { status = "pronto", banco = "ok" })
            : Results.StatusCode(StatusCodes.Status503ServiceUnavailable))
    .AllowAnonymous();

// Toda rota que nao casou com API, hub ou arquivo estatico cai no index.html —
// e' o react-router que resolve dali. Sem isto, abrir
// /configuracoes direto na barra de endereco (ou dar F5 numa rota interna)
// devolveria 404: o BrowserRouter usa History API, e o servidor nunca ouviu
// falar dessas rotas.
//
// Vem DEPOIS de MapControllers e MapHub de proposito: fallback e' ultimo
// recurso, nunca concorre com /api nem com /hubs.
//
// O roteiro publico (Central de Ajuda, wwwroot/roteiro/) tem index.html
// proprio, fora do bundle do React — sem isto, "/roteiro" e "/roteiro/" (sem
// nome de arquivo) cairiam no fallback do SPA acima e mostrariam a tela de
// login em vez do manual. UseStaticFiles ja resolve arquivo citado por nome
// (ex.: /roteiro/atendimento.html) normalmente.
//
// MapGet, deliberadamente NAO MapFallbackToFile: duas tentativas anteriores
// com fallback (testadas e revertidas) quebraram das duas formas possiveis —
// um padrao coringa ({**caminho}) e' casado pelo roteamento ANTES do
// UseStaticFiles servir o arquivo fisico, entao QUALQUER arquivo dentro de
// /roteiro/ (css, js, imagem, outra pagina) passava a devolver este
// index.html; e dois padroes literais ("/roteiro" e "/roteiro/") empatam com
// o fallback generico {*path:nonfile} do proprio React pra path sem extensao,
// e o roteamento de endpoint recusa escolher (AmbiguousMatchException, 500).
// MapGet e' rota normal — sempre resolvida antes de qualquer fallback, sem
// ambiguidade possivel com ele. So UMA rota, nao duas: no roteamento do
// ASP.NET Core "/roteiro" e "/roteiro/" sao o MESMO padrao (a barra final
// nao cria segmento novo) — registrar os dois path literais gerava DOIS
// endpoints identicos, e o proprio roteamento nao sabia escolher entre eles
// (a mesma AmbiguousMatchException, agora entre os dois MapGet).
//
// Dentro do handler, "/roteiro" (sem barra) REDIRECIONA para "/roteiro/" em
// vez de servir o arquivo direto — sem isto, o navegador resolve os caminhos
// relativos do HTML (href="css/estilo.css") contra a URL SEM barra, e
// "roteiro" vira nome de arquivo a ser substituido: o pedido real sai como
// "/css/estilo.css" (um nivel acima), nunca "/roteiro/css/estilo.css".
// Testado ao vivo: pagina carregava, CSS/JS nao. So com a barra final no
// endereco os caminhos relativos resolvem certo.
var indiceDoRoteiro = Path.Combine(app.Environment.WebRootPath, "roteiro", "index.html");
app.MapGet("/roteiro", (HttpContext ctx) => ctx.Request.Path.Value!.EndsWith('/')
        ? Results.File(indiceDoRoteiro, "text/html")
        : Results.Redirect("/roteiro/"))
    .AllowAnonymous();

app.MapFallbackToFile("index.html");

// O semeador nunca cria schema — o DDL fica em sql/001-schema.sql para execucao
// manual. Aqui so entram os dados de demonstracao, e apenas se ligado por config.
if (app.Configuration.GetValue<bool>("Seed:Habilitado"))
{
    using var escopo = app.Services.CreateScope();
    var semeador = escopo.ServiceProvider.GetRequiredService<SemeadorDeDemonstracao>();
    await semeador.ExecutarAsync();
}

app.Run();

return 0;
