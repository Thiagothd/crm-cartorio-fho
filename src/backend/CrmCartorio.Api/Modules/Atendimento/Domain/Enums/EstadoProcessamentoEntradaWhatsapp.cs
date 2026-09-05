namespace CrmCartorio.Api.Modules.Atendimento.Domain.Enums;

/// <summary>
/// Estado duravel do processamento posterior de uma mensagem recebida pelo
/// WhatsApp. A linha da mensagem existe antes de qualquer automacao ou IA.
/// </summary>
public enum EstadoProcessamentoEntradaWhatsapp : byte
{
    Pendente = 1,
    Processando = 2,
    Processada = 3,
    Falha = 4
}
