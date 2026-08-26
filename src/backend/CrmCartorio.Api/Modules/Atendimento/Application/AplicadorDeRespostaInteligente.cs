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
/// <b>Escala para atendente SÓ por pedido explícito do cliente — nunca por
/// contagem de trocas, nunca só porque a IA não sabia responder desta vez.</b>
/// Decisão do Thiago em 2026-08-17 (contagem) e reforçada em 2026-08-18, depois
/// de dois clientes reais no mesmo dia receberem "um atendente vai continuar"
/// sem terem pedido nada — um disse só "somente" (queria dizer "só isso,
/// obrigado"), outra perguntou algo que a IA não sabia. Quando
/// `podeResponder=false` e não houve pedido explícito, o gatilho é
/// <see cref="GatilhoDeMensagem.RespostaSemCorrespondencia"/> — nunca promete
/// atendente, convida a pedir um se quiser, e a conversa continua com a IA
/// ouvindo (<see cref="Conversa.AguardandoRespostaInteligente"/> permanece
/// ligada). Contenção de custo é papel do <c>LimiteDiario</c> (teto por tenant),
/// não deste caminho.
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
        CancellationToken ct,
        Guid? mensagemAtualId = null)
    {
        if (TriagemAutomatica.PediuHumano(conteudo))
        {
            log.LogInformation(
                "Conversa {ConversaId} pediu atendimento humano explicitamente.", conversa.Id);

            await EscalarAsync(db, automaticas, conversa, ct);
            return;
        }

        var historico = await MontarHistoricoAsync(db, conversa.Id, mensagemAtualId, ct);
        var resposta = await ia.ResponderAsync(conversa.TenantId, historico, conteudo, conversa.Id, ct);

        // BUG-ATD-01, corrida B/G do plano: a chamada acima pode levar
        // segundos (rede) — tempo suficiente para um atendente assumir
        // enquanto a IA ainda esta "em voo". Sem este recheck, a resposta
        // gerada ANTES do takeover ainda seria entregue ao cliente por cima
        // do atendente. Leitura fresca (fora do change tracker) do mesmo
        // campo que o despacho externo ja consultou no inicio deste request —
        // se mudou nesse meio tempo, descarta a resposta e nao faz nada.
        //
        // Janela residual, aceita de proposito: entre esta leitura e o commit
        // final de quem chamou sobra uma janela de milissegundos, so operacao
        // local, sem chamada de rede — fechar por completo exigiria segurar
        // lock pelo tempo do timeout do provedor de IA (pior para throughput)
        // ou separar a persistencia da automacao em duas transacoes, fora do
        // escopo desta correcao.
        var conversaFoiAssumidaEnquantoRespondia = await db.Conversas.AsNoTracking()
            .Where(c => c.Id == conversa.Id)
            .Select(c => c.EmAtendimentoHumano)
            .SingleAsync(ct);

        if (conversaFoiAssumidaEnquantoRespondia)
        {
            log.LogInformation(
                "Conversa {ConversaId} entrou em atendimento humano enquanto a IA respondia; resposta descartada.",
                conversa.Id);
            return;
        }

        if (!resposta.PodeResponder)
        {
            log.LogInformation(
                "Resposta inteligente da conversa {ConversaId} nao encontrou resposta especifica: " +
                "{Motivo}. Cliente nao pediu atendente — nao escala.",
                conversa.Id, resposta.Motivo);

            await automaticas.EnfileirarAsync(conversa, GatilhoDeMensagem.RespostaSemCorrespondencia, ct: ct);

            // Ao contrario de EscalarAsync: a IA nao desiste da conversa so
            // porque nao teve o que dizer desta vez. Continua ouvindo.
            conversa.AguardandoRespostaInteligente = true;
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

        // O cliente pediu humano AGORA — a fila humana comeca aqui, antes de
        // qualquer atendente clicar em Assumir. BUG-ATD-01: sem isto, uma
        // mensagem que chegasse enquanto a conversa ainda esta na fila (sem
        // atendente atribuido) continuaria caindo na automacao.
        conversa.EmAtendimentoHumano = true;

        var gatilho = conversa.SetorId is null
            ? GatilhoDeMensagem.TriagemSemCorrespondencia
            : GatilhoDeMensagem.PedidoDeAtendente;

        await automaticas.EnfileirarAsync(conversa, gatilho, ct: ct);
    }

    /// <summary>
    /// Reconstrói as trocas já feitas nesta sequência a partir do banco.
    /// </summary>
    /// <remarks>
    /// No webhook, a mensagem que provocou a chamada ja foi salva para reivindicar
    /// atomicamente o IdExterno; <paramref name="mensagemAtualId"/> a exclui do
    /// historico porque ela ja segue separada como a pergunta atual. Na simulacao
    /// pelo painel ela ainda nao foi salva e o parametro fica nulo.
    ///
    /// Exclui a primeiríssima mensagem do cliente na conversa (o "bom dia" que abriu
    /// o atendimento e disparou as boas-vindas) — ela nunca passou pela IA. As
    /// demais mensagens de cliente, mais as respostas com <c>GeradaPorIa</c>, são
    /// exatamente esta sequência: nenhuma outra combinação de autor gera esse par.
    /// </remarks>
    private static async Task<IReadOnlyList<TrocaAnterior>> MontarHistoricoAsync(
        CrmDbContext db, Guid conversaId, Guid? mensagemAtualId, CancellationToken ct)
    {
        var primeiraMensagemClienteId = await db.Mensagens.AsNoTracking()
            .Where(m => m.ConversaId == conversaId && m.AutorTipo == AutorTipo.Cliente)
            .OrderBy(m => m.EnviadaEm).ThenBy(m => m.Id)
            .Select(m => (Guid?)m.Id)
            .FirstOrDefaultAsync(ct);

        var mensagens = await db.Mensagens.AsNoTracking()
            .Where(m => m.ConversaId == conversaId
                     && m.Id != mensagemAtualId
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
