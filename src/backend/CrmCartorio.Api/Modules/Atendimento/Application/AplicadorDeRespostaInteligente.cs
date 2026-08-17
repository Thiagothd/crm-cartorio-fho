using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Enums;
using CrmCartorio.Api.Modules.RespostaInteligente.Application;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <summary>
/// Tenta responder com a IA antes de escalar para atendimento humano, quando o
/// cliente escreve algo que não bate com o menu de triagem — e continua tentando
/// nas mensagens seguintes, dentro do limite de trocas.
/// </summary>
/// <remarks>
/// Mesma forma de <see cref="AplicadorDeTriagem"/> e pelo mesmo motivo: ctx-free,
/// porque o webhook do WhatsApp real chama isso sem token nenhum, e é chamado dos
/// dois lugares que recebem mensagem de cliente (simulação do painel e webhook)
/// — tanto na primeira tentativa (via <see cref="AplicadorDeTriagem"/>, quando o
/// menu não bate) quanto nas trocas seguintes, enquanto
/// <see cref="Conversa.AguardandoRespostaInteligente"/> estiver ligado.
///
/// <b>Nunca deixa o cliente sem resposta.</b> IA desligada, sem responder, ou
/// respondendo "não sei" caem todos no MESMO encaminhamento humano de sempre
/// (<see cref="GatilhoDeMensagem.TriagemSemCorrespondencia"/>) — o mesmo princípio
/// de contingência do resto do sistema.
/// </remarks>
public static class AplicadorDeRespostaInteligente
{
    /// <summary>
    /// Palavras que fazem escalar direto para humano, sem gastar uma chamada à IA.
    /// Verificação determinística e barata antes de qualquer coisa — pedir para
    /// falar com gente é o único sinal que não vale a pena deixar o modelo julgar.
    /// </summary>
    private static readonly string[] PedidosDeHumano =
        ["atendente", "humano", "pessoa de verdade", "falar com alguem", "falar com alguém"];

    public static async Task AplicarAsync(
        CrmDbContext db,
        IRespostaInteligenteService ia,
        IDisparadorDeMensagemAutomatica automaticas,
        OpcoesDaIA opcoes,
        Conversa conversa,
        string conteudo,
        DateTime agora,
        ILogger log,
        CancellationToken ct)
    {
        if (PediuHumano(conteudo))
        {
            log.LogInformation(
                "Conversa {ConversaId} pediu atendimento humano explicitamente.", conversa.Id);

            await EscalarAsync(db, automaticas, conversa, ct);
            return;
        }

        var historico = await MontarHistoricoAsync(db, conversa.Id, ct);
        var resposta = await ia.ResponderAsync(conversa.TenantId, historico, conteudo, ct);

        if (!resposta.PodeResponder)
        {
            log.LogInformation(
                "Resposta inteligente da conversa {ConversaId} nao respondeu: {Motivo}",
                conversa.Id, resposta.Motivo);

            await EscalarAsync(db, automaticas, conversa, ct);
            return;
        }

        conversa.TentativasRespostaInteligente += 1;

        db.Mensagens.Add(new Mensagem
        {
            TenantId = conversa.TenantId,
            ConversaId = conversa.Id,
            AutorTipo = AutorTipo.Sistema,
            Conteudo = resposta.Resposta,
            Tipo = TipoMensagem.Texto,
            EhAutomatica = true,
            GeradaPorIa = true,

            // BUG encontrado ao vivo, 2026-08-17: estava Entregue, incondicional.
            // Mensagem endereçada ao cliente nasce Pendente — igual a
            // DisparadorDeMensagemAutomatica.EnfileirarAsync, e pelo mesmo motivo
            // (comentário lá: "Pendente é o único estado honesto"). Com Entregue
            // fixo, EnviadorPeloWhatsApp.EnviarPendentesAsync (que só recolhe
            // StatusEntrega == Pendente) nunca via esta mensagem, e a resposta da
            // IA ficava só no painel — nunca saía pelo WhatsApp de verdade.
            StatusEntrega = StatusEntrega.Pendente,
            EnviadaEm = agora

            // LidaEm fica NULO de propósito — mesma omissão de
            // DisparadorDeMensagemAutomatica. Para mensagem Sistema+EhAutomatica,
            // LidaEm significa "o WhatsApp do cliente confirmou leitura", e só o
            // webhook de status real preenche isso; gravar aqui mentiria que foi
            // lida antes mesmo de ter sido enviada.
        });

        if (conversa.TentativasRespostaInteligente >= opcoes.MaxTrocas)
        {
            log.LogInformation(
                "Resposta inteligente da conversa {ConversaId} atingiu o limite de {MaxTrocas} trocas.",
                conversa.Id, opcoes.MaxTrocas);

            await EscalarAsync(db, automaticas, conversa, ct);
            return;
        }

        conversa.AguardandoRespostaInteligente = true;

        log.LogInformation(
            "Resposta inteligente respondeu na conversa {ConversaId} (tentativa {Tentativa} de {MaxTrocas}).",
            conversa.Id, conversa.TentativasRespostaInteligente, opcoes.MaxTrocas);
    }

