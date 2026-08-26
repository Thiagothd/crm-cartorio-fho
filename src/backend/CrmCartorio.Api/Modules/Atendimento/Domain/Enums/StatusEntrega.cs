namespace CrmCartorio.Api.Modules.Atendimento.Domain.Enums;

/// <summary>
/// Na Fase 1 a mensagem enviada vai direto para <see cref="Enviada"/>, porque nao
/// existe WhatsApp real confirmando entrega. Na Fase 2 o webhook de status da Meta
/// alimenta os demais valores.
/// </summary>
public enum StatusEntrega : byte
{
    Pendente = 1,
    Enviada = 2,
    Entregue = 3,
    Lida = 4,
    Falha = 5,
    Enviando = 6
}
