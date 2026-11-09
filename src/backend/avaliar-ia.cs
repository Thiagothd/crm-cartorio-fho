#:project CrmCartorio.Api/CrmCartorio.Api.csproj
#:property PublishAot=false
#:package Microsoft.EntityFrameworkCore.InMemory@10.0.9

using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Entities;
using CrmCartorio.Api.Modules.RespostaInteligente.Application;
using CrmCartorio.Api.Modules.RespostaInteligente.Application.Cota;
using CrmCartorio.Api.Modules.RespostaInteligente.Application.Provedores;
using CrmCartorio.Api.Modules.RespostaInteligente.Domain.Entities;
using CrmCartorio.Api.Modules.RespostaInteligente.Domain.Enums;
using CrmCartorio.Api.Modules.Servicos.Domain.Entities;
using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// AVALIACAO EXPERIMENTAL DA RESPOSTA INTELIGENTE (artigo da N2)
//
// REDE REAL E CUSTO REAL: chama os provedores de verdade. Nao faz parte do build.
//
//   dotnet run avaliar-ia.cs contexto
//   dotnet run avaliar-ia.cs qualidade <gemini|haiku> <completo|sem_regras|sem_catalogo> <repeticoes> [pausaMs]
//   dotnet run avaliar-ia.cs contingencia <503|429|timeout|ambos> <perguntas>
//
// O contexto vem do MontadorDeContexto real, sobre o catalogo em avaliacao/catalogo-demo.json.
// A pergunta passa pelo MascaradorDeDadosPessoais real. Os provedores e o orquestrador sao os
// do produto; a unica intervencao e um DelegatingHandler que troca o prompt de sistema nas
// condicoes de ablacao ou simula a falha do provedor nos ensaios de contingencia.
//
// As saidas sao gravadas em avaliacao/resultados e retomam de onde pararam.

const string ModeloGemini = "gemini-3.6-flash";
const string ModeloHaiku = "claude-haiku-5-5";

// Teto autorizado para esta avaliacao. Conta toda requisicao HTTP enviada a Anthropic,
// tenha ou nao dado certo, e e guardado em disco para valer entre execucoes.
const int TetoDoHaiku = 30;

const string PromptGenerico = """
    Você é o atendimento automático de um cartório brasileiro, respondendo pelo WhatsApp.
    Responda à pergunta do cliente de forma útil e direta, em português do Brasil, usando o que
    você sabe sobre cartórios no Brasil e, se houver, o CONTEXTO enviado na mensagem. Se realmente
    não for possível responder, use podeResponder=false. Quando podeResponder for verdadeiro, use
    NaoClassificado em categoria. Responda sempre no formato pedido.
    """;

var pastaDaAvaliacao = Path.GetFullPath(Path.Combine("..", "..", "avaliacao"));
var pastaDeResultados = Path.Combine(pastaDaAvaliacao, "resultados");
Directory.CreateDirectory(pastaDeResultados);
var arquivoDoTeto = Path.Combine(pastaDeResultados, "chamadas-haiku.txt");

