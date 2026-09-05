using CrmCartorio.Api.Modules.RespostaInteligente.Domain.Enums;

namespace CrmCartorio.Api.Modules.RespostaInteligente.Application.Cota;

public enum EstadoDoDisjuntor
{
    Fechado,
    Aberto,
    MeioAberto,
}

public sealed record SituacaoDoDisjuntor(EstadoDoDisjuntor Estado, DateTime? VoltaATentarEm);

/// <summary>
/// Controle reativo de disponibilidade do Gemini — Fechado → Aberto → Meio-Aberto,
/// global por projeto (mesmo particionamento de <see cref="ContadorDeCotaDoGemini"/>).
/// É a autoridade final: mesmo quando os limites de RPM/TPM/RPD não estão
/// cadastrados, um 429 real ainda abre o circuito.
/// </summary>
/// <remarks>
/// Singleton. <see cref="PodeTentar"/> escolhe no máximo UMA sonda por vez no
/// meio-aberto — as demais mensagens continuam no Haiku sem esperar o resultado
/// da sonda, para não acumular latência em cima de um provedor que pode ainda
/// estar fora do ar.
/// </remarks>
public class DisjuntorDoGemini(TimeSpan? tempoMaximoDeSonda = null)
{
    private readonly object _lock = new();
    private readonly Dictionary<string, EstadoInterno> _porProjeto = new();

    private sealed class EstadoInterno
    {
        public EstadoDoDisjuntor Estado = EstadoDoDisjuntor.Fechado;
        public int FalhasConsecutivas;
        public DateTime AbertoAte = DateTime.MinValue;
        public TimeSpan CooldownAtual = TimeSpan.Zero;
        public bool SondaEmAndamento;
        public DateTime SondaIniciadaEm = DateTime.MinValue;
    }

    /// <summary>
    /// Default de produção de <see cref="_tempoMaximoDeSonda"/> — exposto como
    /// público só para permitir uma checagem de sanidade na configuração do
    /// Gemini no startup (ver <c>Program.cs</c>), sem duplicar o número "60" em
    /// dois lugares.
    /// </summary>
    public static readonly TimeSpan TempoMaximoDeSondaPadrao = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Teto de quanto tempo uma sonda pode ficar "em andamento" antes de ser
    /// considerada perdida. Não depende do <c>TimeoutSegundos</c> configurado do
    /// Gemini de propósito: <see cref="PodeTentar"/> não tem acesso a
    /// <see cref="OpcoesDoGemini"/> (é chamado antes da decisão de cota), e este é
    /// só uma rede de segurança — bem acima de qualquer timeout HTTP razoável.
    /// Parâmetro de construtor (não constante) só para permitir um valor curto nos
    /// verificadores — produção usa sempre <see cref="TempoMaximoDeSondaPadrao"/>
    /// (registro no DI não passa nada).
    /// </summary>
    /// <remarks>
    /// PREMISSA: este valor precisa ser maior que o timeout HTTP efetivo do
    /// Gemini (<c>Math.Max(5, Gemini.TimeoutSegundos)</c>, aplicado em
    /// <c>ProvedorGemini.GerarAsync</c> via <c>CancelAfter</c>). Se
    /// <c>Gemini:TimeoutSegundos</c> for configurado igual ou acima deste valor,
    /// uma sonda lenta (mas NÃO cancelada) pode ainda estar em voo quando outra
    /// mensagem a trata como "perdida" e abre uma segunda sonda concorrente —
    /// risco de duas chamadas reais simultâneas ao Gemini, cada uma podendo
    /// decidir o estado final do disjuntor. <c>Program.cs</c> registra um aviso
    /// no boot se essa premissa for violada (não falha o startup — a IA já segue
    /// a política de nunca travar o sistema por configuração ruim; ver
    /// <see cref="OpcoesDaIA.EstaConfigurada"/>). Os defaults documentados
    /// (<c>TimeoutSegundos = 20</c>) ficam bem abaixo deste teto.
    /// </remarks>
    private readonly TimeSpan _tempoMaximoDeSonda = tempoMaximoDeSonda ?? TempoMaximoDeSondaPadrao;

