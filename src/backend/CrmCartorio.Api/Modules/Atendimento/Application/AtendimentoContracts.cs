using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

// Entrada -------------------------------------------------------------------

public record CriarConversaRequest(
    Guid ClienteId,
    Guid? SetorId,
    Guid? EtapaId,
    string? PrimeiraMensagem);

public record EnviarMensagemRequest(string Conteudo, Guid? MensagemRespondidaId = null);

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
    DateTime? ReabertaEm);

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
    string? RespondidaConteudo = null);

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
    int MinhasConversas,
    int EncerradasHoje,
    int EmAlerta,
    int Atrasadas,
    IReadOnlyList<ContagemPorChaveResponse> PorEtapa,
    IReadOnlyList<ContagemPorChaveResponse> PorSetor);

public record ResultadoPaginado<T>(IReadOnlyList<T> Itens, int Total, int Pagina, int Tamanho);