    private static async Task EscalarAsync(
        CrmDbContext db, IDisparadorDeMensagemAutomatica automaticas, Conversa conversa, CancellationToken ct)
    {
        conversa.AguardandoRespostaInteligente = false;
        conversa.TentativasRespostaInteligente = 0;

        await automaticas.EnfileirarAsync(conversa, GatilhoDeMensagem.TriagemSemCorrespondencia, ct: ct);
    }

    /// <summary>
    /// Reconstrói as trocas já feitas nesta sequência a partir do banco.
    /// </summary>
    /// <remarks>
    /// A mensagem do cliente que provocou esta chamada já foi adicionada ao
    /// <c>DbContext</c> por quem chamou, mas ainda não foi salva — uma consulta
    /// <c>AsNoTracking</c> não a enxerga, então o histórico aqui é sempre só o que
    /// veio ANTES dela, sem duplicar a pergunta atual.
    ///
    /// Exclui a primeiríssima mensagem do cliente na conversa (o "bom dia" que abriu
    /// o atendimento e disparou as boas-vindas) — ela nunca passou pela IA. As
    /// demais mensagens de cliente, mais as respostas com <c>GeradaPorIa</c>, são
    /// exatamente esta sequência: nenhuma outra combinação de autor gera esse par.
    /// </remarks>
    private static async Task<IReadOnlyList<TrocaAnterior>> MontarHistoricoAsync(
        CrmDbContext db, Guid conversaId, CancellationToken ct)
    {
        var primeiraMensagemClienteId = await db.Mensagens.AsNoTracking()
            .Where(m => m.ConversaId == conversaId && m.AutorTipo == AutorTipo.Cliente)
            .OrderBy(m => m.EnviadaEm).ThenBy(m => m.Id)
            .Select(m => (Guid?)m.Id)
            .FirstOrDefaultAsync(ct);

        var mensagens = await db.Mensagens.AsNoTracking()
            .Where(m => m.ConversaId == conversaId
                     && ((m.AutorTipo == AutorTipo.Cliente && m.Id != primeiraMensagemClienteId)
                         || (m.AutorTipo == AutorTipo.Sistema && m.GeradaPorIa)))
            .OrderBy(m => m.EnviadaEm).ThenBy(m => m.Id)
            .Select(m => new { m.AutorTipo, m.Conteudo })
            .ToListAsync(ct);

        return mensagens
            .Select(m => new TrocaAnterior(m.AutorTipo == AutorTipo.Cliente, m.Conteudo))
            .ToList();
    }

    private static bool PediuHumano(string conteudo)
    {
        var normalizado = conteudo.ToLowerInvariant();
        return PedidosDeHumano.Any(palavra => normalizado.Contains(palavra, StringComparison.Ordinal));
    }
}
