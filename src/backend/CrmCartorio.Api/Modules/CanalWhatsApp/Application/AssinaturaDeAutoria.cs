using System.Text;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;

namespace CrmCartorio.Api.Modules.CanalWhatsApp.Application;

/// <summary>
/// Autoria de uma mensagem, na forma que o adaptador de saida precisa para
/// decidir o rodape — nunca a entidade inteira.
/// </summary>
public readonly record struct AutoriaDaMensagem(
    AutorTipo AutorTipo,
    bool EhAutomatica,
    bool GeradaPorIa,
    string? AtendenteNome);

/// <summary>
/// Decora o texto que sai pelo WhatsApp com quem falou — atendente ou IA.
/// </summary>
/// <remarks>
/// Mora no <c>CanalWhatsApp</c>, nao no <c>Atendimento</c>: <c>_italico_</c> e
/// sintaxe do WhatsApp. Trocar para a Cloud API da Meta mantem a decisao;
/// trocar o canal por SMS ou e-mail jogaria fora (regra 7.1 do CLAUDE.md —
/// adaptador fora, porta dentro).
///
/// <b>So decora na SAIDA, nunca persiste.</b> O unico chamador e
/// <see cref="EnviadorPeloWhatsApp"/>, no momento exato da chamada ao gateway.
/// Se a decoracao fosse gravada em <c>Mensagem.Conteudo</c>, o rodape apareceria
/// no painel, na previa da citacao, no texto copiado por encaminhamento e no
/// historico que alimenta a IA — que passaria a ver "— Maria Rezende" como parte
/// da conversa e a imitar.
///
/// <b>Nao e idempotente por construcao</b> — <c>Decorar(Decorar(t), a)</c> assina
/// duas vezes. A garantia contra assinatura dupla vem de a fonte ser sempre o
/// banco (nunca o texto ja decorado de uma tentativa anterior), nunca desta
/// funcao. Ninguem deve "otimizar" guardando o texto decorado num campo.
/// </remarks>
public static class AssinaturaDeAutoria
{
    public const string MarcaDeAutomatica = "Mensagem automática";

    private const int TamanhoMaximoDoNome = 80;

    /// <summary>
    /// Caracteres que quebram a formatacao do WhatsApp (italico/negrito/riscado/
    /// monoespacado) se aparecerem dentro do nome do atendente.
    /// </summary>
    private static readonly char[] CaracteresDeFormatacao = ['_', '*', '~', '`'];

    /// <summary>O rodape para esta autoria, ou <c>null</c> quando nao leva marca nenhuma.</summary>
    /// <remarks>
    /// Mensagem automatica de REGRA CADASTRADA (EhAutomatica, sem GeradaPorIa) nao
    /// leva marca — decisao do Thiago em 2026-09-04: aquele texto ja fala na voz
    /// institucional do cartorio, marcar toda automatica viraria ruido repetido
    /// em quase toda mensagem do fluxo guiado.
    /// </remarks>
    public static string? Rodape(AutoriaDaMensagem autoria)
    {
        if (autoria.AutorTipo == AutorTipo.Atendente)
        {
            var nome = HigienizarNome(autoria.AtendenteNome);
            return nome is null ? null : $"_— {nome}_";
        }

        if (autoria.AutorTipo == AutorTipo.Sistema && autoria.EhAutomatica && autoria.GeradaPorIa)
        {
            return $"_{MarcaDeAutomatica}_";
        }

        // Sistema + EhAutomatica sem GeradaPorIa (regra cadastrada), Sistema sem
        // EhAutomatica (anotacao interna — nunca deveria chegar aqui, mas o
        // enviador so filtra EhAutomatica; defensivo) e Cliente: sem marca.
        return null;
    }

    /// <summary>Texto pronto para o gateway, com o rodape quando houver.</summary>
    public static string Decorar(string texto, AutoriaDaMensagem autoria)
    {
        var rodape = Rodape(autoria);

        // Linha em branco antes do rodape: colado na ultima frase, ele parece
        // parte dela no aparelho do cliente.
        return rodape is null ? texto : $"{texto}\n\n{rodape}";
    }

    /// <summary>
    /// Legenda de midia com o rodape. <c>null</c> entra, <c>null</c> sai — foto
    /// sem legenda continua sem legenda (decisao do Thiago em 2026-09-04): criar
    /// uma legenda que so tem a assinatura, embaixo de uma imagem muda, e mais
    /// estranho do que nao assinar aquela mensagem.
    /// </summary>
    public static string? DecorarLegenda(string? legenda, AutoriaDaMensagem autoria) =>
        legenda is null ? null : Decorar(legenda, autoria);

    /// <summary>
    /// Sem nome utilizavel, some o rodape INTEIRO — nunca <c>_— _</c>. Mesmo
    /// espirito de <c>PlaceholdersDeMensagem.ResolverNomeCliente</c>: um vocativo
    /// vazio some por completo, nao deixa marca orfa.
    /// </summary>
    private static string? HigienizarNome(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome))
        {
            return null;
        }

        var semFormatacao = new StringBuilder(nome.Length);
        foreach (var caractere in nome)
        {
            if (Array.IndexOf(CaracteresDeFormatacao, caractere) >= 0)
            {
                continue;
            }

            // Colapsa qualquer whitespace (inclusive quebra de linha, que
            // romperia o rodape) num espaco simples.
            semFormatacao.Append(char.IsWhiteSpace(caractere) ? ' ' : caractere);
        }

        var normalizado = string.Join(' ', semFormatacao.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

        if (normalizado.Length == 0)
        {
            return null;
        }

        return normalizado.Length > TamanhoMaximoDoNome
            ? normalizado[..TamanhoMaximoDoNome].TrimEnd()
            : normalizado;
    }
}
