using System.Linq.Expressions;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;

namespace CrmCartorio.Api.Modules.Atendimento.Domain;

/// <summary>
/// Regra unica de "o cartorio esta devendo resposta": a ULTIMA mensagem REAL do
/// fio foi do cliente. Mesma disciplina de <see cref="AvaliadorDeSituacaoPrazo"/>
/// — o contador da Visao Geral, o filtro da inbox e o painel de resumo leem daqui,
/// para que o numero do card e a lista que ele abre nunca discordem.
/// </summary>
/// <remarks>
/// NAO usa <c>Conversa.MensagensNaoLidas</c>. Aquele contador mede "nenhum humano
/// abriu no CRM" e nunca zera quando a IA responde — bug confirmado em producao em
/// 2026-09-03 (ver Modules/Atendimento/README.md e VarredorDeInatividade).
///
/// "Mensagem REAL" exclui a ANOTACAO INTERNA do fio. <c>AutorTipo.Sistema</c> cobre
/// duas coisas distintas: saida para o cliente (regra automatica e IA, sempre
/// <c>EhAutomatica</c> — garantido pelo CHECK CK_Mensagens_AutomaticaEhDoSistema) e
/// nota interna ("Conversa assumida por X", "Servico definido como Y"). Sem este
/// filtro, o atendente que ASSUME a conversa sem responder faz o cliente sumir do
/// contador — que e exatamente o caso que ele existe para mostrar.
///
/// O desempate por <c>Id</c> nao e decorativo: a automacao grava nota interna e
/// mensagem automatica no MESMO instante (<c>agora.AddMilliseconds(1)</c> em
/// ProcessadorDeEntradaWhatsapp), e sem ele o "ultimo" seria escolhido pelo plano do
/// SQL Server. <c>Id</c> e Guid v7, monotonico, e e o mesmo desempate que o fio da
/// conversa usa para se ordenar na tela.
///
/// Deliberadamente NAO gateado por <c>EmAtendimentoHumano</c> nem pelas flags
/// <c>Aguardando*</c>: o estado "cliente falou por ultimo" e transitorio por
/// construcao (a automacao decide/despacha na mesma passada da entrada). Se ele
/// persiste, e porque a automacao decidiu nao responder ou falhou — os dois casos
/// sao exatamente o que este contador existe para mostrar.
///
/// Nao filtra tenant, status nem atendente — isso e escopo do chamador, como em
/// AvaliadorDeSituacaoPrazo.
/// </remarks>
public static class AguardandoRespostaDoCartorio
{
    public static Expression<Func<Conversa, bool>> Expressao(IQueryable<Mensagem> mensagens) =>
        c => mensagens
                .Where(m => m.ConversaId == c.Id
                         && (m.AutorTipo != AutorTipo.Sistema || m.EhAutomatica))
                .OrderByDescending(m => m.EnviadaEm)
                .ThenByDescending(m => m.Id)
                .Select(m => m.AutorTipo)
                .FirstOrDefault() == AutorTipo.Cliente;
}
