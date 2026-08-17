using CrmCartorio.Api.Modules.Servicos.Domain.Enums;

namespace CrmCartorio.Api.Modules.Servicos.Application;

public record PrazoServicoRequest(int PrazoHoras, int PercentualAlerta, bool ApenasDiasUteis);

public record SalvarTipoServicoRequest(
    string Nome,
    string? Codigo,
    CategoriaServico Categoria,
    string? Descricao,
    bool Ativo,
    PrazoServicoRequest? Prazo,
    Guid? SetorId = null,
    string? DocumentosNecessarios = null);

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
    int ConversasAtivas,
    Guid? SetorId = null,
    string? SetorNome = null,
    string? DocumentosNecessarios = null);
