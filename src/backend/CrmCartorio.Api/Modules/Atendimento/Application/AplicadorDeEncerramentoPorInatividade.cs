using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Atendimento.Domain;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Enums;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <summary>
/// Le a resposta do cliente a pergunta "deseja encerrar?" que
/// <see cref="VarredorDeInatividade"/> enviou por inatividade.
/// </summary>
/// <remarks>
/// Mesma forma dos outros <c>Aplicador*</c> e pelo mesmo motivo: ctx-free, dois
/// chamadores (simulacao do painel e webhook real).
///
/// <b>E chamado ANTES de qualquer outra flag no despacho — inclusive antes de
/// "menu".</b> A pergunta de encerramento pode ter saido com a conversa em
/// QUALQUER estado (esperando triagem, escolha de servico, conversando com a IA),
/// entao ela nao pode ser tratada dentro de um dos ramos: ficaria refem de qual
/// flag estava ligada na hora. Vindo antes do "menu", quem digita "menu" com uma
/// pergunta pendente cancela o encerramento E recomeca o atendimento, que e a
/// leitura obvia do pedido.
/// </remarks>
public static class AplicadorDeEncerramentoPorInatividade
{
    /// <summary>
    /// Trata a mensagem como resposta a pergunta de encerramento.
    /// </summary>
    /// <returns>
    /// <c>true</c> quando a mensagem foi consumida aqui (o cliente confirmou ou
    /// recusou) e o despacho deve parar. <c>false</c> quando a mensagem nao era
    /// sobre isso e precisa seguir a cadeia normal.
    /// </returns>
    /// <remarks>
    /// <b>Atencao ao efeito colateral no <c>false</c>:</b> mesmo devolvendo
    /// <c>false</c> este metodo ja limpou
    /// <see cref="Conversa.PerguntaDeEncerramentoEnviadaEm"/>. E deliberado —
    /// qualquer texto que o cliente escreva PROVA que ele esta vivo, e uma
    /// conversa viva nao pode ser encerrada por silencio. Quem chama usa isto
    /// dentro de um <c>else if</c>, entao o "cancelou mas nao tratou" acontece
    /// sem uma linha a mais no despacho.
    /// </remarks>
    public static async Task<bool> TratarRespostaAsync(
        CrmDbContext db,
        IDisparadorDeMensagemAutomatica automaticas,
        Conversa conversa,
        string? conteudo,
        DateTime agora,
        CancellationToken ct)
    {
        if (conversa.PerguntaDeEncerramentoEnviadaEm is null)
        {
            return false;
        }

        var resposta = TriagemAutomatica.LerRespostaDeEncerramento(conteudo);

        // Cancela ANTES de decidir o que fazer com o texto: o cliente respondeu
        // alguma coisa, e isso basta para tirar a conversa da fila de encerramento.
        conversa.PerguntaDeEncerramentoEnviadaEm = null;

        switch (resposta)
        {
            case TriagemAutomatica.RespostaDeEncerramento.Confirmou:
                await automaticas.EnfileirarAsync(conversa, GatilhoDeMensagem.Despedida, ct: ct);

                AplicadorDePosAtendimento.EncerrarPeloCliente(
                    db, conversa, agora, "Encerrado pelo cliente, que confirmou apos o aviso de inatividade.");

                return true;

            case TriagemAutomatica.RespostaDeEncerramento.Negou:
                // Nao cai para o piso da IA de proposito: a IA respondendo a um
                // "nao" isolado geraria RespostaSemCorrespondencia, que soa como se
                // o sistema nao tivesse entendido a recusa que ele mesmo provocou.
                // O menu de continuacao ja e, palavra por palavra, "posso ajudar em
                // mais alguma coisa?" — e ja esta cadastrado.
                await automaticas.EnfileirarAsync(conversa, GatilhoDeMensagem.MenuDeContinuacao, ct: ct);
                conversa.AguardandoMenuDeContinuacao = true;

                return true;

            default:
                // O cliente voltou falando de outra coisa. A MESMA mensagem segue o
                // despacho normal, com o estado anterior intacto — nada foi salvo
                // nem restaurado porque nada chegou a ser perdido. Mesmo padrao de
                // AplicadorDePosAtendimento.AplicarAsync quando a resposta nao casa
                // com nenhuma opcao do menu de continuacao.
                return false;
        }
    }
}
