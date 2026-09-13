using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Seguranca.Domain.Enums;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Seguranca.Application;

/// <inheritdoc cref="ISegurancaService"/>
public class SegurancaService(
    CrmDbContext db,
    IContextoDoUsuario ctx,
    IProvedorDeChaveDeDados provedorDeChave) : ISegurancaService
{
    public async Task<PaginaDeSeguranca<RegistroDeAcessoResponse>> ListarAcessosAsync(
        FiltroDeAcessos filtro, CancellationToken ct = default)
    {
        GarantirPermissao(PermissaoDoSistema.SegurancaEventosVer);

        var tenantId = ctx.TenantId;
        var (de, ate, _) = PeriodoDeAnalise.Resolver(filtro.De, filtro.Ate, DateTime.UtcNow);

        var consulta = db.RegistrosDeAcesso.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.OcorridoEm >= de && r.OcorridoEm <= ate);

        if (filtro.AtendenteId is { } atendenteId)
        {
            consulta = consulta.Where(r => r.AtendenteId == atendenteId);
        }

        if (filtro.ConversaId is { } conversaId)
        {
            consulta = consulta.Where(r => r.ConversaId == conversaId);
        }

        var total = await consulta.CountAsync(ct);

        var pagina = filtro.Pagina < 1 ? 1 : filtro.Pagina;
        var tamanho = Math.Clamp(filtro.Tamanho, 1, 200);

        var itens = await consulta
            .OrderByDescending(r => r.OcorridoEm)
            .Skip((pagina - 1) * tamanho)
            .Take(tamanho)
            .Select(r => new RegistroDeAcessoResponse(
                r.Id,
                r.OcorridoEm,
                r.Tipo,
                db.Atendentes.Where(a => a.Id == r.AtendenteId).Select(a => a.Nome).FirstOrDefault(),
                r.AtorExterno,
                db.Conversas.Where(c => c.Id == r.ConversaId).Select(c => c.Protocolo).FirstOrDefault(),
                db.Clientes.Where(cl => cl.Id ==
                        (r.ClienteId ?? db.Conversas.Where(c => c.Id == r.ConversaId).Select(c => c.ClienteId).FirstOrDefault()))
                    .Select(cl => cl.Nome).FirstOrDefault(),
                r.Tipo == TipoDeAcesso.DownloadDeAnexo,
                r.AutorizacaoDeAcessoTecnicoId,
                r.IpDeOrigem))
            .ToListAsync(ct);

        return new PaginaDeSeguranca<RegistroDeAcessoResponse>(itens, total, pagina, tamanho);
    }

    public async Task<PaginaDeSeguranca<EventoDeSegurancaResponse>> ListarEventosAsync(
        FiltroDeEventos filtro, CancellationToken ct = default)
    {
        GarantirPermissao(PermissaoDoSistema.SegurancaEventosVer);

        var tenantId = ctx.TenantId;
        var (de, ate, _) = PeriodoDeAnalise.Resolver(filtro.De, filtro.Ate, DateTime.UtcNow);

        var consulta = db.EventosDeSeguranca.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.OcorridoEm >= de && e.OcorridoEm <= ate);

        var total = await consulta.CountAsync(ct);

        var pagina = filtro.Pagina < 1 ? 1 : filtro.Pagina;
        var tamanho = Math.Clamp(filtro.Tamanho, 1, 200);

        var itens = await consulta
            .OrderByDescending(e => e.OcorridoEm)
            .Skip((pagina - 1) * tamanho)
            .Take(tamanho)
            .Select(e => new EventoDeSegurancaResponse(
                e.Id,
                e.OcorridoEm,
                e.Tipo,
                db.Atendentes.Where(a => a.Id == e.AtendenteId).Select(a => a.Nome).FirstOrDefault(),
                db.Atendentes.Where(a => a.Id == e.AtorAtendenteId).Select(a => a.Nome).FirstOrDefault(),
                e.AtorExterno,
                e.IpDeOrigem,
                e.Motivo))
            .ToListAsync(ct);

        return new PaginaDeSeguranca<EventoDeSegurancaResponse>(itens, total, pagina, tamanho);
    }

    public async Task<ProtecaoAtivaResponse> ObterProtecaoAtivaAsync(CancellationToken ct = default)
    {
        GarantirPermissao(PermissaoDoSistema.SegurancaEventosVer);

        var tenantId = ctx.TenantId;

        var total = await db.RegistrosDeAcesso.AsNoTracking()
            .LongCountAsync(r => r.TenantId == tenantId, ct);

        DateTime? cifraDesde = null;

        if (provedorDeChave.CifraAtiva)
        {
            // O primeiro CifraDeLegadoConcluida marca quando --cifrar-dados-legados
            // varreu a base pela primeira vez. Antes disso rodar, a cifra pode
            // estar ativa (chave ja configurada) sem nenhuma linha legada
            // convertida ainda — CifraDesde continua nulo nesse meio-termo, de
            // proposito: "ativa" e "toda a base ja esta cifrada" sao coisas
            // diferentes.
            cifraDesde = await db.RegistrosDeAcesso.AsNoTracking()
                .Where(r => r.TenantId == tenantId && r.Tipo == TipoDeAcesso.CifraDeLegadoConcluida)
                .OrderBy(r => r.OcorridoEm)
                .Select(r => (DateTime?)r.OcorridoEm)
                .FirstOrDefaultAsync(ct);
        }

        return new ProtecaoAtivaResponse(
            CifraAtiva: provedorDeChave.CifraAtiva,
            VersaoDaChave: provedorDeChave.CifraAtiva ? provedorDeChave.VersaoCorrente : null,
            CifraDesde: cifraDesde,
            TotalDeRegistrosDeAcesso: total);
    }

    private void GarantirPermissao(PermissaoDoSistema permissao)
    {
        if (!ctx.Pode(permissao))
        {
            throw SegurancaServiceException.SemPermissao();
        }
    }
}
