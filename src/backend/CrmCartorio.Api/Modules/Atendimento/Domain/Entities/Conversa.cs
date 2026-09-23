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

    /// <summary>
    /// A conversa já foi entregue a um humano neste ciclo — o cliente pediu
    /// atendente, ou um atendente assumiu por conta própria. Enquanto isto
    /// for verdade, a automação conversacional (triagem, menu de escolha de
    /// serviço, Resposta Inteligente, piso de pós-atendimento) fica muda: a
    /// mensagem do cliente continua sendo gravada, aparecendo no fio,
    /// atualizando <see cref="UltimaMensagemEm"/>/<see cref="MensagensNaoLidas"/>
    /// e chegando por SignalR — só não é respondida automaticamente.
    /// </summary>
    /// <remarks>
    /// <b>Não é o mesmo que <see cref="AtendenteId"/> != null.</b> Esse é o
    /// erro fácil de cometer (BUG-ATD-01): <see cref="AtendenteId"/> volta a
    /// nulo quando um atendente devolve a conversa para a fila
    /// (<c>ConversaService.LiberarAsync</c>), mas a conversa continua devendo
    /// humano, não devendo bot — o cliente está esperando OUTRO atendente, não
    /// pedindo para falar com a IA de novo. <see cref="AtendenteId"/> nulo e
    /// <see cref="EmAtendimentoHumano"/> verdadeiro ao mesmo tempo é um estado
    /// válido e comum: fila humana.
    ///
    /// Liga em <c>AplicadorDeRespostaInteligente.EscalarAsync</c> (pedido
    /// explícito do cliente), em <c>AplicadorDePosAtendimento</c> (opção
    /// "falar com atendente" do menu de continuação), em
    /// <c>ConversaService.AssumirAsync</c> (inclui "responder direto da
    /// fila", que reusa <c>AssumirAsync</c>), em <c>ReabrirAsync</c> e —
    /// desde 2026-09-23 — quando alguém da serventia responde direto pelo
    /// WhatsApp, fora do painel (<c>RecebimentoDeWhatsApp.RegistrarEnviadaPeloAparelhoAsync</c>,
    /// ver <see cref="AtendidaPeloAparelhoEm"/>). Desliga
    /// só em <c>EncerrarAsync</c>/<c>AplicadorDePosAtendimento.EncerrarPeloCliente</c>
    /// — nunca em <c>LiberarAsync</c> nem <c>TransferirAsync</c>. Uma conversa
    /// nova nasce com isto falso (automática) — o próximo atendimento depois
    /// de um encerramento também, porque é sempre uma <see cref="Conversa"/>
    /// nova.
    /// </remarks>
    public bool EmAtendimentoHumano { get; set; }

    /// <summary>
    /// A conversa esta sendo atendida pelo WhatsApp do celular (ou WhatsApp Web),
    /// e ninguem no painel a assumiu. Nulo na imensa maioria.
    /// </summary>
    /// <remarks>
    /// Preenchido quando alguem da serventia responde fora do painel numa conversa
    /// SEM dono (<see cref="AtendenteId"/> nulo). E o que troca o selo "Na fila"
    /// por "No celular" na lista: a conversa continua em aberto — qualquer
    /// atendente pode assumir —, mas o painel sabe que o cliente nao esta
    /// esperando ninguem.
    ///
    /// Apagado sempre que a conversa ganha dono no painel (assumir, transferir,
    /// reabrir). A partir dai quem atende e aquela pessoa, e uma nova resposta pelo
    /// celular so entra no fio, sem tirar a conversa dela.
    ///
    /// Tambem e o que libera o encerramento por inatividade (fase 2 de
    /// <c>VarredorDeInatividade</c>) numa conversa em atendimento humano: uma
    /// conversa com dono no painel e encerrada por ele; uma atendida so pelo
    /// celular nao tem quem a encerre, e ficaria aberta para sempre (decisao do
    /// Thiago em 2026-09-23 — passou o tempo, encerra, e o proximo contato volta ao
    /// robo).
    /// </remarks>
    public DateTime? AtendidaPeloAparelhoEm { get; set; }

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
    /// O contato chegou sem nome utilizavel no perfil do WhatsApp e a conversa
    /// espera que ele diga como quer ser chamado — ANTES de receber o menu.
    /// </summary>
    /// <remarks>
    /// Mesma disciplina de <see cref="AguardandoTriagem"/>: dura UMA mensagem, e
    /// sai tenha o cliente respondido um nome ou nao. Insistir seria pior aqui do
    /// que na triagem — quem nao quis dizer o nome na primeira vez nao vai dizer na
    /// segunda, e o atendimento nao pode ficar preso atras de um dado opcional.
    /// Sem nome, o menu sai na mesma passada e a conversa segue normalmente.
    ///
    /// Diferente das outras flags, esta so liga na conversa recem-aberta, e so
    /// quando <c>NomeDeContato.EhUtil</c> reprova o nome do cliente E ele nunca foi
    /// perguntado antes (<c>Cliente.NomePerguntadoEm</c>).
    /// </remarks>
    public bool AguardandoNome { get; set; }

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

    /// <summary>
    /// Quando a pergunta "deseja encerrar?" saiu, por inatividade. Nulo enquanto
    /// nao ha nenhuma pendente.
    /// </summary>
    /// <remarks>
    /// Fato datado, pelo mesmo motivo de <see cref="AvisoDeAlertaEnviadoEm"/>: o
    /// que fica gravado nao e "esta parada", que envelhece sozinho, e sim "a
    /// pergunta saiu".
    ///
    /// <b>Um campo com tres papeis, de proposito.</b> Ele e (1) o marcador
    /// idempotente da primeira fase da varredura, (2) o relogio da segunda — o
    /// tempo de espera pela resposta conta a partir daqui — e (3) a flag que o
    /// despacho consulta para saber se a proxima mensagem do cliente e resposta a
    /// essa pergunta. Um <c>bool AguardandoConfirmacaoDeEncerramento</c> separado
    /// andaria sempre junto deste carimbo, e dois campos que nunca discordam sao
    /// dois campos que um dia vao discordar — no atendimento de um cliente real.
    ///
    /// Como nao guarda historico ("ja perguntei uma vez"), um novo periodo de
    /// silencio gera uma nova pergunta. E o comportamento certo: o relogio da
    /// primeira fase e <see cref="UltimaMensagemEm"/>, que so avanca com mensagem
    /// de gente.
    /// </remarks>
    public DateTime? PerguntaDeEncerramentoEnviadaEm { get; set; }

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
