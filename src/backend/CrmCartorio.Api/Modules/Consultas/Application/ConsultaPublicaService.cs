using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CrmCartorio.Api.Modules.Consultas.Application;

/// <summary>
/// Consulta CEP e CNPJ na BrasilAPI, com o ViaCEP de reserva para CEP.
/// </summary>
/// <remarks>
/// A consulta sai do SERVIDOR, nao do navegador de cada estacao. Tres motivos:
/// o cache aqui atende a serventia inteira — cartorio atende a mesma cidade o dia
/// todo, entao o mesmo punhado de CEP responde de memoria e continua respondendo
/// com o link caido; so a maquina do servidor precisa de saida para internet; e o
/// endereco do terceiro fica num appsettings, onde da para trocar ou desligar.
/// </remarks>
public partial class ConsultaPublicaService(
    HttpClient http,
    IMemoryCache cache,
    IOptions<OpcoesDeConsultaPublica> opcoes,
    ILogger<ConsultaPublicaService> log) : IConsultaPublicaService
{
    private readonly OpcoesDeConsultaPublica _opcoes = opcoes.Value;

    // CEP nao muda; o que muda e o mapa de ruas, em escala de anos. Trinta dias e
    // longo o bastante para o cache valer e curto o bastante para nao fossilizar.
    private static readonly TimeSpan ValidadeDoCep = TimeSpan.FromDays(30);

    // Dado de empresa muda: endereco, razao social, situacao cadastral. Um dia.
    private static readonly TimeSpan ValidadeDoCnpj = TimeSpan.FromDays(1);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<EnderecoConsultado> ConsultarCepAsync(string cep, CancellationToken ct)
    {
        var digitos = SomenteDigitos(cep);

        if (digitos.Length != 8)
        {
            throw ConsultasServiceException.CepInvalido();
        }

        GarantirHabilitada();

        var chave = $"consulta:cep:{digitos}";

        if (cache.TryGetValue<EnderecoConsultado>(chave, out var guardado) && guardado is not null)
        {
            return guardado;
        }

        // Dois provedores, duas chances. "Nao encontrado" de um so nao encerra o
        // assunto: as bases divergem em CEP de distrito e povoado, que e
        // exatamente o caso de Gameleiras.
        var (viaBrasilApi, respondeuBrasilApi) = await TentarBrasilApiCepAsync(digitos, ct);

        if (viaBrasilApi is not null)
        {
            return Guardar(chave, viaBrasilApi, ValidadeDoCep);
        }

        var (viaViaCep, respondeuViaCep) = await TentarViaCepAsync(digitos, ct);

        if (viaViaCep is not null)
        {
            return Guardar(chave, viaViaCep, ValidadeDoCep);
        }

        // Se nenhum dos dois chegou a responder, o problema e a rede — e dizer
        // "CEP nao existe" nessa hora manda o atendente procurar erro que nao ha.
        throw respondeuBrasilApi || respondeuViaCep
            ? ConsultasServiceException.NaoEncontrado()
            : ConsultasServiceException.Indisponivel();
    }

    public async Task<EmpresaConsultada> ConsultarCnpjAsync(string cnpj, CancellationToken ct)
    {
        var digitos = SomenteDigitos(cnpj);

        if (digitos.Length != 14)
        {
            throw ConsultasServiceException.CnpjInvalido();
        }

        GarantirHabilitada();

        var chave = $"consulta:cnpj:{digitos}";

        if (cache.TryGetValue<EmpresaConsultada>(chave, out var guardado) && guardado is not null)
        {
            return guardado;
        }

        // Sem reserva aqui: o ViaCEP so faz CEP.
        var (resposta, respondeu) = await BuscarAsync<CnpjDaBrasilApi>(
            $"{_opcoes.BaseBrasilApi.TrimEnd('/')}/cnpj/v1/{digitos}", ct);

        if (resposta is null)
        {
            throw respondeu
                ? ConsultasServiceException.NaoEncontrado()
                : ConsultasServiceException.Indisponivel();
        }

        var endereco = new EnderecoConsultado(
            SomenteDigitos(resposta.Cep ?? string.Empty),
            Ajustar(resposta.Logradouro),
            Ajustar(resposta.Bairro),
            Ajustar(resposta.Municipio),
            resposta.Uf?.Trim().ToUpperInvariant());

        var empresa = new EmpresaConsultada(
            digitos,
            Ajustar(resposta.RazaoSocial),
            Ajustar(resposta.NomeFantasia),
            endereco.Cep.Length == 8 ? endereco : null);

        return Guardar(chave, empresa, ValidadeDoCnpj);
    }

    private async Task<(EnderecoConsultado? Endereco, bool Respondeu)> TentarBrasilApiCepAsync(
        string cep,
        CancellationToken ct)
    {
        var (resposta, respondeu) = await BuscarAsync<CepDaBrasilApi>(
            $"{_opcoes.BaseBrasilApi.TrimEnd('/')}/cep/v1/{cep}", ct);

        if (resposta is null)
        {
            return (null, respondeu);
        }

        return (new EnderecoConsultado(
            cep,
            Ajustar(resposta.Street),
            Ajustar(resposta.Neighborhood),
            Ajustar(resposta.City),
            resposta.State?.Trim().ToUpperInvariant()), true);
    }

    private async Task<(EnderecoConsultado? Endereco, bool Respondeu)> TentarViaCepAsync(
        string cep,
        CancellationToken ct)
    {
        var (resposta, respondeu) = await BuscarAsync<CepDoViaCep>(
            $"{_opcoes.BaseViaCep.TrimEnd('/')}/{cep}/json/", ct);

        // O ViaCEP devolve 200 com {"erro": true} para CEP inexistente — o campo
        // presente ja e a resposta, independente de vir como booleano ou string.
        if (resposta is null || resposta.Erro is not null)
        {
            return (null, respondeu);
        }

        return (new EnderecoConsultado(
            cep,
            Ajustar(resposta.Logradouro),
            Ajustar(resposta.Bairro),
            Ajustar(resposta.Localidade),
            resposta.Uf?.Trim().ToUpperInvariant()), true);
    }

    /// <summary>
    /// Faz a chamada e devolve o corpo tipado. O segundo item diz se o provedor
    /// CHEGOU A RESPONDER — e o que separa "nao existe" de "rede fora".
    /// </summary>
    private async Task<(T? Corpo, bool Respondeu)> BuscarAsync<T>(string url, CancellationToken ct)
        where T : class
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _opcoes.TimeoutSegundos)));

        try
        {
            using var resposta = await http.GetAsync(url, limite.Token);

            // 404 e o nao encontrado obvio. O 400 tambem conta: a BrasilAPI usa
            // BadRequest para documento que nao resolve, e devolver "servico
            // indisponivel" ai manda o atendente caçar problema de rede que nao ha.
            if (resposta.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            {
                return (null, true);
            }

            if (!resposta.IsSuccessStatusCode)
            {
                log.LogWarning("Consulta publica devolveu {Status} em {Url}.", resposta.StatusCode, url);
                return (null, false);
            }

            await using var conteudo = await resposta.Content.ReadAsStreamAsync(limite.Token);

            return (await JsonSerializer.DeserializeAsync<T>(conteudo, Json, limite.Token), true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Quem desistiu foi o atendente (fechou o dialogo, trocou o CEP). Isso
            // sobe: nao e falha de provedor e nao pode virar aviso na tela.
            throw;
        }
        catch (Exception falha) when (falha is HttpRequestException or OperationCanceledException or JsonException)
        {
            log.LogWarning(falha, "Consulta publica falhou em {Url}.", url);
            return (null, false);
        }
    }

    private void GarantirHabilitada()
    {
        if (!_opcoes.Habilitada)
        {
            throw ConsultasServiceException.Indisponivel();
        }
    }

    private T Guardar<T>(string chave, T valor, TimeSpan validade)
    {
        // So o acerto vai para o cache. Guardar "nao encontrado" faria um CEP
        // digitado errado continuar errado depois de corrigido na origem.
        cache.Set(chave, valor, validade);
        return valor;
    }

    /// <summary>
    /// Limpa e desmaiuscula. A Receita devolve tudo em caixa alta ("BANCO DO BRASIL
    /// SA"), e sem isto a coluna Cidade da lista de clientes fica com metade
    /// gritando — o CEP volta com caixa normal e o CNPJ nao.
    /// </summary>
    /// <remarks>
    /// Nao acerta tudo: numeral romano em nome de logradouro sai "Torre I, Ii, Iii".
    /// Chuta o caso comum e deixa o campo editavel, que e o comportamento certo
    /// para dado que veio de fora.
    /// </remarks>
    private static string? Ajustar(string? valor)
    {
        var limpo = valor?.Trim();

        if (string.IsNullOrWhiteSpace(limpo))
        {
            return null;
        }

        // Ja veio com caixa normal (e o caso do CEP): nao mexe.
        if (limpo.Any(char.IsLower))
        {
            return limpo;
        }

        var titulo = CultureInfo.GetCultureInfo("pt-BR").TextInfo.ToTitleCase(limpo.ToLowerInvariant());
        var palavras = titulo.Split(' ');

        for (var i = 1; i < palavras.Length; i++)
        {
            // Preposicao no meio do nome e minuscula: "Banco do Brasil", nao
            // "Banco Do Brasil". Nunca na primeira palavra.
            if (Conectivos.Contains(palavras[i]))
            {
                palavras[i] = palavras[i].ToLowerInvariant();
            }
        }

        return string.Join(' ', palavras);
    }

    private static readonly HashSet<string> Conectivos =
        new(StringComparer.OrdinalIgnoreCase) { "Da", "Das", "De", "Do", "Dos", "E" };

    private static string SomenteDigitos(string valor) => RegexDeDigitos().Replace(valor, string.Empty);

    [GeneratedRegex(@"\D")]
    private static partial Regex RegexDeDigitos();

    // ------------------------------------------------------------ formato alheio
    // Ficam privados de proposito: o formato do provedor nao atravessa a fronteira
    // do modulo. Quem chama enxerga so EnderecoConsultado e EmpresaConsultada.

    private sealed class CepDaBrasilApi
    {
        public string? Street { get; set; }
        public string? Neighborhood { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
    }

    private sealed class CepDoViaCep
    {
        public string? Logradouro { get; set; }
        public string? Bairro { get; set; }
        public string? Localidade { get; set; }
        public string? Uf { get; set; }

        /// <summary>Presente apenas quando o CEP nao existe. Vem ora `true`, ora `"true"`.</summary>
        public JsonElement? Erro { get; set; }
    }

    private sealed class CnpjDaBrasilApi
    {
        [JsonPropertyName("razao_social")]
        public string? RazaoSocial { get; set; }

        [JsonPropertyName("nome_fantasia")]
        public string? NomeFantasia { get; set; }

        public string? Logradouro { get; set; }
        public string? Bairro { get; set; }
        public string? Municipio { get; set; }
        public string? Uf { get; set; }
        public string? Cep { get; set; }
    }
}
