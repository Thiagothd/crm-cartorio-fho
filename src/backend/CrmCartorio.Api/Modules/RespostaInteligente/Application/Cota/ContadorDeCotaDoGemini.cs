using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.RespostaInteligente.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.RespostaInteligente.Application.Cota;

public sealed record DecisaoDeCota(bool Permitido, string? MotivoDoBloqueio)
{
    public static DecisaoDeCota Permite() => new(true, null);
    public static DecisaoDeCota Bloqueia(string motivo) => new(false, motivo);
}

/// <summary>
/// Controle preventivo de RPM/TPM/RPD do Gemini — <b>global do projeto Google</b>,
/// não por tenant, porque é assim que o Google conta a franquia. Um <c>lock</c>
/// único serializa toda decisão: é uma reserva atômica (incrementa antes de
/// chamar), não uma leitura seguida de decisão separada — a mesma race condition
/// que hoje existe em <c>LimiteDiario</c> não se repete aqui.
/// </summary>
/// <remarks>
/// Singleton. Hidrata os contadores de <see cref="Domain.Entities.ChamadaDeIa"/>
/// na primeira vez que uma <c>ChaveDeProjeto</c> é vista neste processo, para que
/// uma reciclagem do App Pool não reabra a franquia do zero no meio do dia.
///
/// Quando <see cref="OpcoesDoGemini.LimiteRpm"/>/<c>LimiteTpm</c>/<c>LimiteRpd</c>
/// estão todos em zero (não cadastrados — o Google não publica mais esses valores
/// na documentação), o controle por percentual fica desligado de propósito: não
/// existe "85% de uma cota desconhecida". Em vez disso, aplica só um freio local
/// conservador (<see cref="OpcoesDoGemini.LimiteRpmDeDescoberta"/>) e deixa a
/// camada reativa (<see cref="DisjuntorDoGemini"/>, acionada por 429 real) ser a
/// autoridade de verdade.
/// </remarks>
public class ContadorDeCotaDoGemini(IServiceScopeFactory scopeFactory)
{
    private readonly SemaphoreSlim _semaforo = new(1, 1);
    private readonly Dictionary<string, EstadoDoProjeto> _porProjeto = new();

    private sealed class EstadoDoProjeto
    {
        public bool Hidratado;
        public readonly List<(DateTime Instante, int Tokens)> JanelaDeUmMinuto = [];
        public DateOnly DiaPacifico;
        public int RequisicoesHoje;
        public DateTime UltimaChamadaDeDescoberta = DateTime.MinValue;

        /// <summary>Observado no último 429 de quota diária — sugestão, nunca valor confirmado.</summary>
        public int? SugestaoDeLimiteRpd;
    }

    public async Task<DecisaoDeCota> AvaliarEReservarAsync(string chaveDeProjeto, OpcoesDoGemini opcoes, CancellationToken ct)
    {
        await _semaforo.WaitAsync(ct);

        try
        {
            var estado = await ObterOuHidratarAsync(chaveDeProjeto, ct);
            PurgarJanela(estado);
            RenovarDiaSeNecessario(estado);

            var limitesCadastrados = opcoes.LimiteRpm > 0 || opcoes.LimiteTpm > 0 || opcoes.LimiteRpd > 0;

            if (!limitesCadastrados)
            {
                var intervaloMinimo = TimeSpan.FromMinutes(1) / Math.Max(1, opcoes.LimiteRpmDeDescoberta);

                if (DateTime.UtcNow - estado.UltimaChamadaDeDescoberta < intervaloMinimo)
                {
                    return DecisaoDeCota.Bloqueia(
                        "Franquia não cadastrada — freio local de descoberta ativo.");
                }

                estado.UltimaChamadaDeDescoberta = DateTime.UtcNow;
                estado.RequisicoesHoje++;
                estado.JanelaDeUmMinuto.Add((DateTime.UtcNow, 0));
                return DecisaoDeCota.Permite();
            }

            var margem = Math.Clamp(opcoes.PercentualDeSeguranca, 1, 100) / 100.0;

            if (opcoes.LimiteRpd > 0 && estado.RequisicoesHoje >= opcoes.LimiteRpd * margem)
            {
                return DecisaoDeCota.Bloqueia(
                    $"Margem de segurança do RPD atingida ({estado.RequisicoesHoje}/{opcoes.LimiteRpd}).");
            }

            if (opcoes.LimiteRpm > 0 && estado.JanelaDeUmMinuto.Count >= opcoes.LimiteRpm * margem)
            {
                return DecisaoDeCota.Bloqueia("Margem de segurança do RPM atingida.");
            }

            var tokensNaJanela = estado.JanelaDeUmMinuto.Sum(item => item.Tokens);

            if (opcoes.LimiteTpm > 0 && tokensNaJanela >= opcoes.LimiteTpm * margem)
            {
                return DecisaoDeCota.Bloqueia("Margem de segurança do TPM atingida.");
            }

            estado.RequisicoesHoje++;
            estado.JanelaDeUmMinuto.Add((DateTime.UtcNow, 0));
            return DecisaoDeCota.Permite();
        }
        finally
        {
            _semaforo.Release();
        }
    }

