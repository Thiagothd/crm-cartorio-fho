namespace CrmCartorio.Api.Infrastructure.Automacao;

public class OpcoesDeAutomacao
{
    public const string Secao = "Automacao";

    /// <summary>
    /// Liga a varredura periodica de prazo. Desligada, os gatilhos de prazo nunca
    /// disparam; os de evento (novo atendimento, etapa alterada) continuam normais,
    /// porque nascem da acao do atendente e nao dependem de varredura.
    /// </summary>
    public bool VarrerPrazos { get; set; } = true;

    /// <summary>
    /// Intervalo entre passadas, em minutos.
    /// </summary>
    /// <remarks>
    /// Cinco minutos e a folga aceitavel para um aviso de prazo de cartorio: o menor
    /// SLA do catalogo e de duas horas, entao o atraso maximo do aviso fica em ~4% do
    /// prazo. Reduzir mais e consulta a mais sem ganho perceptivel para o cliente.
    /// </remarks>
    public int IntervaloEmMinutos { get; set; } = 5;
}
