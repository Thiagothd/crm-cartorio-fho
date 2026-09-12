using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;

namespace CrmCartorio.Api.Infrastructure.Security;

/// <summary>
/// Identidade do atendente da requisicao atual, lida das claims do token.
/// Nenhum servico recebe TenantId por parametro vindo do corpo da requisicao —
/// isso permitiria a um atendente ler dado de outro cartorio.
/// </summary>
public interface IContextoDoUsuario
{
    bool Autenticado { get; }

    Guid TenantId { get; }

    Guid AtendenteId { get; }

    string Nome { get; }

    PerfilDeAtendente Perfil { get; }

    /// <summary>
    /// A checagem granular — ver <c>Modules/Usuarios/Domain/CatalogoDePermissoes</c>.
    /// Fail-closed: não autenticado, ou sessão sem permissões resolvidas,
    /// devolve <c>false</c> para qualquer <see cref="PermissaoDoSistema"/>.
    /// </summary>
    bool Pode(PermissaoDoSistema permissao);

    /// <summary>
    /// Blindagem LGPD: preenchido só quando esta sessão é de uma conta
    /// técnica com autorização de acesso técnico ativa neste exato instante
    /// (resolvido a cada requisição por <c>ValidadorDeSessao</c> — nunca uma
    /// claim do JWT, porque a autorização pode ser aprovada ou revogada a
    /// qualquer momento). Toda leitura registrada enquanto isto está
    /// preenchido carimba o <c>RegistroDeAcesso</c> com este id.
    /// </summary>
    Guid? AutorizacaoDeAcessoTecnicoId { get; }

    /// <summary>Preenchido só quando a autorização acima tem escopo de uma conversa específica — nulo em escopo geral.</summary>
    Guid? EscopoDeConversaId { get; }
}
