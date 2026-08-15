using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Configuracoes.Application;

public class SetorService(CrmDbContext db, IContextoDoUsuario ctx) : ISetorService
{
    public async Task<IReadOnlyList<SetorResponse>> ListarAsync(
        bool incluirInativos,
        CancellationToken ct = default)
    {
        var tenantId = ctx.TenantId;

        var consulta = db.Setores.AsNoTracking().Where(s => s.TenantId == tenantId);

        if (!incluirInativos)
        {
            consulta = consulta.Where(s => s.Ativo);
        }

        return await consulta
            .OrderBy(s => s.Ordem)
            .ThenBy(s => s.Nome)
            .Select(s => new SetorResponse(
                s.Id,
                s.Nome,
                s.Descricao,
                s.Ordem,
                s.Ativo,
                db.Conversas.Count(c => c.SetorId == s.Id && c.Status == StatusConversa.Ativa)))
            .ToListAsync(ct);
    }

    public async Task<SetorResponse> CriarAsync(
        SalvarSetorRequest request,
        CancellationToken ct = default)
    {
        GarantirPermissao();

        var nome = ValidarNome(request.Nome);
        await GarantirNomeLivreAsync(nome, null, ct);

        var setor = new Setor
        {
            TenantId = ctx.TenantId,
            Nome = nome,
            Descricao = string.IsNullOrWhiteSpace(request.Descricao) ? null : request.Descricao.Trim(),
            Ordem = request.Ordem,
            Ativo = request.Ativo
        };

        db.Setores.Add(setor);
        await db.SaveChangesAsync(ct);

        return Mapear(setor, 0);
    }

    public async Task<SetorResponse> AtualizarAsync(
        Guid id,
        SalvarSetorRequest request,
        CancellationToken ct = default)
    {
        GarantirPermissao();

        var setor = await db.Setores
            .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == ctx.TenantId, ct)
            ?? throw ConfiguracoesServiceException.SetorNaoEncontrado();

        var nome = ValidarNome(request.Nome);
        await GarantirNomeLivreAsync(nome, id, ct);

        setor.Nome = nome;
        setor.Descricao = string.IsNullOrWhiteSpace(request.Descricao) ? null : request.Descricao.Trim();
        setor.Ordem = request.Ordem;

        // Desativar um setor nao mexe nas conversas que ja apontam para ele: o
        // historico do atendimento continua legivel, so o setor some das opcoes.
        setor.Ativo = request.Ativo;

        await db.SaveChangesAsync(ct);

        var ativas = await db.Conversas.CountAsync(
            c => c.SetorId == setor.Id && c.Status == StatusConversa.Ativa, ct);

        return Mapear(setor, ativas);
    }

    private void GarantirPermissao()
    {
        if (!ctx.PodeGerirCadastros)
        {
            throw ConfiguracoesServiceException.SemPermissao();
        }
    }

    private async Task GarantirNomeLivreAsync(string nome, Guid? ignorarId, CancellationToken ct)
    {
        var existe = await db.Setores.AsNoTracking().AnyAsync(
            s => s.TenantId == ctx.TenantId && s.Nome == nome && (ignorarId == null || s.Id != ignorarId), ct);

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

    private static SetorResponse Mapear(Setor setor, int conversasAtivas) =>
        new(setor.Id, setor.Nome, setor.Descricao, setor.Ordem, setor.Ativo, conversasAtivas);
}
