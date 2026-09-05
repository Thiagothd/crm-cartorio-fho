using CrmCartorio.Api.Modules.RespostaInteligente.Application;

namespace CrmCartorio.Api.Modules.CentralDeIa.Application;

public record EntrarNaCentralDeIaRequest(string Segredo);

public record EntrarNaCentralDeIaResponse(string Token, DateTime ExpiraEm);

/// <summary>
/// Consolidado de uma serventia para o painel do desenvolvedor. Ao contrário de
/// <c>ResumoDaIaResponse</c>/<c>SituacaoDaIaResponse</c> (Modules/RespostaInteligente),
/// que nunca saem do próprio tenant do token, aqui o nome e os números de
/// QUALQUER tenant aparecem lado a lado — é exatamente o que esta tela existe
/// para mostrar.
/// </summary>
public record SituacaoPorTenantResponse(
    Guid TenantId,
    string NomeTenant,
    int TotalDeChamadas,
    int Sucessos,
    int Falhas,
    int AtendidasPeloGemini,
    int AtendidasPeloHaiku,
    decimal CustoEstimadoUsd,
    decimal CustoEstimadoReais,
    MotivosDeFallbackResponse MotivosDeFallback);

/// <summary>
/// Estado do disjuntor do Gemini — global ao PROCESSO
/// (<c>Cota/DisjuntorDoGemini</c>), não por tenant. Por isso aparece uma vez só
/// no topo do painel, e não dentro de cada linha de <see cref="SituacaoPorTenantResponse"/>.
/// </summary>
public record EstadoGlobalDoGeminiResponse(string Status, DateTime? VoltaATentarEm, string? Motivo);

public record SituacaoGlobalDaIaResponse(
    EstadoGlobalDoGeminiResponse Gemini,
    IReadOnlyList<SituacaoPorTenantResponse> PorTenant,
    DateTime PeriodoDe,
    DateTime PeriodoAte);
