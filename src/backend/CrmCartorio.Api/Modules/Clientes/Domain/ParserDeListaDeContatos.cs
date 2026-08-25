namespace CrmCartorio.Api.Modules.Clientes.Domain;

/// <summary>Uma linha lida do texto colado ou do CSV, ainda sem validação de telefone.</summary>
public readonly record struct LinhaDeContato(int NumeroDaLinha, string? Nome, string TelefoneBruto, string LinhaOriginal);

/// <summary>
/// Lê texto colado (ou o conteúdo de um CSV, que é a mesma coisa com separador
/// mais previsível) e devolve linhas candidatas a contato.
/// </summary>
/// <remarks>
/// Lógica pura, sem banco — mesmo motivo de <see cref="NomeDeContato"/>: é a
/// peça que o verificador cobre caso a caso sem infraestrutura.
///
/// <b>O que decide aqui:</b> qual separador o arquivo usa, se a primeira linha
/// é cabeçalho, e qual coluna é nome e qual é telefone. <b>O que NÃO decide
/// aqui:</b> se o telefone é válido (<see cref="TelefoneDeContato"/>) e se o
/// contato já existe (isso precisa do banco — vive no serviço da Application).
///
/// <b>Um só separador para o arquivo inteiro</b>, não por linha: detectar por
/// linha permitiria uma planilha com vírgula decimal ou nome com vírgula
/// confundir a coluna. Amostra as primeiras linhas e usa o que aparecer mais.
/// </remarks>
public static class ParserDeListaDeContatos
{
    private static readonly char[] SeparadoresCandidatos = ['\t', ';', ','];

    private static readonly string[] SinonimosDeNome = ["nome", "name", "cliente", "contato"];

    private static readonly string[] SinonimosDeTelefone =
        ["telefone", "tel", "celular", "whatsapp", "fone", "numero", "número", "phone", "cel"];

