using CrmCartorio.Api.Modules.Tenants.Domain.Enums;

namespace CrmCartorio.Api.Modules.Tenants.Application;

public record SalvarAparenciaRequest(AparenciaDaConversa AparenciaDaConversa);

public record AparenciaResponse(AparenciaDaConversa AparenciaDaConversa);

/// <summary>
/// Encerramento automatico de conversa parada. <c>null</c> nos minutos = desligado.
/// </summary>
public record EncerramentoAutomaticoResponse(
    int? MinutosParaPerguntar,
    int? MinutosParaEncerrar,
    int? HoraInicial,
    int? HoraFinal);

/// <inheritdoc cref="EncerramentoAutomaticoResponse"/>
public record SalvarEncerramentoAutomaticoRequest(
    int? MinutosParaPerguntar,
    int? MinutosParaEncerrar,
    int? HoraInicial,
    int? HoraFinal);
