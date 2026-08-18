using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;

namespace CrmCartorio.Api.Modules.Atendimento.Domain.Entities;

/// <summary>
/// Entidade central do produto: o atendimento de um cliente pelo WhatsApp do cartorio.
/// </summary>
/// <remarks>
/// A trava contra duplicidade de resposta mora aqui, em tres camadas:
/// <list type="number">
///   <item>UPDATE condicional em <see cref="AtendenteId"/> (WHERE AtendenteId IS NULL);</item>
///   <item><see cref="RowVersion"/> como token de concorrencia otimista do EF Core;</item>
///   <item>SignalR refletindo a atribuicao nos demais paineis.</item>
/// </list>
/// </remarks>
public class Conversa : EntidadeBase, IEntidadeDeTenant
{
    public Guid TenantId { get; set; }

    public Guid ClienteId { get; set; }

    /// <summary>Formato AAAAMM-NNNN. Unico por tenant.</summary>
    public required string Protocolo { get; set; }

    /// <summary>
    /// Dono da conversa. Nulo = na fila, disponivel. Preenchido = travada para os demais.
    /// </summary>
    public Guid? AtendenteId { get; set; }

    /// <summary>Eixo de roteamento.</summary>
    public Guid? SetorId { get; set; }

    /// <summary>Eixo de progressao (funil).</summary>
    public Guid? EtapaId { get; set; }

    /// <summary>Preenchido na etapa de captacao. Dispara o calculo do prazo.</summary>
    public Guid? TipoServicoId { get; set; }

    public StatusConversa Status { get; set; } = StatusConversa.Ativa;

    public CanalOrigem Canal { get; set; } = CanalOrigem.Simulado;

    public DateTime IniciadaEm { get; set; } = DateTime.UtcNow;

    /// <summary>Ordenacao padrao da inbox.</summary>
    public DateTime UltimaMensagemEm { get; set; } = DateTime.UtcNow;

    /// <summary>Mensagens do cliente ainda nao lidas por nenhum atendente.</summary>
    public int MensagensNaoLidas { get; set; }

    public DateTime? PrazoIniciadoEm { get; set; }

    public DateTime? PrazoLimite { get; set; }

    /// <summary>
    /// Instante em que a conversa entra em alerta, derivado de
    /// PrazoServico.PercentualAlerta no momento da vinculacao do servico.
    /// Materializado de proposito: com ele, filtrar a inbox por situacao de prazo
    /// vira comparacao de datas em SQL, sem subconsulta de percentual por linha.
    /// </summary>
    public DateTime? PrazoAlertaEm { get; set; }

    /// <summary>
    /// A conversa recebeu o menu de triagem e espera a escolha do setor pelo cliente.
    /// </summary>
    /// <remarks>
    /// Dura UMA mensagem. A proxima coisa que o cliente escrever limpa a flag —
    /// tenha ela correspondido a uma opcao ou nao. Deixar o cliente preso tentando
    /// adivinhar a resposta certa e o pior comportamento possivel num canal de
    /// cartorio; sem correspondencia, o sistema avisa que vai encaminhar para um
    /// atendente e sai do caminho.
    /// </remarks>
    public bool AguardandoTriagem { get; set; }

    /// <summary>
    /// A triagem não achou setor, a IA respondeu e a conversa segue tentando
    /// resolver — a próxima mensagem do cliente volta para a Resposta Inteligente
    /// em vez de cair direto no encaminhamento humano.
    /// </summary>
    /// <remarks>
    /// Limpa quando a IA não sabe responder, quando o cliente pede humano, quando
    /// <see cref="TentativasRespostaInteligente"/> estoura
    /// <c>OpcoesDaIA.MaxTrocas</c>, ou quando um atendente assume o setor manualmente.
    /// </remarks>
    public bool AguardandoRespostaInteligente { get; set; }

    /// <summary>Quantas vezes a IA já tentou responder nesta sequência sem setor.</summary>
    public int TentativasRespostaInteligente { get; set; }

    /// <summary>
    /// O setor já foi reconhecido e a conversa espera a escolha do serviço
    /// específico dentro dele (menu de 2º nível, {MenuServicos}).
    /// </summary>
    /// <remarks>
    /// Mesma disciplina de <see cref="AguardandoTriagem"/>: dura UMA mensagem, limpa
    /// tenha ela correspondido a um serviço ou não. Sem correspondência, cai para
    /// <see cref="AguardandoRespostaInteligente"/> antes de escalar — mesmo
    /// contingenciamento do resto do sistema.
    /// </remarks>
    public bool AguardandoEscolhaDeServico { get; set; }

    /// <summary>
    /// O atendimento estruturado terminou e a conversa espera a escolha do
    /// <see cref="Configuracoes.Domain.Enums.GatilhoDeMensagem.MenuDeContinuacao"/>
    /// (ver outro serviço, trocar de setor, falar com atendente, encerrar).
    /// </summary>
    /// <remarks>
    /// Mesma disciplina das outras "Aguardando": dura UMA mensagem. Sem
    /// correspondência, cai para <see cref="AguardandoRespostaInteligente"/> — a
    /// IA nunca fica sem ser tentada.
    /// </remarks>
    public bool AguardandoMenuDeContinuacao { get; set; }

    /// <summary>
    /// Quando o aviso de prazo proximo do vencimento foi gerado para esta conversa.
    /// </summary>
    /// <remarks>
    /// Isto NAO contradiz a regra de nao persistir situacao de prazo: o que fica
    /// gravado nao e "esta em alerta" — que envelhece e mentiria entre duas leituras
    /// — e sim "o aviso saiu", que e um fato datado e nao muda mais.
    ///
    /// E tambem o que torna o disparo idempotente: o varredor roda em intervalo
    /// curto e reencontraria a mesma conversa em alerta a cada volta.
    /// </remarks>
    public DateTime? AvisoDeAlertaEnviadoEm { get; set; }

    /// <summary>Quando o aviso de prazo vencido foi gerado. Mesma logica do aviso de alerta.</summary>
    public DateTime? AvisoDeVencimentoEnviadoEm { get; set; }

    public DateTime? EncerradaEm { get; set; }

    public Guid? EncerradaPorAtendenteId { get; set; }

    public string? MotivoEncerramento { get; set; }

    public DateTime? ReabertaEm { get; set; }

    public Guid? ReabertaPorAtendenteId { get; set; }

    /// <summary>
    /// Token de concorrencia. Qualquer escrita sobre uma versao antiga falha
    /// com DbUpdateConcurrencyException, traduzida para HTTP 409.
    /// </summary>
    public byte[]? RowVersion { get; set; }

    public List<Mensagem> Mensagens { get; set; } = [];

    public List<HistoricoEtapa> Historico { get; set; } = [];

    public bool EstaAtiva => Status == StatusConversa.Ativa;

    public bool EstaNaFila => AtendenteId is null && Status == StatusConversa.Ativa;

    /// <summary>
    /// Situacao do SLA no instante informado. Nunca persistida: persistir exigiria
    /// um job periodico e o valor mentiria entre duas execucoes.
    /// </summary>
    public SituacaoPrazo CalcularSituacaoPrazo(DateTime agora) =>
        AvaliadorDeSituacaoPrazo.Avaliar(
            Status, PrazoLimite, PrazoAlertaEm, EncerradaEm, agora);
}
