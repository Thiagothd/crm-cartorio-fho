using CrmCartorio.Api.Modules.RespostaInteligente.Application;
using CrmCartorio.Api.Modules.RespostaInteligente.Application.Provedores;
using Microsoft.Extensions.Options;

namespace CrmCartorio.Api.Modules.RespostaInteligente.Infrastructure;

/// <summary>
/// Mantém viva a conexão HTTP com o Google, para que a primeira pergunta de um
/// cliente não pague a abertura de conexão.
/// </summary>
/// <remarks>
/// ACHADO AO VIVO, 2026-09-03: na telemetria de <c>ChamadaDeIa</c> do ambiente de
/// teste, a primeira chamada ao Gemini depois de um período ocioso estourava o
/// limite de 20s, enquanto as chamadas seguintes respondiam em ~2,4s. O cliente
/// esperava 20 segundos em silêncio antes de a contingência (Haiku, 1,5–3,7s) ser
/// sequer consultada — 22 a 25 segundos para uma pergunta trivial.
///
/// Num cartório o canal fica ocioso a maior parte do dia, então quase toda conversa
/// começava justamente nessa condição de conexão fria: o caso raro no papel era o
/// caso comum na prática.
///
/// <para><b>Custo zero de token e de cota.</b> A sondagem é um GET sem chave de API
/// — o Google responde 401/403 na hora, e é exatamente isso que se quer: o objetivo
/// é abrir DNS + TCP + TLS e deixar a conexão no pool, nunca gerar texto. Nenhuma
/// requisição de geração é feita aqui, então a franquia gratuita não é consumida.</para>
///
/// <para><b>O que isto NÃO resolve.</b> Se a lentidão da primeira chamada vier do
/// lado do Google (modelo frio, não conexão fria), aquecer a conexão não elimina o
/// problema — apenas o reduz. É por isso que o orçamento de latência curto em
/// <see cref="OpcoesDoGemini.TimeoutSegundos"/> continua sendo a proteção principal:
/// ele limita o prejuízo independentemente da causa. Medir <c>ChamadaDeIa</c> depois
/// de alguns dias é o que diz se ainda sobra latência a atacar.</para>
///
/// Falha aqui nunca interrompe nada: se a sondagem não funcionar, a única
/// consequência é a conexão continuar fria, exatamente como era antes.
/// </remarks>
public sealed class AquecedorDeConexaoDoGemini(
    IHttpClientFactory fabricaDeClientes,
    IOptions<OpcoesDaIA> opcoes,
    ILogger<AquecedorDeConexaoDoGemini> log) : BackgroundService
{
    /// <summary>
    /// Menor que o <c>PooledConnectionIdleTimeout</c> configurado no handler
    /// (5 min), para que cada sondagem ainda encontre a conexão anterior viva e a
    /// renove em vez de abrir uma nova.
    /// </summary>
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(4);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var configuracao = opcoes.Value.Gemini;

        if (!configuracao.Habilitado)
        {
            return;
        }

        log.LogInformation(
            "Aquecedor de conexao do Gemini ativo, a cada {Intervalo}.", Intervalo);

        while (!ct.IsCancellationRequested)
        {
            await SondarAsync(ct);

            try
            {
                await Task.Delay(Intervalo, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task SondarAsync(CancellationToken ct)
    {
        try
        {
            // Mesmo nome do cliente tipado registrado em Program.cs — e o que garante
            // que a conexao aquecida aqui seja a MESMA que ProvedorGemini vai reusar.
            var http = fabricaDeClientes.CreateClient(nameof(ProvedorGemini));

            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeSpan.FromSeconds(10));

            // Sem chave: 401/403 e resposta esperada e suficiente. Nao ha corpo a ler.
            using var resposta = await http.GetAsync(
                "models", HttpCompletionOption.ResponseHeadersRead, limite.Token);

            log.LogDebug(
                "Conexao com o Gemini aquecida (HTTP {Status}).", (int)resposta.StatusCode);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Desligando a aplicacao: silencio proposital.
        }
        catch (Exception erro)
        {
            // Nunca propaga: a pior consequencia possivel e a conexao seguir fria.
            log.LogDebug(erro, "Sondagem de aquecimento do Gemini falhou. Sem impacto no atendimento.");
        }
    }
}
