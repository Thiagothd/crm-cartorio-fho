namespace CrmCartorio.Api.Modules.Clientes.Domain;

/// <summary>
/// O que aconteceu ao tentar interpretar um telefone de contato para
/// IMPORTAÇÃO EM LOTE.
/// </summary>
public enum SituacaoDoTelefone
{
    /// <summary>E.164 válido, pronto para gravar.</summary>
    Valido,

    /// <summary>Vazio, letras demais, ou fora da faixa de 12–15 dígitos após o prefixo.</summary>
    FormatoInvalido,

    /// <summary>12 ou 13 dígitos com prefixo 55, mas o DDD não existe.</summary>
    DddInvalido,

    /// <summary>
    /// 12 dígitos com DDD válido — pode ser um fixo de verdade, ou um celular
    /// legado sem o nono dígito. Ver <see cref="TelefoneDeContato"/>.
    /// </summary>
    Ambiguo
}

/// <summary>Resultado de <see cref="TelefoneDeContato.Interpretar"/>.</summary>
public readonly record struct ResultadoDoTelefone(SituacaoDoTelefone Situacao, string? E164)
{
    public bool Valido => Situacao == SituacaoDoTelefone.Valido;
}

/// <summary>
/// Duas leituras de telefone, para dois contextos diferentes — e a diferença
/// entre elas é deliberada, não descuido.
/// </summary>
/// <remarks>
/// <b><see cref="ParaE164Basico"/> é a regra de sempre, extraída de
/// <c>ClienteService.ValidarTelefone</c> sem mudar UM bit de comportamento</b>:
/// 10/11 dígitos ganham o 55, a faixa aceita é 12–15, e não há checagem de
/// DDD. É o que o cadastro manual continua usando — um atendente digitando na
/// tela vê o erro na hora e corrige; validar DDD ali só atrapalharia números
/// legítimos que a lista de DDDs porventura não cobrisse.
///
/// <b><see cref="Interpretar"/> é mais desconfiada, e só a importação em lote
/// usa.</b> Duas coisas que o cadastro individual nunca precisou:
///
/// 1. <b>Validar o DDD.</b> Numa importação de 500 linhas, um DDD inexistente
///    vira uma chamada de gateway desperdiçada em sequência — e é esse padrão
///    (várias tentativas para número que não existe) que o WhatsApp lê como
///    automação suspeita.
/// 2. <b>Devolver <see cref="SituacaoDoTelefone.Ambiguo"/> em vez de aceitar
///    12 dígitos como válido.</b> Pode ser um fixo de verdade, ou um celular
///    brasileiro na forma legada (sem o nono). O gateway resolve essa mesma
///    ambiguidade em
///    <see cref="Modules.CanalWhatsApp.Application.NormalizadorDeTelefone.CanonicalizarMovelBrasileiro"/>,
///    mas só quando quem chama É o dono do número — ali um fixo nunca aparece,
///    porque o WhatsApp exige celular para parear. Telefone de CONTATO não tem
///    essa garantia: completar o nono a força inventaria um número que não
///    existe. Em 500 linhas, adivinhar cria clientes fantasma — melhor marcar
///    "revisar" e mostrar na tela.
/// </remarks>
public static class TelefoneDeContato
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

    /// <summary>A regra de sempre — ver o <c>&lt;remarks&gt;</c> da classe. Nunca lança.</summary>
    public static string? ParaE164Basico(string? bruto)
    {
        var digitos = SomenteDigitos(bruto ?? string.Empty);

        if (digitos.Length is 10 or 11)
        {
            digitos = "55" + digitos;
        }

        return digitos.Length is >= 12 and <= 15 ? digitos : null;
    }

    /// <summary>
    /// Classificação mais rígida, só para importação em lote. Nunca lança — o
    /// chamador decide o que fazer com cada situação.
    /// </summary>
    public static ResultadoDoTelefone Interpretar(string? bruto)
    {
        var digitos = ParaE164Basico(bruto);

        if (digitos is null)
        {
            return new ResultadoDoTelefone(SituacaoDoTelefone.FormatoInvalido, null);
        }

        // Faixa internacional (>13 digitos, ou 12/13 sem prefixo 55): sem lista
        // de DDD para conferir, aceita como esta — a importacao e para a
        // agenda do cartorio, majoritariamente brasileira, e recusar todo
        // numero estrangeiro seria pior que aceitar sem o crivo extra.
        if (digitos.Length is not (12 or 13) || !digitos.StartsWith("55", StringComparison.Ordinal))
        {
            return new ResultadoDoTelefone(SituacaoDoTelefone.Valido, digitos);
        }

        if (!DddValido(digitos))
        {
            return new ResultadoDoTelefone(SituacaoDoTelefone.DddInvalido, null);
        }

        // 13 digitos com DDD valido: 55 + DDD + 9 + numero. Sem ambiguidade.
        // 12 digitos com DDD valido: fixo de verdade, ou celular sem o nono —
        // ver o <remarks> da classe.
        return digitos.Length == 13
            ? new ResultadoDoTelefone(SituacaoDoTelefone.Valido, digitos)
            : new ResultadoDoTelefone(SituacaoDoTelefone.Ambiguo, null);
    }

    private static string SomenteDigitos(string valor) =>
        new(valor.Where(char.IsDigit).ToArray());

    private static bool DddValido(string numero) =>
        numero.Length >= 4
        && int.TryParse(numero.AsSpan(2, 2), out var ddd)
        && DddsBrasileiros.Contains(ddd);
}
