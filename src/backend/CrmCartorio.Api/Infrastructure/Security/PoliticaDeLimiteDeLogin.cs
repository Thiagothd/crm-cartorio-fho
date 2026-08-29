using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CrmCartorio.Api.Infrastructure.Security;

/// <summary>
/// SEC-02: limite de tentativas de login por IP — o amortecedor raso da protecao
/// contra forca bruta.
/// </summary>
/// <remarks>
/// <b>Nao e a protecao que importa.</b> A que de fato para um ataque e o backoff
/// por CONTA, persistido no banco (<c>AutenticacaoService.AutenticarAsync</c>).
/// Este limite existe so para que um script obvio martelando o endpoint pare cedo,
/// antes de gastar PBKDF2 e uma ida ao banco por tentativa.
///
/// Vale por PROCESSO, nao pelo cluster. Hoje isso nao e uma lacuna real: os dois
/// App Pools rodam com <c>Maximum Worker Processes = 1</c> (docs/INFRAESTRUTURA.md,
/// secao 4.1), entao ha um processo por ambiente. Se algum dia virar web garden ou
/// mais de uma instancia, esta camada dilui na mesma proporcao — e a de conta, que
/// e a exata, continua exata.
///
/// A configuracao mora aqui, e nao solta no <c>Program.cs</c>, para que o
/// verificador exercite a POLITICA REAL contra um servidor de verdade em vez de
/// uma copia dela — copiar a configuracao testaria a copia.
/// </remarks>
public static class PoliticaDeLimiteDeLogin
{
    /// <summary>
    /// Nome da politica, compartilhado entre quem a registra e quem a aplica
    /// (<c>AutenticacaoController.Login</c>).
    /// </summary>
    /// <remarks>
    /// Uma constante, e nao a string repetida nos dois lugares, porque um erro de
    /// digitacao em qualquer um dos lados nao daria erro de compilacao nem falha
    /// visivel — a rota simplesmente ficaria SEM limite nenhum, em silencio.
    /// </remarks>
    public const string Nome = "login";

    public const int TentativasPorMinuto = 10;

    public static IServiceCollection AdicionarLimiteDeLogin(this IServiceCollection servicos) =>
        servicos.AddRateLimiter(opcoes =>
        {
            opcoes.AddPolicy(Nome, http => RateLimitPartition.GetFixedWindowLimiter(
                // Nunca o texto cru de um cabecalho: ver ResolvedorDeIpDoCliente.
                partitionKey: ResolvedorDeIpDoCliente.Resolver(http),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = TentativasPorMinuto,
                    Window = TimeSpan.FromMinutes(1),

                    // Login nao entra em fila: quem passou do limite recebe 429 na
                    // hora. Enfileirar deixaria a resposta lenta sem informar nada a
                    // quem esperou, e ainda prenderia conexao do servidor durante um
                    // ataque — exatamente o que um atacante quer.
                    QueueLimit = 0
                }));

            opcoes.OnRejected = async (contexto, ct) =>
            {
                // O TratamentoDeErroMiddleware so intercepta excecao, e a recusa do
                // limitador nao passa por ele. Escreve aqui o MESMO formato de
                // ProblemDetails, para o painel nao ter de tratar dois formatos.
                var segundos = contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera)
                    ? (int)espera.TotalSeconds
                    : (int)TimeSpan.FromMinutes(1).TotalSeconds;

                contexto.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                contexto.HttpContext.Response.Headers.RetryAfter = segundos.ToString();

                var problema = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Muitas tentativas de login. Aguarde um instante e tente de novo.",
                    Type = "https://crm-cartorio/erros/muitas_tentativas",
                    Instance = contexto.HttpContext.Request.Path,
                    Extensions = { ["codigo"] = "muitas_tentativas" }
                };

                // O content-type vai por PARAMETRO, e nao atribuindo
                // Response.ContentType antes: WriteAsJsonAsync sobrescreve o que
                // estiver la por "application/json" e a resposta sairia com o
                // content-type errado — corpo certo, cabecalho errado. Achado pelo
                // verificador (verificar-limite-de-login.cs, cenario 2b).
                await contexto.HttpContext.Response.WriteAsJsonAsync(
                    problema, options: null, contentType: "application/problem+json", ct);
            };
        });
}
