namespace CrmCartorio.Api.Modules.CanalWhatsApp.Domain.Enums;

/// <summary>Por que uma linha de <c>CanalWhatsappConexoes</c> foi aberta ou fechada.</summary>
public enum MotivoDaConexao : byte
{
    /// <summary>Primeiro pareamento, ou repareamento depois de uma sessão caída.</summary>
    Pareamento = 1,

    /// <summary>Fechada porque alguém pediu para trocar de número pela tela.</summary>
    Troca = 2,

    /// <summary>Fechada porque alguém pediu para desconectar, sem trocar.</summary>
    Desconexao = 3,

    /// <summary>
    /// Fechada porque o gateway avisou <c>connection.update</c> = <c>close</c> sem
    /// pedido de ninguém pelo painel — sessão caída no celular, internet do
    /// aparelho, ou desvínculo feito no próprio WhatsApp.
    /// </summary>
    QuedaDoGateway = 4
}
