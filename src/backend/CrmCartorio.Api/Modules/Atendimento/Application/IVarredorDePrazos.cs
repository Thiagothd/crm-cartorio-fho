namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <summary>
/// Dispara os avisos de prazo proximo do vencimento e de prazo vencido.
/// </summary>
/// <remarks>
/// Existe como varredura, e nao como calculo na leitura, porque estes dois gatilhos
/// sao os unicos do sistema que ninguem provoca: os outros nascem de uma acao — abrir
/// conversa, mover etapa, o cliente escrever. Prazo simplesmente passa.
///
/// A alternativa seria disparar quando alguem abrisse a inbox, e ela e pior de duas
/// formas: um GET passaria a escrever no banco, e o aviso ao cliente ficaria
/// dependendo de um atendente ter olhado a tela — de noite, no fim de semana ou num
/// dia parado, ninguem seria avisado de nada.
/// </remarks>
public interface IVarredorDePrazos
{
    /// <summary>Executa uma passada. Devolve quantas conversas receberam aviso.</summary>
    Task<int> ExecutarAsync(CancellationToken ct = default);
}
