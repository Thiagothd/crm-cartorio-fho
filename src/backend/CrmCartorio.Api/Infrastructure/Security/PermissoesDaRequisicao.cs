using CrmCartorio.Api.Modules.Usuarios.Domain;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;

namespace CrmCartorio.Api.Infrastructure.Security;

/// <summary>
/// Depósito, por requisição (scoped), do conjunto de permissões já resolvido
/// pelo <see cref="SessaoAtivaHandler"/>. Existe para <see cref="ContextoDoUsuario"/>
/// não precisar consultar o banco de novo: o handler já leu
/// <c>Atendente.Permissoes</c>/<c>Perfil</c> ao validar a sessão (SEC-01), e
/// deposita o conjunto derivado aqui.
/// </summary>
/// <remarks>
/// Vazio (<see cref="Definido"/> == false) significa "sessão não passou pelo
/// handler" — falha fechado: <see cref="ContextoDoUsuario.Pode"/> devolve
/// <c>false</c> para tudo nesse caso. Isso é inalcançável em qualquer
/// endpoint autenticado de verdade (a policy padrão do <c>Program.cs</c>
/// sempre inclui <see cref="SessaoAtivaRequirement"/>), mas é a rede de
/// segurança correta a ter mesmo assim.
/// </remarks>
public class PermissoesDaRequisicao
{
    private IReadOnlySet<PermissaoDoSistema>? _conjunto;

    public bool Definido => _conjunto is not null;

    public void Definir(IReadOnlySet<PermissaoDoSistema> conjunto) => _conjunto = conjunto;

    public bool Pode(PermissaoDoSistema permissao) => _conjunto?.Contains(permissao) ?? false;
}
