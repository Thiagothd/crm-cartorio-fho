namespace CrmCartorio.Api.Modules.Tenants.Application;

/// <summary>
/// Configuracao da propria serventia.
/// </summary>
/// <remarks>
/// Slice minimo por enquanto: so a aparencia da conversa. CNS, endereco, horario
/// e jurisdicao ainda vivem em docs/CONTEXTO.md e entram aqui quando houver tela
/// de configuracao da serventia — adicionar coluna que nenhuma tela preenche seria
/// peso morto (docs/MODELO-DADOS.md, 3.1).
/// </remarks>
public interface ITenantService
{
    Task<AparenciaResponse> ObterAparenciaAsync(CancellationToken ct = default);

    Task<AparenciaResponse> DefinirAparenciaAsync(
        SalvarAparenciaRequest request,
        CancellationToken ct = default);
}
