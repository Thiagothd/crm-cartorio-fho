namespace CrmCartorio.Api.Modules.CanalWhatsApp.Domain.Enums;

/// <summary>Por que uma mensagem recebida foi descartada antes de virar Cliente/Conversa.</summary>
public enum MotivoDoDescarte : byte
{
    /// <summary>Modo <see cref="ModoDeRecebimento.SomenteLiberados"/> e o número não está na lista.</summary>
    ForaDaLista = 1,

    /// <summary>Número presente em <see cref="TipoDeNumero.Bloqueado"/>.</summary>
    Bloqueado = 2
}
