using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Atendimento.Domain;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Enums;
using CrmCartorio.Api.Modules.RespostaInteligente.Application;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <summary>
/// Depois do texto de orientação de um serviço (ex.: registro de nascimento), o robô
/// pergunta se ficou dúvida — pedido do Thiago em 2026-09-30, no teste do menu do cartório parceiro.
/// </summary>
/// <remarks>
/// O PROBLEMA QUE ISTO RESOLVE
///     Os serviços que só orientam (registro de nascimento, habilitação, óbito,
///     averbações — atos feitos no balcão) mandavam um texto longo e, logo depois, o
///     menu de continuação. O cliente lia, não escolhia nada, e a conversa acabava
///     encerrada por inatividade sem ninguém saber se ele entendeu.
///
/// COMO
///     <see cref="AplicadorDeEscolhaDeServico"/> manda o texto do serviço e o gatilho
///     <see cref="GatilhoDeMensagem.PerguntaDeDuvida"/>, e liga
///     <c>Conversa.AguardandoDuvidaDoServico</c>. A resposta do cliente, lida por
///     <see cref="TriagemAutomatica.LerRespostaDeDuvida"/>:
///       - sem dúvida ("1", "não", "obrigado"...) → despedida e encerra;
///       - quer atendente ("2", ou pedido explícito) → passa para a equipe;
///       - qualquer outra coisa é a dúvida → a IA responde, com o texto do serviço no
///         contexto (<c>MontadorDeContexto</c>), e a pergunta volta.
///     A pergunta volta a cada resposta da IA até o cliente dizer que está tudo certo
///     ou pedir uma pessoa. Se ele sumir, o encerramento por inatividade de sempre
///     fecha a conversa.
///
/// SE MEXER AQUI, as duas cadeias de despacho chamam este aplicador: a do webhook
/// (<see cref="ProcessadorDeEntradaWhatsapp"/>) e a da simulação pelo painel
/// (<see cref="MensagemService"/>).
/// </remarks>
public static class AplicadorDeDuvidaDoServico
{
    public static async Task AplicarAsync(
        CrmDbContext db,
        IRespostaInteligenteService ia,
        IDisparadorDeMensagemAutomatica automaticas,
        OpcoesDaIA opcoesDaIA,
        Conversa conversa,
        string conteudo,
        DateTime agora,
        ILogger log,
        CancellationToken ct,
        Guid? mensagemAtualId = null)
    {
        conversa.AguardandoDuvidaDoServico = false;

        switch (TriagemAutomatica.LerRespostaDeDuvida(conteudo))
        {
            case TriagemAutomatica.RespostaDeDuvida.SemDuvida:
                await automaticas.EnfileirarAsync(conversa, GatilhoDeMensagem.Despedida, ct: ct);
                AplicadorDePosAtendimento.EncerrarPeloCliente(
                    db, conversa, agora, "Encerrado pelo cliente, sem dúvidas depois da orientação do serviço.");

                log.LogInformation("Conversa {ConversaId} encerrada: cliente sem duvidas depois da orientacao.", conversa.Id);
                return;

            case TriagemAutomatica.RespostaDeDuvida.Atendente:
                await automaticas.EnfileirarAsync(conversa, GatilhoDeMensagem.PedidoDeAtendente, ct: ct);
                conversa.EmAtendimentoHumano = true;
                db.Mensagens.Add(Anotacao(conversa, agora,
                    "Cliente pediu atendente depois da orientação do serviço; atendimento passado para a equipe."));

                log.LogInformation("Conversa {ConversaId} pediu atendente depois da orientacao.", conversa.Id);
                return;
        }

        // A dúvida vai para a IA, que tem o texto do serviço no contexto. O que ela
        // decidir (responder, dizer que não sabe, ou escalar se o cliente pediu uma
        // pessoa com outras palavras) é o de sempre.
        var antes = RespostasNovas(db, conversa.Id);
        await AplicadorDeRespostaInteligente.AplicarAsync(
            db, ia, automaticas, opcoesDaIA, conversa, conteudo, agora, log, ct, mensagemAtualId);

        // Quem conduz a conversa aqui é a pergunta de dúvida, não a troca aberta com
        // a IA: a próxima mensagem volta a este aplicador.
        conversa.AguardandoRespostaInteligente = false;

        // Não pergunta de novo se a conversa foi para uma pessoa, nem se nada saiu
        // (a IA descartou a resposta porque um atendente assumiu enquanto ela pensava).
        if (conversa.EmAtendimentoHumano || RespostasNovas(db, conversa.Id) == antes)
        {
            return;
        }

        var pergunta = await automaticas.EnfileirarAsync(conversa, GatilhoDeMensagem.PerguntaDeDuvida, ct: ct);
        conversa.AguardandoDuvidaDoServico = pergunta.Count > 0;
    }

    private static int RespostasNovas(CrmDbContext db, Guid conversaId) =>
        db.ChangeTracker.Entries<Mensagem>()
            .Count(e => e.State == EntityState.Added && e.Entity.ConversaId == conversaId && e.Entity.EhAutomatica);

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
