using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CrmCartorio.Api.Infrastructure.Security;

/// <summary>
/// SEC-02/SEC-03: limites de tentativas por IP nas rotas de autenticacao — o
/// amortecedor raso da protecao contra forca bruta.
/// </summary>
/// <remarks>
/// <b>Nao e a protecao que importa.</b> A que de fato para um ataque e o
/// backoff por CONTA, persistido no banco (<c>AutenticacaoService</c> para
/// senha e MFA; <c>MfaService</c> para step-up). Este limite existe so para que
/// um script obvio martelando um endpoint pare cedo, antes de gastar PBKDF2,
/// TOTP ou uma ida ao banco por tentativa.
///
/// Vale por PROCESSO, nao pelo cluster. Hoje isso nao e uma lacuna real: os dois
/// App Pools rodam com <c>Maximum Worker Processes = 1</c> (docs/INFRAESTRUTURA.md,
/// secao 4.1), entao ha um processo por ambiente. Se algum dia virar web garden ou
/// mais de uma instancia, esta camada dilui na mesma proporcao — e a de conta, que
/// e a exata, continua exata.
///
/// A configuracao mora aqui, e nao solta no <c>Program.cs</c>, para que o
/// verificador exercite exatamente esta configuracao, e nao uma copia dela.
/// </remarks>
public static class PoliticaDeLimiteDeLogin
{
    /// <summary>Etapa 1 do login (senha).</summary>
    public const string Nome = "login";

    /// <summary>Etapa 2 do login (codigo de segundo fator) — janela propria, mais generosa: quem chega aqui ja provou a senha.</summary>
    public const string NomeMfa = "login-mfa";

    /// <summary>Gestao de MFA (iniciar, confirmar, desativar, codigos, reset, ticket, politica) — todas exigem step-up, entao o limite aqui e so o mesmo amortecedor de sempre.</summary>
    public const string NomeGestao = "mfa-gestao";

    public const int TentativasPorMinuto = 10;
    public const int TentativasDeMfaPorMinuto = 20;
    public const int TentativasDeGestaoPorMinuto = 10;

    public static IServiceCollection AdicionarLimiteDeLogin(this IServiceCollection servicos) =>
        servicos.AddRateLimiter(opcoes =>
        {
            AdicionarJanelaFixaPorIp(opcoes, Nome, TentativasPorMinuto);
            AdicionarJanelaFixaPorIp(opcoes, NomeMfa, TentativasDeMfaPorMinuto);
            AdicionarJanelaFixaPorIp(opcoes, NomeGestao, TentativasDeGestaoPorMinuto);

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
                    Title = "Muitas tentativas. Aguarde um instante e tente de novo.",
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

    private static void AdicionarJanelaFixaPorIp(RateLimiterOptions opcoes, string nome, int limite) =>
        opcoes.AddPolicy(nome, http => RateLimitPartition.GetFixedWindowLimiter(
            // Nunca o texto cru de um cabecalho: ver ResolvedorDeIpDoCliente.
            // Cada politica nomeada ja tem seu proprio limiter interno no
            // ASP.NET Core; o prefixo do nome aqui e so defensivo/de leitura —
            // deixa a chave auto-explicativa em qualquer log ou metrica futura.
            partitionKey: $"{nome}:{ResolvedorDeIpDoCliente.Resolver(http)}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limite,
                Window = TimeSpan.FromMinutes(1),

                // Nenhuma destas rotas entra em fila: quem passou do limite
                // recebe 429 na hora. Enfileirar deixaria a resposta lenta sem
                // informar nada a quem esperou, e prenderia conexao do servidor
                // durante um ataque.
                QueueLimit = 0
            }));
}
