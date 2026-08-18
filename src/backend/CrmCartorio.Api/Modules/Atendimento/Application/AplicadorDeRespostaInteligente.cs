using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Atendimento.Domain;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Enums;
using CrmCartorio.Api.Modules.RespostaInteligente.Application;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <summary>
/// Tenta responder com a IA antes de escalar para atendimento humano, quando o
/// cliente escreve algo que não bate com o menu de triagem — e continua tentando
/// nas mensagens seguintes, enquanto ela conseguir ajudar.
/// </summary>
/// <remarks>
/// Mesma forma de <see cref="AplicadorDeTriagem"/> e pelo mesmo motivo: ctx-free,
/// porque o webhook do WhatsApp real chama isso sem token nenhum, e é chamado dos
/// dois lugares que recebem mensagem de cliente (simulação do painel e webhook)
/// — tanto na primeira tentativa (via <see cref="AplicadorDeTriagem"/>, quando o
/// menu não bate) quanto nas trocas seguintes, enquanto
/// <see cref="Conversa.AguardandoRespostaInteligente"/> estiver ligado.
///
/// <b>Escala SÓ por pedido explícito ou porque a IA não sabe — nunca por
/// contagem de trocas.</b> Decisão do Thiago em 2026-08-17, depois de ver a IA
/// responder certo e no mesmo fôlego disparar "um atendente vai continuar": um
/// corte por número de mensagens interrompe conversa que estava indo bem só
/// porque bateu um número redondo. Contenção de custo é papel do
/// <c>LimiteDiario</c> (teto por tenant, não por conversa), não deste contador.
/// </remarks>
public static class AplicadorDeRespostaInteligente
{
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
        if (TriagemAutomatica.PediuHumano(conteudo))
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

        conversa.AguardandoRespostaInteligente = true;

        log.LogInformation(
            "Resposta inteligente respondeu na conversa {ConversaId} (tentativa {Tentativa}).",
            conversa.Id, conversa.TentativasRespostaInteligente);
    }

    /// <remarks>
    /// O gatilho depende de ONDE a conversa esta: sem setor ainda, e o primeiro
    /// contato de sempre (<see cref="GatilhoDeMensagem.TriagemSemCorrespondencia"/>,
    /// com "obrigado pelo contato" e o protocolo como novidade). Com setor ja
    /// definido, o cliente esta EM ATENDIMENTO — repetir a fala de primeiro contato
    /// soa como se o sistema tivesse esquecido quem ele e (achado ao vivo,
    /// 2026-08-17). <see cref="GatilhoDeMensagem.PedidoDeAtendente"/> e o texto
    /// certo para esse segundo caso.
    /// </remarks>
    private static async Task EscalarAsync(
        CrmDbContext db, IDisparadorDeMensagemAutomatica automaticas, Conversa conversa, CancellationToken ct)
    {
        conversa.AguardandoRespostaInteligente = false;
        conversa.TentativasRespostaInteligente = 0;

        var gatilho = conversa.SetorId is null
            ? GatilhoDeMensagem.TriagemSemCorrespondencia
            : GatilhoDeMensagem.PedidoDeAtendente;

        await automaticas.EnfileirarAsync(conversa, gatilho, ct: ct);
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
}
