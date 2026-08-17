using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <summary>Arquivo ja gravado, no formato que a <c>Mensagem</c> guarda.</summary>
/// <param name="CaminhoRelativo">Relativo a raiz configurada, nunca absoluto.</param>
public record AnexoGravado(
    string CaminhoRelativo,
    string Nome,
    string TipoMime,
    long TamanhoBytes,
    TipoMensagem Tipo);

/// <summary>
/// Guarda e devolve o binario dos anexos.
/// </summary>
/// <remarks>
/// Porta no slice, adaptador fora — mesma disciplina de
/// <see cref="INotificadorDeAtendimento"/>. O Atendimento sabe que existe um
/// arquivo e onde ele esta registrado; NAO sabe se o destino e disco local, rede
/// ou outra coisa.
///
/// A implementacao da Fase 2 grava no disco do proprio servidor. E a mesma
/// soberania de dado que justifica o self-hosted: documento de cliente de
/// cartorio nao passa por storage de terceiro.
/// </remarks>
public interface IArmazenamentoDeAnexos
{
    /// <summary>Teto por arquivo, em bytes.</summary>
    long TamanhoMaximoBytes { get; }

    /// <summary>
    /// O tipo esta na lista de permitidos.
    /// </summary>
    /// <remarks>
    /// Lista de PERMITIDOS, e nao de proibidos: uma lista de proibidos deixa
    /// passar tudo que ninguem lembrou de escrever nela, e o que passa aqui vira
    /// arquivo servido de volta pelo nosso dominio.
    /// </remarks>
    bool TipoPermitido(string? tipoMime);

    /// <summary>
    /// Grava o binario e devolve o que a mensagem precisa guardar.
    /// </summary>
    /// <param name="mensagemId">Vira o NOME do arquivo em disco.</param>
    /// <exception cref="AtendimentoServiceException">
    /// Tipo fora da lista ou tamanho acima do teto.
    /// </exception>
    Task<AnexoGravado> GravarAsync(
        Guid tenantId,
        Guid mensagemId,
        DateTime quando,
        byte[] conteudo,
        string? nomeOriginal,
        string? tipoMime,
        CancellationToken ct = default);

    /// <summary>
    /// Le o binario, ou <c>null</c> se o arquivo sumiu do disco.
    /// </summary>
    /// <remarks>
    /// Null e um caminho REAL, nao defensivismo: a linha vive no banco e o
    /// arquivo vive no disco, e restaurar um .bak sem restaurar a pasta produz
    /// exatamente este estado. Quem chama devolve 404 com explicacao, em vez de
    /// 500.
    /// </remarks>
    Task<byte[]?> LerAsync(string caminhoRelativo, CancellationToken ct = default);
}
