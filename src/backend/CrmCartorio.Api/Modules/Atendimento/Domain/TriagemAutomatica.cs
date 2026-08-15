using System.Globalization;
using System.Text;

namespace CrmCartorio.Api.Modules.Atendimento.Domain;

/// <summary>
/// Menu de triagem do primeiro contato e leitura da resposta do cliente.
/// </summary>
/// <remarks>
/// Logica pura, sem banco e sem relogio, no mesmo lugar que
/// <see cref="AvaliadorDeSituacaoPrazo"/> — e regra de dominio, nao consulta.
///
/// O menu e a interpretacao da resposta moram na MESMA classe de proposito: os dois
/// dependem da mesma lista de setores na mesma ordem. Separados, uma alteracao de
/// ordenacao em um lado faria "1" levar ao setor errado no outro, e o sintoma
/// apareceria semanas depois, na conversa de um cliente real.
/// </remarks>
public static class TriagemAutomatica
{
    /// <summary>Setor oferecido no menu. A ordem da lista E a numeracao mostrada.</summary>
    public sealed record OpcaoDeSetor(Guid Id, string Nome);

    /// <summary>
    /// Minimo de caracteres para aceitar correspondencia parcial de nome.
    /// </summary>
    /// <remarks>
    /// Sem piso, um "a" solto casaria com "Tabelionato de Notas". Tres e o menor
    /// valor que ainda aceita as abreviacoes que uma pessoa realmente digita
    /// ("notas", "civil") sem transformar ruido em escolha.
    /// </remarks>
    private const int MinimoParaParcial = 3;

    /// <summary>
    /// Lista numerada dos setores, para o marcador {MenuSetores}.
    /// </summary>
    public static string MontarMenu(IReadOnlyList<OpcaoDeSetor> setores) =>
        string.Join(
            Environment.NewLine,
            setores.Select((s, i) => $"{i + 1} - {s.Nome}"));

    /// <summary>
    /// Setor escolhido pelo cliente, ou nulo quando a resposta nao corresponde a
    /// nenhuma opcao — ou corresponde a mais de uma.
    /// </summary>
    /// <remarks>
    /// Ambiguidade devolve nulo de proposito. Chutar entre dois setores manda o
    /// cliente para a fila errada e o erro so aparece quando alguem le a conversa;
    /// cair para atendimento humano, no mesmo caminho de quem digitou qualquer
    /// coisa, custa uma mensagem e nao erra.
    /// </remarks>
    public static Guid? InterpretarResposta(string? resposta, IReadOnlyList<OpcaoDeSetor> setores)
    {
        if (setores.Count == 0)
        {
            return null;
        }

        var texto = Normalizar(resposta);
        if (texto.Length == 0)
        {
            return null;
        }

        // 1. Numero da opcao. E o caminho que o menu pede, entao vem primeiro.
        if (int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out var escolha)
            && escolha >= 1
            && escolha <= setores.Count)
        {
            return setores[escolha - 1].Id;
        }

        var normalizados = setores
            .Select(s => (s.Id, Nome: Normalizar(s.Nome)))
            .ToList();

        // 2. Nome exato.
        var exatos = normalizados.Where(s => s.Nome == texto).ToList();
        if (exatos.Count == 1)
        {
            return exatos[0].Id;
        }

        // 3. Parcial, nas duas direcoes: "quero registro civil" contem o nome, e
        //    "notas" esta contido nele. Uma correspondencia so ou nenhuma.
        if (texto.Length >= MinimoParaParcial)
        {
            var parciais = normalizados
                .Where(s => s.Nome.Contains(texto, StringComparison.Ordinal)
                         || texto.Contains(s.Nome, StringComparison.Ordinal))
                .ToList();

            if (parciais.Count == 1)
            {
                return parciais[0].Id;
            }
        }

        return null;
    }

    /// <summary>
    /// Minusculas, sem acento, sem pontuacao e com espaco colapsado.
    /// </summary>
    /// <remarks>
    /// O cliente digita no WhatsApp, no celular, com pressa: "2." , "Registro Civil!",
    /// "REGISTRO CIVIL". Comparar o texto cru recusaria os tres.
    /// </remarks>
    private static string Normalizar(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return string.Empty;
        }

        var decomposto = valor.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var limpo = new StringBuilder(decomposto.Length);
        var espacoPendente = false;

        foreach (var caractere in decomposto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(caractere) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(caractere))
            {
                if (espacoPendente && limpo.Length > 0)
                {
                    limpo.Append(' ');
                }

                espacoPendente = false;
                limpo.Append(caractere);
            }
            else
            {
                // Pontuacao e espaco viram um separador so, adicionado apenas se
                // vier outra letra depois — assim "2." nao fica "2 ".
                espacoPendente = true;
            }
        }

        return limpo.ToString().Normalize(NormalizationForm.FormC);
    }
}
