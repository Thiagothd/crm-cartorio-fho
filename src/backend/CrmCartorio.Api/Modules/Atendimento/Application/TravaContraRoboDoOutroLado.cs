using System.Text;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <summary>
/// Pausa o robô quando quem está do outro lado também é um robô — o atendimento
/// automático de uma empresa (banco, previdência, operadora) que caiu no número do
/// cartório.
/// </summary>
/// <remarks>
/// <para>
/// Pedido do cartório parceiro depois de 2026-10-22: a "Mila", robô da empresa externa, mandou
/// mensagem para o número do cartório, o menu do CRM respondeu, ela respondeu o
/// menu, e os dois ficaram conversando sozinhos. Além do fio sem sentido, é volume
/// de envio automático — o mesmo tipo de rajada que derrubou o chip em 2026-10-09.
/// </para>
/// <para>
/// Não há como saber pelo WhatsApp se o contato é uma pessoa. O que dá para ver é
/// o <b>jeito</b> da conversa nos últimos <see cref="Janela"/>. Cada sinal é
/// algo que uma pessoa de verdade quase nunca faz — e, quando faz, é porque está
/// perdida no menu, que é justamente quando um atendente deve assumir:
/// </para>
/// <list type="bullet">
///   <item>o contato manda o MESMO texto longo <see cref="RepeticoesQueIndicamLaco"/> vezes
///   (robô repetindo a própria mensagem);</item>
///   <item>o robô do CRM manda a MESMA mensagem <see cref="RepeticoesQueIndicamLaco"/> vezes
///   (o menu está andando em círculo);</item>
///   <item>rajada: <see cref="MaximoDeAutomaticas"/> envios automáticos com
///   <see cref="MinimoDeRecebidasNaRajada"/> mensagens do contato no mesmo intervalo.</item>
/// </list>
/// <para>
/// Respostas curtas ("1", "sim", "ok") nunca contam como repetição: é o jeito
/// normal de responder menu. O efeito é o mesmo da pausa manual
/// (<see cref="ConversaService.PausarRoboAsync"/>): a mensagem continua chegando ao
/// painel, nada mais sai sozinho, e o atendente liga o robô de novo se quiser.
/// O contato NÃO é marcado como "sempre sem robô" — se o sinal errou, a próxima
/// conversa dele volta com o robô normal.
/// </para>
/// </remarks>
public static class TravaContraRoboDoOutroLado
{
    public static readonly TimeSpan Janela = TimeSpan.FromMinutes(10);

    public const int RepeticoesQueIndicamLaco = 3;

    /// <summary>Abaixo disso é resposta de menu, não texto de robô.</summary>
    public const int TamanhoMinimoParaRepeticao = 20;

    public const int MaximoDeAutomaticas = 12;

    public const int MinimoDeRecebidasNaRajada = 6;

    /// <summary>Teto da leitura: um laço de verdade passa disso em minutos.</summary>
    private const int MensagensLidas = 200;

    /// <summary>
    /// Se a conversa parece robô contra robô, pausa o robô nela, registra o motivo no
    /// fio e devolve <c>true</c>. Não salva — quem chama salva junto com o resto do
    /// processamento da entrada.
    /// </summary>
    public static async Task<bool> PausarSeForRoboAsync(
        CrmDbContext db,
        Conversa conversa,
        DateTime agora,
        CancellationToken ct)
    {
        var desde = agora - Janela;

        var recentes = await db.Mensagens.AsNoTracking()
            .Where(m => m.TenantId == conversa.TenantId
                     && m.ConversaId == conversa.Id
                     && m.EnviadaEm >= desde)
            .OrderByDescending(m => m.EnviadaEm)
            .Take(MensagensLidas)
            .Select(m => new MensagemRecente(m.AutorTipo, m.EhAutomatica || m.GeradaPorIa, m.Conteudo))
            .ToListAsync(ct);

        var motivo = Avaliar(recentes);
        if (motivo is null)
        {
            return false;
        }

        conversa.RoboPausadoEm = agora;
        conversa.RoboPausadoPorAtendenteId = null;
        ConversaService.ZerarFluxoDoRobo(conversa);

        db.Mensagens.Add(new Mensagem
        {
            TenantId = conversa.TenantId,
            ConversaId = conversa.Id,
            AutorTipo = AutorTipo.Sistema,
            Conteudo = $"Robô pausado sozinho: o contato parece ser outro robô ({motivo}). " +
                       "Responda pelo painel, ou ligue o robô de novo se for uma pessoa.",
            Tipo = TipoMensagem.Texto,
            StatusEntrega = StatusEntrega.Entregue,
            EnviadaEm = agora,
            LidaEm = agora
        });

        return true;
    }

