using System.Text.RegularExpressions;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Configuracoes.Application;

public partial class EtapaFunilService(CrmDbContext db, IContextoDoUsuario ctx) : IEtapaFunilService
{
    public async Task<IReadOnlyList<EtapaResponse>> ListarAsync(
        bool incluirInativas,
        CancellationToken ct = default)
    {
        var tenantId = ctx.TenantId;

        var consulta = db.EtapasFunil.AsNoTracking().Where(e => e.TenantId == tenantId);

        if (!incluirInativas)
        {
            consulta = consulta.Where(e => e.Ativo);
        }

        return await consulta
            .OrderBy(e => e.Ordem)
            .ThenBy(e => e.Nome)
            .Select(e => new EtapaResponse(
                e.Id,
                e.Nome,
                e.Descricao,
                e.Ordem,
                e.Cor,
                e.EhFinal,
                e.Ativo,
                db.Conversas.Count(c => c.EtapaId == e.Id && c.Status == StatusConversa.Ativa)))
            .ToListAsync(ct);
    }

    public async Task<EtapaResponse> CriarAsync(
        SalvarEtapaRequest request,
        CancellationToken ct = default)
    {
        GarantirPermissao();

        var nome = ValidarNome(request.Nome);
        var cor = ValidarCor(request.Cor);
        await GarantirNomeLivreAsync(nome, null, ct);

        var etapa = new EtapaFunil
        {
            TenantId = ctx.TenantId,
            Nome = nome,
            Descricao = string.IsNullOrWhiteSpace(request.Descricao) ? null : request.Descricao.Trim(),
            Ordem = request.Ordem,
            Cor = cor,
            EhFinal = request.EhFinal,
            Ativo = request.Ativo
        };

        db.EtapasFunil.Add(etapa);
        await db.SaveChangesAsync(ct);

        return Mapear(etapa, 0);
    }

    public async Task<EtapaResponse> AtualizarAsync(
        Guid id,
        SalvarEtapaRequest request,
        CancellationToken ct = default)
    {
        GarantirPermissao();

        var etapa = await db.EtapasFunil
            .FirstOrDefaultAsync(e => e.Id == id && e.TenantId == ctx.TenantId, ct)
            ?? throw ConfiguracoesServiceException.EtapaNaoEncontrada();

        var nome = ValidarNome(request.Nome);
        var cor = ValidarCor(request.Cor);
        await GarantirNomeLivreAsync(nome, id, ct);

        if (etapa.Ativo && !request.Ativo)
        {
            var outrasAtivas = await db.EtapasFunil.CountAsync(
                e => e.TenantId == ctx.TenantId && e.Ativo && e.Id != id, ct);

            if (outrasAtivas == 0)
            {
                throw ConfiguracoesServiceException.UltimaEtapaAtiva();
            }
        }

        etapa.Nome = nome;
        etapa.Descricao = string.IsNullOrWhiteSpace(request.Descricao) ? null : request.Descricao.Trim();
        etapa.Ordem = request.Ordem;
        etapa.Cor = cor;
        etapa.EhFinal = request.EhFinal;
        etapa.Ativo = request.Ativo;

        await db.SaveChangesAsync(ct);

        var ativas = await db.Conversas.CountAsync(
            c => c.EtapaId == etapa.Id && c.Status == StatusConversa.Ativa, ct);

        return Mapear(etapa, ativas);
    }

    public async Task<IReadOnlyList<EtapaResponse>> ReordenarAsync(
        IReadOnlyList<Guid> idsNaOrdem,
        CancellationToken ct = default)
    {
        GarantirPermissao();

        var etapas = await db.EtapasFunil
            .Where(e => e.TenantId == ctx.TenantId)
            .ToListAsync(ct);

        for (var i = 0; i < idsNaOrdem.Count; i++)
        {
            var etapa = etapas.FirstOrDefault(e => e.Id == idsNaOrdem[i]);
            if (etapa is null)
            {
                throw ConfiguracoesServiceException.EtapaNaoEncontrada();
            }

            etapa.Ordem = i + 1;
        }

        await db.SaveChangesAsync(ct);

        return await ListarAsync(true, ct);
    }

    private void GarantirPermissao()
    {
        if (!ctx.Pode(PermissaoDoSistema.EtapasEditar))
        {
            throw ConfiguracoesServiceException.SemPermissao();
        }
    }

    private async Task GarantirNomeLivreAsync(string nome, Guid? ignorarId, CancellationToken ct)
    {
        var existe = await db.EtapasFunil.AsNoTracking().AnyAsync(
            e => e.TenantId == ctx.TenantId && e.Nome == nome && (ignorarId == null || e.Id != ignorarId), ct);

        if (existe)
        {
            throw ConfiguracoesServiceException.NomeEmUso(nome);
        }
    }

    private static string ValidarNome(string? nome)
    {
        var limpo = nome?.Trim();

        return string.IsNullOrWhiteSpace(limpo)
            ? throw ConfiguracoesServiceException.NomeObrigatorio()
            : limpo;
    }

    private static string ValidarCor(string? cor)
    {
        var limpo = cor?.Trim();

        return string.IsNullOrWhiteSpace(limpo) || !RegexDeCorHex().IsMatch(limpo)
            ? throw ConfiguracoesServiceException.CorInvalida()
            : limpo.ToUpperInvariant();
    }

    private static EtapaResponse Mapear(EtapaFunil etapa, int conversasAtivas) =>
        new(etapa.Id, etapa.Nome, etapa.Descricao, etapa.Ordem, etapa.Cor,
            etapa.EhFinal, etapa.Ativo, conversasAtivas);

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex RegexDeCorHex();
}
