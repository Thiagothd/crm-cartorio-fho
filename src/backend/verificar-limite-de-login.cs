#:project CrmCartorio.Api/CrmCartorio.Api.csproj
#:property PublishAot=false

using CrmCartorio.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;

// VERIFICADOR DO LIMITE DE TENTATIVAS DE LOGIN (SEC-02/SEC-03, camada por IP)
//
// Rode com: dotnet run verificar-limite-de-login.cs
//
// Sobe um Kestrel de verdade em porta efemera de loopback e bate nele com um
// HttpClient de verdade — o limitador so existe como middleware do pipeline HTTP,
// entao chamar o servico direto (como fazem os outros verificadores) nao provaria
// nada sobre ele.
//
// Usa PoliticaDeLimiteDeLogin.AdicionarLimiteDeLogin(), a MESMA chamada que o
// Program.cs faz: o que esta sob teste e a politica real (10/min/IP, QueueLimit=0,
// resposta 429 com Retry-After), nao uma copia dela escrita para o teste passar.
// As rotas aqui sao de mentira de proposito — o alvo e o limitador, nao o login.
//
// NAO toca em banco, nao abre conexao externa, nao depende de configuracao.

var falhas = 0;
void Checar(string nome, bool ok)
{
    Console.WriteLine($"{(ok ? "OK   " : "FALHA")} {nome}");
    if (!ok) falhas++;
}

var construtor = WebApplication.CreateBuilder();
construtor.Logging.ClearProviders();
construtor.WebHost.UseUrls("http://127.0.0.1:0");

// A politica REAL, exatamente como o Program.cs a registra.
construtor.Services.AdicionarLimiteDeLogin();

var app = construtor.Build();
app.UseRateLimiter();

// Espelha o par de rotas que importa: uma anonima COM o limite (o login) e uma
// anonima SEM ele (a serventia publica). O corpo nao interessa; o que se mede e
// quem recebe 429 e quem nunca recebe.
app.MapPost("/api/autenticacao/login", () => Results.Ok())
   .RequireRateLimiting(PoliticaDeLimiteDeLogin.Nome);

app.MapPost("/api/autenticacao/login/mfa", () => Results.Ok())
   .RequireRateLimiting(PoliticaDeLimiteDeLogin.NomeMfa);

app.MapPost("/api/mfa/desativar", () => Results.Ok())
   .RequireRateLimiting(PoliticaDeLimiteDeLogin.NomeGestao);

app.MapGet("/api/autenticacao/serventia", () => Results.Ok());

await app.StartAsync();

var endereco = app.Services.GetRequiredService<IServer>()
    .Features.Get<IServerAddressesFeature>()!
    .Addresses.First();

using var http = new HttpClient { BaseAddress = new Uri(endereco) };

try
{
    // ------------------------------------------------------------- cenario 1
    // As 10 primeiras passam; a 11a e recusada.

    var statusDoLogin = new List<HttpStatusCode>();
    for (var i = 0; i < 11; i++)
    {
        using var resposta = await http.PostAsync("/api/autenticacao/login", null);
        statusDoLogin.Add(resposta.StatusCode);
    }

    Checar("1a. as 10 primeiras tentativas de login passam",
        statusDoLogin.Take(10).All(s => s == HttpStatusCode.OK));

    Checar("1b. a 11a tentativa no mesmo minuto recebe 429",
        statusDoLogin[10] == HttpStatusCode.TooManyRequests);

    // ------------------------------------------------------------- cenario 2
    // A recusa fala a mesma lingua do resto da API.

    using (var recusada = await http.PostAsync("/api/autenticacao/login", null))
    {
        var corpo = await recusada.Content.ReadAsStringAsync();

        Checar("2a. a recusa traz Retry-After",
            recusada.Headers.TryGetValues("Retry-After", out var valores)
            && int.TryParse(valores.First(), out var segundos) && segundos > 0);

        Checar("2b. a recusa e um ProblemDetails com o codigo estavel do projeto",
            recusada.Content.Headers.ContentType?.MediaType == "application/problem+json"
            && corpo.Contains("muitas_tentativas"));
    }

    // ------------------------------------------------------------- cenario 3
    // A rota anonima vizinha NAO e afetada — o limite e so do login. Se este
    // teste falhar, a tela de login para de abrir sozinha depois de 10 acessos.

    var statusDaServentia = new List<HttpStatusCode>();
    for (var i = 0; i < 20; i++)
    {
        using var resposta = await http.GetAsync("/api/autenticacao/serventia");
        statusDaServentia.Add(resposta.StatusCode);
    }

    Checar("3. /api/autenticacao/serventia nao e afetada pelo limite do login (20 chamadas seguidas)",
        statusDaServentia.All(s => s == HttpStatusCode.OK));

    // ------------------------------------------------------------- cenario 4
    // SEC-03 — etapa 2 do login (login/mfa): janela propria, mais generosa
    // (20/min) — quem chega aqui ja provou a senha.

    var statusDoLoginMfa = new List<HttpStatusCode>();
    for (var i = 0; i < 21; i++)
    {
        using var resposta = await http.PostAsync("/api/autenticacao/login/mfa", null);
        statusDoLoginMfa.Add(resposta.StatusCode);
    }

    Checar($"4a. as {PoliticaDeLimiteDeLogin.TentativasDeMfaPorMinuto} primeiras tentativas de login/mfa passam",
        statusDoLoginMfa.Take(PoliticaDeLimiteDeLogin.TentativasDeMfaPorMinuto).All(s => s == HttpStatusCode.OK));

    Checar("4b. a tentativa seguinte no mesmo minuto recebe 429",
        statusDoLoginMfa[PoliticaDeLimiteDeLogin.TentativasDeMfaPorMinuto] == HttpStatusCode.TooManyRequests);

    // ------------------------------------------------------------- cenario 5
    // SEC-03 — gestao de MFA (mfa-gestao): 10/min, janela PROPRIA e
    // independente da do login — esgotar uma nao contamina a outra.

    var statusDaGestao = new List<HttpStatusCode>();
    for (var i = 0; i < 11; i++)
    {
        using var resposta = await http.PostAsync("/api/mfa/desativar", null);
        statusDaGestao.Add(resposta.StatusCode);
    }

    Checar($"5a. as {PoliticaDeLimiteDeLogin.TentativasDeGestaoPorMinuto} primeiras chamadas de gestao de MFA passam",
        statusDaGestao.Take(PoliticaDeLimiteDeLogin.TentativasDeGestaoPorMinuto).All(s => s == HttpStatusCode.OK));

    Checar("5b. a chamada seguinte no mesmo minuto recebe 429",
        statusDaGestao[PoliticaDeLimiteDeLogin.TentativasDeGestaoPorMinuto] == HttpStatusCode.TooManyRequests);

    Checar("5c. login e login/mfa continuam bloqueados (janelas independentes, nao foram resetadas)",
        (await http.PostAsync("/api/autenticacao/login", null)).StatusCode == HttpStatusCode.TooManyRequests
        && (await http.PostAsync("/api/autenticacao/login/mfa", null)).StatusCode == HttpStatusCode.TooManyRequests);
}
finally
{
    await app.StopAsync();
}

Console.WriteLine();
Console.WriteLine(falhas == 0 ? "TODOS OS TESTES PASSARAM." : $"{falhas} TESTE(S) FALHARAM.");

return falhas == 0 ? 0 : 1;
