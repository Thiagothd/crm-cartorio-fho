using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Configuracoes.Domain;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Configuracoes.Application;

public class RespostaRapidaService(CrmDbContext db, IContextoDoUsuario ctx) : IRespostaRapidaService
{
    public async Task<IReadOnlyList<RespostaRapidaResponse>> ListarAsync(
        bool incluirInativas,
        CancellationToken ct = default)
    {
        var tenantId = ctx.TenantId;

        var consulta = db.RespostasRapidas.AsNoTracking().Where(r => r.TenantId == tenantId);

        if (!incluirInativas)
        {
            consulta = consulta.Where(r => r.Ativo);
        }

        // Por atalho, nao por titulo: e o atalho que o atendente digita, entao e a
        // ordem em que ele espera encontrar as opcoes no seletor.
        return await consulta
            .OrderBy(r => r.Atalho)
            .Select(r => new RespostaRapidaResponse(
                r.Id,
                r.Atalho,
                r.Titulo,
                r.Conteudo,
                r.Ativo))
            .ToListAsync(ct);
    }

    public async Task<RespostaRapidaResponse> CriarAsync(
        SalvarRespostaRapidaRequest request,
        CancellationToken ct = default)
    {
        GarantirPermissao();

        var atalho = ValidarAtalho(request.Atalho);
        var titulo = ValidarTitulo(request.Titulo);
        var conteudo = ValidarConteudo(request.Conteudo);

        await GarantirAtalhoLivreAsync(atalho, null, ct);

        var resposta = new RespostaRapida
        {
            TenantId = ctx.TenantId,
            Atalho = atalho,
            Titulo = titulo,
            Conteudo = conteudo,
            Ativo = request.Ativo
        };

        db.RespostasRapidas.Add(resposta);
        await db.SaveChangesAsync(ct);

        return Mapear(resposta);
    }

    public async Task<RespostaRapidaResponse> AtualizarAsync(
        Guid id,
        SalvarRespostaRapidaRequest request,
        CancellationToken ct = default)
    {
        GarantirPermissao();

        var resposta = await db.RespostasRapidas
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == ctx.TenantId, ct)
            ?? throw ConfiguracoesServiceException.RespostaRapidaNaoEncontrada();

        var atalho = ValidarAtalho(request.Atalho);

        await GarantirAtalhoLivreAsync(atalho, id, ct);

        resposta.Atalho = atalho;
        resposta.Titulo = ValidarTitulo(request.Titulo);
        resposta.Conteudo = ValidarConteudo(request.Conteudo);
        resposta.Ativo = request.Ativo;

        await db.SaveChangesAsync(ct);

        return Mapear(resposta);
    }

    private void GarantirPermissao()
    {
        if (!ctx.PodeGerirCadastros)
        {
            throw ConfiguracoesServiceException.SemPermissao();
        }
    }

    private async Task GarantirAtalhoLivreAsync(string atalho, Guid? ignorarId, CancellationToken ct)
    {
        var existe = await db.RespostasRapidas.AsNoTracking().AnyAsync(
            r => r.TenantId == ctx.TenantId
              && r.Atalho == atalho
              && (ignorarId == null || r.Id != ignorarId), ct);

        if (existe)
        {
            throw ConfiguracoesServiceException.AtalhoEmUso(atalho);
        }
    }

    /// <summary>
    /// Normaliza o atalho: sem a barra, em minusculas, sem espaco.
    /// </summary>
    /// <remarks>
    /// A barra e tirada aqui porque o usuario a digita por habito ao cadastrar
    /// ("/prazo"), enquanto o seletor compara o que vem DEPOIS dela. Guardar as duas
    /// formas faria "/prazo" e "prazo" virarem cadastros diferentes que se
    /// comportariam igual — e a unicidade nao pegaria nenhum dos dois.
    /// </remarks>
    private static string ValidarAtalho(string? atalho)
    {
        var limpo = atalho?.Trim().TrimStart('/').Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(limpo))
        {
            throw ConfiguracoesServiceException.AtalhoObrigatorio();
        }

        return limpo.Any(char.IsWhiteSpace)
            ? throw ConfiguracoesServiceException.AtalhoComEspaco()
            : limpo;
    }

    private static string ValidarTitulo(string? titulo)
    {
        var limpo = titulo?.Trim();

        return string.IsNullOrWhiteSpace(limpo)
            ? throw ConfiguracoesServiceException.TituloObrigatorio()
            : limpo;
    }

    /// <summary>
    /// Mesma validacao de marcador da mensagem automatica: o texto de uma resposta
    /// rapida tambem chega ao cliente, e um marcador escrito errado sairia literal.
    /// </summary>
    private static string ValidarConteudo(string? conteudo)
    {
        var limpo = conteudo?.Trim();

        if (string.IsNullOrWhiteSpace(limpo))
        {
            throw ConfiguracoesServiceException.ConteudoObrigatorio();
        }

        var desconhecidos = PlaceholdersDeMensagem.MarcadoresDesconhecidos(limpo);

        return desconhecidos.Count > 0
            ? throw ConfiguracoesServiceException.MarcadorDesconhecido(desconhecidos)
            : limpo;
    }

    private static RespostaRapidaResponse Mapear(RespostaRapida resposta) =>
        new(resposta.Id, resposta.Atalho, resposta.Titulo, resposta.Conteudo, resposta.Ativo);
}