var json = new JsonSerializerOptions { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
var modo = args.Length > 0 ? args[0] : "contexto";

var config = new ConfigurationBuilder()
    .AddJsonFile(Path.GetFullPath(Path.Combine("CrmCartorio.Api", "appsettings.Development.json")), optional: true)
    .Build();
var opcoesLidas = config.GetSection(OpcoesDaIA.Secao).Get<OpcoesDaIA>() ?? new OpcoesDaIA();

var tenantId = Guid.Parse("0199b2c0-0000-7000-8000-000000000001");
var contextoGerado = await MontarContextoAsync();
await File.WriteAllTextAsync(Path.Combine(pastaDaAvaliacao, "contexto-gerado.txt"), contextoGerado);
Console.WriteLine($"Contexto: {contextoGerado.Length} caracteres.");

if (modo == "contexto")
{
    return 0;
}

return modo switch
{
    "qualidade" => await QualidadeAsync(args[1], args[2], int.Parse(args[3]), args.Length > 4 ? int.Parse(args[4]) : 1000, args.Length > 5 ? args[5].Split(',') : null),
    "contingencia" => await ContingenciaAsync(args[1], int.Parse(args[2])),
    _ => 1
};

async Task<int> QualidadeAsync(string nomeProvedor, string condicao, int repeticoes, int pausaMs, string[]? somenteIds)
{
    var corpus = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(pastaDaAvaliacao, "corpus.json")))!.AsArray()
        .Where(i => somenteIds is null || somenteIds.Contains(i!["id"]!.GetValue<string>()))
        .ToList();
    var arquivo = Path.Combine(pastaDeResultados, $"qualidade-{nomeProvedor}-{condicao}.jsonl");

    var feitos = new HashSet<string>();
    if (File.Exists(arquivo))
    {
        foreach (var linha in await File.ReadAllLinesAsync(arquivo))
        {
            var n = JsonNode.Parse(linha)!;

            // No Gemini gratuito, a falha nao custa e a pergunta pode ser refeita. No Haiku,
            // toda chamada gravada ja consumiu o teto e nao se repete.
            if (nomeProvedor != "gemini" || n["sucesso"]!.GetValue<bool>())
            {
                feitos.Add($"{n["id"]}#{n["rep"]}");
            }
        }
    }

    var prompt = condicao == "completo" ? null : PromptGenerico;
    var contexto = condicao == "sem_catalogo" ? string.Empty : contextoGerado;
    IProvedorDeIa provedor = nomeProvedor == "gemini"
        ? MontarGemini(new TrocaDePrompt(prompt, new HttpClientHandler()), 20)
        : MontarHaiku(new TrocaDePrompt(prompt, new TetoDeChamadasPagas(arquivoDoTeto, TetoDoHaiku, new HttpClientHandler())));

    Console.WriteLine($"{nomeProvedor}/{condicao}: modelo {provedor.Modelo}, {corpus.Count} perguntas, {repeticoes} repeticoes.");

    for (var rep = 1; rep <= repeticoes; rep++)
    {
        foreach (var item in corpus)
        {
            var id = item!["id"]!.GetValue<string>();
            if (feitos.Contains($"{id}#{rep}"))
            {
                continue;
            }

            var historico = (item["h"]?.AsArray() ?? [])
                .Select(t => new TrocaAnterior(t!["c"]!.GetValue<bool>(), MascaradorDeDadosPessoais.Mascarar(t["t"]!.GetValue<string>())))
                .ToList();
            var pergunta = MascaradorDeDadosPessoais.Mascarar(item["p"]!.GetValue<string>());

            var (resultado, tentativas) = await ChamarAsync(provedor, new PedidoDeIa(contexto, historico, pergunta));

            var registro = new
            {
                id,
                rep,
                provedor = nomeProvedor,
                modelo = provedor.Modelo,
                condicao,
                sucesso = resultado.Sucesso,
                falha = resultado.Falha.ToString(),
                http = resultado.CodigoHttp,
                tentativas,
                pode = resultado.Resposta?.PodeResponder,
                categoria = resultado.Resposta?.CategoriaDaNaoResposta.ToString(),
                resposta = resultado.Resposta?.Resposta,
                motivo = resultado.Resposta?.Motivo,
                latenciaMs = resultado.LatenciaMs,
                tokensEntrada = resultado.TokensEntrada,
                tokensSaida = resultado.TokensSaida,
                tokensPensamento = resultado.TokensPensamento,
                quando = DateTime.UtcNow
            };

            await File.AppendAllTextAsync(arquivo, JsonSerializer.Serialize(registro, json) + "\n");
            Console.WriteLine($"  [{rep}] {id} sucesso={resultado.Sucesso} pode={resultado.Resposta?.PodeResponder} {resultado.LatenciaMs}ms");
            await Task.Delay(pausaMs);
        }
    }

    return 0;
}

