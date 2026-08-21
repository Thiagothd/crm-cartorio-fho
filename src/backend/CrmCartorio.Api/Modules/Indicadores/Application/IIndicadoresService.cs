namespace CrmCartorio.Api.Modules.Indicadores.Application;

public interface IIndicadoresService
{
    /// <summary>Aberto a qualquer autenticado — cada perfil vê os campos de saúde que sua permissão libera.</summary>
    Task<VisaoGeralResponse> ObterVisaoGeralAsync(FiltroDeIndicadores filtro, CancellationToken ct = default);

    /// <summary>Análise do período. Restrito a Supervisor/Administrador.</summary>
    Task<RelatorioResponse> ObterRelatorioAsync(FiltroDeIndicadores filtro, CancellationToken ct = default);

    /// <summary>Lista enxuta para o sino de alertas operacionais.</summary>
    Task<IReadOnlyList<AlertaOperacionalResponse>> ObterAlertasAsync(CancellationToken ct = default);

    // --------------------------------------------- Abas de Indicadores (gestão)
    // Um método por aba. A tela troca de aba sem recarregar o que já está na
    // mão, e nenhuma aba paga a agregação das outras três.

    /// <summary>Aba Dashboard: 12 meses de fluxo e as três janelas fixas. Sem filtro de período.</summary>
    Task<PainelResponse> ObterPainelAsync(CancellationToken ct = default);

    /// <summary>Aba Atendimentos.</summary>
    Task<RelatorioDeAtendimentosResponse> ObterRelatorioDeAtendimentosAsync(
        FiltroDeIndicadores filtro, CancellationToken ct = default);

    /// <summary>Aba Atendentes.</summary>
    Task<RelatorioDeAtendentesResponse> ObterRelatorioDeAtendentesAsync(
        FiltroDeIndicadores filtro, CancellationToken ct = default);

    /// <summary>Aba Clientes.</summary>
    Task<RelatorioDeClientesResponse> ObterRelatorioDeClientesAsync(
        FiltroDeIndicadores filtro, CancellationToken ct = default);
}
