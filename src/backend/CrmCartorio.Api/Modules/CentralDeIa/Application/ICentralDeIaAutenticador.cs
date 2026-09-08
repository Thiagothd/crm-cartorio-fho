namespace CrmCartorio.Api.Modules.CentralDeIa.Application;

public interface ICentralDeIaAutenticador
{
    Task<EntrarNaCentralDeIaResponse> EntrarAsync(string segredo, CancellationToken ct = default);
}
