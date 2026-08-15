namespace CrmCartorio.Api.Domain.Common;

/// <summary>
/// Erro de negocio previsto. O middleware traduz para ProblemDetails com um codigo
/// estavel, para que o frontend reaja ao codigo e nao ao texto da mensagem.
/// Excecao nao derivada desta e falha inesperada e vira 500 sem detalhe vazado.
/// </summary>
public abstract class ExcecaoDeAplicacao(string codigo, string mensagem, int statusHttp)
    : Exception(mensagem)
{
    public string Codigo { get; } = codigo;

    public int StatusHttp { get; } = statusHttp;

    /// <summary>Dados extras enviados ao cliente junto do ProblemDetails.</summary>
    public IDictionary<string, object?> Detalhes { get; } = new Dictionary<string, object?>();
}
