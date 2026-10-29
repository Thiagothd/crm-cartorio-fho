using CrmCartorio.Api.Modules.Devedores.Domain.Enums;

namespace CrmCartorio.Api.Modules.Devedores.Application;

// Entrada -------------------------------------------------------------------

public record FiltroDeDevedores
{
    /// <summary>
    /// Recorte rápido: <c>abertas</c> (padrão), <c>vencidas</c>, <c>hoje</c>,
    /// <c>sem_cobranca</c>, <c>parciais</c>, <c>sem_valor</c>, <c>quitadas</c> ou <c>todas</c>.
    /// </summary>
    public string? Recorte { get; init; } = "abertas";
    public StatusPendencia? Status { get; init; }
    public Guid? ClienteId { get; init; }
    public Guid? TipoServicoId { get; init; }
    public string? Busca { get; init; }
    /// <summary>
    /// Ordenação: <c>maior_valor</c> (padrão), <c>mais_antigas</c>, <c>promessa_proxima</c>,
    /// <c>recentes</c> ou <c>nome</c>.
    /// </summary>
    public string? Ordenar { get; init; } = "maior_valor";
}

public record CriarPendenciaFinanceiraRequest(
    Guid? ClienteId,
    string? NovoClienteNome,
    string? NovoClienteTelefone,
    string? NovoClienteDocumento,
    Guid? ConversaOrigemId,
    Guid? TipoServicoId,
    string Titulo,
    string? Observacoes,
    decimal? ValorOriginal,
    DateOnly? DataReferencia,
    DateOnly? DataPromessaPagamento);

public record AtualizarPendenciaFinanceiraRequest(
    Guid? TipoServicoId,
    string Titulo,
    string? Observacoes,
    decimal? ValorOriginal,
    DateOnly DataReferencia,
    DateOnly? DataPromessaPagamento);

public record RegistrarPagamentoRequest(
    decimal? Valor,
    MeioDePagamento MeioPagamento,
    string? Observacao,
    bool QuitarTotalmente = false,
    bool EnviarComprovanteWhatsapp = false,
    string? MensagemComprovante = null);

public record CobrarNoWhatsappRequest(
    Guid ClienteId,
    IReadOnlyList<Guid>? PendenciaIds,
    string Mensagem);

public record ItemDeImportacaoDeDevedorRequest(
    Guid? ClienteId,
    string? Nome,
    string? Telefone,
    string? Documento,
    string? Titulo,
    decimal? ValorOriginal,
    DateOnly? DataPromessaPagamento,
    string? Observacoes);

public record ImportarDevedoresEmLoteRequest(
    string? TituloPadrao,
    Guid? TipoServicoPadraoId,
    DateOnly? DataPromessaPadrao,
    IReadOnlyList<ItemDeImportacaoDeDevedorRequest> Itens);

// Saída ---------------------------------------------------------------------

public record ResumoDeDevedoresResponse(
    decimal TotalEmAberto,
    int ClientesDevedores,
    int PendenciasAbertas,
    int PendenciasSemValor,
    decimal PromessasVencidasTotal,
    int PromessasVencidasQuantidade,
    decimal PromessasParaHojeTotal,
    int PromessasParaHojeQuantidade,
    decimal RecuperadoNoMesTotal,
    int RecuperadoNoMesQuantidade);

public record PagamentoDePendenciaResponse(
    Guid Id,
    Guid PendenciaFinanceiraId,
    decimal Valor,
    MeioDePagamento MeioPagamento,
    string? Observacao,
    DateTime PagoEm,
    Guid RegistradoPorAtendenteId,
    string? RegistradoPorNome);

public record PendenciaFinanceiraResponse(
    Guid Id,
    Guid ClienteId,
    string ClienteNome,
    string ClienteTelefone,
    string? ClienteDocumento,
    string ClienteFotoPerfilUrl,
    Guid? ConversaOrigemId,
    Guid? ConversaAtivaId,
    Guid? TipoServicoId,
    string? TipoServicoNome,
    string Titulo,
    string? Observacoes,
    decimal? ValorOriginal,
    decimal ValorPago,
    decimal? SaldoRestante,
    DateOnly DataReferencia,
    DateOnly? DataPromessaPagamento,
    int DiasEmAberto,
    int? DiasDeAtraso,
    StatusPendencia Status,
    DateTime? UltimaCobrancaEm,
    int TotalCobrancas,
    Guid RegistradoPorAtendenteId,
    string? RegistradoPorNome,
    DateTime? QuitadaEm,
    Guid? QuitadoPorAtendenteId,
    string? QuitadoPorNome,
    DateTime CriadoEm,
    IReadOnlyList<PagamentoDePendenciaResponse> Pagamentos);

public record DevedorConsolidadoResponse(
    Guid ClienteId,
    string ClienteNome,
    string ClienteTelefone,
    string? ClienteDocumento,
    string ClienteFotoPerfilUrl,
    Guid? ConversaAtivaId,
    int QuantidadePendenciasAbertas,
    int QuantidadeSemValor,
    decimal ValorOriginalTotal,
    decimal ValorPagoTotal,
    decimal SaldoRestanteTotal,
    DateOnly DataReferenciaMaisAntiga,
    DateOnly? ProximaPromessaPagamento,
    bool TemPromessaVencida,
    bool TemPromessaHoje,
    DateTime? UltimaCobrancaEm,
    int TotalCobrancas,
    IReadOnlyList<PendenciaFinanceiraResponse> Pendencias);

public record ListagemDeDevedoresResponse(
    ResumoDeDevedoresResponse Resumo,
    IReadOnlyList<DevedorConsolidadoResponse> Devedores,
    IReadOnlyList<PendenciaFinanceiraResponse> Lancamentos,
    int TotalLancamentos);

public record ResumoDeDividaDoClienteResponse(
    Guid ClienteId,
    string ClienteNome,
    string ClienteTelefone,
    int PendenciasAbertas,
    decimal SaldoDevedor,
    IReadOnlyList<PendenciaFinanceiraResponse> Pendencias);

public record CobrarNoWhatsappResponse(
    Guid ConversaId,
    Guid MensagemId,
    int PendenciasAtualizadas);

public record AbrirConversaComDevedorResponse(
    Guid ConversaId,
    bool CriadaAgora);

public record ResultadoDeImportacaoDeDevedoresResponse(
    int PendenciasCriadas,
    int ClientesNovosCriados,
    int ClientesExistentesVinculados,
    int Ignorados,
    IReadOnlyList<string> Avisos);
