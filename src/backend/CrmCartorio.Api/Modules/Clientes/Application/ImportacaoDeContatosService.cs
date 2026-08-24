using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.CanalWhatsApp.Application;
using CrmCartorio.Api.Modules.Clientes.Domain;
using CrmCartorio.Api.Modules.Clientes.Domain.Entities;
using CrmCartorio.Api.Modules.Clientes.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Clientes.Application;

/// <remarks>
/// Não usa <see cref="ClienteService.CriarAsync"/> no lote — ele faz uma query
/// de telefone livre por linha e fecha com três queries extras em
/// <c>ObterAsync</c>. Escreve direto no <see cref="CrmDbContext"/>, mesma
/// decisão que <c>RecebimentoDeWhatsApp</c> já toma pelo mesmo motivo.
/// </remarks>
public class ImportacaoDeContatosService(
    CrmDbContext db,
    IContextoDoUsuario ctx,
    IEvolutionClient gateway) : IImportacaoDeContatosService
{
    private const int TamanhoDoLote = 500;
    private const int MaximoDeLinhas = 5000;

    public async Task<AnalisarImportacaoResponse> AnalisarAsync(
        AnalisarImportacaoRequest request,
        CancellationToken ct = default)
    {
        var linhas = request.Fonte == FonteDeImportacao.WhatsApp
            ? await LerContatosDoWhatsAppAsync(ct)
            : LerDoTexto(request.Texto);

        if (linhas.Count > MaximoDeLinhas)
        {
            throw ClientesServiceException.ImportacaoMuitoGrande(linhas.Count);
        }

        return await ClassificarAsync(linhas, ct);
    }

    private static IReadOnlyList<LinhaDeContato> LerDoTexto(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            throw ClientesServiceException.TextoDeImportacaoObrigatorio();
        }

        return ParserDeListaDeContatos.Analisar(texto);
    }

    /// <summary>
    /// Agenda do WhatsApp pareado, filtrada para o que pode virar cliente.
    /// </summary>
    /// <remarks>
    /// <see cref="IEvolutionClient.ObterContatosAsync"/> já resolve
    /// <c>@lid</c> para o E.164 real via <c>remoteJidAlt</c> quando o gateway
    /// entrega (mesma regra de <c>TradutorDePayload.LerMensagem</c>), ou
    /// descarta quando não há como. Aqui só resta filtrar o que sobrou:
    /// grupo (<c>@g.us</c>), lista de transmissão (<c>status@broadcast</c>) e o
    /// próprio número pareado — só <c>@s.whatsapp.net</c> passa. Vazio é
    /// resultado válido, não erro.
    /// </remarks>
    private async Task<IReadOnlyList<LinhaDeContato>> LerContatosDoWhatsAppAsync(CancellationToken ct)
    {
        IReadOnlyList<ContatoDoWhatsApp> contatos;
        PerfilDaInstancia? perfil;

        try
        {
            contatos = await gateway.ObterContatosAsync(ct);
            perfil = await gateway.ObterPerfilDaInstanciaAsync(ct);
        }
        catch (CanalWhatsAppServiceException)
        {
            throw ClientesServiceException.GatewayIndisponivelParaImportacao();
        }

        var numeroDaInstancia = perfil?.Numero;
        var linhas = new List<LinhaDeContato>();
        var numeroDaLinha = 0;

        foreach (var contato in contatos)
        {
            numeroDaLinha++;

            var arroba = contato.Jid.IndexOf('@');

            if (arroba <= 0 || !contato.Jid.EndsWith("@s.whatsapp.net", StringComparison.Ordinal))
            {
                continue;
            }

            var telefone = contato.Jid[..arroba];

            if (numeroDaInstancia is not null && telefone == numeroDaInstancia)
            {
                continue; // o proprio numero pareado nao e "contato"
            }

            linhas.Add(new LinhaDeContato(numeroDaLinha, contato.Nome, telefone, contato.Jid));
        }

        return linhas;
    }

    public async Task<ConfirmarImportacaoResponse> ConfirmarAsync(
        ConfirmarImportacaoRequest request,
        CancellationToken ct = default)
    {
        GarantirPermissao();

        if (request.Linhas.Count == 0)
        {
            return new ConfirmarImportacaoResponse(0, 0);
        }

        var origem = request.Fonte == FonteDeImportacao.WhatsApp
            ? OrigemDoCadastro.ImportacaoWhatsapp
            : OrigemDoCadastro.ImportacaoPlanilha;

        var tenantId = ctx.TenantId;
        var criados = 0;
        var ignorados = 0;
        var telefonesVistos = new HashSet<string>(StringComparer.Ordinal);

        foreach (var lote in request.Linhas.Chunk(TamanhoDoLote))
        {
            var candidatos = new List<LinhaParaGravar>();

            foreach (var linha in lote)
            {
                var telefone = TelefoneDeContato.ParaE164Basico(linha.TelefoneE164);

                if (telefone is null || !telefonesVistos.Add(telefone))
                {
                    ignorados++;
                    continue;
                }

                candidatos.Add(linha with { TelefoneE164 = telefone });
            }

            if (candidatos.Count == 0)
            {
                continue;
            }

            var telefonesDoLote = candidatos.Select(c => c.TelefoneE164).ToList();

            var existentes = new HashSet<string>(
                await db.Clientes.AsNoTracking()
                    .Where(c => c.TenantId == tenantId && telefonesDoLote.Contains(c.TelefoneWhatsapp))
                    .Select(c => c.TelefoneWhatsapp)
                    .ToListAsync(ct),
                StringComparer.Ordinal);

            var agora = DateTime.UtcNow;
            var novos = new List<Cliente>();

            foreach (var linha in candidatos)
            {
                if (existentes.Contains(linha.TelefoneE164))
                {
                    ignorados++;
                    continue;
                }

                var cliente = new Cliente
                {
                    TenantId = tenantId,
                    Nome = NomeDeContato.DoPerfil(linha.Nome, linha.TelefoneE164),
                    TelefoneWhatsapp = linha.TelefoneE164,
                    PrimeiroContatoEm = agora,
                    Ativo = true,
                    OrigemDoCadastro = origem
                };

                db.Clientes.Add(cliente);
                novos.Add(cliente);
            }

            if (novos.Count == 0)
            {
                continue;
            }

            try
            {
                await db.SaveChangesAsync(ct);
                criados += novos.Count;
            }
            catch (DbUpdateException)
            {
                // Corrida com o webhook: ele pode ter criado um destes telefones
                // entre a consulta acima e este SaveChanges. O indice unico
                // UQ_Clientes_Tenant_Telefone e a garantia real — aqui regrava um a
                // um para perder so a linha que colidiu, nao o lote inteiro.
                foreach (var cliente in novos)
                {
                    db.Entry(cliente).State = EntityState.Detached;
                }

                foreach (var cliente in novos)
                {
                    db.Clientes.Add(cliente);

                    try
                    {
                        await db.SaveChangesAsync(ct);
                        criados++;
                    }
                    catch (DbUpdateException)
                    {
                        db.Entry(cliente).State = EntityState.Detached;
                        ignorados++;
                    }
                }
            }
        }

        return new ConfirmarImportacaoResponse(criados, ignorados);
    }

    private void GarantirPermissao()
    {
        if (!ctx.PodeGerirCadastros)
        {
            throw ClientesServiceException.SemPermissao();
        }
    }

    private async Task<AnalisarImportacaoResponse> ClassificarAsync(
        IReadOnlyList<LinhaDeContato> linhas,
        CancellationToken ct)
    {
        var telefonesVistos = new HashSet<string>(StringComparer.Ordinal);
        var interpretadas = new List<(LinhaDeContato Linha, ResultadoDoTelefone Telefone, bool DuplicadaNoArquivo)>();

        foreach (var linha in linhas)
        {
            var telefone = TelefoneDeContato.Interpretar(linha.TelefoneBruto);

            // So um telefone VALIDO conta como duplicado — o mesmo numero com DDD
            // invalido em duas linhas nao e "duplicado", e "invalido duas vezes".
            var duplicadaNoArquivo = telefone.Valido && !telefonesVistos.Add(telefone.E164!);

            interpretadas.Add((linha, telefone, duplicadaNoArquivo));
        }

        var telefonesNovos = interpretadas
            .Where(x => x.Telefone.Valido && !x.DuplicadaNoArquivo)
            .Select(x => x.Telefone.E164!)
            .Distinct()
            .ToList();

        var tenantId = ctx.TenantId;
        var cadastrados = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var lote in telefonesNovos.Chunk(TamanhoDoLote))
        {
            var encontrados = await db.Clientes.AsNoTracking()
                .Where(c => c.TenantId == tenantId && lote.Contains(c.TelefoneWhatsapp))
                .Select(c => new { c.TelefoneWhatsapp, c.Nome })
                .ToListAsync(ct);

            foreach (var encontrado in encontrados)
            {
                cadastrados[encontrado.TelefoneWhatsapp] = encontrado.Nome;
            }
        }

        var respostas = new List<LinhaDeImportacaoResponse>(interpretadas.Count);

        foreach (var (linha, telefone, duplicadaNoArquivo) in interpretadas)
        {
            var situacao = Classificar(telefone, duplicadaNoArquivo, cadastrados, out var nomeExistente);

            respostas.Add(new LinhaDeImportacaoResponse(
                linha.NumeroDaLinha,
                linha.Nome,
                linha.TelefoneBruto,
                telefone.Valido ? telefone.E164 : null,
                situacao,
                nomeExistente));
        }

        var resumo = new ResumoDaImportacaoResponse(
            TotalLidas: respostas.Count,
            Novas: respostas.Count(r => r.Situacao == SituacaoDaLinhaImportada.Nova),
            JaCadastradas: respostas.Count(r => r.Situacao == SituacaoDaLinhaImportada.JaCadastrada),
            Invalidas: respostas.Count(r =>
                r.Situacao is SituacaoDaLinhaImportada.TelefoneInvalido or SituacaoDaLinhaImportada.DddInvalido),
            Ambiguas: respostas.Count(r => r.Situacao == SituacaoDaLinhaImportada.Ambigua),
            Duplicadas: respostas.Count(r => r.Situacao == SituacaoDaLinhaImportada.DuplicadaNoArquivo));

        return new AnalisarImportacaoResponse(resumo, respostas);
    }

    private static SituacaoDaLinhaImportada Classificar(
        ResultadoDoTelefone telefone,
        bool duplicadaNoArquivo,
        IReadOnlyDictionary<string, string> cadastrados,
        out string? nomeExistente)
    {
        nomeExistente = null;

        switch (telefone.Situacao)
        {
            case SituacaoDoTelefone.FormatoInvalido:
                return SituacaoDaLinhaImportada.TelefoneInvalido;

            case SituacaoDoTelefone.DddInvalido:
                return SituacaoDaLinhaImportada.DddInvalido;

            case SituacaoDoTelefone.Ambiguo:
                return SituacaoDaLinhaImportada.Ambigua;
        }

        if (duplicadaNoArquivo)
        {
            return SituacaoDaLinhaImportada.DuplicadaNoArquivo;
        }

        if (cadastrados.TryGetValue(telefone.E164!, out var nome))
        {
            nomeExistente = nome;
            return SituacaoDaLinhaImportada.JaCadastrada;
        }

        return SituacaoDaLinhaImportada.Nova;
    }
}
