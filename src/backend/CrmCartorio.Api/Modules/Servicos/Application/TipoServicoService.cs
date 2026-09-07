using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.Servicos.Domain.Entities;
using CrmCartorio.Api.Modules.Servicos.Domain.Enums;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Servicos.Application;

public class TipoServicoService(CrmDbContext db, IContextoDoUsuario ctx) : ITipoServicoService
{
    public async Task<IReadOnlyList<TipoServicoResponse>> ListarAsync(
        bool incluirInativos,
        CancellationToken ct = default)
    {
        var tenantId = ctx.TenantId;

        var consulta = db.TiposServico.AsNoTracking()
            .Include(t => t.Prazo)
            .Where(t => t.TenantId == tenantId);

        if (!incluirInativos)
        {
            consulta = consulta.Where(t => t.Ativo);
        }

        var itens = await consulta
            .OrderBy(t => t.Categoria)
            .ThenBy(t => t.Nome)
            .ToListAsync(ct);

        var ativasPorServico = await db.Conversas.AsNoTracking()
            .Where(c => c.TenantId == tenantId
                     && c.Status == StatusConversa.Ativa
                     && c.TipoServicoId != null)
            .GroupBy(c => c.TipoServicoId!.Value)
            .Select(g => new { TipoServicoId = g.Key, Total = g.Count() })
            .ToListAsync(ct);

        var nomesDeSetor = await NomesDeSetorAsync(tenantId, ct);

        return
        [
            .. itens.Select(t => Mapear(
                t,
                ativasPorServico.FirstOrDefault(x => x.TipoServicoId == t.Id)?.Total ?? 0,
                nomesDeSetor))
        ];
    }

    public async Task<TipoServicoResponse> ObterAsync(Guid id, CancellationToken ct = default)
    {
        var tipo = await db.TiposServico.AsNoTracking()
            .Include(t => t.Prazo)
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == ctx.TenantId, ct)
            ?? throw ServicosServiceException.NaoEncontrado();

        var ativas = await db.Conversas.CountAsync(
            c => c.TipoServicoId == id && c.Status == StatusConversa.Ativa, ct);

        var nomesDeSetor = await NomesDeSetorAsync(ctx.TenantId, ct);