    /// <summary>
    /// Chamado ANTES de decidir tentar o Gemini. <paramref name="ehSonda"/> vem
    /// verdadeiro no máximo uma vez por ciclo de abertura — enquanto a sonda está
    /// em curso, novas chamadas devolvem falso mesmo com o cooldown já vencido.
    /// </summary>
    /// <remarks>
    /// A sonda tem validade (<see cref="_tempoMaximoDeSonda"/>): sem isso, o
    /// cancelamento da requisição HTTP que carregava a sonda (ex.: o gateway do
    /// WhatsApp desiste, ou o cliente do webhook aborta) faz
    /// <see cref="OrquestradorDeProvedores"/> relançar a exceção de cancelamento
    /// ANTES de chamar <see cref="RegistrarSucesso"/>/<see cref="RegistrarFalha"/>
    /// — <c>SondaEmAndamento</c> nunca seria limpo, e o Gemini ficaria bloqueado
    /// para sempre (achado ao vivo, 2026-08-26). Uma sonda "perdida" expira e
    /// libera uma nova tentativa.
    /// </remarks>
    public bool PodeTentar(string chaveDeProjeto, out bool ehSonda)
    {
        lock (_lock)
        {
            var estado = ObterOuCriar(chaveDeProjeto);
            var agora = DateTime.UtcNow;

            switch (estado.Estado)
            {
                case EstadoDoDisjuntor.Fechado:
                    ehSonda = false;
                    return true;

                case EstadoDoDisjuntor.Aberto when agora < estado.AbertoAte:
                    ehSonda = false;
                    return false;

                case EstadoDoDisjuntor.Aberto:
                    // Cooldown vencido — a PRIMEIRA chamada a chegar aqui vira a
                    // sonda; a marca de sonda em andamento evita que duas
                    // mensagens simultâneas sondem ao mesmo tempo.
                    estado.Estado = EstadoDoDisjuntor.MeioAberto;
                    estado.SondaEmAndamento = true;
                    estado.SondaIniciadaEm = agora;
                    ehSonda = true;
                    return true;

                case EstadoDoDisjuntor.MeioAberto when estado.SondaEmAndamento
                    && agora - estado.SondaIniciadaEm < _tempoMaximoDeSonda:
                    ehSonda = false;
                    return false;

                case EstadoDoDisjuntor.MeioAberto:
                    // Ou nao havia sonda em andamento (estado inconsistente — sonda
                    // anterior nem fechou nem reabriu, nao deveria acontecer), ou
                    // havia uma e ela expirou sem nunca reportar sucesso/falha
                    // (cancelamento da requisicao que a carregava). Nos dois casos,
                    // esta chamada vira a nova sonda.
                    estado.SondaEmAndamento = true;
                    estado.SondaIniciadaEm = agora;
                    ehSonda = true;
                    return true;

                default:
                    ehSonda = false;
                    return false;
            }
        }
    }

    public void RegistrarSucesso(string chaveDeProjeto, bool eraSonda)
    {
        lock (_lock)
        {
            var estado = ObterOuCriar(chaveDeProjeto);
            estado.Estado = EstadoDoDisjuntor.Fechado;
            estado.FalhasConsecutivas = 0;
            estado.CooldownAtual = TimeSpan.Zero;
            estado.SondaEmAndamento = false;
        }
    }

    public void RegistrarFalha(
        string chaveDeProjeto,
        TipoDeFalhaDeIa falha,
        int? codigoHttp,
        TimeSpan? retryApos,
        bool eraSonda,
        OpcoesDoGemini opcoes)
    {
        lock (_lock)
        {
            var estado = ObterOuCriar(chaveDeProjeto);
            estado.SondaEmAndamento = false;

            if (falha == TipoDeFalhaDeIa.QuotaEsgotada)
            {
                estado.Estado = EstadoDoDisjuntor.Aberto;
                estado.AbertoAte = RelogioPacifico.ProximaMeiaNoiteUtc();
                estado.FalhasConsecutivas = 0;
                return;
            }

            var cooldownMaximo = TimeSpan.FromSeconds(Math.Max(1, opcoes.CooldownMaximoSegundos));

            // 429 de rate limit e 401/403 (credencial) abrem na hora — nao faz
            // sentido gastar mais tentativas para "confirmar" um problema que ja
            // e conclusivo. 401/403 usa o cooldown MAXIMO de proposito: chave
            // invalida nao se corrige sozinha em segundos.
            var credencialInvalida = falha == TipoDeFalhaDeIa.Configuracao && codigoHttp is 401 or 403;
            var aberturaImediata = falha == TipoDeFalhaDeIa.RateLimit || credencialInvalida || eraSonda;

            if (!aberturaImediata)
            {
                estado.FalhasConsecutivas++;

                if (estado.FalhasConsecutivas < Math.Max(1, opcoes.FalhasParaAbrirDisjuntor))
                {
                    return;
                }
            }

            var cooldownBase = credencialInvalida
                ? cooldownMaximo
                : estado.CooldownAtual == TimeSpan.Zero
                    ? TimeSpan.FromSeconds(Math.Max(1, opcoes.CooldownSegundos))
                    : estado.CooldownAtual * 2;

            var cooldown = cooldownBase > cooldownMaximo ? cooldownMaximo : cooldownBase;

            // O retryDelay do Google e so uma sugestao — nunca pode abrir o
            // disjuntor por mais tempo que o nosso proprio teto configurado.
            // Achado ao vivo em 2026-08-25: um parse de texto malformado (ou o
            // Google mandando um numero fora do esperado) gerou um retryApos de
            // "anos", e sem este clamp o disjuntor abriria (na pratica) para
            // sempre.
            var duracao = retryApos.HasValue && retryApos.Value > cooldown
                ? (retryApos.Value > cooldownMaximo ? cooldownMaximo : retryApos.Value)
                : cooldown;

            estado.Estado = EstadoDoDisjuntor.Aberto;
            estado.CooldownAtual = cooldown;
            estado.AbertoAte = DateTime.UtcNow + duracao;
            estado.FalhasConsecutivas = 0;
        }
    }

    public SituacaoDoDisjuntor Consultar(string chaveDeProjeto)
    {
        lock (_lock)
        {
            if (!_porProjeto.TryGetValue(chaveDeProjeto, out var estado))
            {
                return new SituacaoDoDisjuntor(EstadoDoDisjuntor.Fechado, null);
            }

            return new SituacaoDoDisjuntor(
                estado.Estado,
                estado.Estado != EstadoDoDisjuntor.Fechado ? estado.AbertoAte : null);
        }
    }

    private EstadoInterno ObterOuCriar(string chaveDeProjeto)
    {
        if (!_porProjeto.TryGetValue(chaveDeProjeto, out var estado))
        {
            estado = new EstadoInterno();
            _porProjeto[chaveDeProjeto] = estado;
        }

        return estado;
    }
}