async Task<int> ContingenciaAsync(string tipoDeFalha, int quantidade)
{
    var corpus = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(pastaDaAvaliacao, "corpus.json")))!.AsArray()
        .Where(i => i!["grupo"]!.GetValue<string>() == "A")
        .Take(quantidade)
        .ToList();

    var arquivo = Path.Combine(pastaDeResultados, $"contingencia-{tipoDeFalha}.jsonl");
    File.Delete(arquivo);

    var nome = $"contingencia-{Guid.NewGuid():N}";
    var servicos = new ServiceCollection();
    servicos.AddSingleton<ICifraDeDados>(FabricaDeCifraDeDados.CriarDormente());
    servicos.AddDbContext<CrmDbContext>(o => o.UseInMemoryDatabase(nome));
    var escopos = servicos.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

    var opcoes = new OpcoesDaIA
    {
        LimiteDiario = 1000,
        Gemini = new OpcoesDoGemini
        {
            Habilitado = true,
            ApiKey = "chave-nao-usada-falha-simulada",
            Modelo = ModeloGemini,
            TimeoutSegundos = 8,
            LimiteRpmDeDescoberta = 600,
            CooldownSegundos = 3600
        },
        Anthropic = new OpcoesDoAnthropic
        {
            Habilitado = true,
            ApiKey = opcoesLidas.ApiKeyDoAnthropicResolvida,
            Modelo = ModeloHaiku,
            LimiteDiarioHaiku = 1000,
            TimeoutSegundos = 20
        }
    };

    var falhaGemini = new SimuladorDeFalha(tipoDeFalha == "ambos" ? "503" : tipoDeFalha);
    var http = new HttpClient(falhaGemini) { BaseAddress = new Uri(opcoes.Gemini.BaseUrl) };
    var gemini = new ProvedorGemini(http, Options.Create(opcoes), NullLogger<ProvedorGemini>.Instance);

    var clienteHaiku = new AnthropicClient
    {
        ApiKey = opcoes.ApiKeyDoAnthropicResolvida,
        HttpClient = tipoDeFalha == "ambos" ? new HttpClient(new SimuladorDeFalha("503")) : new HttpClient(new TetoDeChamadasPagas(arquivoDoTeto, TetoDoHaiku, new HttpClientHandler())),
        MaxRetries = 0
    };
    var haiku = new ProvedorAnthropic(clienteHaiku, Options.Create(opcoes), NullLogger<ProvedorAnthropic>.Instance);

    var disjuntor = new DisjuntorDoGemini(null);
    var cota = new ContadorDeCotaDoGemini(escopos);
    var limite = new ContadorDeLimiteDiario(escopos);
    var politica = new PoliticaDeSelecao(cota, disjuntor);
    var registrador = new RegistradorDeChamadas(escopos, NullLogger<RegistradorDeChamadas>.Instance);
    var orquestrador = new OrquestradorDeProvedores(
        gemini, haiku, politica, disjuntor, cota, limite, registrador,
        Options.Create(opcoes), NullLogger<OrquestradorDeProvedores>.Instance);

    var ordem = 0;
    foreach (var item in corpus)
    {
        ordem++;
        var historico = (item!["h"]?.AsArray() ?? [])
            .Select(t => new TrocaAnterior(t!["c"]!.GetValue<bool>(), t["t"]!.GetValue<string>()))
            .ToList();

        var chamadasAntes = falhaGemini.Chamadas;
        var cronometro = Stopwatch.StartNew();
        var resposta = await orquestrador.ResponderAsync(
            tenantId, null, new PedidoDeIa(contextoGerado, historico, item["p"]!.GetValue<string>()), CancellationToken.None);
        cronometro.Stop();

        var registro = new
        {
            ordem,
            id = item["id"]!.GetValue<string>(),
            tipoDeFalha,
            geminiDiscado = falhaGemini.Chamadas > chamadasAntes,
            podeResponder = resposta.PodeResponder,
            tamanhoDaResposta = resposta.Resposta.Length,
            motivo = resposta.Motivo,
            latenciaTotalMs = cronometro.ElapsedMilliseconds
        };

        await File.AppendAllTextAsync(arquivo, JsonSerializer.Serialize(registro, json) + "\n");
        Console.WriteLine($"  {ordem} gemini={registro.geminiDiscado} pode={resposta.PodeResponder} {cronometro.ElapsedMilliseconds}ms");
    }

    return 0;
}

