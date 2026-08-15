using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;

namespace CrmCartorio.Api.Modules.Atendimento.Domain;

/// <summary>
/// Regra unica de situacao de prazo, usada tanto pela entidade quanto pela
/// projecao da inbox. Ficar em um lugar so evita que a lista e o detalhe da
/// conversa discordem sobre a mesma conversa.
/// </summary>
public static class AvaliadorDeSituacaoPrazo
{
    public static SituacaoPrazo Avaliar(
        StatusConversa status,
        DateTime? prazoLimite,
        DateTime? prazoAlertaEm,
        DateTime? encerradaEm,
        DateTime agora)
    {
        if (prazoLimite is null)
        {
            return SituacaoPrazo.SemPrazo;
        }

        // Conversa encerrada congela no resultado que teve na entrega. Mostrar
        // "atrasada" para atendimento ja concluido so poluiria o painel.
        var referencia = status == StatusConversa.Encerrada && encerradaEm.HasValue
            ? encerradaEm.Value
            : agora;

        if (referencia > prazoLimite.Value)
        {
            return SituacaoPrazo.Atrasada;
        }

        if (prazoAlertaEm.HasValue && referencia >= prazoAlertaEm.Value)
        {
            return SituacaoPrazo.EmAlerta;
        }

        return SituacaoPrazo.EmDia;
    }
}
