namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <summary>
/// Pergunta ao cliente se pode encerrar a conversa parada e, sem resposta,
/// encerra sozinho.
/// </summary>
/// <remarks>
/// <b>O problema que isto resolve.</b> Conversa que ninguem encerra fica Ativa
/// para sempre, e <c>RecebimentoDeWhatsApp.ResolverConversaAsync</c> reaproveita
/// a conversa Ativa do cliente sem olhar quando foi a ultima mensagem. Resultado
/// observado: quem voltava dias depois caia na conversa velha — sem boas-vindas,
/// sem triagem, sem menu. O atendimento automatico simplesmente nao acontecia
/// para cliente recorrente.
///
/// Encerrar e o que conserta, porque conversa Encerrada ja faz a coisa certa:
/// o proximo contato abre conversa nova, com protocolo novo e o fluxo do zero.
///
/// Varredura, e nao calculo na leitura, pelo mesmo motivo de
/// <see cref="IVarredorDePrazos"/>: silencio nao e evento, ninguem o provoca. E
/// fazer isso quando um atendente abrisse a inbox deixaria o encerramento na mao
/// de alguem ter olhado a tela.
/// </remarks>
public interface IVarredorDeInatividade
{
    /// <summary>
    /// Executa uma passada das duas fases. Devolve quantas conversas foram
    /// perguntadas ou encerradas.
    /// </summary>
    Task<int> ExecutarAsync(CancellationToken ct = default);
}
