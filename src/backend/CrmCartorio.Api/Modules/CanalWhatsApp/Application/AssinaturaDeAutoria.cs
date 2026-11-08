using System.Text;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;

namespace CrmCartorio.Api.Modules.CanalWhatsApp.Application;

/// <summary>
/// Autoria de uma mensagem, na forma que o adaptador de saida precisa para
/// decidir o cabecalho — nunca a entidade inteira.
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
/// Mora no <c>CanalWhatsApp</c>, nao no <c>Atendimento</c>: <c>*negrito*</c> e
/// sintaxe do WhatsApp. Trocar para a Cloud API da Meta mantem a decisao;
/// trocar o canal por SMS ou e-mail jogaria fora (regra 7.1 do docs/CONVENCOES.md —
/// adaptador fora, porta dentro).
///
/// <b>So decora na SAIDA, nunca persiste.</b> O unico chamador e
/// <see cref="EnviadorPeloWhatsApp"/>, no momento exato da chamada ao gateway.
/// Se a decoracao fosse gravada em <c>Mensagem.Conteudo</c>, o cabecalho
/// apareceria no painel, na previa da citacao, no texto copiado por
/// encaminhamento e no historico que alimenta a IA — que passaria a ver
/// "Maria Rezende" como parte da conversa e a imitar.
///
/// <b>Cabecalho, nao rodape</b> — decisao do Thiago em 2026-09-04 depois de ver
/// o formato em rodape ao vivo: o nome (ou rotulo) precisa aparecer em negrito
/// ANTES do conteudo, no formato de quem manda mensagem "assinada" — mais perto
/// de como um cabecalho de e-mail identifica o remetente do que uma nota de
/// rodape.
///
/// <b>Nao e idempotente por construcao</b> — <c>Decorar(Decorar(t), a)</c> assina
/// duas vezes. A garantia contra assinatura dupla vem de a fonte ser sempre o
/// banco (nunca o texto ja decorado de uma tentativa anterior), nunca desta
/// funcao. Ninguem deve "otimizar" guardando o texto decorado num campo.
/// </remarks>
public static class AssinaturaDeAutoria
{
    /// <summary>
    /// Rotulo da IA. "Assistente Virtual" e nao "Mensagem automática" — o
    /// primeiro descreve QUEM fala (casa com o conteudo, que e sempre resposta
    /// em 1a pessoa do sistema); o segundo descrevia o mecanismo, no lugar
    /// errado para quem so quer saber com quem esta falando.
    /// </summary>
    public const string RotuloDoAssistenteVirtual = "Assistente Virtual";

    private const int TamanhoMaximoDoNome = 80;

    /// <summary>
    /// Caracteres que quebram a formatacao do WhatsApp (italico/negrito/riscado/
    /// monoespacado) se aparecerem dentro do nome do atendente ou do rotulo.
    /// </summary>
    private static readonly char[] CaracteresDeFormatacao = ['_', '*', '~', '`'];

    /// <summary>O texto do cabecalho (sem o negrito), ou <c>null</c> quando nao leva marca nenhuma.</summary>
    /// <remarks>
    /// Mensagem automatica de REGRA CADASTRADA (EhAutomatica, sem GeradaPorIa) nao
    /// leva marca — decisao do Thiago em 2026-09-04: aquele texto ja fala na voz
    /// institucional do cartorio, marcar toda automatica viraria ruido repetido
    /// em quase toda mensagem do fluxo guiado.
    /// </remarks>
    public static string? Cabecalho(AutoriaDaMensagem autoria)
    {
        if (autoria.AutorTipo == AutorTipo.Atendente)
        {
            return HigienizarTexto(autoria.AtendenteNome);
        }

        if (autoria.AutorTipo == AutorTipo.Sistema && autoria.EhAutomatica && autoria.GeradaPorIa)
        {
            return RotuloDoAssistenteVirtual;
        }

        // Sistema + EhAutomatica sem GeradaPorIa (regra cadastrada), Sistema sem
        // EhAutomatica (anotacao interna — nunca deveria chegar aqui, mas o
        // enviador so filtra EhAutomatica; defensivo) e Cliente: sem marca.
        return null;
    }

    /// <summary>
    /// Texto pronto para o gateway: o cabecalho em negrito numa linha, o
    /// conteudo original logo abaixo. Sem cabecalho, o texto sai identico.
    /// </summary>
    public static string Decorar(string texto, AutoriaDaMensagem autoria)
    {
        var cabecalho = Cabecalho(autoria);
        return cabecalho is null ? texto : $"*{cabecalho}*\n{texto}";
    }

    /// <summary>
    /// Legenda de midia com o mesmo cabecalho. <c>null</c> entra, <c>null</c>
    /// sai — foto sem legenda continua sem legenda (decisao do Thiago em
    /// 2026-09-04): criar uma legenda que so tem o cabecalho, embaixo de uma
    /// imagem muda, e mais estranho do que nao assinar aquela mensagem.
    /// </summary>
    public static string? DecorarLegenda(string? legenda, AutoriaDaMensagem autoria) =>
        legenda is null ? null : Decorar(legenda, autoria);

    /// <summary>
    /// Sem nome utilizavel, some o cabecalho INTEIRO — nunca <c>**</c> vazio.
    /// Mesmo espirito de <c>PlaceholdersDeMensagem.ResolverNomeCliente</c>: um
    /// vocativo vazio some por completo, nao deixa marca orfa.
    /// </summary>
    private static string? HigienizarTexto(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }

        var semFormatacao = new StringBuilder(texto.Length);
        foreach (var caractere in texto)
        {
            if (Array.IndexOf(CaracteresDeFormatacao, caractere) >= 0)
            {
                continue;
            }

            // Colapsa qualquer whitespace (inclusive quebra de linha, que
            // romperia o cabecalho de uma linha so) num espaco simples.
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
