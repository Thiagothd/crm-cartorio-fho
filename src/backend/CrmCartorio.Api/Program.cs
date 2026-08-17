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
using CrmCartorio.Api.Modules.RespostaInteligente.Application;
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

if (string.IsNullOrWhiteSpace(opcoesJwt.Chave) || Encoding.UTF8.GetByteCount(opcoesJwt.Chave) < 32)
{
    // Falhar aqui e melhor do que subir com uma chave fraca e so descobrir depois
    // que qualquer um consegue forjar um token.
    throw new InvalidOperationException(
        "Jwt:Chave ausente ou com menos de 32 bytes. Defina em variavel de ambiente " +
        "ou user-secrets antes de iniciar a API.");
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
builder.Services.AddScoped<INotificadorDeAtendimento, NotificadorDeAtendimento>();
builder.Services.AddScoped<SemeadorDeDemonstracao>();

// Canal real de WhatsApp (PILOTO, numero pessoal — ver docs/DEPLOY-WHATSAPP.md).
// O recebimento fica no Atendimento porque e regra de atendimento; so o formato
// do gateway mora no slice CanalWhatsApp.
builder.Services.AddScoped<IRecebimentoDeWhatsApp, RecebimentoDeWhatsApp>();
builder.Services.AddScoped<IEnviadorDeCanal, EnviadorPeloWhatsApp>();
builder.Services.AddScoped<IProcessadorDeWebhook, ProcessadorDeWebhook>();
builder.Services.AddScoped<IPareamentoDoCanal, PareamentoDoCanal>();
builder.Services.AddHttpClient<IEvolutionClient, EvolutionClient>();

// Resposta Inteligente (Fase 2B). Sem chave configurada, o SDK sobe do mesmo jeito
// — GarantirConfigurado nunca chega a chamar a API. O cliente e' singleton porque
// nao guarda estado de requisicao; o timeout de rede real fica no CancelAfter do
// proprio servico, nao aqui.
builder.Services.AddSingleton(sp =>
{
    var opcoesDaIA = sp.GetRequiredService<IOptions<OpcoesDaIA>>().Value;
    return new AnthropicClient { ApiKey = opcoesDaIA.ApiKey };
});
builder.Services.AddScoped<IRespostaInteligenteService, RespostaInteligenteService>();

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

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<AtendimentoHub>("/hubs/atendimento");

app.MapGet("/health", () => Results.Ok(new { status = "ok", fase = 1 }))
    .AllowAnonymous();

// O semeador nunca cria schema — o DDL fica em sql/001-schema.sql para execucao
// manual. Aqui so entram os dados de demonstracao, e apenas se ligado por config.
if (app.Configuration.GetValue<bool>("Seed:Habilitado"))
{
    using var escopo = app.Services.CreateScope();
    var semeador = escopo.ServiceProvider.GetRequiredService<SemeadorDeDemonstracao>();
    await semeador.ExecutarAsync();
}

app.Run();
