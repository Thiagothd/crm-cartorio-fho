namespace CrmCartorio.Api.Modules.Usuarios.Application;

// Identidade -----------------------------------------------------------------

/// <summary>
/// Quem esta operando o MFA — vem de <c>IContextoDoUsuario</c> (sessao JWT
/// normal) ou de um <c>DesafioDeMfa</c> ja validado (matricula privilegiada por
/// ticket). As duas fontes convergem aqui para que <see cref="IMfaService"/>
/// nao precise saber qual delas foi usada.
/// </summary>
public readonly record struct IdentidadeParaMfa(Guid TenantId, Guid AtendenteId);

// Entrada — etapa 1/2 do login ------------------------------------------------

public record ConcluirLoginMfaRequest(string Desafio, string Codigo);

// Saida — etapa 1 do login -----------------------------------------------------

/// <summary>
/// Resposta unica da etapa 1. <see cref="RequerMfa"/> decide qual dos dois
/// campos esta preenchido — nunca os dois, nunca nenhum.
/// </summary>
public record LoginEtapa1Response(bool RequerMfa, LoginResponse? Sessao, DesafioMfaResponse? Desafio);

public record DesafioMfaResponse(string Desafio, string Tipo, DateTime ExpiraEm);

// Entrada — gestao de MFA (sessao JWT) ----------------------------------------

public record IniciarMfaRequest(string SenhaAtual, string? CodigoAtual);

public record ConfirmarMfaRequest(string Codigo);

public record DesativarMfaRequest(string SenhaAtual, string Codigo);

public record RegenerarCodigosRequest(string SenhaAtual, string Codigo);

// Saida — gestao de MFA --------------------------------------------------------

public record IniciarMfaResponse(string OtpauthUri, string SegredoBase32, DateTime ExpiraEm);

public record ConfirmarMfaResponse(LoginResponse Sessao, IReadOnlyList<string> CodigosDeRecuperacao);

public record RegenerarCodigosResponse(IReadOnlyList<string> CodigosDeRecuperacao);

public record MfaSituacaoResponse(
    bool Ativo,
    bool Obrigatorio,
    int? CodigosDeRecuperacaoRestantes,
    DateTime? ConfirmadoEm);

// Matricula privilegiada (ticket, sem JWT) ------------------------------------

public record MatricularPrivilegiadoRequest(string Email, string Senha, string Ticket);

public record IniciarViaDesafioRequest(string Desafio);

public record ConfirmarViaDesafioRequest(string Desafio, string Codigo);

// Administrativo ----------------------------------------------------------------

public record EmitirTicketRequest(string SenhaDoAtor, string CodigoDoAtor);

public record TicketResponse(string Ticket, DateTime ExpiraEm);

public record ResetarMfaRequest(string SenhaDoAtor, string CodigoDoAtor, string Motivo);

public record AtivarObrigatoriedadeRequest(string SenhaDoAtor, string CodigoDoAtor, string Motivo);
