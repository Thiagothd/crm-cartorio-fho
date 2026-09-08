using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.RespostaInteligente.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.RespostaInteligente.Application;

public class ConhecimentoDeIaService(CrmDbContext db, IContextoDoUsuario ctx) : IConhecimentoDeIaService
{
    public async Task<IReadOnlyList<ConhecimentoDeIaResponse>> ListarAsync(
        bool incluirInativos,
        CancellationToken ct = default)
    {
        var tenantId = ctx.TenantId;

        var consulta = db.ConhecimentosDeIa.AsNoTracking().Where(c => c.TenantId == tenantId);

        if (!incluirInativos)
        {
            consulta = consulta.Where(c => c.Ativo);
        }

        return await consulta
            .OrderBy(c => c.Tema).ThenBy(c => c.Titulo)
            .Select(c => new ConhecimentoDeIaResponse(
                c.Id,
                c.Titulo,
                c.Conteudo,
                c.Tema,
                c.TipoServicoId,
                db.TiposServico.Where(t => t.Id == c.TipoServicoId).Select(t => t.Nome).FirstOrDefault(),
                c.VigenteDesde,
                c.VigenteAte,
                c.Versao,
                db.Atendentes.Where(a => a.Id == c.CriadoPorAtendenteId).Select(a => (string?)a.Nome).FirstOrDefault(),
                db.Atendentes.Where(a => a.Id == c.AtualizadoPorAtendenteId).Select(a => (string?)a.Nome).FirstOrDefault(),
                c.Ativo,
                c.CriadoEm,
                c.AtualizadoEm))
            .ToListAsync(ct);
    }

    public async Task<ConhecimentoDeIaResponse> CriarAsync(
        SalvarConhecimentoDeIaRequest request,
        CancellationToken ct = default)
    {
        GarantirPermissao();

        var titulo = ValidarTitulo(request.Titulo);
        var conteudo = ValidarConteudo(request.Conteudo);
        var tema = ValidarTema(request.Tema);
        ValidarVigencia(request.VigenteDesde, request.VigenteAte);
        await GarantirTipoServicoValidoAsync(request.TipoServicoId, ct);

        var conhecimento = new ConhecimentoDeIa
        {
            TenantId = ctx.TenantId,
            Titulo = titulo,
            Conteudo = conteudo,
            Tema = tema,
            TipoServicoId = request.TipoServicoId,
            VigenteDesde = request.VigenteDesde,
            VigenteAte = request.VigenteAte,
            Versao = 1,
            CriadoPorAtendenteId = ctx.AtendenteId,
            Ativo = request.Ativo
        };

        db.ConhecimentosDeIa.Add(conhecimento);
        await db.SaveChangesAsync(ct);

        return await MapearAsync(conhecimento, ct);
    }

    public async Task<ConhecimentoDeIaResponse> AtualizarAsync(
        Guid id,
        SalvarConhecimentoDeIaRequest request,
        CancellationToken ct = default)
    {
        GarantirPermissao();

        var conhecimento = await db.ConhecimentosDeIa
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == ctx.TenantId, ct)
            ?? throw RespostaInteligenteServiceException.ConhecimentoNaoEncontrado();

        var titulo = ValidarTitulo(request.Titulo);
        var conteudo = ValidarConteudo(request.Conteudo);
        var tema = ValidarTema(request.Tema);
        ValidarVigencia(request.VigenteDesde, request.VigenteAte);
        await GarantirTipoServicoValidoAsync(request.TipoServicoId, ct);

        // Snapshot do estado ANTES de aplicar a edicao — preserva a versao que
        // estava valendo. Mesma transacao do SaveChangesAsync abaixo.
        db.HistoricoDeConhecimentosDeIa.Add(new HistoricoDeConhecimentoDeIa
        {
            TenantId = conhecimento.TenantId,
            ConhecimentoDeIaId = conhecimento.Id,
            Versao = conhecimento.Versao,
            Titulo = conhecimento.Titulo,
            Conteudo = conhecimento.Conteudo,
            Tema = conhecimento.Tema,
            TipoServicoId = conhecimento.TipoServicoId,
            VigenteDesde = conhecimento.VigenteDesde,
            VigenteAte = conhecimento.VigenteAte,
            Ativo = conhecimento.Ativo,
            AlteradoPorAtendenteId = conhecimento.AtualizadoPorAtendenteId ?? conhecimento.CriadoPorAtendenteId
        });

        conhecimento.Titulo = titulo;
        conhecimento.Conteudo = conteudo;
        conhecimento.Tema = tema;
        conhecimento.TipoServicoId = request.TipoServicoId;
        conhecimento.VigenteDesde = request.VigenteDesde;
        conhecimento.VigenteAte = request.VigenteAte;
        conhecimento.Ativo = request.Ativo;
        conhecimento.Versao += 1;
        conhecimento.AtualizadoPorAtendenteId = ctx.AtendenteId;

