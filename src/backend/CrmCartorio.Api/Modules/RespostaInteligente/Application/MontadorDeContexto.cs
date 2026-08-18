using System.Text;
using System.Text.RegularExpressions;
using CrmCartorio.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.RespostaInteligente.Application;

/// <summary>
/// Monta o texto de grounding da IA: só dado cadastrado, nada inventado.
/// </summary>
/// <remarks>
/// Fontes: <c>TipoServico</c> ativo (o catálogo) e <c>RespostaRapida</c> ativa. O
/// plano original previa "horário/endereço do Tenant", mas o Tenant não tem esses
/// campos — o sistema já resolve isso via a resposta rápida <c>/horario</c>
/// (REQUISITOS.md, item 19), que carrega o texto oficial confirmado em
/// CONTEXTO.md. Reusar RespostaRapida evita duplicar esse dado em outro lugar, e
/// continua "editável pela tela" sem nenhuma tela nova.
///
/// Cada serviço leva o nome do <c>Setor</c> entre parênteses (achado ao vivo,
/// 2026-08-18: sem isso, "quais os serviços de notas?" não tinha como ser
/// respondido — a lista de nomes chegava sem nenhuma pista de a qual setor cada
/// um pertence, e a IA, corretamente, recusava adivinhar).
///
/// <c>RespostaRapida.Conteudo</c> aceita os marcadores de
/// <see cref="Configuracoes.Domain.PlaceholdersDeMensagem"/>. Só {NomeCartorio} é
/// resolvido aqui — é o único que faz sentido fora de uma conversa específica; os
/// demais ({NomeCliente}, {Servico}, {Protocolo}, {MenuSetores}) são removidos, para
/// a IA nunca ver um marcador solto e reproduzi-lo litealmente numa resposta.
/// </remarks>
public static partial class MontadorDeContexto
{
    public static async Task<string> MontarAsync(CrmDbContext db, Guid tenantId, CancellationToken ct)
    {
        var nomeCartorio = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.NomeFantasia ?? t.RazaoSocial)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        var servicos = await db.TiposServico.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.Ativo)
            .OrderBy(t => t.Nome)
            .Select(t => new
            {
                t.Nome,
                t.Descricao,
                SetorNome = db.Setores
                    .Where(s => s.Id == t.SetorId)
                    .Select(s => s.Nome)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var respostas = await db.RespostasRapidas.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.Ativo)
            .OrderBy(r => r.Titulo)
            .Select(r => new { r.Titulo, r.Conteudo })
            .ToListAsync(ct);

        var texto = new StringBuilder();

        texto.AppendLine("CATALOGO DE SERVICOS:");
        if (servicos.Count == 0)
        {
            texto.AppendLine("(nenhum servico cadastrado)");
        }

        foreach (var servico in servicos)
        {
            texto.Append("- ").Append(servico.Nome);

            if (!string.IsNullOrWhiteSpace(servico.SetorNome))
            {
                texto.Append(" (").Append(servico.SetorNome).Append(')');
            }

            if (!string.IsNullOrWhiteSpace(servico.Descricao))
            {
                texto.Append(": ").Append(servico.Descricao);
            }

            texto.AppendLine();
        }

        texto.AppendLine();
        texto.AppendLine("INFORMACOES CADASTRADAS DO CARTORIO:");
        if (respostas.Count == 0)
        {
            texto.AppendLine("(nenhuma informacao cadastrada)");
        }

        foreach (var resposta in respostas)
        {
            texto.Append("- ").Append(resposta.Titulo).Append(": ")
                .Append(ResolverConteudo(resposta.Conteudo, nomeCartorio))
                .AppendLine();
        }

        return texto.ToString();
    }

    private static string ResolverConteudo(string conteudo, string nomeCartorio)
    {
        var comNomeResolvido = conteudo.Replace("{NomeCartorio}", nomeCartorio, StringComparison.Ordinal);
        return RegexDeMarcadorRestante().Replace(comNomeResolvido, string.Empty);
    }

    [GeneratedRegex(@"\{[A-Za-z][A-Za-z0-9]*\}")]
    private static partial Regex RegexDeMarcadorRestante();
}
