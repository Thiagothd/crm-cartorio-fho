using System.Text.RegularExpressions;

namespace CrmCartorio.Api.Modules.RespostaInteligente.Application;

/// <summary>
/// Converte a formatação que o modelo de linguagem escreve (Markdown) para a do
/// WhatsApp, antes de a resposta da IA sair.
/// </summary>
/// <remarks>
/// O PROBLEMA QUE ISTO RESOLVE
///     Teste de 2026-09-30: a IA respondeu "**Alteração de nome**". No WhatsApp o
///     negrito é UM asterisco de cada lado; com dois, ele deixa em negrito só o miolo
///     e mostra os asteriscos de fora — o cliente lê "*Alteração de nome*". O prompt
///     pede o formato certo (<see cref="PromptDeSistema"/>, regra 5), mas modelo
///     escorrega; esta conversão é a garantia, e roda em toda resposta, de qualquer
///     provedor.
///
/// O QUE CONVERTE
///     **negrito** e ***negrito*** → *negrito*; __itálico__ → _itálico_;
///     "# Título" no começo da linha → *Título*; "- item", "* item" e "+ item" no
///     começo da linha → "• item"; [texto](https://…) → "texto: https://…".
///     O que já está no formato do WhatsApp passa sem mudança.
///
/// Lógica pura, sem estado.
/// </remarks>
public static partial class FormatacaoDeWhatsapp
{
    [GeneratedRegex(@"^[ \t]*#{1,6}[ \t]+(.+?)[ \t#]*$", RegexOptions.Multiline)]
    private static partial Regex Titulo();

    [GeneratedRegex(@"^([ \t]*)[-*+][ \t]+", RegexOptions.Multiline)]
    private static partial Regex ItemDeLista();

    [GeneratedRegex(@"\*{2,3}(?=\S)(.+?)(?<=\S)\*{2,3}")]
    private static partial Regex NegritoDuplo();

    [GeneratedRegex(@"__(?=\S)(.+?)(?<=\S)__")]
    private static partial Regex ItalicoDuplo();

    [GeneratedRegex(@"\[([^\]\n]+)\]\((https?://[^)\s]+)\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"\*{2,}")]
    private static partial Regex AsteriscosSobrando();

    public static string Ajustar(string? texto)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return texto ?? string.Empty;
        }

        var t = texto.Replace("\r\n", "\n");

        t = Titulo().Replace(t, m => $"*{m.Groups[1].Value.Trim('*', ' ')}*");
        t = ItemDeLista().Replace(t, m => $"{m.Groups[1].Value}• ");
        t = NegritoDuplo().Replace(t, "*$1*");
        t = ItalicoDuplo().Replace(t, "_$1_");
        t = Link().Replace(t, "$1: $2");

        // Par desencontrado ("**Atenção:" sem fechar) não vira negrito em lugar
        // nenhum; sobra só o asterisco repetido, que no WhatsApp aparece literal.
        t = AsteriscosSobrando().Replace(t, "*");

        return t;
    }
}
