namespace CrmCartorio.Api.Modules.CentralDeIa.Application;

public record EntrarNaCentralDeIaRequest(string Segredo);

public record EntrarNaCentralDeIaResponse(string Token, DateTime ExpiraEm);
