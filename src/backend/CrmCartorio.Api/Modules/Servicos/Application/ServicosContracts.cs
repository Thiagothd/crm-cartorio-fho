using CrmCartorio.Api.Modules.Servicos.Domain.Enums;

namespace CrmCartorio.Api.Modules.Servicos.Application;

public record PrazoServicoRequest(int PrazoHoras, int PercentualAlerta, bool ApenasDiasUteis);

public record SalvarTipoServicoRequest(
    string Nome,
    string? Codigo,
    CategoriaServico Categoria,
    string? Descricao,
    bool Ativo,
    PrazoServicoRequest? Prazo);

public record PrazoServicoResponse(
    Guid Id,
    int PrazoHoras,
    int PercentualAlerta,
    bool ApenasDiasUteis,
    string PrazoDescrito);

public record TipoServicoResponse(
    Guid Id,
    string Nome,
    string? Codigo,
    CategoriaServico Categoria,
    string CategoriaDescrita,
    string? Descricao,
    bool Ativo,
    PrazoServicoResponse? Prazo,
    int ConversasAtivas);
