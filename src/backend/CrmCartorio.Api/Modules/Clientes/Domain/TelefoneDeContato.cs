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
    /// 12 dígitos com DDD válido e o primeiro dígito do assinante fora das
    /// faixas 2–5 (fixo) e 6–9 (celular legado) do plano de numeração da
    /// Anatel — caso residual, não deveria acontecer em número brasileiro
    /// real. Ver <see cref="TelefoneDeContato"/>.
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
/// 2. <b>Decidir 12 dígitos com DDD válido pelo plano de numeração, não por
///    aceitar tudo cegamente.</b> O primeiro dígito do assinante (logo após o
///    DDD) já diz o tipo, por regra da Anatel que vale para o Brasil inteiro:
///    <c>2</c>–<c>5</c> é fixo (8 dígitos, já completo, nada a inserir);
///    <c>6</c>–<c>9</c> é celular na forma legada (sem o nono, que existe só
///    para não faltar numeração de celular — fixo nunca precisou). Isso não é
///    a mesma aposta que
///    <see cref="Modules.CanalWhatsApp.Application.NormalizadorDeTelefone.CanonicalizarMovelBrasileiro"/>
///    faz (aquele insere o nono em QUALQUER 12 dígitos, seguro só porque ali
///    quem chama é o dono da própria sessão pareada — fixo nunca aparece,
///    porque o WhatsApp exige celular para parear). Aqui, telefone de
///    CONTATO pode ser fixo de verdade, então o dígito do assinante é quem
///    decide, não o comprimento sozinho.
///    <see cref="SituacaoDoTelefone.Ambiguo"/> sobra só para o dígito fora
///    das duas faixas — teoricamente não deveria acontecer em número
///    brasileiro real, mas nunca inventa um número quando acontece.
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
        if (digitos.Length == 13)
        {
            return new ResultadoDoTelefone(SituacaoDoTelefone.Valido, digitos);
        }

        // 12 digitos com DDD valido: o primeiro digito do assinante (posicao 4,
        // logo apos os dois do DDD) decide pelo plano de numeracao da Anatel —
        // ver o <remarks> da classe. "55381234567" -> assinante comeca em "1",
        // indice 4.
        return digitos[4] switch
        {
            >= '2' and <= '5' => new ResultadoDoTelefone(SituacaoDoTelefone.Valido, digitos),
            >= '6' and <= '9' => new ResultadoDoTelefone(SituacaoDoTelefone.Valido, digitos.Insert(4, "9")),
            _ => new ResultadoDoTelefone(SituacaoDoTelefone.Ambiguo, null)
        };
    }

    /// <summary>
    /// As formas sob as quais o MESMO contato pode estar gravado em
    /// <c>Clientes.TelefoneWhatsapp</c>, dado o número que chegou pelo
    /// WhatsApp (só dígitos, E.164). O próprio número vem sempre primeiro.
    /// </summary>
    /// <remarks>
    /// <b>Bug que isto corrige (produção, 2026-10-28):</b> o atendente chamou
    /// pelo painel uma cliente cadastrada com o celular completo (13 dígitos,
    /// com o nono); quando ela respondeu, o WhatsApp entregou o remetente na
    /// forma legada (12 dígitos, sem o nono) e a recepção, comparando por
    /// igualdade exata, criou OUTRO cliente e OUTRO atendimento — na fila, com
    /// o robô ativo.
    ///
    /// Mesma regra de
    /// <c>CanalWhatsApp.Application.NormalizadorDeTelefone.NumerosEquivalentes</c>
    /// (que o verificador do canal confere contra esta, par a par), escrita de
    /// novo aqui porque o Atendimento não referencia o <c>Application</c> de
    /// outro módulo (CLAUDE.md, 7.1) — como <see cref="NomeDeContato"/>, mora no
    /// <c>Domain</c> de Clientes, que é dono do número.
    ///
    /// Segura nos dois sentidos, e por isso NÃO é a aposta de
    /// <see cref="Interpretar"/>: aqui não se inventa número nenhum, só se
    /// procura o cadastro pela outra forma — e só quando ela é inequivocamente
    /// celular:
    /// <list type="bullet">
    ///   <item>13 dígitos, 55, DDD válido, nono dígito e o dígito seguinte em
    ///   6–9 → também a forma sem o nono;</item>
    ///   <item>12 dígitos, 55, DDD válido e primeiro dígito do assinante em 6–9
    ///   → também a forma com o nono.</item>
    /// </list>
    /// Fixo (assinante em 2–5), número estrangeiro ou qualquer outro formato
    /// volta só com ele mesmo. A lista devolvida é o que vai no <c>IN</c> da
    /// consulta — montada em C#, para o filtro traduzir para SQL.
    /// </remarks>
    public static IReadOnlyList<string> FormasDoMesmoNumero(string telefoneE164)
    {
        if (string.IsNullOrEmpty(telefoneE164))
        {
            return [];
        }

        if (telefoneE164.Length == 13
            && telefoneE164.StartsWith("55", StringComparison.Ordinal)
            && DddValido(telefoneE164)
            && telefoneE164[4] == '9'
            && telefoneE164[5] is >= '6' and <= '9'
            && telefoneE164.All(char.IsAsciiDigit))
        {
            return [telefoneE164, telefoneE164.Remove(4, 1)];
        }

        if (telefoneE164.Length == 12
            && telefoneE164.StartsWith("55", StringComparison.Ordinal)
            && DddValido(telefoneE164)
            && telefoneE164[4] is >= '6' and <= '9'
            && telefoneE164.All(char.IsAsciiDigit))
        {
            return [telefoneE164, telefoneE164.Insert(4, "9")];
        }

        return [telefoneE164];
    }

    private static string SomenteDigitos(string valor) =>
        new(valor.Where(char.IsDigit).ToArray());

    private static bool DddValido(string numero) =>
        numero.Length >= 4
        && int.TryParse(numero.AsSpan(2, 2), out var ddd)
        && DddsBrasileiros.Contains(ddd);
}
