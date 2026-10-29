using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

// Entrada -------------------------------------------------------------------

public record CriarConversaRequest(
    Guid ClienteId,
    Guid? SetorId,
    Guid? EtapaId,
    string? PrimeiraMensagem,
    // Só vale para atendimento que sai pelo WhatsApp. Falso (o padrão) é a
    // conversa livre: o atendente escreve, o robô fica de fora. Verdadeiro
    // devolve o fluxo do primeiro contato — boas-vindas e menu, na fila.
    bool ComecarComRobo = false);

public record EnviarMensagemRequest(string Conteudo, Guid? MensagemRespondidaId = null);

/// <summary>
/// Envio de arquivo pelo painel.
/// </summary>
/// <param name="Conteudo">
/// Bytes ja lidos do formulario. O controller resolve o multipart; o servico so
/// ve o arquivo — assim a regra nao depende do formato do transporte.
/// </param>
/// <param name="Legenda">
/// Opcional, ao contrario da mensagem de texto: um documento sozinho ja e uma
/// mensagem completa.
/// </param>
public record EnviarAnexoRequest(
    byte[] Conteudo,
    string? NomeDoArquivo,
    string? TipoMime,
    string? Legenda = null,
    Guid? MensagemRespondidaId = null);

/// <summary>Anexo pronto para o controller devolver.</summary>
/// <param name="ExibirNaTela">
/// Imagem abre na tela; qualquer outra coisa baixa. E a diferenca entre
/// <c>inline</c> e <c>attachment</c> no Content-Disposition, e ela e de
/// SEGURANCA antes de ser de conforto: abrir no dominio do painel um arquivo que
/// o cliente escolheu e o que transformaria um anexo em script rodando com a
/// sessao do atendente.
/// </param>
public record AnexoParaDownload(
    byte[] Conteudo,
    string Nome,
    string TipoMime,
    bool ExibirNaTela);

/// <summary>
/// Reenvia mensagens desta conversa para outra. Aceita varias de uma vez porque a
/// selecao multipla do fio existe justamente para isso.
/// </summary>
public record EncaminharMensagensRequest(Guid ConversaDestinoId, IReadOnlyList<Guid> MensagemIds);

/// <summary>
/// Oculta ou reexibe. Nunca apaga — nao existe DELETE de mensagem em caminho
/// nenhum do sistema (RNF-008).
/// </summary>
public record OcultarMensagensRequest(IReadOnlyList<Guid> MensagemIds, bool Ocultar);

/// <summary>Conversa candidata a receber um encaminhamento.</summary>
public record DestinoDeEncaminhamentoResponse(
    Guid Id,
    string Protocolo,
    string ClienteNome,
    string? SetorNome,
    string? AtendenteNome);

/// <summary>
/// Fase 1: simula a chegada de mensagem do cliente para demonstrar o fluxo sem
/// WhatsApp real. Na Fase 2 quem chama esse caminho e o webhook da Cloud API.
/// </summary>
public record SimularMensagemDeClienteRequest(string Conteudo);

public record MoverConversaRequest(Guid? EtapaId, Guid? SetorId, string? Observacao);

public record TransferirConversaRequest(Guid AtendenteDestinoId, string? Observacao);

public record VincularServicoRequest(Guid TipoServicoId);

public record EncerrarConversaRequest(string? Motivo);

public record FiltroDeConversas
{
    public StatusConversa? Status { get; init; } = StatusConversa.Ativa;
    public Guid? SetorId { get; init; }
    public Guid? EtapaId { get; init; }
    public Guid? AtendenteId { get; init; }
    public Guid? TipoServicoId { get; init; }
    public bool ApenasNaFila { get; init; }
    public bool ApenasMinhas { get; init; }

    /// <summary>
    /// Encerradas a partir do inicio do dia local da serventia. Existe para que o
    /// indicador "encerradas hoje" do painel possa virar filtro sem que a conta e
    /// a lista discordem — as duas usam o mesmo limite de dia, calculado no servidor.
    /// </summary>
    public bool ApenasEncerradasHoje { get; init; }

    /// <summary>
    /// Conversas em que a ULTIMA mensagem real do fio e do cliente — o cartorio
    /// esta devendo resposta. Existe para que o card "Aguardando resposta" da Visao
    /// Geral possa virar lista sem que a conta e a lista discordem: os dois lados
    /// leem AguardandoRespostaDoCartorio.Expressao. Mesma razao de
    /// <see cref="ApenasEncerradasHoje"/>.
    /// </summary>
    public bool ApenasAguardandoResposta { get; init; }
    public bool ApenasDevedores { get; init; }