async Task<(ResultadoDoProvedor Resultado, int Tentativas)> ChamarAsync(IProvedorDeIa provedor, PedidoDeIa pedido)
{
    // O Haiku e pago e tem teto de chamadas: nunca repete.
    var maximo = provedor.Identidade == ProvedorDeIa.Anthropic ? 1 : 4;

    for (var tentativa = 1; ; tentativa++)
    {
        ResultadoDoProvedor resultado;
        try
        {
            resultado = await provedor.GerarAsync(pedido, CancellationToken.None);
        }
        catch (Exception falha)
        {
            resultado = ResultadoDoProvedor.DeFalha(TipoDeFalhaDeIa.Indisponibilidade, null, 0, falha.GetType().Name);
        }

        // A qualidade da resposta nao pode ser confundida com indisponibilidade do provedor:
        // falha operacional repete a chamada, e o campo "tentativas" guarda quantas foram,
        // o que permite calcular a disponibilidade na primeira tentativa.
        if (resultado.Sucesso || tentativa == maximo)
        {
            return (resultado, tentativa);
        }

        var espera = resultado.RetryApos ?? TimeSpan.FromSeconds(8 * tentativa);
        await Task.Delay(espera > TimeSpan.FromSeconds(65) ? TimeSpan.FromSeconds(65) : espera);
    }
}

IProvedorDeIa MontarGemini(HttpMessageHandler handler, int timeoutSegundos)
{
    var opcoes = new OpcoesDaIA
    {
        Gemini = new OpcoesDoGemini
        {
            Habilitado = true,
            ApiKey = opcoesLidas.Gemini.ApiKey,
            Modelo = ModeloGemini,
            NivelDeRaciocinio = "low",
            BaseUrl = opcoesLidas.Gemini.BaseUrl,
            TimeoutSegundos = timeoutSegundos
        }
    };
    var http = new HttpClient(handler) { BaseAddress = new Uri(opcoes.Gemini.BaseUrl) };
    return new ProvedorGemini(http, Options.Create(opcoes), NullLogger<ProvedorGemini>.Instance);
}

IProvedorDeIa MontarHaiku(HttpMessageHandler handler)
{
    var opcoes = new OpcoesDaIA
    {
        Anthropic = new OpcoesDoAnthropic
        {
            Habilitado = true,
            ApiKey = opcoesLidas.ApiKeyDoAnthropicResolvida,
            Modelo = ModeloHaiku,
            TimeoutSegundos = 20
        }
    };
    var cliente = new AnthropicClient { ApiKey = opcoes.ApiKeyDoAnthropicResolvida, HttpClient = new HttpClient(handler), MaxRetries = 0 };
    return new ProvedorAnthropic(cliente, Options.Create(opcoes), NullLogger<ProvedorAnthropic>.Instance);
}

