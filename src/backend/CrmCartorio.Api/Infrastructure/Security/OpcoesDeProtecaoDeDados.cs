namespace CrmCartorio.Api.Infrastructure.Security;

/// <summary>
/// PR3 da blindagem LGPD (docs/PROTECAO-DE-DADOS.md): cifra em repouso das
/// colunas com conteudo de conversa e dado sensivel da ficha do cliente.
/// Mesma disciplina de <see cref="OpcoesDeMfa"/> — vive em
/// <c>appsettings.&lt;Ambiente&gt;.json</c>, nunca versionada, nunca junto do
/// backup do banco (ver docs/BACKUP.md).
/// </summary>
/// <remarks>
/// <b>Ausencia e estado valido, de proposito.</b> Ao contrario de
/// <see cref="OpcoesDeMfa"/> (que ja e infraestrutura ativa em todo ambiente),
/// esta secao pode simplesmente nao existir em <c>appsettings</c> — o boot nao
/// recusa subir por isso. Enquanto <see cref="VersaoCorrente"/> ou
/// <see cref="Chaves"/> nao estiverem presentes, a cifra fica dormente: as
/// colunas marcadas <c>.Cifrada()</c> continuam gravando o texto exatamente
/// como chegam (nenhuma mudanca de comportamento), e a tela "Acesso aos
/// dados" mostra honestamente "cifra ainda nao ativa". Isso existe porque a
/// chave so pode ser colocada no servidor por quem tem acesso direto a ele
/// (SSH/RDP) — o pipeline de deploy nunca toca <c>appsettings.*.json</c> de
/// proposito (<c>Deploy-Release.ps1</c>, <c>Sync-ReleaseToSite</c>) — e essa
/// entrega pode chegar antes desse acesso estar disponivel. Ver
/// <see cref="IProvedorDeChaveDeDados.CifraAtiva"/>.
/// </remarks>
public class OpcoesDeProtecaoDeDados
{
    public const string Secao = "ProtecaoDeDados";

    public string? VersaoCorrente { get; set; }

    /// <summary>Mapa {versao (string) → chave AES-256 em base64, 32 bytes}.</summary>
    public Dictionary<string, string> Chaves { get; set; } = [];

    /// <summary>Chave HMAC-SHA256 em base64 (32 bytes) para o indice cego de busca (CPF/CNPJ).</summary>
    public string? ChaveDeIndice { get; set; }

    /// <summary>
    /// Quando <c>true</c>, ler uma coluna cifrada que ainda esta em texto puro
    /// (sem o prefixo <c>$c1$</c>) lanca <see cref="DadoNaoCifradoException"/>
    /// em vez de devolver o texto — usado depois que
    /// <c>--cifrar-dados-legados</c> confirma que nao sobrou linha antiga, para
    /// travar qualquer caminho de escrita esquecido que ainda grave em claro.
    /// Nunca vale nada enquanto a cifra esta dormente.
    /// </summary>
    public bool ExigirCifrado { get; set; }
}
