namespace CrmCartorio.Api.Infrastructure.Security;

/// <summary>
/// Fonte da chave de cifra em repouso (PR3) e do indice cego de busca.
/// Ponto de troca do "nivel 2" do plano de protecao de dados (cofre
/// destravado pelo cartorio, chave so em memoria) — hoje a unica
/// implementacao le de <c>appsettings</c> (nivel 1, custodia do
/// administrador do servidor); trocar para o nivel 2 e trocar esta
/// implementacao, nao o que a chama.
/// </summary>
public interface IProvedorDeChaveDeDados
{
    /// <summary>
    /// <c>false</c> enquanto nenhuma chave foi configurada — estado normal
    /// antes do primeiro provisionamento manual no servidor. Todo o resto da
    /// cifra (<see cref="ICifraDeDados"/>, <see cref="IndiceCego"/>) consulta
    /// isto para decidir entre cifrar de verdade e passar o dado adiante sem
    /// tocar nele.
    /// </summary>
    bool CifraAtiva { get; }

    /// <summary>Versao usada para CIFRAR agora. Lanca se <see cref="CifraAtiva"/> for falso.</summary>
    string VersaoCorrente { get; }

    /// <summary>Chave AES-256 (32 bytes) da versao pedida — para decifrar dado gravado com uma versao antiga.</summary>
    byte[] ObterChave(string versao);

    /// <summary>Chave HMAC-SHA256 (32 bytes) do indice cego. Lanca se <see cref="CifraAtiva"/> for falso.</summary>
    byte[] ChaveDeIndice { get; }
}
