using System.Security.Cryptography;
using System.Text;
using CrmCartorio.Api.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace CrmCartorio.Api.Modules.CentralDeIa.Application;

public class CentralDeIaAutenticador(
    IOptions<OpcoesDaCentralDeIa> opcoes,
    IGeradorDeTokenDaCentralDeIa gerador) : ICentralDeIaAutenticador
{
    private readonly OpcoesDaCentralDeIa _opcoes = opcoes.Value;

    public Task<EntrarNaCentralDeIaResponse> EntrarAsync(string segredo, CancellationToken ct = default)
    {
        if (!SegredoConfere(segredo))
        {
            throw CentralDeIaServiceException.SegredoInvalido();
        }

        var (token, expiraEm) = gerador.Gerar();

        return Task.FromResult(new EntrarNaCentralDeIaResponse(token, expiraEm));
    }

    /// <remarks>
    /// Comparação de tempo fixo — mesmo padrão de
    /// <c>Modules/CanalWhatsApp/Application/ProcessadorDeWebhook.TokenConfere</c>:
    /// comparar segredo com <c>==</c> vaza o tamanho do prefixo correto pelo
    /// tempo de resposta. Chave não configurada (<see cref="OpcoesDaCentralDeIa.Chave"/>
    /// vazia) nunca compara igual a nada — a Central simplesmente não aceita
    /// segredo nenhum enquanto ninguém configurar, sem derrubar a subida da API.
    /// </remarks>
    private bool SegredoConfere(string? segredo)
    {
        if (string.IsNullOrEmpty(segredo) || string.IsNullOrEmpty(_opcoes.Chave))
        {
            return false;
        }

        var recebido = Encoding.UTF8.GetBytes(segredo);
        var esperado = Encoding.UTF8.GetBytes(_opcoes.Chave);

        return CryptographicOperations.FixedTimeEquals(recebido, esperado);
    }
}
