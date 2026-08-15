using CrmCartorio.Api.Modules.Servicos.Domain.Entities;

namespace CrmCartorio.Api.Modules.Servicos.Domain;

/// <summary>
/// Converte o SLA configurado em uma data-limite concreta.
/// </summary>
public static class CalculadoraDePrazo
{
    /// <summary>
    /// Avanca <paramref name="prazo"/>.PrazoHoras a partir de <paramref name="inicio"/>.
    /// Com ApenasDiasUteis, sabado e domingo nao consomem prazo: o cartorio esta
    /// fechado e cobrar o cliente por isso seria mentira do contador.
    /// </summary>
    public static DateTime CalcularLimite(DateTime inicio, PrazoServico prazo)
    {
        if (prazo.PrazoHoras <= 0)
        {
            return inicio;
        }

        if (!prazo.ApenasDiasUteis)
        {
            return inicio.AddHours(prazo.PrazoHoras);
        }

        var atual = inicio;
        var restante = TimeSpan.FromHours(prazo.PrazoHoras);

        while (restante > TimeSpan.Zero)
        {
            if (EhFimDeSemana(atual))
            {
                // Pula para a meia-noite do proximo dia sem consumir prazo.
                atual = atual.Date.AddDays(1);
                continue;
            }

            var fimDoDia = atual.Date.AddDays(1);
            var disponivelNoDia = fimDoDia - atual;

            if (restante <= disponivelNoDia)
            {
                return atual.Add(restante);
            }

            restante -= disponivelNoDia;
            atual = fimDoDia;
        }

        return atual;
    }

    private static bool EhFimDeSemana(DateTime data) =>
        data.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
}