        return Mapear(tipo, ativas, nomesDeSetor);
    }

    /// <summary>
    /// TipoServico nao tem propriedade de navegacao para Setor — modulos distintos,
    /// e so o Id atravessa a fronteira (CLAUDE.md, 7.1). Mesmo padrao de
    /// DisparadorDeMensagemAutomatica.MontarDadosAsync: consulta direta pelo
    /// DbContext, nao chamada a um Service de outro modulo.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, string>> NomesDeSetorAsync(Guid tenantId, CancellationToken ct) =>
        await db.Setores.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .ToDictionaryAsync(s => s.Id, s => s.Nome, ct);

    public async Task<TipoServicoResponse> CriarAsync(
        SalvarTipoServicoRequest request,
        CancellationToken ct = default)
    {
        GarantirPermissao();

        var nome = ValidarNome(request.Nome);
        await GarantirNomeLivreAsync(nome, null, ct);
        await GarantirSetorValidoAsync(request.SetorId, ct);

        var tipo = new TipoServico
        {
            TenantId = ctx.TenantId,
            Nome = nome,
            Codigo = Limpar(request.Codigo),
            Categoria = request.Categoria,
            Descricao = Limpar(request.Descricao),
            Ativo = request.Ativo,
            SetorId = request.SetorId,
            DocumentosNecessarios = Limpar(request.DocumentosNecessarios)
        };

        db.TiposServico.Add(tipo);

        if (request.Prazo is { } prazo)
        {
            ValidarPrazo(prazo);

            db.PrazosServico.Add(new PrazoServico
            {
                TenantId = ctx.TenantId,
                TipoServicoId = tipo.Id,
                PrazoHoras = prazo.PrazoHoras,
                PercentualAlerta = prazo.PercentualAlerta,
                ApenasDiasUteis = prazo.ApenasDiasUteis,
                Ativo = true
            });
        }

        await db.SaveChangesAsync(ct);

        return await ObterAsync(tipo.Id, ct);
    }

    public async Task<TipoServicoResponse> AtualizarAsync(
        Guid id,
        SalvarTipoServicoRequest request,
        CancellationToken ct = default)
    {
        GarantirPermissao();

        var tipo = await db.TiposServico
            .Include(t => t.Prazo)
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == ctx.TenantId, ct)
            ?? throw ServicosServiceException.NaoEncontrado();

        var nome = ValidarNome(request.Nome);
        await GarantirNomeLivreAsync(nome, id, ct);
        await GarantirSetorValidoAsync(request.SetorId, ct);

        tipo.Nome = nome;
        tipo.Codigo = Limpar(request.Codigo);
        tipo.Categoria = request.Categoria;
        tipo.Descricao = Limpar(request.Descricao);
        tipo.Ativo = request.Ativo;
        tipo.SetorId = request.SetorId;
        tipo.DocumentosNecessarios = Limpar(request.DocumentosNecessarios);

        if (request.Prazo is { } prazo)
        {
            ValidarPrazo(prazo);

            if (tipo.Prazo is null)
            {
                db.PrazosServico.Add(new PrazoServico
                {
                    TenantId = ctx.TenantId,
                    TipoServicoId = tipo.Id,
                    PrazoHoras = prazo.PrazoHoras,
                    PercentualAlerta = prazo.PercentualAlerta,
                    ApenasDiasUteis = prazo.ApenasDiasUteis,
                    Ativo = true
                });
            }
            else
            {
                tipo.Prazo.PrazoHoras = prazo.PrazoHoras;
                tipo.Prazo.PercentualAlerta = prazo.PercentualAlerta;
                tipo.Prazo.ApenasDiasUteis = prazo.ApenasDiasUteis;
                tipo.Prazo.Ativo = true;
            }
        }
        else if (tipo.Prazo is not null)
        {
            // O SLA e desativado, nao removido: conversas antigas continuam com o
            // prazo que valia quando foram abertas, e o registro nao some do banco.
            tipo.Prazo.Ativo = false;
        }

        await db.SaveChangesAsync(ct);

        return await ObterAsync(tipo.Id, ct);
    }

    private void GarantirPermissao()
    {
        if (!ctx.Pode(PermissaoDoSistema.ServicosEditar))
        {
            throw ServicosServiceException.SemPermissao();
        }
    }

    private async Task GarantirNomeLivreAsync(string nome, Guid? ignorarId, CancellationToken ct)
    {
        var existe = await db.TiposServico.AsNoTracking().AnyAsync(
            t => t.TenantId == ctx.TenantId && t.Nome == nome && (ignorarId == null || t.Id != ignorarId), ct);

        if (existe)
        {
            throw ServicosServiceException.NomeEmUso(nome);
        }
    }

    private async Task GarantirSetorValidoAsync(Guid? setorId, CancellationToken ct)
    {
        if (setorId is null)
        {
            return;
        }

        var existe = await db.Setores.AsNoTracking()
            .AnyAsync(s => s.Id == setorId && s.TenantId == ctx.TenantId, ct);

        if (!existe)
        {
            throw ServicosServiceException.SetorInvalido();
        }
    }

    private static void ValidarPrazo(PrazoServicoRequest prazo)
    {
        if (prazo.PrazoHoras < 1)
        {
            throw ServicosServiceException.PrazoInvalido();
        }

        if (prazo.PercentualAlerta is < 1 or > 99)
        {
            throw ServicosServiceException.PercentualAlertaInvalido();
        }
    }

    private static string ValidarNome(string? nome)
    {
        var limpo = nome?.Trim();

        return string.IsNullOrWhiteSpace(limpo)
            ? throw ServicosServiceException.NomeObrigatorio()
            : limpo;
    }

    private static string? Limpar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static TipoServicoResponse Mapear(
        TipoServico tipo, int conversasAtivas, IReadOnlyDictionary<Guid, string> nomesDeSetor) =>
        new(tipo.Id,
            tipo.Nome,
            tipo.Codigo,
            tipo.Categoria,
            DescreverCategoria(tipo.Categoria),
            tipo.Descricao,
            tipo.Ativo,
            tipo.Prazo is null || !tipo.Prazo.Ativo
                ? null
                : new PrazoServicoResponse(
                    tipo.Prazo.Id,
                    tipo.Prazo.PrazoHoras,
                    tipo.Prazo.PercentualAlerta,
                    tipo.Prazo.ApenasDiasUteis,
                    DescreverPrazo(tipo.Prazo)),
            conversasAtivas,
            tipo.SetorId,
            tipo.SetorId is { } setorId ? nomesDeSetor.GetValueOrDefault(setorId) : null,
            tipo.DocumentosNecessarios);

    private static string DescreverCategoria(CategoriaServico categoria) => categoria switch
    {
        CategoriaServico.Notas => "Tabelionato de Notas",
        CategoriaServico.RegistroCivil => "Registro Civil",
        CategoriaServico.RegistroImoveis => "Registro de Imoveis",
        CategoriaServico.TitulosDocumentos => "Titulos e Documentos",
        CategoriaServico.Protesto => "Protesto",
        _ => "Outros"
    };

    /// <summary>
    /// O prazo e guardado em horas, mas ninguem no balcao pensa em "120 horas".
    /// A descricao converte para a unidade que a pessoa usaria na frase.
    /// </summary>
    private static string DescreverPrazo(PrazoServico prazo)
    {
        var sufixo = prazo.ApenasDiasUteis ? " uteis" : " corridos";

        if (prazo.PrazoHoras < 24)
        {
            return prazo.PrazoHoras == 1 ? "1 hora" : $"{prazo.PrazoHoras} horas";
        }

        var dias = prazo.PrazoHoras / 24;
        var horas = prazo.PrazoHoras % 24;

        var texto = dias == 1 ? "1 dia" : $"{dias} dias";

        if (horas > 0)
        {
            texto += horas == 1 ? " e 1 hora" : $" e {horas} horas";
        }

        return texto + sufixo;
    }
}
