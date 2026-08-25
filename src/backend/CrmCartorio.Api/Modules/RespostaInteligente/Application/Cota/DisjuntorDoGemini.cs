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
public class DisjuntorDoGemini
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
    }

    /// <summary>
    /// Chamado ANTES de decidir tentar o Gemini. <paramref name="ehSonda"/> vem
    /// verdadeiro no máximo uma vez por ciclo de abertura — enquanto a sonda está
    /// em curso, novas chamadas devolvem falso mesmo com o cooldown já vencido.
    /// </summary>
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
                    ehSonda = true;
                    return true;

                case EstadoDoDisjuntor.MeioAberto when estado.SondaEmAndamento:
                    ehSonda = false;
                    return false;

                case EstadoDoDisjuntor.MeioAberto:
                    // Estado inconsistente (sonda anterior nem fechou nem reabriu
                    // — não deveria acontecer, mas não trava o disjuntor para sempre).
                    estado.SondaEmAndamento = true;
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
