using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;

namespace CrmCartorio.Api.Modules.Usuarios.Application;

// Entrada -------------------------------------------------------------------

public record LoginRequest(string Email, string Senha);

public record CriarAtendenteRequest(
    string Nome,
    string Email,
    string Senha,
    PerfilDeAtendente Perfil,
    Guid? SetorPadraoId,
    CargoNaServentia Cargo = CargoNaServentia.NaoInformado);

public record AtualizarAtendenteRequest(
    string Nome,
    string Email,
    PerfilDeAtendente Perfil,
    Guid? SetorPadraoId,
    bool Ativo,
    CargoNaServentia Cargo = CargoNaServentia.NaoInformado);

public record AlterarSenhaRequest(string SenhaAtual, string NovaSenha);

public record RedefinirSenhaRequest(string NovaSenha);

// Saida ---------------------------------------------------------------------

public record AtendenteResponse(
    Guid Id,
    string Nome,
    string Email,
    /// <summary>Permissao no sistema.</summary>
    PerfilDeAtendente Perfil,
    /// <summary>Funcao na serventia. Independente do perfil — ver CargoNaServentia.</summary>
    CargoNaServentia Cargo,
    /// <summary>Rotulo do cargo para a tela.</summary>
    string CargoDescrito,
    Guid? SetorPadraoId,
    string? SetorPadraoNome,
    bool Ativo,
    DateTime? UltimoAcessoEm);

public record LoginResponse(
    string Token,
    DateTime ExpiraEm,
    AtendenteResponse Atendente,
    TenantResponse Tenant);

public record TenantResponse(
    Guid Id,
    string RazaoSocial,
    string? NomeFantasia,
    string Cnpj,
    string? WhatsappNumeroExibicao,
    bool WhatsappConfigurado);

/// <summary>
/// Identificacao da serventia para a tela de LOGIN, antes de existir sessao.
/// </summary>
/// <remarks>
/// Deliberadamente magra: so o nome. CNPJ, telefone e qualquer outro dado ficam
/// no <see cref="TenantResponse"/>, que exige autenticacao — um endpoint anonimo
/// nao deve devolver mais do que a tela que o consome precisa desenhar.
/// </remarks>
public record ServentiaPublicaResponse(string Nome);
