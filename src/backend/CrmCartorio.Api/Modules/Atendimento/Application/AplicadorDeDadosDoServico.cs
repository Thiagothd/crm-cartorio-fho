using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Enums;
using CrmCartorio.Api.Modules.Servicos.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <summary>
/// A sequência dos serviços que pedem dados e respondem com instrução — pedido pelo
/// cartório parceiro em 2026-09-29 para as segundas vias de certidão: o cliente manda os dados,
/// o robô manda a chave Pix, o cliente manda o comprovante e uma pessoa assume.
/// </summary>
/// <remarks>
/// NADA DAQUI É ESPECÍFICO DE PIX
///     Os textos são o <c>TipoServico.MensagemAposDados</c> e o
///     <c>TipoServico.MensagemAposComprovante</c> que o cartório cadastrou. O robô
///     só sabe a ordem: dados → texto → comprovante → atendente.
///
/// OS DADOS PODEM VIR EM VÁRIAS MENSAGENS (desde 2026-09-29)
///     A mensagem de dados não é respondida na hora: ela só marca
///     <c>Conversa.DadosDoServicoRecebidosEm</c>. A instrução sai quando o cliente
///     para de digitar, pela varredura (<see cref="VarredorDeDadosDoServico"/>),
///     que chama <see cref="ResponderAosDadosAsync"/>. Responder à primeira mensagem
///     mandava o Pix no meio da digitação, e o CPF que vinha depois era tomado como
///     comprovante.
///
/// QUALQUER RESPOSTA CONTA
///     Não há como o robô saber se "Maria da Silva, 12/03/1990" são dados completos, nem
///     se uma foto é mesmo um comprovante. Tentar adivinhar deixaria o cliente preso num
///     laço de "não entendi". Cada passo avança com a próxima mensagem, e quem confere
///     tudo no fim é o atendente.
///
/// SE MEXER AQUI, as duas cadeias de despacho chamam este aplicador: a do webhook
/// (<see cref="ProcessadorDeEntradaWhatsapp"/>) e a da simulação pelo painel
/// (<see cref="MensagemService"/>).
/// </remarks>
public static class AplicadorDeDadosDoServico
{
    public static async Task AplicarAsync(
        CrmDbContext db,
        IDisparadorDeMensagemAutomatica automaticas,
        Conversa conversa,
        DateTime agora,
        ILogger log,
        CancellationToken ct)
    {
        if (conversa.AguardandoDadosDoServico)
        {
            // Só a primeira mensagem marca e anota. As seguintes não mudam nada aqui:
            // cada uma já empurra UltimaMensagemEm, que é o relógio do silêncio.
            if (conversa.DadosDoServicoRecebidosEm is null)
            {
                conversa.DadosDoServicoRecebidosEm = agora;
                db.Mensagens.Add(Anotacao(conversa, agora,
                    "Cliente começou a enviar os dados do serviço; o robô responde quando ele terminar."));
            }

            return;
        }

        if (conversa.AguardandoComprovante)
        {
            conversa.AguardandoComprovante = false;

            var aviso = await TextoDoServicoAsync(db, conversa, t => t.MensagemAposComprovante, ct);
            var gerada = string.IsNullOrWhiteSpace(aviso)
                ? []
                : await automaticas.EnfileirarTextoAsync(conversa, aviso, "comprovante recebido", ct);

            // Sem texto próprio, o aviso de sempre da opção "falar com atendente".
            await PassarParaAtendenteAsync(db, automaticas, conversa, agora,
                "Cliente enviou o comprovante; atendimento passado para a equipe conferir.",
                avisar: gerada.Count == 0, ct);

            log.LogInformation("Conversa {ConversaId} enviou o comprovante e foi para a equipe.", conversa.Id);
        }
    }

    /// <summary>
    /// Manda a instrução (<c>MensagemAposDados</c>) e passa a esperar o comprovante.
    /// Chamado pela varredura quando o cliente parou de mandar os dados. Devolve as
    /// mensagens geradas para o cliente, para a varredura notificar o painel.
    /// </summary>
    public static async Task<IReadOnlyList<Mensagem>> ResponderAosDadosAsync(
        CrmDbContext db,
        IDisparadorDeMensagemAutomatica automaticas,
        Conversa conversa,
        DateTime agora,
        ILogger log,
        CancellationToken ct)
    {
        conversa.AguardandoDadosDoServico = false;
        conversa.DadosDoServicoRecebidosEm = null;

        var instrucao = await TextoDoServicoAsync(db, conversa, t => t.MensagemAposDados, ct);

        // O serviço pode ter sido trocado pelo painel, ou o texto apagado, enquanto
        // o cliente digitava. Sem instrução a dar, a única resposta honesta é
        // passar para uma pessoa.
        var gerada = string.IsNullOrWhiteSpace(instrucao)
            ? []
            : await automaticas.EnfileirarTextoAsync(conversa, instrucao, "dados do servico recebidos", ct);

        if (gerada.Count == 0)
        {
            return await PassarParaAtendenteAsync(db, automaticas, conversa, agora,
                "Cliente enviou os dados do serviço; atendimento passado para a equipe.", avisar: true, ct);
        }

        conversa.AguardandoComprovante = true;
        db.Mensagens.Add(Anotacao(conversa, agora,
            "Cliente enviou os dados do serviço; o robô respondeu com as instruções cadastradas e aguarda o comprovante."));

        log.LogInformation("Conversa {ConversaId} recebeu os dados do servico e aguarda o comprovante.", conversa.Id);
        return gerada;
    }

    private static async Task<string?> TextoDoServicoAsync(
        CrmDbContext db,
        Conversa conversa,
        System.Linq.Expressions.Expression<Func<TipoServico, string?>> campo,
        CancellationToken ct) =>
        conversa.TipoServicoId is { } servicoId
            ? await db.TiposServico.AsNoTracking()
                .Where(t => t.Id == servicoId && t.TenantId == conversa.TenantId)
                .Select(campo)
                .FirstOrDefaultAsync(ct)
            : null;

    /// <summary>Mesmo desfecho da opção "Falar com um atendente" do menu de continuação.</summary>
    private static async Task<IReadOnlyList<Mensagem>> PassarParaAtendenteAsync(
        CrmDbContext db,
        IDisparadorDeMensagemAutomatica automaticas,
        Conversa conversa,
        DateTime agora,
        string anotacao,
        bool avisar,
        CancellationToken ct)
    {
        var geradas = avisar
            ? await automaticas.EnfileirarAsync(conversa, GatilhoDeMensagem.PedidoDeAtendente, ct: ct)
            : [];
        conversa.EmAtendimentoHumano = true;
        db.Mensagens.Add(Anotacao(conversa, agora, anotacao));
        return geradas;
    }

    private static Mensagem Anotacao(Conversa conversa, DateTime agora, string texto) => new()
    {
        TenantId = conversa.TenantId,
        ConversaId = conversa.Id,
        AutorTipo = AutorTipo.Sistema,
        Conteudo = texto,
        Tipo = TipoMensagem.Texto,
        StatusEntrega = StatusEntrega.Entregue,
        EnviadaEm = agora,
        LidaEm = agora
    };
}
