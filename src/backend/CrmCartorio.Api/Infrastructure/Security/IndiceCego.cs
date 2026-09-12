using System.Security.Cryptography;
using System.Text;

namespace CrmCartorio.Api.Infrastructure.Security;

/// <summary>
/// Indice cego (blind index) para achar por igualdade exata um valor cuja
/// coluna esta cifrada — hoje, so <c>Cliente.Documento</c>
/// (<c>DocumentoHash</c>). HMAC, nao hash simples: sem a chave, nao da para
/// forcar por dicionario de CPFs conhecidos contra o valor gravado.
/// </summary>
/// <remarks>
/// Chave separada da chave de cifra (<see cref="OpcoesDeProtecaoDeDados.ChaveDeIndice"/>)
/// de proposito — rotacionar a chave de cifra (re-cifrar as colunas com uma
/// chave nova) nao pode obrigar a recalcular todo indice cego junto; sao
/// operacoes independentes.
/// </remarks>
public class IndiceCego(IProvedorDeChaveDeDados provedor)
{
    /// <param name="tenantId">Amarra o hash ao tenant — o mesmo CPF em dois cartorios-cliente nao pode ser correlacionavel entre eles.</param>
    /// <param name="valorNormalizado">Ja normalizado pelo chamador (ex.: so digitos do documento) — este metodo nao normaliza.</param>
    /// <returns>64 caracteres hex minusculos.</returns>
    public string Calcular(Guid tenantId, string valorNormalizado)
    {
        var chave = provedor.ChaveDeIndice;
        var entrada = Encoding.UTF8.GetBytes($"{tenantId:N}|{valorNormalizado}");
        var hash = HMACSHA256.HashData(chave, entrada);
        return Convert.ToHexStringLower(hash);
    }
}
