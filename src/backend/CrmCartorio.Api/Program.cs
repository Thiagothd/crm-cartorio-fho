using System.Text;
using System.Text.Json.Serialization;
using Anthropic;
using CrmCartorio.Api.Infrastructure.Automacao;
using CrmCartorio.Api.Infrastructure.Identity;
using CrmCartorio.Api.Infrastructure.Anexos;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Persistence.Seeding;
using CrmCartorio.Api.Infrastructure.Realtime;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Infrastructure.Web;
using CrmCartorio.Api.Modules.Atendimento.Application;
using CrmCartorio.Api.Modules.CanalWhatsApp.Application;
using CrmCartorio.Api.Modules.Clientes.Application;
using CrmCartorio.Api.Modules.Configuracoes.Application;
using CrmCartorio.Api.Modules.Consultas.Application;
using CrmCartorio.Api.Modules.Indicadores.Application;
using CrmCartorio.Api.Modules.RespostaInteligente.Application;
using CrmCartorio.Api.Modules.RespostaInteligente.Application.Cota;
using CrmCartorio.Api.Modules.RespostaInteligente.Application.Provedores;
using CrmCartorio.Api.Modules.Servicos.Application;
using CrmCartorio.Api.Modules.Tenants.Application;
using CrmCartorio.Api.Modules.Usuarios.Application;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

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

builder.Services.AddDbContext<CrmDbContext>(opcoes =>
    ConfiguracaoDoBanco.Configurar(opcoes, builder.Configuration.GetConnectionString("SqlServer")));

// -------------------------------------------------------------------- servicos

builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<IHashDeSenha, HashDeSenha>();
builder.Services.AddScoped<IContextoDoUsuario, ContextoDoUsuario>();
builder.Services.AddScoped<IGeradorDeToken, GeradorDeToken>();

builder.Services.AddScoped<IAutenticacaoService, AutenticacaoService>();
builder.Services.AddScoped<IAtendenteService, AtendenteService>();
builder.Services.AddScoped<IClienteService, ClienteService>();
builder.Services.AddScoped<IImportacaoDeContatosService, ImportacaoDeContatosService>();
builder.Services.AddScoped<ISetorService, SetorService>();
builder.Services.AddScoped<IEtapaFunilService, EtapaFunilService>();
builder.Services.AddScoped<IMensagemAutomaticaService, MensagemAutomaticaService>();
builder.Services.AddScoped<IRespostaRapidaService, RespostaRapidaService>();
builder.Services.AddScoped<ITipoServicoService, TipoServicoService>();
builder.Services.AddScoped<ITenantService, TenantService>();
builder.Services.AddScoped<IGeradorDeProtocolo, GeradorDeProtocolo>();
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
builder.Services.AddScoped<IEnviadorDeCanal, EnviadorPeloWhatsApp>();
builder.Services.AddScoped<IProcessadorDeWebhook, ProcessadorDeWebhook>();
builder.Services.AddScoped<IPareamentoDoCanal, PareamentoDoCanal>();
builder.Services.AddScoped<IRegrasDoCanal, RegrasDoCanal>();
builder.Services.AddSingleton<ControleDeTentativasDePareamento>();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<IEvolutionClient, EvolutionClient>();

// Resposta Inteligente (Fase 2B, dois provedores desde a Fase 2G): Gemini 3.7
// Flash principal, Haiku 4.5 de contingencia — ver Modules/RespostaInteligente/README.md.
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
builder.Services.AddHttpClient<ProvedorGemini>((sp, http) =>
{
    var opcoesGemini = sp.GetRequiredService<IOptions<OpcoesDaIA>>().Value.Gemini;
    http.BaseAddress = new Uri(opcoesGemini.BaseUrl);
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
            }
        };
    });

builder.Services.AddAuthorization();

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

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<AtendimentoHub>("/hubs/atendimento");

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
