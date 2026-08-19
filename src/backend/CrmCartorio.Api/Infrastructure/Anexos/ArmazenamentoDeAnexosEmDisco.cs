using CrmCartorio.Api.Modules.Atendimento.Application;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using Microsoft.Extensions.Options;

namespace CrmCartorio.Api.Infrastructure.Anexos;

/// <summary>Onde e ate que tamanho os anexos sao gravados.</summary>
public class OpcoesDeAnexos
{
    /// <summary>
    /// Raiz da pasta de anexos. Relativa vira relativa ao diretorio da aplicacao.
    /// </summary>
    public string Raiz { get; set; } = "anexos";

    /// <summary>
    /// Teto por arquivo, em megabytes.
    /// </summary>
    /// <remarks>
    /// 16 MB e o proprio teto do WhatsApp para midia comum, entao subir daqui
    /// nao faz o cliente conseguir mandar mais — so aumentaria o que um upload
    /// pelo painel pode ocupar em disco.
    /// </remarks>
    public int TamanhoMaximoMb { get; set; } = 16;
}

/// <summary>
/// Grava anexo no disco do proprio servidor.
/// </summary>
/// <remarks>
/// O binario NAO vai para o banco: o .bak cresceria com cada foto de documento
/// que um cliente manda, e sao muitas num cartorio. Em troca, backup de banco
/// sozinho nao basta — a pasta entra na rotina junto (docs/BACKUP.md).
/// </remarks>
public class ArmazenamentoDeAnexosEmDisco(
    IOptions<OpcoesDeAnexos> opcoes,
    IHostEnvironment ambiente,
    ILogger<ArmazenamentoDeAnexosEmDisco> log) : IArmazenamentoDeAnexos
{
    private readonly OpcoesDeAnexos _opcoes = opcoes.Value;

    /// <summary>
    /// Tipos aceitos, e a extensao com que cada um e gravado.
    /// </summary>
    /// <remarks>
    /// Lista de PERMITIDOS. Uma lista de proibidos deixaria passar tudo que
    /// ninguem lembrou de escrever nela, e o que passa aqui volta a ser servido
    /// pelo nosso proprio dominio.
    ///
    /// <b>Ausencias deliberadas:</b> <c>image/svg+xml</c> e <c>text/html</c>.
    /// Os dois carregam script; exibidos no dominio do painel, executariam com a
    /// sessao do atendente. SVG parece imagem e nao e — e a armadilha classica
    /// de lista de permitidos escrita as pressas.
    /// </remarks>
    private static readonly Dictionary<string, (string Extensao, TipoMensagem Tipo)> Permitidos =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = (".jpg", TipoMensagem.Imagem),
            ["image/png"] = (".png", TipoMensagem.Imagem),
            ["image/webp"] = (".webp", TipoMensagem.Imagem),
            ["image/gif"] = (".gif", TipoMensagem.Imagem),

            ["application/pdf"] = (".pdf", TipoMensagem.Documento),
            ["application/msword"] = (".doc", TipoMensagem.Documento),
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] =
                (".docx", TipoMensagem.Documento),
            ["application/vnd.ms-excel"] = (".xls", TipoMensagem.Documento),
            ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] =
                (".xlsx", TipoMensagem.Documento),
            ["text/plain"] = (".txt", TipoMensagem.Documento),

            // Audio de WhatsApp e ogg/opus; os demais aparecem em encaminhamento.
            ["audio/ogg"] = (".ogg", TipoMensagem.Audio),
            ["audio/mpeg"] = (".mp3", TipoMensagem.Audio),
            ["audio/mp4"] = (".m4a", TipoMensagem.Audio),
            ["audio/aac"] = (".aac", TipoMensagem.Audio),
            ["audio/amr"] = (".amr", TipoMensagem.Audio),

            ["video/mp4"] = (".mp4", TipoMensagem.Video),
            ["video/3gpp"] = (".3gp", TipoMensagem.Video),
            ["video/quicktime"] = (".mov", TipoMensagem.Video),
        };

    public long TamanhoMaximoBytes => Math.Max(1, _opcoes.TamanhoMaximoMb) * 1024L * 1024L;

    public bool TipoPermitido(string? tipoMime) =>
        !string.IsNullOrWhiteSpace(tipoMime) && Permitidos.ContainsKey(Normalizar(tipoMime));

    public async Task<AnexoGravado> GravarAsync(
        Guid tenantId,
        Guid mensagemId,
        DateTime quando,
        byte[] conteudo,
        string? nomeOriginal,
        string? tipoMime,
        CancellationToken ct = default)
    {
        if (conteudo.Length == 0)
        {
            throw AtendimentoServiceException.AnexoVazio();
        }

        if (conteudo.LongLength > TamanhoMaximoBytes)
        {
            throw AtendimentoServiceException.AnexoGrandeDemais(_opcoes.TamanhoMaximoMb);
        }

        var mime = Normalizar(tipoMime);

        if (!Permitidos.TryGetValue(mime, out var formato))
        {
            throw AtendimentoServiceException.AnexoDeTipoNaoAceito(tipoMime);
        }

        // {tenant}/{ano}/{mes}/{id}.ext — a subdivisao por mes existe para nao
        // terminar com uma pasta unica de centenas de milhares de arquivos, que
        // trava qualquer listagem no servidor.
        //
        // O NOME E O ID DA MENSAGEM, nunca o nome que veio do cliente: um
        // "..\..\web.config" viraria escrita fora da pasta de anexos.
        var pasta = Path.Combine(
            tenantId.ToString("N"),
            quando.ToString("yyyy"),
            quando.ToString("MM"));

        var relativo = Path.Combine(pasta, $"{mensagemId:N}{formato.Extensao}");
        var absoluto = CaminhoAbsoluto(relativo);

        Directory.CreateDirectory(Path.GetDirectoryName(absoluto)!);

        await File.WriteAllBytesAsync(absoluto, conteudo, ct);

        log.LogInformation(
            "Anexo de {Tamanho} bytes ({Mime}) gravado para a mensagem {MensagemId}.",
            conteudo.LongLength, mime, mensagemId);

        return new AnexoGravado(
            // Barra normal no banco: o caminho e lido de volta em
            // Path.Combine, que aceita as duas, e um valor gravado no Windows
            // nao pode ficar ilegivel se um dia isto rodar em Linux.
            relativo.Replace('\\', '/'),
            NomeExibivel(nomeOriginal, formato.Extensao),
            mime,
            conteudo.LongLength,
            formato.Tipo);
    }

    public async Task<byte[]?> LerAsync(string caminhoRelativo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(caminhoRelativo))
        {
            return null;
        }

        string absoluto;

        try
        {
            absoluto = CaminhoAbsoluto(caminhoRelativo);
        }
        catch (AtendimentoServiceException)
        {
            // Caminho que escapa da raiz. Ja logado em CaminhoAbsoluto.
            return null;
        }

        if (!File.Exists(absoluto))
        {
            // Linha no banco, arquivo fora do disco. Acontece de verdade ao
            // restaurar um .bak sem restaurar a pasta de anexos.
            log.LogWarning("Anexo ausente no disco: {Caminho}", caminhoRelativo);
            return null;
        }

        return await File.ReadAllBytesAsync(absoluto, ct);
    }

    /// <summary>
    /// Resolve o caminho e CONFERE que ele caiu dentro da raiz.
    /// </summary>
    /// <remarks>
    /// Defesa em profundidade. Hoje o caminho e sempre montado por
    /// <see cref="GravarAsync"/> a partir de Guids, entao nao ha entrada de
    /// usuario nele. A conferencia existe para o dia em que houver: uma linha
    /// adulterada no banco, uma importacao, um caminho vindo de query — qualquer
    /// um deles daria leitura de arquivo arbitrario do servidor sem esta linha.
    /// </remarks>
    private string CaminhoAbsoluto(string relativo)
    {
        var raiz = Path.GetFullPath(
            Path.IsPathRooted(_opcoes.Raiz)
                ? _opcoes.Raiz
                : Path.Combine(ambiente.ContentRootPath, _opcoes.Raiz));

        var completo = Path.GetFullPath(Path.Combine(raiz, relativo));

        if (!completo.StartsWith(
                raiz.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            log.LogError("Caminho de anexo fora da raiz recusado: {Caminho}", relativo);
            throw AtendimentoServiceException.AnexoNaoEncontrado();
        }

        return completo;
    }

    /// <summary>
    /// Nome que o fio mostra e o download devolve.
    /// </summary>
    /// <remarks>
    /// Fica so o nome final do arquivo — <c>Path.GetFileName</c> corta qualquer
    /// pasta que o cliente tenha embutido. A extensao vem da NOSSA tabela, nao
    /// da que ele mandou: um <c>boleto.pdf.exe</c> declarado como PDF sai daqui
    /// como <c>boleto.pdf</c>.
    /// </remarks>
    private static string NomeExibivel(string? nomeOriginal, string extensao)
    {
        var limpo = string.IsNullOrWhiteSpace(nomeOriginal)
            ? null
            : Path.GetFileName(nomeOriginal.Replace('\\', '/').Trim());

        // Caractere de controle em Content-Disposition permite injetar cabecalho.
        if (!string.IsNullOrWhiteSpace(limpo))
        {
            limpo = new string(limpo.Where(c => !char.IsControl(c) && c != '"').ToArray()).Trim();
        }

        if (string.IsNullOrWhiteSpace(limpo))
        {
            return $"anexo{extensao}";
        }

        if (limpo.Length > 200)
        {
            limpo = limpo[..200];
        }

        return Path.GetExtension(limpo).Equals(extensao, StringComparison.OrdinalIgnoreCase)
            ? limpo
            : Path.GetFileNameWithoutExtension(limpo) + extensao;
    }

    /// <summary>
    /// "image/jpeg; codecs=..." vira "image/jpeg".
    /// </summary>
    /// <remarks>
    /// O WhatsApp manda o mimetype com parametros com frequencia — audio de voz
    /// chega como <c>audio/ogg; codecs=opus</c>. Sem cortar o parametro, todo
    /// audio de voz cairia como tipo nao aceito.
    /// </remarks>
    private static string Normalizar(string? tipoMime) =>
        string.IsNullOrWhiteSpace(tipoMime)
            ? string.Empty
            : tipoMime.Split(';')[0].Trim().ToLowerInvariant();
}