    public SituacaoPrazo? SituacaoPrazo { get; init; }
    public string? Busca { get; init; }
    public int Pagina { get; init; } = 1;
    public int Tamanho { get; init; } = 30;
}

// Saida ---------------------------------------------------------------------

public record ConversaResumoResponse(
    Guid Id,
    string Protocolo,
    Guid ClienteId,
    string ClienteNome,
    string ClienteTelefone,
    Guid? AtendenteId,
    string? AtendenteNome,
    Guid? SetorId,
    string? SetorNome,
    Guid? EtapaId,
    string? EtapaNome,
    string? EtapaCor,
    Guid? TipoServicoId,
    string? TipoServicoNome,
    StatusConversa Status,
    DateTime IniciadaEm,
    DateTime UltimaMensagemEm,
    int MensagensNaoLidas,
    /// <summary>A conversa recebeu o menu de triagem e espera a escolha do cliente.</summary>
    bool AguardandoTriagem,
    DateTime? PrazoIniciadoEm,
    DateTime? PrazoLimite,
    SituacaoPrazo SituacaoPrazo,
    string? PreviaUltimaMensagem,
    DateTime? EncerradaEm,
    string? EncerradaPorNome,
    string? MotivoEncerramento,
    DateTime? ReabertaEm,
    string? ClienteFotoPerfilUrl = null,
    /// <summary>
    /// O nome do contato nao serve para se dirigir a ele (".", emoji, o proprio
    /// numero). A tela mostra o telefone no lugar; a regra fica no backend para os
    /// dois lados nao divergirem.
    /// </summary>
    bool ClienteNomePendente = false,
    /// <summary>
    /// Sem dono no painel, mas sendo atendida pelo WhatsApp do celular. A lista
    /// troca "Na fila" por "No celular"; assumir continua possivel.
    /// </summary>
    bool AtendidaPeloAparelho = false,
    /// <summary>
    /// A automação conversacional está calada nesta conversa porque uma pessoa a
    /// assumiu ou o cliente pediu atendente. O painel oferece devolver ao robô.
    /// </summary>
    bool EmAtendimentoHumano = false,
    /// <summary>Quando o robô foi pausado nesta conversa. Nulo = robô ligado.</summary>
    DateTime? RoboPausadoEm = null,
    /// <summary>Quem pausou. Nulo com a pausa automática de contato "sem robô".</summary>
    string? RoboPausadoPorNome = null,
    /// <summary>O contato está marcado para ser atendido sempre sem robô.</summary>
    bool ClienteSemRobo = false,
    /// <summary>
    /// Quantidade de pendências financeiras abertas deste cliente (zerado quando
    /// o atendente não tem <c>devedores.ver</c>).
    /// </summary>
    int ClientePendenciasAbertas = 0,
    /// <summary>
    /// Saldo devedor total em aberto deste cliente em reais (zerado quando o
    /// atendente não tem <c>devedores.ver</c>).
    /// </summary>
    decimal ClienteSaldoDevedor = 0m);

/// <param name="SempreSemRoboParaOContato">
/// Marca também o contato (Cliente.AtendimentoSemRobo): as próximas conversas dele já
/// nascem com o robô pausado.
/// </param>
public record PausarRoboRequest(bool SempreSemRoboParaOContato = false);

/// <param name="TirarMarcaDoContato">Tira também a marca "sem robô" do contato.</param>
public record RetomarRoboRequest(
    ModoDeRetomadaDoRobo Modo = ModoDeRetomadaDoRobo.SoResponder,
    bool TirarMarcaDoContato = false);

