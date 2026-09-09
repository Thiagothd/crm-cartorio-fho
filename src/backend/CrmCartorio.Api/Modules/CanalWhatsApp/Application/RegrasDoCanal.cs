using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.CanalWhatsApp.Domain.Entities;
using CrmCartorio.Api.Modules.CanalWhatsApp.Domain.Enums;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CrmCartorio.Api.Modules.CanalWhatsApp.Application;

/// <inheritdoc cref="IRegrasDoCanal"/>
public class RegrasDoCanal(
    CrmDbContext db,
    IContextoDoUsuario ctx,
    IOptions<OpcoesDoCanalWhatsApp> opcoes,
    ILogger<RegrasDoCanal> log) : IRegrasDoCanal
{
    private readonly OpcoesDoCanalWhatsApp _opcoes = opcoes.Value;

    public async Task<RegrasDoCanalResponse> ObterAsync(CancellationToken ct = default) =>
        await MontarRespostaAsync(ctx.TenantId, ct);

    public async Task<RegrasDoCanalResponse> DefinirModoAsync(
        DefinirModoRequest request,
        CancellationToken ct = default)
    {
        GarantirPermissao();

        if (!Enum.IsDefined(request.Modo))
        {
            throw CanalWhatsAppServiceException.ModoInvalido();
        }

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == ctx.TenantId, ct)
            ?? throw CanalWhatsAppServiceException.InstanciaSemTenant("(tenant atual)");

        tenant.WhatsappModoDeRecebimento = request.Modo;

        await db.SaveChangesAsync(ct);

        log.LogInformation(
            "Modo de recebimento do canal WhatsApp definido para {Modo} pelo tenant {TenantId}.",
            request.Modo, ctx.TenantId);

        return await MontarRespostaAsync(ctx.TenantId, ct);
    }

    public async Task<NumeroDoCanalResponse> AdicionarAsync(
        AdicionarNumeroRequest request,
        CancellationToken ct = default)
    {
        GarantirPermissao();

        if (!Enum.IsDefined(request.Tipo))
        {
            throw CanalWhatsAppServiceException.ModoInvalido();
        }

        var telefone = NormalizadorDeTelefone.ParaE164(request.Telefone);

        // Mesma equivalencia com/sem nono digito do PodeFalarComAsync — sem
        // isto, cadastrar a mesma pessoa nas duas formas (o contorno manual
        // que o achado de 2026-09-09 exigiu antes desta correcao) passaria
        // batido, como duas pessoas diferentes.
        var jaExiste = (await db.CanalWhatsappNumeros.AsNoTracking()
                .Where(n => n.TenantId == ctx.TenantId)
                .Select(n => n.Telefone)
                .ToListAsync(ct))
            .Any(numero => NormalizadorDeTelefone.NumerosEquivalentes(numero, telefone));

        if (jaExiste)
        {
            throw CanalWhatsAppServiceException.NumeroDuplicado(telefone);
        }

        var numero = new NumeroDoCanal
        {
            TenantId = ctx.TenantId,
            Telefone = telefone,
            Rotulo = string.IsNullOrWhiteSpace(request.Rotulo) ? null : request.Rotulo.Trim(),
            Tipo = request.Tipo,
            CriadoPorAtendenteId = ctx.AtendenteId
        };

        db.CanalWhatsappNumeros.Add(numero);
        await db.SaveChangesAsync(ct);

        return new NumeroDoCanalResponse(
            numero.Id, numero.Telefone, numero.Rotulo, numero.Tipo, numero.CriadoEm, ctx.Nome);
    }

    public async Task RemoverAsync(Guid numeroId, CancellationToken ct = default)
    {
        GarantirPermissao();

        var numero = await db.CanalWhatsappNumeros
            .FirstOrDefaultAsync(n => n.Id == numeroId && n.TenantId == ctx.TenantId, ct)
            ?? throw CanalWhatsAppServiceException.NumeroNaoEncontrado();

        db.CanalWhatsappNumeros.Remove(numero);
        await db.SaveChangesAsync(ct);
    }

    public async Task<VeredictoDoNumero> PodeFalarComAsync(
        Guid tenantId,
        string telefoneE164,
        CancellationToken ct = default)
    {
        var digitos = NormalizadorDeTelefone.SomenteDigitos(telefoneE164);

        var modo = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.WhatsappModoDeRecebimento)
            .FirstOrDefaultAsync(ct);

        // Nunca configurado pela tela: cai no comportamento de sempre, via
        // OpcoesDoCanalWhatsApp.NumeroPermitido — NAO uma checagem manual em
        // paralelo. Esse metodo ja resolve NumerosBloqueados (precedencia sobre
        // a lista) e a equivalencia de JID brasileiro legado (com/sem nono
        // digito); reimplementar so o NumerosDeTeste aqui deixaria essa fonte
        // mais fraca que a que ela substitui.
        if (modo is null)
        {
            if (_opcoes.NumeroPermitido(telefoneE164))
            {
                return new VeredictoDoNumero(true, null);
            }

            // Motivo aproximado so para o rotulo do descarte — a comparacao que
            // decide de verdade e a de cima. Bloqueio tem precedencia, entao
            // checar aqui de novo (sem a equivalencia de JID) e suficiente para
            // rotular corretamente o caso comum.
            var estaNaListaDeBloqueio = _opcoes.NumerosBloqueados
                .Select(NormalizadorDeTelefone.SomenteDigitos)
                .Contains(digitos, StringComparer.Ordinal);

            return new VeredictoDoNumero(
                false, estaNaListaDeBloqueio ? MotivoDoDescarte.Bloqueado : MotivoDoDescarte.ForaDaLista);
        }

        if (modo == ModoDeRecebimento.Todos)
        {
            return new VeredictoDoNumero(true, null);
        }

        if (modo == ModoDeRecebimento.SomenteLiberados)
        {
            var liberado = await ExisteEquivalenteAsync(tenantId, digitos, TipoDeNumero.Liberado, ct);

            return liberado
                ? new VeredictoDoNumero(true, null)
                : new VeredictoDoNumero(false, MotivoDoDescarte.ForaDaLista);
        }

        // TodosMenosBloqueados
        var bloqueado = await ExisteEquivalenteAsync(tenantId, digitos, TipoDeNumero.Bloqueado, ct);

        return bloqueado
            ? new VeredictoDoNumero(false, MotivoDoDescarte.Bloqueado)
            : new VeredictoDoNumero(true, null);
    }

    /// <remarks>
    /// CORRECAO (achado ao vivo, 2026-09-09) — a comparacao era <c>n.Telefone
    /// == digitos</c>, exata. O WhatsApp entrega o mesmo celular brasileiro ora
    /// com o nono digito, ora sem (a mesma inconsistencia que
    /// <see cref="OpcoesDoCanalWhatsApp.NumeroPermitido"/> ja tratava para a
    /// lista de bloqueio do appsettings): uma atendente cadastrada em
    /// "Somente os liberados" com o numero completo teve mensagens dela mesma
    /// descartadas como "fora da lista" so' porque chegaram na forma sem o
    /// nono. A lista de liberados/bloqueados de um tenant e' pequena (pessoas,
    /// nao clientes) — trazer para memoria e comparar com
    /// <see cref="NormalizadorDeTelefone.NumerosEquivalentes"/> e seguro e
    /// correto; nao ha' como expressar essa equivalencia num predicado que o
    /// EF traduza para SQL.
    /// </remarks>
    private async Task<bool> ExisteEquivalenteAsync(
        Guid tenantId, string digitos, TipoDeNumero tipo, CancellationToken ct)
    {
        var cadastrados = await db.CanalWhatsappNumeros.AsNoTracking()
            .Where(n => n.TenantId == tenantId && n.Tipo == tipo)
            .Select(n => n.Telefone)
            .ToListAsync(ct);

        return cadastrados.Any(numero => NormalizadorDeTelefone.NumerosEquivalentes(numero, digitos));
    }

    public async Task RegistrarDescarteAsync(
        Guid tenantId,
        string telefoneE164,
        MotivoDoDescarte motivo,
        CancellationToken ct = default)
    {
        await PodarDescartesAntigosAsync(tenantId, ct);

        db.CanalWhatsappDescartes.Add(new DescarteDoCanal
        {
            TenantId = tenantId,
            Telefone = NormalizadorDeTelefone.SomenteDigitos(telefoneE164),
            Motivo = motivo
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task<DescartesResponse> DescartesRecentesAsync(CancellationToken ct = default)
    {
        await PodarDescartesAntigosAsync(ctx.TenantId, ct);

        var corte = DateTime.UtcNow.AddDays(-1);

        var totalHoje = await db.CanalWhatsappDescartes.AsNoTracking()
            .CountAsync(d => d.TenantId == ctx.TenantId && d.CriadoEm >= corte, ct);

        var recentes = await db.CanalWhatsappDescartes.AsNoTracking()
            .Where(d => d.TenantId == ctx.TenantId)
            .OrderByDescending(d => d.CriadoEm)
            .Take(10)
            .Select(d => new DescarteResponse(d.Telefone, d.Motivo, d.CriadoEm))
            .ToListAsync(ct);

        return new DescartesResponse(totalHoje, recentes);
    }

    /// <remarks>
    /// Telefone de quem nao esta autorizado a falar com o canal nao e dado de
    /// atendimento -- poda antes de cada escrita/leitura em vez de um job a
    /// parte, porque o volume e baixo e um BackgroundService a mais para isto
    /// seria peso desnecessario.
    /// </remarks>
    private async Task PodarDescartesAntigosAsync(Guid tenantId, CancellationToken ct)
    {
        var corte = DateTime.UtcNow.AddDays(-7);

        // RemoveRange, e nao ExecuteDeleteAsync: o volume aqui e baixo (so
        // telefone fora da regra, poucos por dia), e carregar antes de remover
        // funciona em qualquer provider — o verificador deste modulo roda contra
        // EF InMemory, que nao suporta ExecuteDelete.
        var antigos = await db.CanalWhatsappDescartes
            .Where(d => d.TenantId == tenantId && d.CriadoEm < corte)
            .ToListAsync(ct);

        if (antigos.Count > 0)
        {
            db.CanalWhatsappDescartes.RemoveRange(antigos);
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<RegrasDoCanalResponse> MontarRespostaAsync(Guid tenantId, CancellationToken ct)
    {
        var modo = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.WhatsappModoDeRecebimento)
            .FirstOrDefaultAsync(ct);

        var origem = modo is null ? OrigemDaRegra.Appsettings : OrigemDaRegra.Banco;

        var numeros = await db.CanalWhatsappNumeros.AsNoTracking()
            .Where(n => n.TenantId == tenantId)
            .OrderByDescending(n => n.CriadoEm)
            .Select(n => new
            {
                n.Id,
                n.Telefone,
                n.Rotulo,
                n.Tipo,
                n.CriadoEm,
                CriadoPorNome = db.Atendentes
                    .Where(a => a.Id == n.CriadoPorAtendenteId)
                    .Select(a => (string?)a.Nome)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var liberados = numeros
            .Where(n => n.Tipo == TipoDeNumero.Liberado)
            .Select(n => new NumeroDoCanalResponse(n.Id, n.Telefone, n.Rotulo, n.Tipo, n.CriadoEm, n.CriadoPorNome))
            .ToList();

        var bloqueados = numeros
            .Where(n => n.Tipo == TipoDeNumero.Bloqueado)
            .Select(n => new NumeroDoCanalResponse(n.Id, n.Telefone, n.Rotulo, n.Tipo, n.CriadoEm, n.CriadoPorNome))
            .ToList();

        return new RegrasDoCanalResponse(modo ?? ModoDeRecebimento.Todos, origem, liberados, bloqueados);
    }

    private void GarantirPermissao()
    {
        // Mesma trava do resto do canal (WhatsAppController.Garantir): decidir
        // quem fala com o numero do cartorio e decisao de quem gere o canal,
        // nao de qualquer atendente.
        if (!ctx.Pode(PermissaoDoSistema.WhatsappRegras))
        {
            throw CanalWhatsAppServiceException.WebhookNaoAutorizado();
        }
    }

}
