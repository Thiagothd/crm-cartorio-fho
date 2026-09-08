namespace CrmCartorio.Api.Modules.RespostaInteligente.Application;

public record SalvarConhecimentoDeIaRequest(
    string Titulo,
    string Conteudo,
    string Tema,
    Guid? TipoServicoId,
    DateOnly? VigenteDesde,
    DateOnly? VigenteAte,
    bool Ativo);

public record ConhecimentoDeIaResponse(
    Guid Id,
    string Titulo,
    string Conteudo,
    string Tema,
    Guid? TipoServicoId,
    string? TipoServicoNome,
    DateOnly? VigenteDesde,
    DateOnly? VigenteAte,
    int Versao,
    string? CriadoPorNome,
    string? AtualizadoPorNome,
    bool Ativo,
    DateTime CriadoEm,
    DateTime AtualizadoEm);

public record VersaoDeConhecimentoDeIaResponse(
    int Versao,
    string Titulo,
    string Conteudo,
    string Tema,
    string? TipoServicoNome,
    DateOnly? VigenteDesde,
    DateOnly? VigenteAte,
    bool Ativo,
    string? AlteradoPorNome,
    DateTime OcorridoEm);