public record MensagemResponse(
    Guid Id,
    Guid ConversaId,
    AutorTipo AutorTipo,
    Guid? AtendenteId,
    string? AtendenteNome,
    string Conteudo,
    TipoMensagem Tipo,
    StatusEntrega StatusEntrega,
    /// <summary>
    /// Gerada por regra de mensagem automatica. Sempre acompanha
    /// <see cref="AutorTipo"/> = Sistema, e e o que a interface usa para desenhar a
    /// mensagem como envio ao cliente — com o rotulo "Automatica" — em vez de
    /// anotacao interna do fio.
    /// </summary>
    bool EhAutomatica,
    DateTime EnviadaEm,
    DateTime? LidaEm,
    /// <summary>Fixada no topo da conversa. Uma por conversa.</summary>
    bool Fixada = false,
    /// <summary>
    /// Ocultada do fio. A mensagem continua no banco e continua vindo na API —
    /// quem oculta esconde de quem atende, não do registro.
    /// </summary>
    bool Oculta = false,
    /// <summary>Quem ocultou. Preenchido só quando <see cref="Oculta"/>.</summary>
    string? OcultaPorNome = null,
    /// <summary>Protocolo da conversa de origem, quando é um encaminhamento.</summary>
    string? EncaminhadaDeProtocolo = null,
    Guid? MensagemRespondidaId = null,
    /// <summary>Autor da mensagem citada, para a previa da citacao.</summary>
    string? RespondidaAutor = null,
    /// <summary>Texto da mensagem citada, resolvido na leitura.</summary>
    string? RespondidaConteudo = null,
    /// <summary>
    /// Nome do arquivo anexado, quando ha um.
    /// </summary>
    /// <remarks>
    /// O CAMINHO no disco nunca sai daqui. A interface pede o binario pelo id da
    /// mensagem; expor o caminho daria a quem lesse a resposta um mapa da pasta
    /// de anexos do servidor.
    /// </remarks>
    string? ArquivoNome = null,
    string? ArquivoTipoMime = null,
    long? ArquivoTamanhoBytes = null,
    /// <summary>
    /// A mensagem automática veio da Resposta Inteligente (IA), não de uma regra de
    /// mensagem automática cadastrada. Sempre acompanha <see cref="EhAutomatica"/>.
    /// </summary>
    bool GeradaPorIa = false,
    /// <summary>
    /// O texto foi corrigido depois de enviado. <see cref="Conteudo"/> já é a versão
    /// vigente — é ela que o cliente vê no WhatsApp e ela que alimenta citação,
    /// encaminhamento e histórico da IA.
    /// </summary>
    bool Corrigida = false,
    DateTime? CorrigidaEm = null,
    /// <summary>Quem corrigiu. Preenchido só quando <see cref="Corrigida"/>.</summary>
    string? CorrigidaPorNome = null,
    /// <summary>
    /// O texto como saiu da primeira vez. Vai para a tela porque a correção é
    /// auditável por desenho: quem abre o fio depois consegue ver o que o cliente
    /// recebeu antes do conserto, sem abrir o banco.
    /// </summary>
    string? ConteudoOriginal = null,
    /// <summary>
    /// Apagada no aparelho do cliente. A linha continua aqui e continua vindo na
    /// API, com o <see cref="Conteudo"/> intacto — o cliente deixou de ver, o
    /// registro da serventia não.
    /// </summary>
    bool ApagadaParaTodos = false,
    DateTime? ApagadaParaTodosEm = null,
    /// <summary>Quem apagou. Preenchido só quando <see cref="ApagadaParaTodos"/>.</summary>
    string? ApagadaParaTodosPorNome = null,
    /// <summary>
    /// Resposta dada direto pelo WhatsApp do celular, fora do painel. Vem com
    /// <see cref="AutorTipo"/> Atendente e sem <see cref="AtendenteNome"/>: a
    /// serventia respondeu, mas o aparelho não diz quem.
    /// </summary>
    bool EnviadaPeloAparelho = false);

/// <summary>
/// Novo texto de uma mensagem já enviada ao cliente.
/// </summary>
/// <remarks>
/// Só o texto: gatilho, citação e anexo continuam sendo o que eram. Corrigir é
/// consertar o que foi dito, não reescrever a mensagem inteira — trocar o anexo
/// ou a citação depois de enviada seria outra mensagem, e essa o atendente manda
/// normalmente.
/// </remarks>
public record CorrigirMensagemRequest(string Conteudo);

public record HistoricoEtapaResponse(
    Guid Id,
    TipoMudancaHistorico TipoMudanca,
    string? EtapaAnterior,
    string? EtapaNova,
    string? SetorAnterior,
    string? SetorNovo,
    string? AtendenteNome,
    DateTime OcorridoEm,
    string? Observacao);

public record ConversaDetalheResponse(
    ConversaResumoResponse Conversa,
    IReadOnlyList<MensagemResponse> Mensagens,
    IReadOnlyList<HistoricoEtapaResponse> Historico);

public record ContagemPorChaveResponse(Guid? Id, string Nome, string? Cor, int Total);

public record PainelResumoResponse(
    int TotalAtivas,
    int NaFila,
    int AguardandoResposta,
    int MinhasConversas,
    int EncerradasHoje,
    int EmAlerta,
    int Atrasadas,
    IReadOnlyList<ContagemPorChaveResponse> PorEtapa,
    IReadOnlyList<ContagemPorChaveResponse> PorSetor);

public record ResultadoPaginado<T>(IReadOnlyList<T> Itens, int Total, int Pagina, int Tamanho);
