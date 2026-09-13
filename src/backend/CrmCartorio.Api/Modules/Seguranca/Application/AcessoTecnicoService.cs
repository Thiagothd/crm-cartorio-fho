using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Infrastructure.Auditoria;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Seguranca.Domain.Entities;
using CrmCartorio.Api.Modules.Seguranca.Domain.Enums;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Seguranca.Application;

/// <inheritdoc cref="IAcessoTecnicoService"/>
public class AcessoTecnicoService(
    CrmDbContext db,
    IContextoDoUsuario ctx,
    IRegistradorDeAcesso registrador,
    IHttpContextAccessor acessor) : IAcessoTecnicoService
{
    /// <summary>Teto de 8h — reforçado aqui, nunca confiado só à validação de entrada da tela.</summary>
    private const int DuracaoMaximaMinutos = 480;

    public async Task<AutorizacaoDeAcessoTecnicoResponse> SolicitarAsync(
        SolicitarAcessoTecnicoRequest request, CancellationToken ct = default)
    {
        GarantirPermissao(PermissaoDoSistema.SuporteSolicitarAcesso);

        if (string.IsNullOrWhiteSpace(request.Motivo))
        {
            throw SegurancaServiceException.MotivoObrigatorio();
        }

        if (request.DuracaoMinutos is < 1 or > DuracaoMaximaMinutos)
        {
            throw SegurancaServiceException.DuracaoInvalida(DuracaoMaximaMinutos);
        }

        Guid? conversaId = null;

        if (request.Escopo == EscopoDeAcesso.Conversa)
        {
            if (string.IsNullOrWhiteSpace(request.ConversaProtocolo))
            {
                throw SegurancaServiceException.EscopoDeConversaExigeConversaId();
            }

            // Resolve por PROTOCOLO, nao por Guid: a conta tecnica ainda nao
            // tem atendimento.ver neste momento (so ganha DEPOIS de aprovada),
            // entao nunca teria como saber o id interno — so o protocolo, que
            // o cartorio ja comunica por fora. E busca de EXISTENCIA, nunca de
            // conteudo: nao precisa de atendimento.ver para confirmar que um
            // protocolo existe neste tenant.
            conversaId = await db.Conversas.AsNoTracking()
                .Where(c => c.TenantId == ctx.TenantId && c.Protocolo == request.ConversaProtocolo.Trim())
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(ct);

            if (conversaId is null)
            {
                throw SegurancaServiceException.ConversaNaoEncontrada();
            }
        }

        var agora = DateTime.UtcNow;

        // Uma conta técnica só tem UM pedido em aberto (pendente ou ativo)
        // por vez — pedir de novo enquanto o anterior ainda vale só
        // confundiria quem aprova.
        var jaTemEmAberto = await db.AutorizacoesDeAcessoTecnico.AsNoTracking()
            .AnyAsync(a => a.ContaTecnicaId == ctx.AtendenteId
                        && a.TenantId == ctx.TenantId
                        && a.NegadaEm == null
                        && a.RevogadaEm == null
                        && (a.AprovadaEm == null || a.ValidaAte > agora), ct);

        if (jaTemEmAberto)
        {
            throw SegurancaServiceException.JaExisteSolicitacaoEmAberto();
        }

        var autorizacao = new AutorizacaoDeAcessoTecnico
        {
            TenantId = ctx.TenantId,
            ContaTecnicaId = ctx.AtendenteId,
            Motivo = request.Motivo.Trim(),
            Escopo = request.Escopo,
            ConversaId = conversaId,
            DuracaoSolicitadaMinutos = request.DuracaoMinutos,
            SolicitadaEm = agora,
            IpDeSolicitacao = ObterIp()
        };

        db.AutorizacoesDeAcessoTecnico.Add(autorizacao);
        await db.SaveChangesAsync(ct);

        await registrador.RegistrarAsync(
            TipoDeAcesso.AcessoTecnicoSolicitado, autorizacaoDeAcessoTecnicoId: autorizacao.Id, ct: ct);

        return await MapearAsync(autorizacao, ct);
    }

    public async Task<AutorizacaoDeAcessoTecnicoResponse> AprovarAsync(Guid id, CancellationToken ct = default)
    {
        GarantirPermissao(PermissaoDoSistema.SegurancaAcessoTecnicoAprovar);

        var autorizacao = await db.AutorizacoesDeAcessoTecnico.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id && a.TenantId == ctx.TenantId, ct)
            ?? throw SegurancaServiceException.AutorizacaoNaoEncontrada();

        if (autorizacao.AprovadaEm is not null || autorizacao.NegadaEm is not null)
        {
            throw SegurancaServiceException.AutorizacaoNaoEstaPendente();
        }

        // Decisão do Thiago em 2026-09-12: sem segundo-fator adicional aqui.
        // O desenho original (PR4 do plano de blindagem LGPD) pedia TOTP do
        // aprovador como step-up sobre a sessão já aberta — defesa extra
        // contra sessão roubada. Avaliado e descartado: o ator já autenticou
        // com senha (+ MFA, quando a política do tenant exige) para entrar
        // no painel; exigir TOTP de novo só para aprovar foi considerado
        // atrito sem ganho que compensasse, na prática do cartório. A trava
        // de permissao (SegurancaAcessoTecnicoAprovar) + a trilha registrada
        // abaixo continuam de pé.
        var agora = DateTime.UtcNow;
        var validaAte = agora.AddMinutes(autorizacao.DuracaoSolicitadaMinutos);

        // Atomico: so aprova se AINDA estiver pendente no instante da escrita
        // — evita dois administradores aprovando/negando a mesma solicitacao
        // em paralelo.
        var aprovou = await db.AutorizacoesDeAcessoTecnico
            .Where(a => a.Id == id && a.AprovadaEm == null && a.NegadaEm == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.AprovadaPorAtendenteId, ctx.AtendenteId)
                .SetProperty(a => a.AprovadaEm, agora)
                .SetProperty(a => a.ValidaAte, validaAte), ct);

        if (aprovou != 1)
        {
            throw SegurancaServiceException.AutorizacaoNaoEstaPendente();
        }

        await registrador.RegistrarAsync(TipoDeAcesso.AcessoTecnicoAprovado, autorizacaoDeAcessoTecnicoId: id, ct: ct);

        var atualizada = await db.AutorizacoesDeAcessoTecnico.AsNoTracking().FirstAsync(a => a.Id == id, ct);
        return await MapearAsync(atualizada, ct);
    }

    public async Task<AutorizacaoDeAcessoTecnicoResponse> NegarAsync(Guid id, CancellationToken ct = default)
    {
        GarantirPermissao(PermissaoDoSistema.SegurancaAcessoTecnicoAprovar);

        var negou = await db.AutorizacoesDeAcessoTecnico
            .Where(a => a.Id == id && a.TenantId == ctx.TenantId && a.AprovadaEm == null && a.NegadaEm == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.NegadaPorAtendenteId, ctx.AtendenteId)
                .SetProperty(a => a.NegadaEm, DateTime.UtcNow), ct);

        if (negou != 1)
        {
            var existe = await db.AutorizacoesDeAcessoTecnico.AsNoTracking()
                .AnyAsync(a => a.Id == id && a.TenantId == ctx.TenantId, ct);
            throw existe ? SegurancaServiceException.AutorizacaoNaoEstaPendente() : SegurancaServiceException.AutorizacaoNaoEncontrada();
        }

        await registrador.RegistrarAsync(TipoDeAcesso.AcessoTecnicoNegado, autorizacaoDeAcessoTecnicoId: id, ct: ct);

        var atualizada = await db.AutorizacoesDeAcessoTecnico.AsNoTracking().FirstAsync(a => a.Id == id, ct);
        return await MapearAsync(atualizada, ct);
    }

    public async Task<AutorizacaoDeAcessoTecnicoResponse> RevogarAsync(Guid id, CancellationToken ct = default)
    {
        GarantirPermissao(PermissaoDoSistema.SegurancaAcessoTecnicoAprovar);

        // Sem segundo fator de propósito: reduzir acesso nunca é ação
        // sensível o bastante para exigir atrito — mesma decisão de
        // MfaService.EsquecerDispositivosConfiaveisAsync.
        var revogou = await db.AutorizacoesDeAcessoTecnico
            .Where(a => a.Id == id && a.TenantId == ctx.TenantId && a.AprovadaEm != null && a.RevogadaEm == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.RevogadaPorAtendenteId, ctx.AtendenteId)
                .SetProperty(a => a.RevogadaEm, DateTime.UtcNow), ct);

        if (revogou != 1)
        {
            var existe = await db.AutorizacoesDeAcessoTecnico.AsNoTracking()
                .AnyAsync(a => a.Id == id && a.TenantId == ctx.TenantId, ct);
            throw existe ? SegurancaServiceException.AutorizacaoNaoPodeSerRevogada() : SegurancaServiceException.AutorizacaoNaoEncontrada();
        }

        await registrador.RegistrarAsync(TipoDeAcesso.AcessoTecnicoRevogado, autorizacaoDeAcessoTecnicoId: id, ct: ct);

        var atualizada = await db.AutorizacoesDeAcessoTecnico.AsNoTracking().FirstAsync(a => a.Id == id, ct);
        return await MapearAsync(atualizada, ct);
    }

    public async Task<AcessoTecnicoAtivoResponse?> ObterAtivaAsync(CancellationToken ct = default)
    {
        // Sem guarda de permissão de propósito: é o que alimenta a faixa
        // global — todo autenticado do tenant precisa ver que um acesso
        // técnico está em curso, não só quem administra.
        var agora = DateTime.UtcNow;

        return await db.AutorizacoesDeAcessoTecnico.AsNoTracking()
            .Where(a => a.TenantId == ctx.TenantId
                     && a.AprovadaEm != null
                     && a.RevogadaEm == null
                     && a.ValidaAte != null && a.ValidaAte > agora)
            .OrderByDescending(a => a.AprovadaEm)
            .Select(a => new AcessoTecnicoAtivoResponse(a.ValidaAte!.Value, a.Motivo))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Duas platéias, um endpoint só. Quem tem <c>seguranca.eventos.ver</c>
    /// (administrador) vê TODOS os pedidos do tenant — é o painel de
    /// aprovação. Quem só tem <c>suporte.solicitar_acesso</c> (a própria
    /// conta técnica) vê SÓ os pedidos que ela mesma fez — é "Meus pedidos"
    /// na tela de Suporte técnico. Antes desta correção, a conta técnica
    /// batia sem-permissão aqui (só tinha a segunda chave, não a primeira) e
    /// nunca via nem o próprio histórico de pedidos.
    /// </summary>
    public async Task<PaginaDeSeguranca<AutorizacaoDeAcessoTecnicoResponse>> ListarAsync(
        FiltroDeAcessoTecnico filtro, CancellationToken ct = default)
    {
        var podeVerTodos = ctx.Pode(PermissaoDoSistema.SegurancaEventosVer);
        if (!podeVerTodos)
        {
            GarantirPermissao(PermissaoDoSistema.SuporteSolicitarAcesso);
        }

        var tenantId = ctx.TenantId;
        var (de, ate, _) = PeriodoDeAnalise.Resolver(filtro.De, filtro.Ate, DateTime.UtcNow);

        var consulta = db.AutorizacoesDeAcessoTecnico.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.SolicitadaEm >= de && a.SolicitadaEm <= ate);

        if (!podeVerTodos)
        {
            var atendenteId = ctx.AtendenteId;
            consulta = consulta.Where(a => a.ContaTecnicaId == atendenteId);
        }

        var total = await consulta.CountAsync(ct);

        var pagina = filtro.Pagina < 1 ? 1 : filtro.Pagina;
        var tamanho = Math.Clamp(filtro.Tamanho, 1, 200);

        var itens = await consulta
            .OrderByDescending(a => a.SolicitadaEm)
            .Skip((pagina - 1) * tamanho)
            .Take(tamanho)
            .ToListAsync(ct);

        var respostas = new List<AutorizacaoDeAcessoTecnicoResponse>(itens.Count);
        foreach (var item in itens)
        {
            respostas.Add(await MapearAsync(item, ct));
        }

        return new PaginaDeSeguranca<AutorizacaoDeAcessoTecnicoResponse>(respostas, total, pagina, tamanho);
    }

    private async Task<AutorizacaoDeAcessoTecnicoResponse> MapearAsync(AutorizacaoDeAcessoTecnico a, CancellationToken ct)
    {
        var contaTecnicaNome = await db.Atendentes.AsNoTracking()
            .Where(x => x.Id == a.ContaTecnicaId).Select(x => x.Nome).FirstOrDefaultAsync(ct) ?? "—";

        var conversaProtocolo = a.ConversaId is null
            ? null
            : await db.Conversas.AsNoTracking()
                .Where(c => c.Id == a.ConversaId).Select(c => c.Protocolo).FirstOrDefaultAsync(ct);

        var aprovadaPorNome = a.AprovadaPorAtendenteId is null
            ? null
            : await db.Atendentes.AsNoTracking()
                .Where(x => x.Id == a.AprovadaPorAtendenteId).Select(x => x.Nome).FirstOrDefaultAsync(ct);

        var negadaPorNome = a.NegadaPorAtendenteId is null
            ? null
            : await db.Atendentes.AsNoTracking()
                .Where(x => x.Id == a.NegadaPorAtendenteId).Select(x => x.Nome).FirstOrDefaultAsync(ct);

        var revogadaPorNome = a.RevogadaPorAtendenteId is null
            ? null
            : await db.Atendentes.AsNoTracking()
                .Where(x => x.Id == a.RevogadaPorAtendenteId).Select(x => x.Nome).FirstOrDefaultAsync(ct);

        return new AutorizacaoDeAcessoTecnicoResponse(
            a.Id,
            a.ContaTecnicaId,
            contaTecnicaNome,
            a.Motivo,
            a.Escopo,
            a.ConversaId,
            conversaProtocolo,
            a.DuracaoSolicitadaMinutos,
            a.SolicitadaEm,
            DerivarEstado(a, DateTime.UtcNow),
            aprovadaPorNome,
            a.ValidaAte,
            negadaPorNome,
            revogadaPorNome);
    }

    private static EstadoDeAutorizacao DerivarEstado(AutorizacaoDeAcessoTecnico a, DateTime agora) => a switch
    {
        { RevogadaEm: not null } => EstadoDeAutorizacao.Revogada,
        { NegadaEm: not null } => EstadoDeAutorizacao.Negada,
        { AprovadaEm: not null, ValidaAte: { } validaAte } when validaAte > agora => EstadoDeAutorizacao.Ativa,
        { AprovadaEm: not null } => EstadoDeAutorizacao.Expirada,
        _ => EstadoDeAutorizacao.Pendente
    };

    private string? ObterIp() =>
        acessor.HttpContext is { } http ? ResolvedorDeIpDoCliente.Resolver(http) : null;

    private void GarantirPermissao(PermissaoDoSistema permissao)
    {
        if (!ctx.Pode(permissao))
        {
            throw SegurancaServiceException.SemPermissao();
        }
    }
}
