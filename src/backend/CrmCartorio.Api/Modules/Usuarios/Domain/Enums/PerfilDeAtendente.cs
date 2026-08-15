namespace CrmCartorio.Api.Modules.Usuarios.Domain.Enums;

public enum PerfilDeAtendente : byte
{
    /// <summary>Opera as proprias conversas.</summary>
    Atendente = 1,

    /// <summary>Atendente + transferir conversa de terceiro, reabrir e gerir cadastros.</summary>
    Supervisor = 2,

    /// <summary>Supervisor + gerir atendentes.</summary>
    Administrador = 3
}
