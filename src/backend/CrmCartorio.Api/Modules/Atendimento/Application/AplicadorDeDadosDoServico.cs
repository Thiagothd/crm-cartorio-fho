using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <summary>
/// A sequência dos serviços que pedem dados e respondem com instrução — pedido pelo
/// cartório parceiro em 2026-09-29 para as segundas vias de certidão: o cliente manda os dados,
/// o robô manda a chave Pix, o cliente manda o comprovante e uma pessoa assume.
/// </summary>
/// <remarks>
/// NADA DAQUI É ESPECÍFICO DE PIX
///     O texto é o <c>TipoServico.MensagemAposDados</c> que o cartório cadastrou. O robô
///     só sabe a ordem: dados → texto → comprovante → atendente.
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
            conversa.AguardandoDadosDoServico = false;

            var instrucao = conversa.TipoServicoId is { } servicoId
                ? await db.TiposServico.AsNoTracking()
                    .Where(t => t.Id == servicoId && t.TenantId == conversa.TenantId)
                    .Select(t => t.MensagemAposDados)
                    .FirstOrDefaultAsync(ct)
                : null;

            // O serviço pode ter sido trocado pelo painel, ou o texto apagado, enquanto
            // o cliente digitava. Sem instrução a dar, a única resposta honesta é
            // passar para uma pessoa.
            var gerada = string.IsNullOrWhiteSpace(instrucao)
                ? []
                : await automaticas.EnfileirarTextoAsync(conversa, instrucao, "dados do servico recebidos", ct);

            if (gerada.Count == 0)
            {
                await PassarParaAtendenteAsync(db, automaticas, conversa, agora,
                    "Cliente enviou os dados do serviço; atendimento passado para a equipe.", ct);
                return;
            }

            conversa.AguardandoComprovante = true;
            db.Mensagens.Add(Anotacao(conversa, agora,
                "Cliente enviou os dados do serviço; o robô respondeu com as instruções cadastradas e aguarda o comprovante."));

            log.LogInformation("Conversa {ConversaId} recebeu os dados do servico e aguarda o comprovante.", conversa.Id);
            return;
        }

        if (conversa.AguardandoComprovante)
        {
            conversa.AguardandoComprovante = false;
            await PassarParaAtendenteAsync(db, automaticas, conversa, agora,
                "Cliente enviou o comprovante; atendimento passado para a equipe conferir.", ct);

            log.LogInformation("Conversa {ConversaId} enviou o comprovante e foi para a equipe.", conversa.Id);
        }
    }

    /// <summary>Mesmo desfecho da opção "Falar com um atendente" do menu de continuação.</summary>
    private static async Task PassarParaAtendenteAsync(
        CrmDbContext db,
        IDisparadorDeMensagemAutomatica automaticas,
        Conversa conversa,
        DateTime agora,
        string anotacao,
        CancellationToken ct)
    {
        await automaticas.EnfileirarAsync(conversa, GatilhoDeMensagem.PedidoDeAtendente, ct: ct);
        conversa.EmAtendimentoHumano = true;
        db.Mensagens.Add(Anotacao(conversa, agora, anotacao));
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
