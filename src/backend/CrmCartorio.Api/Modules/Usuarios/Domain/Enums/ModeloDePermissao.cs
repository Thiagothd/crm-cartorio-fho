namespace CrmCartorio.Api.Modules.Usuarios.Domain.Enums;

/// <summary>
/// Rótulo do último preset de permissões aplicado a um atendente — nunca a
/// verdade. A verdade é sempre <see cref="Entities.Atendente.Permissoes"/>.
/// Guardar só o rótulo permite a tela dizer "Supervisão · 2 ajustes" e a
/// definição de um modelo evoluir numa release futura sem alterar
/// silenciosamente o que alguém já cadastrado pode fazer.
/// </summary>
public enum ModeloDePermissao : byte
{
    /// <summary>Só recebe e encaminha — a mais enxuta.</summary>
    Recepcao = 1,

    /// <summary>Atende do começo ao fim. Preset de quem tem Classe = Atendente.</summary>
    Balcao = 2,

    /// <summary>Cuida do catálogo e da automação, não atende.</summary>
    Catalogo = 3,

    /// <summary>Toca a equipe do dia. Preset de quem tem Classe = Supervisor.</summary>
    Supervisao = 4,

    /// <summary>Responde pelo sistema. Preset de quem tem Classe = Administrador.</summary>
    Administracao = 5,

    /// <summary>Vê tudo, não muda nada.</summary>
    Auditoria = 6,
}
