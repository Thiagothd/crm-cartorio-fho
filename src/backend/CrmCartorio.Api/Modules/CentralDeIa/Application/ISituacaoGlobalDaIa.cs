namespace CrmCartorio.Api.Modules.CentralDeIa.Application;

public interface ISituacaoGlobalDaIa
{
    /// <summary>
    /// <paramref name="de"/>/<paramref name="ate"/> nulos caem no dia UTC
    /// corrente — mesmo recorte de <c>ISituacaoDaIa.ObterAsync</c>.
    /// </summary>
    Task<SituacaoGlobalDaIaResponse> ObterAsync(DateTime? de, DateTime? ate, CancellationToken ct = default);
}