        await db.SaveChangesAsync(ct);

        return await MapearAsync(conhecimento, ct);
    }

    public async Task<IReadOnlyList<VersaoDeConhecimentoDeIaResponse>> HistoricoAsync(
        Guid id,
        CancellationToken ct = default)
    {
        var existe = await db.ConhecimentosDeIa.AsNoTracking()
            .AnyAsync(c => c.Id == id && c.TenantId == ctx.TenantId, ct);

        if (!existe)
        {
            throw RespostaInteligenteServiceException.ConhecimentoNaoEncontrado();
        }

        return await db.HistoricoDeConhecimentosDeIa.AsNoTracking()
            .Where(h => h.ConhecimentoDeIaId == id && h.TenantId == ctx.TenantId)
            .OrderByDescending(h => h.Versao)
            .Select(h => new VersaoDeConhecimentoDeIaResponse(
                h.Versao,
                h.Titulo,
                h.Conteudo,
                h.Tema,
                db.TiposServico.Where(t => t.Id == h.TipoServicoId).Select(t => t.Nome).FirstOrDefault(),
                h.VigenteDesde,
                h.VigenteAte,
                h.Ativo,
                db.Atendentes.Where(a => a.Id == h.AlteradoPorAtendenteId).Select(a => (string?)a.Nome).FirstOrDefault(),
                h.OcorridoEm))
            .ToListAsync(ct);
    }

    private void GarantirPermissao()
    {
        if (!ctx.Pode(PermissaoDoSistema.IaConhecimentoEditar))
        {
            throw RespostaInteligenteServiceException.SemPermissaoParaGerirConhecimento();
        }
    }

    private async Task GarantirTipoServicoValidoAsync(Guid? tipoServicoId, CancellationToken ct)
    {
        if (tipoServicoId is null)
        {
            return;
        }

        var existe = await db.TiposServico.AsNoTracking()
            .AnyAsync(t => t.Id == tipoServicoId && t.TenantId == ctx.TenantId, ct);

        if (!existe)
        {
            throw RespostaInteligenteServiceException.TipoServicoNaoEncontrado();
        }
    }

    private static string ValidarTitulo(string? titulo)
    {
        var limpo = titulo?.Trim();

        return string.IsNullOrWhiteSpace(limpo)
            ? throw RespostaInteligenteServiceException.TituloObrigatorio()
            : limpo;
    }

    private static string ValidarConteudo(string? conteudo)
    {
        var limpo = conteudo?.Trim();

        return string.IsNullOrWhiteSpace(limpo)
            ? throw RespostaInteligenteServiceException.ConteudoObrigatorio()
            : limpo;
    }

    private static string ValidarTema(string? tema)
    {
        var limpo = tema?.Trim();

        return string.IsNullOrWhiteSpace(limpo)
            ? throw RespostaInteligenteServiceException.TemaObrigatorio()
            : limpo;
    }

    private static void ValidarVigencia(DateOnly? desde, DateOnly? ate)
    {
        if (desde is not null && ate is not null && desde > ate)
        {
            throw RespostaInteligenteServiceException.VigenciaInvalida();
        }
    }

    private async Task<ConhecimentoDeIaResponse> MapearAsync(ConhecimentoDeIa conhecimento, CancellationToken ct)
    {
        var tipoServicoNome = conhecimento.TipoServicoId is null
            ? null
            : await db.TiposServico.AsNoTracking()
                .Where(t => t.Id == conhecimento.TipoServicoId)
                .Select(t => t.Nome)
                .FirstOrDefaultAsync(ct);

        var criadoPorNome = conhecimento.CriadoPorAtendenteId is null
            ? null
            : await db.Atendentes.AsNoTracking()
                .Where(a => a.Id == conhecimento.CriadoPorAtendenteId)
                .Select(a => (string?)a.Nome)
                .FirstOrDefaultAsync(ct);

        var atualizadoPorNome = conhecimento.AtualizadoPorAtendenteId is null
            ? null
            : await db.Atendentes.AsNoTracking()
                .Where(a => a.Id == conhecimento.AtualizadoPorAtendenteId)
                .Select(a => (string?)a.Nome)
                .FirstOrDefaultAsync(ct);

        return new ConhecimentoDeIaResponse(
            conhecimento.Id,
            conhecimento.Titulo,
            conhecimento.Conteudo,
            conhecimento.Tema,
            conhecimento.TipoServicoId,
            tipoServicoNome,
            conhecimento.VigenteDesde,
            conhecimento.VigenteAte,
            conhecimento.Versao,
            criadoPorNome,
            atualizadoPorNome,
            conhecimento.Ativo,
            conhecimento.CriadoEm,
            conhecimento.AtualizadoEm);
    }
}
