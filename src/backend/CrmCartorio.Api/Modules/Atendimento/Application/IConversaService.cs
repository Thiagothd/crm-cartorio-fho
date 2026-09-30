namespace CrmCartorio.Api.Modules.Atendimento.Application;

public interface IConversaService
{
    Task<ResultadoPaginado<ConversaResumoResponse>> ListarAsync(FiltroDeConversas filtro, CancellationToken ct = default);

    Task<ConversaDetalheResponse> ObterAsync(Guid id, CancellationToken ct = default);

    /// <summary>Conversa sem o fio de mensagens. Usada nas notificacoes em tempo real.</summary>
    Task<ConversaResumoResponse> ObterResumoAsync(Guid id, CancellationToken ct = default);

    Task<ConversaResumoResponse> CriarAsync(CriarConversaRequest request, CancellationToken ct = default);

    /// <summary>Assume a conversa. Falha com conflito tratado se outro chegou primeiro.</summary>
    Task<ConversaResumoResponse> AssumirAsync(Guid id, CancellationToken ct = default);

    Task<ConversaResumoResponse> LiberarAsync(Guid id, CancellationToken ct = default);

    /// <summary>Pausa o robô nesta conversa (e, se pedido, para o contato).</summary>
    Task<ConversaResumoResponse> PausarRoboAsync(Guid id, PausarRoboRequest request, CancellationToken ct = default);

    /// <summary>Liga o robô de novo nesta conversa, devolvendo-a para a fila.</summary>
    Task<ConversaResumoResponse> RetomarRoboAsync(Guid id, RetomarRoboRequest request, CancellationToken ct = default);

    Task<ConversaResumoResponse> TransferirAsync(Guid id, TransferirConversaRequest request, CancellationToken ct = default);

    Task<ConversaResumoResponse> MoverAsync(Guid id, MoverConversaRequest request, CancellationToken ct = default);

    Task<ConversaResumoResponse> VincularServicoAsync(Guid id, VincularServicoRequest request, CancellationToken ct = default);

    Task<ConversaResumoResponse> EncerrarAsync(Guid id, EncerrarConversaRequest request, CancellationToken ct = default);

    Task<ConversaResumoResponse> ReabrirAsync(Guid id, CancellationToken ct = default);

    Task<ConversaResumoResponse> MarcarComoLidaAsync(Guid id, CancellationToken ct = default);

    Task<PainelResumoResponse> ResumoDoPainelAsync(CancellationToken ct = default);
}