    public static IReadOnlyList<LinhaDeContato> Analisar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return [];
        }

        // \r\n, \r solto (Mac classico) e \n — o textarea do navegador e o
        // conteudo de um .csv exportado do Windows ou de fora podem trazer
        // qualquer um dos tres.
        var linhasBrutas = texto.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var separador = DetectarSeparador(linhasBrutas);

        var resultado = new List<LinhaDeContato>();
        var primeiraLinhaUtil = true;
        var indiceNome = 0;
        var indiceTelefone = 1;

        for (var i = 0; i < linhasBrutas.Length; i++)
        {
            var linha = linhasBrutas[i].Trim();

            if (linha.Length == 0)
            {
                continue;
            }

            var campos = DividirLinha(linha, separador);

            if (primeiraLinhaUtil)
            {
                primeiraLinhaUtil = false;

                if (EhCabecalho(campos))
                {
                    (indiceNome, indiceTelefone) = ResolverColunas(campos);
                    continue; // cabecalho nao vira linha de contato
                }
            }

            var (nome, telefone) = ExtrairNomeETelefone(campos, indiceNome, indiceTelefone);

            if (string.IsNullOrWhiteSpace(telefone))
            {
                continue; // linha sem nada que pareca telefone: ignorada, nao "invalida"
            }

            resultado.Add(new LinhaDeContato(i + 1, nome, telefone, linha));
        }

        return resultado;
    }

    /// <summary>
    /// Amostra as primeiras linhas não vazias e escolhe o separador mais
    /// frequente. Nenhum encontrado = <c>null</c>, e cada linha cai no
    /// fallback de espaços (<see cref="DividirLinha"/>).
    /// </summary>
    private static char? DetectarSeparador(string[] linhas)
    {
        var contagem = new Dictionary<char, int>();

        foreach (var separador in SeparadoresCandidatos)
        {
            contagem[separador] = 0;
        }

        var amostradas = 0;

        foreach (var linhaBruta in linhas)
        {
            var linha = linhaBruta.Trim();

            if (linha.Length == 0)
            {
                continue;
            }

            foreach (var separador in SeparadoresCandidatos)
            {
                contagem[separador] += linha.Count(c => c == separador);
            }

            if (++amostradas >= 20)
            {
                break;
            }
        }

        var melhor = contagem.Where(par => par.Value > 0).OrderByDescending(par => par.Value).FirstOrDefault();

        return melhor.Value > 0 ? melhor.Key : null;
    }

    /// <summary>
    /// Separador conhecido: divisão simples, com suporte a aspas duplas
    /// (comum em CSV exportado de planilha, para nome com vírgula). Sem
    /// separador detectado: tenta duas colunas separadas por 2+ espaços ou
    /// por um tab solto; sem isso, a linha inteira é um campo só.
    /// </summary>
    private static string[] DividirLinha(string linha, char? separador)
    {
        if (separador is { } sep)
        {
            return DividirComAspas(linha, sep);
        }

        var porTabOuEspacos = System.Text.RegularExpressions.Regex.Split(linha, @"\t|  +");

        return porTabOuEspacos.Length >= 2
            ? [porTabOuEspacos[0].Trim(), string.Join(' ', porTabOuEspacos[1..]).Trim()]
            : [linha];
    }

    private static string[] DividirComAspas(string linha, char separador)
    {
        var campos = new List<string>();
        var atual = new System.Text.StringBuilder();
        var dentroDeAspas = false;

        foreach (var caractere in linha)
        {
            if (caractere == '"')
            {
                dentroDeAspas = !dentroDeAspas;
            }
            else if (caractere == separador && !dentroDeAspas)
            {
                campos.Add(atual.ToString().Trim().Trim('"'));
                atual.Clear();
            }
            else
            {
                atual.Append(caractere);
            }
        }

        campos.Add(atual.ToString().Trim().Trim('"'));

        return [.. campos];
    }

    /// <summary>
    /// Primeira linha é cabeçalho quando NENHUM campo parece telefone (poucos
    /// dígitos) — um cabeçalho de verdade é feito de letras.
    /// </summary>
    private static bool EhCabecalho(string[] campos) =>
        campos.All(campo => campo.Count(char.IsDigit) < campo.Length / 2 + 1);

    /// <summary>
    /// Casa os rótulos do cabeçalho com os sinônimos conhecidos. Sem
    /// correspondência de telefone, assume a ordem padrão (nome, telefone) —
    /// era cabeçalho, só não um que reconhecemos.
    /// </summary>
    private static (int indiceNome, int indiceTelefone) ResolverColunas(string[] campos)
    {
        var normalizados = campos.Select(Normalizar).ToArray();

        var indiceTelefone = Array.FindIndex(normalizados, c => SinonimosDeTelefone.Contains(c));
        var indiceNome = Array.FindIndex(normalizados, c => SinonimosDeNome.Contains(c));

        if (indiceTelefone < 0)
        {
            return (0, campos.Length > 1 ? 1 : 0);
        }

        if (indiceNome < 0)
        {
            indiceNome = indiceTelefone == 0 && campos.Length > 1 ? 1 : 0;
        }

        return (indiceNome, indiceTelefone);
    }

    private static (string? nome, string telefone) ExtrairNomeETelefone(string[] campos, int indiceNome, int indiceTelefone)
    {
        if (campos.Length == 1)
        {
            // Uma coluna so: e o telefone. Nao adivinha nome de um campo so —
            // "5538999000045" e claramente telefone, "Joana" sozinha tambem
            // poderia ser, e adivinhar errado grava o telefone como nome.
            return (null, campos[0]);
        }

        var telefone = indiceTelefone < campos.Length ? campos[indiceTelefone] : string.Empty;
        var nome = indiceNome < campos.Length && indiceNome != indiceTelefone ? campos[indiceNome] : null;

        return (string.IsNullOrWhiteSpace(nome) ? null : nome, telefone);
    }

    private static string Normalizar(string valor)
    {
        var decomposto = valor.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var limpo = new System.Text.StringBuilder(decomposto.Length);

        foreach (var caractere in decomposto)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(caractere)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                limpo.Append(caractere);
            }
        }

        return limpo.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }
}
