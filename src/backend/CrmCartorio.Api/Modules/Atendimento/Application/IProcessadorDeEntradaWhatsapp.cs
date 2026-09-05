namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <summary>
/// Processa, em ordem e sob coordenacao do SQL Server, as mensagens que ja foram
/// aceitas duravelmente pelo webhook.
/// </summary>
public interface IProcessadorDeEntradaWhatsapp
{
    Task ProcessarConversaAsync(Guid conversaId, CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> ListarConversasPendentesAsync(
        int limite = 100,
        CancellationToken ct = default);
}
