namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <summary>
/// Responde aos dados do serviço (<c>TipoServico.MensagemAposDados</c>, ex.: a chave
/// Pix) só depois que o cliente para de digitar.
/// </summary>
/// <remarks>
/// <b>O problema que isto resolve.</b> Teste ao vivo em 2026-09-29: quem manda os
/// dados numa mensagem só recebe o Pix na hora, mas muita gente manda o nome numa
/// mensagem, o CPF em outra e a data em outra. Respondendo à primeira, o Pix saía
/// no meio da digitação e a segunda mensagem (o CPF) era tomada como comprovante —
/// o atendimento ia para a equipe antes da hora.
///
/// Agora a primeira mensagem de dados só marca
/// <c>Conversa.DadosDoServicoRecebidosEm</c>, e cada mensagem nova empurra
/// <c>UltimaMensagemEm</c>. Esta varredura manda a instrução quando o fio fica em
/// silêncio pelo tempo configurado.
///
/// Varredura, e não espera dentro do webhook, pelo mesmo motivo de
/// <see cref="IVarredorDeInatividade"/>: silêncio não é evento, ninguém o provoca.
/// </remarks>
public interface IVarredorDeDadosDoServico
{
    /// <summary>
    /// Executa uma passada. <paramref name="silencio"/> é quanto tempo sem mensagem
    /// nova conta como "o cliente terminou de mandar os dados". Devolve quantas
    /// conversas receberam a instrução.
    /// </summary>
    Task<int> ExecutarAsync(TimeSpan silencio, CancellationToken ct = default);
}
