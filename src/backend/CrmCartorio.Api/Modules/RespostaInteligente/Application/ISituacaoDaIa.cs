namespace CrmCartorio.Api.Modules.RespostaInteligente.Application;

/// <summary>
/// Estado da IA para a tela Automação → IA. Nunca expõe a chave — só o que
/// ajuda a responder "a IA está funcionando e quanto ela já usou hoje".
/// </summary>
public record SituacaoDaIaResponse(
    bool Habilitada,
    bool EstaConfigurada,
    string Modelo,
    int LimiteDiario,
    int UsadoHoje);

public interface ISituacaoDaIa
{
    Task<SituacaoDaIaResponse> ObterAsync(CancellationToken ct = default);
}