    /// <summary>Ajusta a reserva feita em <see cref="AvaliarEReservarAsync"/> com o consumo real de tokens da chamada que acabou de terminar com sucesso.</summary>
    public async Task RegistrarTokensReaisAsync(string chaveDeProjeto, int tokensTotal, CancellationToken ct)
    {
        await _semaforo.WaitAsync(ct);

        try
        {
            if (!_porProjeto.TryGetValue(chaveDeProjeto, out var estado) || estado.JanelaDeUmMinuto.Count == 0)
            {
                return;
            }

            var ultimo = estado.JanelaDeUmMinuto[^1];
            estado.JanelaDeUmMinuto[^1] = (ultimo.Instante, tokensTotal);
        }
        finally
        {
            _semaforo.Release();
        }
    }

    /// <summary>Chamado quando o Gemini devolve 429 de quota diária — vira sugestão de <see cref="OpcoesDoGemini.LimiteRpd"/> no painel, nunca valor confirmado.</summary>
    public async Task RegistrarEstouroDeQuotaDiariaAsync(string chaveDeProjeto, CancellationToken ct)
    {
        await _semaforo.WaitAsync(ct);

        try
        {
            var estado = await ObterOuHidratarAsync(chaveDeProjeto, ct);
            RenovarDiaSeNecessario(estado);
            estado.SugestaoDeLimiteRpd = estado.RequisicoesHoje;
        }
        finally
        {
            _semaforo.Release();
        }
    }

    public async Task<SituacaoDaCota> ConsultarAsync(string chaveDeProjeto, OpcoesDoGemini opcoes, CancellationToken ct)
    {
        await _semaforo.WaitAsync(ct);

        try
        {
            var estado = await ObterOuHidratarAsync(chaveDeProjeto, ct);
            PurgarJanela(estado);
            RenovarDiaSeNecessario(estado);

            var limitesCadastrados = opcoes.LimiteRpm > 0 || opcoes.LimiteTpm > 0 || opcoes.LimiteRpd > 0;

            return new SituacaoDaCota(
                LimitesCadastrados: limitesCadastrados,
                RequisicoesHoje: estado.RequisicoesHoje,
                RequisicoesNaJanela: estado.JanelaDeUmMinuto.Count,
                TokensNaJanela: estado.JanelaDeUmMinuto.Sum(item => item.Tokens),
                SugestaoDeLimiteRpd: estado.SugestaoDeLimiteRpd);
        }
        finally
        {
            _semaforo.Release();
        }
    }

    private async Task<EstadoDoProjeto> ObterOuHidratarAsync(string chaveDeProjeto, CancellationToken ct)
    {
        if (_porProjeto.TryGetValue(chaveDeProjeto, out var existente))
        {
            return existente;
        }

        var estado = new EstadoDoProjeto { DiaPacifico = RelogioPacifico.DiaAtual() };
        _porProjeto[chaveDeProjeto] = estado;

        // Hidratacao unica por projeto por processo — sobrevive a reciclagem do
        // App Pool sem reabrir a franquia do dia do zero. Falha em ler o banco
        // aqui nao pode impedir a IA de funcionar: comeca do zero, na pior das
        // hipoteses subestimando o quanto ja foi usado hoje.
        try
        {
            using var escopo = scopeFactory.CreateScope();
            var db = escopo.ServiceProvider.GetRequiredService<CrmDbContext>();

            var inicioDoDiaPacifico = RelogioPacifico.DiaAtual();
            var inicioDaJanela = DateTime.UtcNow.AddMinutes(-1);

            var chamadasDeHoje = await db.ChamadasDeIa.AsNoTracking()
                .Where(c => c.ChaveDeProjeto == chaveDeProjeto
                         && c.Provedor == ProvedorDeIa.Gemini
                         && c.IniciadaEm >= DateTime.UtcNow.Date)
                .Select(c => new { c.IniciadaEm, c.TokensEntrada, c.TokensSaida, c.TokensPensamento })
                .ToListAsync(ct);

            estado.RequisicoesHoje = chamadasDeHoje.Count(c => RelogioPacifico.DiaDoInstante(c.IniciadaEm) == inicioDoDiaPacifico);

            foreach (var chamada in chamadasDeHoje.Where(c => c.IniciadaEm >= inicioDaJanela))
            {
                estado.JanelaDeUmMinuto.Add((chamada.IniciadaEm, chamada.TokensEntrada + chamada.TokensSaida + chamada.TokensPensamento));
            }
        }
        catch (Exception)
        {
            // Sem log aqui de proposito: quem chama esta dentro do semaforo, e um
            // ILogger nao e essencial para a decisao. O comportamento seguro
            // (comecar do zero) ja e a rede de seguranca.
        }

        estado.Hidratado = true;
        return estado;
    }

    private static void PurgarJanela(EstadoDoProjeto estado)
    {
        var limite = DateTime.UtcNow.AddMinutes(-1);
        estado.JanelaDeUmMinuto.RemoveAll(item => item.Instante < limite);
    }

    private static void RenovarDiaSeNecessario(EstadoDoProjeto estado)
    {
        var diaAtual = RelogioPacifico.DiaAtual();

        if (estado.DiaPacifico != diaAtual)
        {
            estado.DiaPacifico = diaAtual;
            estado.RequisicoesHoje = 0;
            estado.SugestaoDeLimiteRpd = null;
        }
    }
}

public sealed record SituacaoDaCota(
    bool LimitesCadastrados,
    int RequisicoesHoje,
    int RequisicoesNaJanela,
    int TokensNaJanela,
    int? SugestaoDeLimiteRpd);
