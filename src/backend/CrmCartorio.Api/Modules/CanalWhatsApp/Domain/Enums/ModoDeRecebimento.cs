namespace CrmCartorio.Api.Modules.CanalWhatsApp.Domain.Enums;

/// <summary>
/// Quem pode falar com o número pareado. Controla os dois sentidos — o que
/// entra pelo webhook e o que o painel consegue enviar (ver
/// <c>IRegrasDoCanal</c>).
/// </summary>
/// <remarks>
/// A coluna que guarda isto em <c>Tenants.WhatsappModoDeRecebimento</c> é
/// <c>TINYINT NULL</c>, e o <c>NULL</c> é um quarto estado por fora deste enum:
/// "nunca configurado pela tela". Nesse caso quem manda é
/// <c>CanalWhatsApp:NumerosDeTeste</c> do <c>appsettings</c> — o comportamento
/// que já está em produção. Assim que alguém salvar um modo pela tela, o banco
/// passa a mandar e o <c>appsettings</c> vira letra morta.
/// </remarks>
public enum ModoDeRecebimento : byte
{
    /// <summary>Sem filtro. Qualquer número entra e recebe envio.</summary>
    Todos = 0,

    /// <summary>Só os números da lista com <see cref="TipoDeNumero.Liberado"/> falam com o canal.</summary>
    SomenteLiberados = 1,

    /// <summary>Todo número fala com o canal, exceto os marcados <see cref="TipoDeNumero.Bloqueado"/>.</summary>
    TodosMenosBloqueados = 2
}