async Task<string> MontarContextoAsync()
{
    var opcoes = new DbContextOptionsBuilder<CrmDbContext>().UseInMemoryDatabase("avaliacao-contexto").Options;
    await using var db = new CrmDbContext(opcoes, FabricaDeCifraDeDados.CriarDormente());
    var catalogo = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(pastaDaAvaliacao, "catalogo-demo.json")))!;

    db.Tenants.Add(new Tenant { Id = tenantId, RazaoSocial = "Cartório de Gameleiras", NomeFantasia = "Cartório de Gameleiras", Cnpj = "00000000000000" });

    foreach (var s in catalogo["setores"]!.AsArray())
    {
        db.Setores.Add(new Setor { Id = Guid.Parse(s!["id"]!.GetValue<string>()), TenantId = tenantId, Nome = s["nome"]!.GetValue<string>() });
    }

    foreach (var t in catalogo["tipos"]!.AsArray())
    {
        var id = Guid.Parse(t!["id"]!.GetValue<string>());
        db.TiposServico.Add(new TipoServico
        {
            Id = id,
            TenantId = tenantId,
            Nome = t["nome"]!.GetValue<string>(),
            Descricao = t["descricao"]?.GetValue<string>(),
            DocumentosNecessarios = t["documentosNecessarios"]?.GetValue<string>(),
            SetorId = t["setorId"] is null ? null : Guid.Parse(t["setorId"]!.GetValue<string>()),
            Ativo = t["ativo"]!.GetValue<bool>()
        });

        if (t["prazo"] is { } p)
        {
            db.PrazosServico.Add(new PrazoServico
            {
                Id = Guid.Parse(p["id"]!.GetValue<string>()),
                TenantId = tenantId,
                TipoServicoId = id,
                PrazoHoras = p["prazoHoras"]!.GetValue<int>(),
                ApenasDiasUteis = p["apenasDiasUteis"]!.GetValue<bool>(),
                Ativo = true
            });
        }
    }

    foreach (var r in catalogo["rapidas"]!.AsArray())
    {
        db.RespostasRapidas.Add(new RespostaRapida
        {
            Id = Guid.Parse(r!["id"]!.GetValue<string>()),
            TenantId = tenantId,
            Atalho = r["atalho"]?.GetValue<string>() ?? "/x",
            Titulo = r["titulo"]!.GetValue<string>(),
            Conteudo = r["conteudo"]!.GetValue<string>(),
            Ativo = r["ativo"]?.GetValue<bool>() ?? true
        });
    }

    foreach (var c in catalogo["conhecimentos"]!.AsArray())
    {
        db.ConhecimentosDeIa.Add(new ConhecimentoDeIa
        {
            Id = Guid.Parse(c!["id"]!.GetValue<string>()),
            TenantId = tenantId,
            Titulo = c["titulo"]!.GetValue<string>(),
            Conteudo = c["conteudo"]!.GetValue<string>(),
            Tema = c["tema"]!.GetValue<string>(),
            Ativo = c["ativo"]!.GetValue<bool>()
        });
    }

    await db.SaveChangesAsync();
    return await MontadorDeContexto.MontarAsync(db, tenantId, CancellationToken.None);
}

/// <summary>Troca so o prompt de sistema; o restante da chamada continua sendo o do produto.</summary>
sealed class TrocaDePrompt(string? prompt, HttpMessageHandler interno) : DelegatingHandler(interno)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage requisicao, CancellationToken ct)
    {
        if (prompt is not null && requisicao.Content is not null)
        {
            var corpo = JsonNode.Parse(await requisicao.Content.ReadAsStringAsync(ct))!.AsObject();
            if (corpo.ContainsKey("system_instruction"))
            {
                corpo["system_instruction"] = prompt;
            }

            if (corpo.ContainsKey("system"))
            {
                corpo["system"] = prompt;
            }

            requisicao.Content = new StringContent(corpo.ToJsonString(), Encoding.UTF8, "application/json");
        }

        return await base.SendAsync(requisicao, ct);
    }
}

/// <summary>Recusa localmente qualquer chamada paga acima do teto autorizado.</summary>
sealed class TetoDeChamadasPagas(string arquivo, int teto, HttpMessageHandler interno) : DelegatingHandler(interno)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage requisicao, CancellationToken ct)
    {
        var usadas = File.Exists(arquivo) ? int.Parse(File.ReadAllText(arquivo)) : 0;
        if (usadas >= teto)
        {
            throw new InvalidOperationException($"Teto de {teto} chamadas pagas atingido; nada foi enviado.");
        }

        File.WriteAllText(arquivo, (usadas + 1).ToString());
        return await base.SendAsync(requisicao, ct);
    }
}

/// <summary>Simula a falha do provedor sem abrir conexao.</summary>
sealed class SimuladorDeFalha(string tipo) : HttpMessageHandler
{
    public int Chamadas;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage requisicao, CancellationToken ct)
    {
        Interlocked.Increment(ref Chamadas);

        if (tipo == "timeout")
        {
            await Task.Delay(Timeout.Infinite, ct);
        }

        var codigo = tipo == "429" ? HttpStatusCode.TooManyRequests : HttpStatusCode.ServiceUnavailable;
        return new HttpResponseMessage(codigo)
        {
            Content = new StringContent("{\"error\":{\"code\":" + (int)codigo + ",\"message\":\"falha simulada\"}}", Encoding.UTF8, "application/json")
        };
    }
}
