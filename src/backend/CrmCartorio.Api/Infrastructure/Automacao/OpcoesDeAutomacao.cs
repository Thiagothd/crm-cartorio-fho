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
    /// Bem menor que o de prazo, de proposito. O pior caso do atraso de uma
    /// varredura periodica e o proprio intervalo dela: com o intervalo igual ao
    /// minimo configuravel na tela (5 min), quem escolhesse "5 minutos" via a
    /// pergunta sair entre 5 e 10 — medido ao vivo em 2026-08-23, saiu em 11.
    /// Com o intervalo em 1 minuto, o pior caso vira "N + ate 1", que e o que a
    /// tela promete de verdade. A consulta e barata (indice, LoteMaximo=200, poucos
    /// tenants) — o custo de rodar 5x mais vezes por minuto e desprezivel perto do
    /// ganho de a configuracao nao mentir.
    /// </remarks>
    public int IntervaloDeInatividadeEmMinutos { get; set; } = 1;

    /// <summary>
    /// Liga a espera dos dados do servico: a instrucao (ex.: a chave Pix) sai quando o
    /// cliente para de mandar os dados. Ver <c>IVarredorDeDadosDoServico</c>.
    /// </summary>
    /// <remarks>
    /// Chave de emergencia do operador. Desligada, quem mandar os dados fica sem a
    /// instrucao — por isso so para parar o motor, nunca como configuracao.
    /// </remarks>
    public bool EsperarDadosDoServico { get; set; } = true;

    /// <summary>
    /// Quantos segundos sem mensagem nova contam como "o cliente terminou de mandar os dados".
    /// </summary>
    /// <remarks>
    /// 45 s: o bastante para quem digita o CPF numa mensagem e a data na seguinte,
    /// curto o bastante para quem mandou tudo de uma vez nao achar que o robo travou.
    /// </remarks>
    public int SegundosDeSilencioAntesDaInstrucao { get; set; } = 45;

    /// <summary>Intervalo entre passadas da espera de dados, em segundos.</summary>
    public int IntervaloDaEsperaDeDadosEmSegundos { get; set; } = 10;
}
