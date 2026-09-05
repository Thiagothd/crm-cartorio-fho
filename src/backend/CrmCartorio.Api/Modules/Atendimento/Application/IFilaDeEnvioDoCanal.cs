namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <summary>
/// Porta de saida para agendar o envio pendente de uma conversa SEM bloquear
/// quem enfileirou a mensagem automatica.
/// </summary>
/// <remarks>
/// Existe porque <see cref="IEnviadorDeCanal.EnviarPendentesAsync"/> faz uma
/// chamada de rede de verdade ao gateway — instantanea na maioria das vezes,
/// mas sujeita a soluco (achado ao vivo: chamadas de poucos segundos a dezenas
/// de segundos, e ate o teto de <c>TimeoutSegundos</c> quando ha mensagem em
/// <c>Falha</c> para retentar antes da nova). Uma acao do atendente (mudar
/// etapa, por exemplo) grava no banco e devolve na hora; quem espera a
/// mensagem sair de fato e' o SignalR, nao o clique do atendente.
///
/// Nao serve para o recebimento de mensagem do cliente — RecebimentoDeWhatsApp
/// continua chamando <see cref="IEnviadorDeCanal"/> direto ali, porque o unico
/// "cliente" esperando essa resposta e o proprio gateway (que ja tem seu
/// timeout e sua logica de reentrega), nao um atendente com o mouse parado
/// sobre um botao.
/// </remarks>
public interface IFilaDeEnvioDoCanal
{
    /// <summary>Agenda o envio das mensagens pendentes da conversa. Nunca bloqueia.</summary>
    void Agendar(Guid conversaId);
}
