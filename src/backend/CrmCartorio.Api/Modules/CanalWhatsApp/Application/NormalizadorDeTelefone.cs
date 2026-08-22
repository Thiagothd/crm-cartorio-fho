namespace CrmCartorio.Api.Modules.CanalWhatsApp.Application;

/// <summary>
/// Normaliza telefone digitado pelo atendente para E.164 só-dígitos.
/// </summary>
/// <remarks>
/// Mesma regra de <c>Clientes.Application.ClienteService.ValidarTelefone</c>:
/// número de 10/11 dígitos (sem código do país) recebe o 55 automaticamente —
/// no balcão ninguém digita "+55". Duplicada aqui, e não referenciada de lá,
/// porque os dois módulos não compartilham <c>Application</c> entre si
/// (CLAUDE.md, 7.1) — só entidade lida pelo <c>DbContext</c> atravessa a
/// fronteira.
/// </remarks>
public static class NormalizadorDeTelefone
{
    /// <summary>Valida e devolve em E.164 só-dígitos. Lança <see cref="CanalWhatsAppServiceException"/> se inválido.</summary>
    public static string ParaE164(string? telefone)
    {
        var digitos = SomenteDigitos(telefone ?? string.Empty);

        if (digitos.Length is 10 or 11)
        {
            digitos = "55" + digitos;
        }

        if (digitos.Length is < 12 or > 15)
        {
            throw CanalWhatsAppServiceException.TelefoneInvalido();
        }

        return digitos;
    }

    public static string SomenteDigitos(string valor) =>
        new(valor.Where(char.IsDigit).ToArray());
}
