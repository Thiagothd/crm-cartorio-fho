using System.Text.RegularExpressions;

namespace CrmCartorio.Api.Modules.RespostaInteligente.Application;

/// <summary>
/// Blindagem LGPD (2026-09): oculta CPF, CNPJ, telefone, e-mail e CEP do texto
/// que sai da nossa infraestrutura para o provedor de IA (Anthropic). Aplicado
/// SÓ ao histórico da conversa e à pergunta do cliente — nunca ao catálogo ou
/// ao conhecimento cadastrado, que é conteúdo nosso, não dado pessoal de
/// terceiro. Ver <see cref="RespostaInteligenteService.ResponderAsync"/>, o
/// único ponto do sistema que monta o pedido enviado ao provedor.
/// </summary>
/// <remarks>
/// <b>Limite honesto:</b> isto é reconhecimento por padrão de texto, não
/// reconhecimento de entidade — não pega nome próprio (não há como distinguir
/// "Maria" de dado pessoal com confiança suficiente sem inventar falso
/// positivo constante), e um CPF de 11 dígitos sem pontuação é
/// indistinguível de um telefone de 11 dígitos sem pontuação — nesse caso
/// específico, cai na regra de telefone primeiro. Reduz drasticamente o que
/// sai, não elimina o risco por completo; documentado assim em
/// docs/PROTECAO-DE-DADOS.md, não vendido como blindagem total.
/// </remarks>
public static partial class MascaradorDeDadosPessoais
{
    /// <summary>
    /// Ordem importa: cada regex roda sobre o texto JÁ mascarado pela
    /// anterior, então um padrão mais específico (CNPJ, 14 dígitos) precisa
    /// vir antes de um mais genérico que poderia recortar só um pedaço dele.
    /// </summary>
    public static string Mascarar(string texto)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return texto;
        }

        var resultado = RegexDeCnpj().Replace(texto, "[CNPJ]");
        resultado = RegexDeCpf().Replace(resultado, "[CPF]");
        resultado = RegexDeEmail().Replace(resultado, "[EMAIL]");
        resultado = RegexDeTelefone().Replace(resultado, "[TELEFONE]");
        resultado = RegexDeCep().Replace(resultado, "[CEP]");
        return resultado;
    }

    // 00.000.000/0000-00 ou 14 dígitos corridos.
    [GeneratedRegex(@"\b\d{2}\.?\d{3}\.?\d{3}\/?\d{4}-?\d{2}\b")]
    private static partial Regex RegexDeCnpj();

    // 000.000.000-00 ou 11 dígitos corridos com pontuação de CPF.
    [GeneratedRegex(@"\b\d{3}\.\d{3}\.\d{3}-\d{2}\b|\b\d{3}\.\d{3}\.\d{3}\d{2}\b")]
    private static partial Regex RegexDeCpf();

    [GeneratedRegex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}")]
    private static partial Regex RegexDeEmail();

    // Com ou sem +55, com ou sem parênteses/traço, com ou sem o 9º dígito —
    // cobre também o CPF de 11 dígitos corridos sem pontuação (ambíguo com
    // telefone; ver limite honesto na doc da classe).
    [GeneratedRegex(@"(?:\+?55\s?)?\(?\d{2}\)?[\s.-]?9?\d{4}[\s.-]?\d{4}\b")]
    private static partial Regex RegexDeTelefone();

    [GeneratedRegex(@"\b\d{5}-?\d{3}\b")]
    private static partial Regex RegexDeCep();
}