    /// <summary>
    /// A decisão, sem banco — é o que o verificador exercita direto. Recebe as
    /// mensagens da janela da MAIS NOVA para a mais antiga.
    /// </summary>
    /// <remarks>
    /// Só conta o que veio depois da última intervenção de gente: mensagem de
    /// atendente ou registro do sistema (robô religado, conversa assumida, a própria
    /// pausa). Sem isso, o atendente que religa o robô por ter visto que era uma
    /// pessoa veria a trava disparar de novo na mensagem seguinte, contando o laço
    /// que já foi resolvido.
    /// </remarks>
    public static string? Avaliar(IEnumerable<MensagemRecente> recentesDaMaisNova)
    {
        var desdeAUltimaIntervencao = recentesDaMaisNova.TakeWhile(m => !m.EhIntervencao).ToList();

        var doContato = desdeAUltimaIntervencao.Where(m => m.Autor == AutorTipo.Cliente).ToList();
        var automaticas = desdeAUltimaIntervencao.Where(m => m.Autor != AutorTipo.Cliente && m.Automatica).ToList();

        if (MaiorRepeticao(doContato) >= RepeticoesQueIndicamLaco)
        {
            return "repetiu a mesma mensagem várias vezes";
        }

        if (MaiorRepeticao(automaticas) >= RepeticoesQueIndicamLaco)
        {
            return "o robô mandou a mesma resposta várias vezes";
        }

        if (automaticas.Count >= MaximoDeAutomaticas && doContato.Count >= MinimoDeRecebidasNaRajada)
        {
            return $"{automaticas.Count} respostas automáticas em {Janela.TotalMinutes:0} minutos";
        }

        return null;
    }

    private static int MaiorRepeticao(IEnumerable<MensagemRecente> mensagens) =>
        mensagens
            .Select(m => Normalizar(m.Conteudo))
            .Where(t => t.Length >= TamanhoMinimoParaRepeticao)
            .GroupBy(t => t, StringComparer.Ordinal)
            .Select(g => g.Count())
            .DefaultIfEmpty(0)
            .Max();

    /// <summary>
    /// Minúsculas, sem dígitos e com espaço único: robô costuma repetir o texto com
    /// número de protocolo ou horário diferente a cada vez.
    /// </summary>
    private static string Normalizar(string conteudo)
    {
        var saida = new StringBuilder(conteudo.Length);
        var espacoPendente = false;

        foreach (var c in conteudo)
        {
            if (char.IsDigit(c))
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                espacoPendente = saida.Length > 0;
                continue;
            }

            if (espacoPendente)
            {
                saida.Append(' ');
                espacoPendente = false;
            }

            saida.Append(char.ToLowerInvariant(c));
        }

        return saida.ToString();
    }

    /// <summary>
    /// Começo dos registros de pausa e retomada do robô — os de
    /// <see cref="ConversaService.PausarRoboAsync"/>, <see cref="ConversaService.RetomarRoboAsync"/>
    /// e o desta trava. As outras notas do sistema ("Triagem automatica encaminhou…")
    /// são escritas pelo próprio robô e não reiniciam a contagem.
    /// </summary>
    private static readonly string[] RegistrosDeIntervencao = ["Robô pausado", "Robô ligado de novo"];

    public sealed record MensagemRecente(AutorTipo Autor, bool Automatica, string Conteudo)
    {
        /// <summary>Atendente falou, ou alguém pausou/religou o robô.</summary>
        public bool EhIntervencao =>
            Autor == AutorTipo.Atendente
            || (Autor == AutorTipo.Sistema
                && !Automatica
                && RegistrosDeIntervencao.Any(r => Conteudo.StartsWith(r, StringComparison.Ordinal)));
    }
}
