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

    /// <summary>
    /// Liga a varredura periodica de inatividade (perguntar se pode encerrar e,
    /// sem resposta, encerrar).
    /// </summary>
    /// <remarks>
    /// Chave de emergencia do operador, nao a configuracao do recurso: QUANTO tempo
    /// de silencio e por tenant, editavel pela tela (ver
    /// <c>Tenant.MinutosParaPerguntarEncerramento</c>). Aqui so se desliga o motor
    /// inteiro, para o caso de precisar parar tudo sem esperar alguem entrar no
    /// painel.
    /// </remarks>
    public bool VarrerInatividade { get; set; } = true;

    /// <summary>
    /// Intervalo entre passadas da varredura de inatividade, em minutos.
    /// </summary>
    /// <remarks>
    /// Menor que o de prazo de proposito: o menor tempo que a tela aceita configurar
    /// e de 5 minutos, e uma varredura mais lenta que isso faria a configuracao
    /// mentir — quem escolhesse "5 minutos" veria a pergunta sair em 10.
    /// </remarks>
    public int IntervaloDeInatividadeEmMinutos { get; set; } = 5;
}
