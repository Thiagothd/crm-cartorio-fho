namespace CrmCartorio.Api.Modules.CanalWhatsApp.Application;

/// <summary>
/// Normaliza telefone digitado pelo atendente para E.164 só-dígitos.
/// </summary>
/// <remarks>
/// Mesma regra de <c>Clientes.Application.ClienteService.ValidarTelefone</c>:
/// número de 10/11 dígitos (sem código do país) recebe o 55 automaticamente —
/// no balcão ninguém digita "+55". Duplicada aqui, e não referenciada de lá,
/// porque os dois módulos não compartilham <c>Application</c> entre si
/// (docs/CONVENCOES.md, 7.1) — só entidade lida pelo <c>DbContext</c> atravessa a
/// fronteira.
/// </remarks>
public static class NormalizadorDeTelefone
{
    private static readonly HashSet<int> DddsBrasileiros =
    [
        11, 12, 13, 14, 15, 16, 17, 18, 19,
        21, 22, 24, 27, 28,
        31, 32, 33, 34, 35, 37, 38,
        41, 42, 43, 44, 45, 46, 47, 48, 49,
        51, 53, 54, 55,
        61, 62, 63, 64, 65, 66, 67, 68, 69,
        71, 73, 74, 75, 77, 79,
        81, 82, 83, 84, 85, 86, 87, 88, 89,
        91, 92, 93, 94, 95, 96, 97, 98, 99
    ];

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

    /// <summary>
    /// Insere o nono dígito quando o valor for a forma legada (12 dígitos, sem
    /// nono) de um celular brasileiro. Qualquer outro formato — fixo, já
    /// canônico, ou fora do Brasil — volta inalterado.
    /// </summary>
    /// <remarks>
    /// Existe porque <c>ownerJid</c> do gateway devolve o celular pareado nessa
    /// forma legada — visto ao vivo em 2026-08-22: a instância
    /// <c>cartorio-teste</c> devolveu <c>553898000901@s.whatsapp.net</c> para o
    /// número real <c>+55 38 99800-0901</c>. Seguro só porque quem chama é o
    /// dono da própria sessão pareada: WhatsApp exige celular para criar conta,
    /// então um fixo nunca aparece aqui. Não usar para telefone de cliente —
    /// ali 12 dígitos pode ser fixo de verdade, e completar o nono inventaria
    /// um número que não existe.
    /// </remarks>
    public static string CanonicalizarMovelBrasileiro(string digitosSoDigitos)
    {
        if (digitosSoDigitos.Length != 12
            || !digitosSoDigitos.StartsWith("55", StringComparison.Ordinal)
            || !DddValido(digitosSoDigitos))
        {
            return digitosSoDigitos;
        }

        return digitosSoDigitos.Insert(4, "9");
    }

    /// <summary>
    /// Dois números (já só-dígitos) representam o mesmo celular brasileiro?
    /// Compara igualdade exata primeiro, e só então a forma legada do WhatsApp
    /// (12 dígitos, sem o nono) contra a canônica (13 dígitos, com o nono).
    /// </summary>
    /// <remarks>
    /// Achado ao vivo em 2026-09-09: a checagem de "Somente os liberados" e
    /// "Todos, menos os bloqueados" (<see cref="Application.RegrasDoCanal"/>)
    /// comparava só igualdade exata, e o WhatsApp entregou o número liberado
    /// de uma atendente ora com o nono dígito, ora sem — bloqueando mensagens
    /// dela mesma, já cadastrada, porque as duas formas nunca batiam. Extraído
    /// daqui, e não duplicado, porque <see cref="OpcoesDoCanalWhatsApp"/> já
    /// resolvia exatamente este mesmo problema para a lista de bloqueio do
    /// appsettings — a mesma equivalência vale para as duas fontes.
    ///
    /// Deliberadamente NÃO insere o nono dígito de um número de 12 dígitos
    /// isolado (ver <see cref="CanonicalizarMovelBrasileiro"/>): aqui os DOIS
    /// lados vêm de fontes diferentes (cadastro vs. remetente da mensagem), e
    /// o de 12 dígitos pode legitimamente ser um fixo — comparar as duas
    /// formas possíveis é seguro; assumir que é sempre celular não é.
    /// </remarks>
    public static bool NumerosEquivalentes(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return false;
        }

        if (a.Equals(b, StringComparison.Ordinal))
        {
            return true;
        }

        return SaoFormasDoMesmoMovelBrasileiro(a, b);
    }

    /// <summary>
    /// 13 dígitos, DDI 55, DDD brasileiro válido, e nono dígito (índice 4) — a
    /// forma completa de um celular brasileiro, a única capaz de "perder" o
    /// nono e ainda ser inequivocamente um celular (nunca um fixo).
    /// </summary>
    public static bool EhMovelBrasileiroCanonico(string numero) =>
        numero.Length == 13
        && numero.StartsWith("55", StringComparison.Ordinal)
        && DddValido(numero)
        && numero[4] == '9';

    private static bool SaoFormasDoMesmoMovelBrasileiro(string primeiro, string segundo)
    {
        var comNono = primeiro.Length == 13 ? primeiro : segundo;
        var semNono = primeiro.Length == 12 ? primeiro : segundo;

        return EhMovelBrasileiroCanonico(comNono)
            && comNono[5] is >= '6' and <= '9'
            && semNono.Length == 12
            && semNono.StartsWith("55", StringComparison.Ordinal)
            && DddValido(semNono)
            && comNono.Remove(4, 1).Equals(semNono, StringComparison.Ordinal);
    }

    private static bool DddValido(string numero) =>
        numero.Length >= 4
        && int.TryParse(numero.AsSpan(2, 2), out var ddd)
        && DddsBrasileiros.Contains(ddd);
}
