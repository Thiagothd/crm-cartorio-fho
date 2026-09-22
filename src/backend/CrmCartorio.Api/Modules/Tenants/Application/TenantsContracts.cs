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

/// <summary>
/// Chaves gerais de automacao do tenant. Desligar mensagens automaticas cala
/// TODOS os gatilhos, inclusive o bot de triagem; desligar a IA faz a mensagem
/// cair no piso de sempre (atendimento humano). Nenhuma das duas apaga cadastro.
/// </summary>
public record ChavesGeraisResponse(
    bool MensagensAutomaticasHabilitadas,
    bool RespostaInteligenteHabilitada);

public record SalvarChaveGeralRequest(bool Habilitada);
