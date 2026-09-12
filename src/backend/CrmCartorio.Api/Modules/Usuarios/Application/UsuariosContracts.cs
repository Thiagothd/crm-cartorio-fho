using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;

namespace CrmCartorio.Api.Modules.Usuarios.Application;

// ---------------------------------------------------- Permissoes granulares

public record PermissaoDoCatalogoResponse(
    string Chave, string Rotulo, string Descricao, string Bloco, bool EhTela);

public record ModeloDePermissaoResponse(
    ModeloDePermissao Modelo, string Rotulo, string Descricao, IReadOnlyList<string> Chaves);

public record PermissoesDoAtendenteResponse(
    Guid AtendenteId,
    /// <summary>Nulo quando o atendente segue a Classe da conta (nunca personalizou).</summary>
    ModeloDePermissao? Modelo,
    IReadOnlyList<string> Chaves,
    IReadOnlyList<PermissaoDoCatalogoResponse> Catalogo,
    IReadOnlyList<ModeloDePermissaoResponse> Modelos);

public record DefinirPermissoesRequest(ModeloDePermissao? Modelo, IReadOnlyList<string> Chaves);

// Entrada -------------------------------------------------------------------

public record LoginRequest(string Email, string Senha);

public record CriarAtendenteRequest(
    string Nome,
    string Email,
    string Senha,
    PerfilDeAtendente Perfil,
    Guid? SetorPadraoId,
    CargoNaServentia Cargo = CargoNaServentia.NaoInformado,
    /// <summary>
    /// Blindagem LGPD: marca a conta como de suporte tecnico — nunca lê
    /// dado de cliente por permissão concedida direto, só por autorização de
    /// acesso técnico ativa. Ver <see cref="Domain.Entities.Atendente.EhContaTecnica"/>.
    /// </summary>
    bool EhContaTecnica = false);

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
    /// <summary>Classe da conta — exigencia de seguranca (MFA obrigatorio, protecao do Titular). Nao e o que a pessoa acessa; isso e Permissoes/Modelo.</summary>
    PerfilDeAtendente Perfil,
    /// <summary>Funcao na serventia. Independente da Classe — ver CargoNaServentia.</summary>
    CargoNaServentia Cargo,
    /// <summary>Rotulo do cargo para a tela.</summary>
    string CargoDescrito,
    Guid? SetorPadraoId,
    string? SetorPadraoNome,
    bool Ativo,
    DateTime? UltimoAcessoEm,
    /// <summary>Verificacao em duas etapas confirmada e ativa nesta conta.</summary>
    bool MfaAtivo,
    /// <summary>Nulo quando o atendente segue a Classe da conta (nunca personalizou permissoes).</summary>
    ModeloDePermissao? Modelo,
    /// <summary>
    /// O conjunto EFETIVO de permissoes (chave de texto — ver CatalogoDePermissoes),
    /// ja resolvido: se Permissoes for NULL no banco, aqui vem o preset da
    /// Classe da conta. O frontend usa isto para decidir o que mostrar no
    /// menu — o backend continua sendo quem decide de verdade em cada
    /// endpoint (ctx.Pode), esconder aqui e so conforto de navegacao.
    /// </summary>
    IReadOnlyList<string> Permissoes,
    /// <summary>Blindagem LGPD — decidido só na criação, nunca editável depois.</summary>
    bool EhContaTecnica = false);

/// <summary>Projecao minima para o seletor de "transferir para" do balcao — nunca e-mail, perfil ou ultimo acesso.</summary>
public record AtendenteOpcaoResponse(Guid Id, string Nome);

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
