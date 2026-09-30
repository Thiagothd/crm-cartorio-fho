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
/// Cada serviço também leva, quando cadastrados, os <c>DocumentosNecessarios</c>
/// e o <c>Prazo</c> (SLA em <c>PrazoServico</c>, convertido de horas para a
/// unidade de balcão). Antes ficavam de fora do grounding e a IA recusava
/// perguntas de documento/prazo mesmo com o dado no banco — só chegavam ao
/// cliente por marcador em mensagem automática roteirizada, nunca em resposta
/// livre. O <c>PromptDeSistema</c> (Regra 1) já libera usar o que está no
/// contexto e continua proibindo inventar o que não está.
///
/// <c>RespostaRapida.Conteudo</c> aceita os marcadores de
/// <see cref="Configuracoes.Domain.PlaceholdersDeMensagem"/>. Só {NomeCartorio} é
/// resolvido aqui — é o único que faz sentido fora de uma conversa específica; os
/// demais ({NomeCliente}, {Servico}, {Protocolo}, {MenuSetores}) são removidos, para
/// a IA nunca ver um marcador solto e reproduzi-lo litealmente numa resposta.
///
/// Terceira fonte: <see cref="Domain.Entities.ConhecimentoDeIa"/> ativo e dentro da
/// janela de vigência (quando cadastrada). Diferente de <c>RespostaRapida</c> —
/// atalho de digitação do atendente que só de raspão alimenta a IA — este é o
/// cadastro governado: versão incrementada e estado anterior preservado a cada
/// edição (<see cref="ConhecimentoDeIaService"/>), autor rastreado, vigência
/// opcional. Nota: as duas fontes podem, com o tempo, competir pelo mesmo tipo de
/// conteúdo — não resolvido agora, decisão futura possível é aposentar o papel de
/// "informação do cartório" do RespostaRapida em favor deste cadastro.
/// </remarks>
public static partial class MontadorDeContexto
{
    public static Task<string> MontarAsync(CrmDbContext db, Guid tenantId, CancellationToken ct) =>
        MontarAsync(db, tenantId, null, ct);

    /// <param name="tipoServicoId">
    /// O serviço que a conversa escolheu. Quando ele tem texto próprio
    /// (<c>TipoServico.MensagemAoEscolher</c> — a orientação que o cartório escreveu),
    /// o texto entra no contexto: é dele que sai a resposta para "posso registrar na
    /// cidade onde moro?" depois da orientação do registro de nascimento (2026-09-30).
    /// Só o do serviço da conversa, não o de todos: os textos passam de 3 mil
    /// caracteres cada.
    /// </param>
    public static async Task<string> MontarAsync(CrmDbContext db, Guid tenantId, Guid? tipoServicoId, CancellationToken ct)
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
                t.DocumentosNecessarios,
                SetorNome = db.Setores
                    .Where(s => s.Id == t.SetorId)
                    .Select(s => s.Nome)
                    .FirstOrDefault(),
                Prazo = db.PrazosServico
                    .Where(p => p.TipoServicoId == t.Id && p.Ativo)
                    .Select(p => new { p.PrazoHoras, p.ApenasDiasUteis })
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var respostas = await db.RespostasRapidas.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.Ativo)
            .OrderBy(r => r.Titulo)
            .Select(r => new { r.Titulo, r.Conteudo })
            .ToListAsync(ct);

        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var conhecimentos = await db.ConhecimentosDeIa.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Ativo
                && (c.VigenteDesde == null || c.VigenteDesde <= hoje)
                && (c.VigenteAte == null || c.VigenteAte >= hoje))
            .OrderBy(c => c.Tema).ThenBy(c => c.Titulo)
            .Select(c => new
            {
                c.Titulo,
                c.Conteudo,
                c.Tema,
                ServicoNome = db.TiposServico
                    .Where(t => t.Id == c.TipoServicoId)
                    .Select(t => t.Nome)
                    .FirstOrDefault()
            })
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

            if (!string.IsNullOrWhiteSpace(servico.DocumentosNecessarios))
            {
                texto.Append("  Documentos necessarios: ")
                    .AppendLine(servico.DocumentosNecessarios.Trim());
            }

            if (servico.Prazo is not null)
            {
                texto.Append("  Prazo: ~")
                    .AppendLine(DescreverPrazo(servico.Prazo.PrazoHoras, servico.Prazo.ApenasDiasUteis));
            }
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

        texto.AppendLine();
        texto.AppendLine("CONHECIMENTO APROVADO:");
        if (conhecimentos.Count == 0)
        {
            texto.AppendLine("(nenhum conhecimento cadastrado)");
        }

        foreach (var conhecimento in conhecimentos)
        {
            texto.Append("- ").Append(conhecimento.Titulo)
                .Append(" (").Append(conhecimento.Tema);

            if (!string.IsNullOrWhiteSpace(conhecimento.ServicoNome))
            {
                texto.Append(", ").Append(conhecimento.ServicoNome);
            }

            texto.Append("): ").Append(conhecimento.Conteudo).AppendLine();
        }

        if (tipoServicoId is { } servicoDaConversa)
        {
            var orientacao = await db.TiposServico.AsNoTracking()
                .Where(t => t.Id == servicoDaConversa && t.TenantId == tenantId && t.MensagemAoEscolher != null)
                .Select(t => new { t.Nome, t.MensagemAoEscolher })
                .FirstOrDefaultAsync(ct);

            if (orientacao is not null && !string.IsNullOrWhiteSpace(orientacao.MensagemAoEscolher))
            {
                texto.AppendLine();
                texto.Append("ORIENTACAO DO CARTORIO SOBRE O SERVICO QUE O CLIENTE ESCOLHEU (")
                    .Append(orientacao.Nome).AppendLine("):");
                texto.AppendLine(ResolverConteudo(
                    orientacao.MensagemAoEscolher.Replace("\n---\n", "\n\n", StringComparison.Ordinal), nomeCartorio));
            }
        }

        return texto.ToString();
    }

    private static string ResolverConteudo(string conteudo, string nomeCartorio)
    {
        var comNomeResolvido = conteudo.Replace("{NomeCartorio}", nomeCartorio, StringComparison.Ordinal);
        return RegexDeMarcadorRestante().Replace(comNomeResolvido, string.Empty);
    }

    /// <summary>
    /// O prazo e guardado em horas, mas ninguem no balcao pensa em "120 horas".
    /// Converte para a unidade que a pessoa usaria na frase. Espelha
    /// <c>TipoServicoService.DescreverPrazo</c> (privado la): duplicar ~15 linhas de
    /// formatacao pura evita este modulo referenciar o Application do modulo Servicos.
    /// </summary>
    private static string DescreverPrazo(int prazoHoras, bool apenasDiasUteis)
    {
        if (prazoHoras < 24)
        {
            return prazoHoras == 1 ? "1 hora" : $"{prazoHoras} horas";
        }

        var dias = prazoHoras / 24;
        var horas = prazoHoras % 24;

        var texto = dias == 1 ? "1 dia" : $"{dias} dias";

        if (horas > 0)
        {
            texto += horas == 1 ? " e 1 hora" : $" e {horas} horas";
        }

        return texto + (apenasDiasUteis ? " uteis" : " corridos");
    }

    [GeneratedRegex(@"\{[A-Za-z][A-Za-z0-9]*\}")]
    private static partial Regex RegexDeMarcadorRestante();
}
