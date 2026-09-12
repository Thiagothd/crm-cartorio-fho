using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <summary>
/// Projeta mensagens com o nome do autor e a previa da citacao resolvidos.
/// </summary>
/// <remarks>
/// Existe para que a listagem do fio e o detalhe da conversa usem exatamente a
/// mesma consulta. Antes eram duas copias, e duas copias divergem.
///
/// O texto citado e resolvido AQUI, a partir da mensagem original — nao e um
/// recorte gravado no envio. Guardar o recorte criaria duas versoes da mesma
/// frase no banco.
/// </remarks>
public static class ProjecaoDeMensagem
{
    /// <summary>Quantos caracteres da mensagem citada vao para a previa.</summary>
    private const int TamanhoDaPrevia = 160;

    /// <summary>
    /// Materializa e devolve a lista com a previa da citacao ja cortada em
    /// <see cref="TamanhoDaPrevia"/>. O corte acontece EM MEMORIA, depois do
    /// <c>ToListAsync</c> — nao dentro da consulta (ver <see cref="De"/>):
    /// desde que <c>Mensagem.Conteudo</c> ficou cifrado em repouso (PR3), um
    /// <c>Substring</c> traduzido para SQL cortaria bytes do CIPHERTEXT, nao
    /// do texto em claro, e devolveria lixo sempre que a cifra estiver ativa.
    /// </summary>
    public static async Task<List<MensagemResponse>> ListarAsync(
        CrmDbContext db,
        IQueryable<Mensagem> origem,
        string clienteNome,
        CancellationToken ct = default) =>
        (await De(db, origem, clienteNome).ToListAsync(ct))
            .ConvertAll(CortarPreviaDaCitacao);

    /// <summary>Mesma regra de <see cref="ListarAsync"/>, para quando so uma linha e esperada.</summary>
    public static async Task<MensagemResponse> ObterUmaAsync(
        CrmDbContext db,
        IQueryable<Mensagem> origem,
        string clienteNome,
        CancellationToken ct = default) =>
        CortarPreviaDaCitacao(await De(db, origem, clienteNome).FirstAsync(ct));

    private static MensagemResponse CortarPreviaDaCitacao(MensagemResponse mensagem) =>
        mensagem.RespondidaConteudo is { Length: > TamanhoDaPrevia } texto
            ? mensagem with { RespondidaConteudo = texto[..TamanhoDaPrevia] }
            : mensagem;

    /// <param name="clienteNome">
    /// Nome do cliente da conversa. Vem de fora porque a citacao pode ser de uma
    /// mensagem do cliente, e resolver isso por subconsulta obrigaria a passar
    /// pela conversa a cada linha.
    /// </param>
    /// <remarks>
    /// Publico so para <c>verificar-traducao.cs</c> chamar <c>ToQueryString()</c>
    /// sem executar. Fora de teste de traducao, use
    /// <see cref="ListarAsync"/>/<see cref="ObterUmaAsync"/> — chamar isto direto
    /// e materializar devolve <see cref="MensagemResponse.RespondidaConteudo"/>
    /// sem cortar para <see cref="TamanhoDaPrevia"/>.
    /// </remarks>
    public static IQueryable<MensagemResponse> De(
        CrmDbContext db,
        IQueryable<Mensagem> origem,
        string clienteNome) =>
        origem.Select(m => new MensagemResponse(
            m.Id,
            m.ConversaId,
            m.AutorTipo,
            m.AtendenteId,
            db.Atendentes.Where(a => a.Id == m.AtendenteId).Select(a => a.Nome).FirstOrDefault(),
            m.Conteudo,
            m.Tipo,
            m.StatusEntrega,
            m.EhAutomatica,
            m.EnviadaEm,
            m.LidaEm,

            m.FixadaEm != null,
            m.OcultaEm != null,

            db.Atendentes.Where(a => a.Id == m.OcultaPorAtendenteId).Select(a => a.Nome).FirstOrDefault(),

            // Protocolo da origem, nao a conversa inteira: e o que identifica o
            // atendimento de onde a mensagem veio, e e o numero que o atendente
            // procura na lista.
            db.Mensagens
                .Where(o => o.Id == m.EncaminhadaDeMensagemId)
                .Select(o => db.Conversas
                    .Where(c => c.Id == o.ConversaId)
                    .Select(c => c.Protocolo)
                    .FirstOrDefault())
                .FirstOrDefault(),

            m.MensagemRespondidaId,

            db.Mensagens
                .Where(r => r.Id == m.MensagemRespondidaId)
                .Select(r => r.AutorTipo == AutorTipo.Atendente
                    ? db.Atendentes.Where(a => a.Id == r.AtendenteId).Select(a => a.Nome).FirstOrDefault()
                    : r.AutorTipo == AutorTipo.Cliente
                        ? clienteNome
                        : "Sistema")
                .FirstOrDefault(),

            // Conteudo INTEIRO aqui de proposito — o corte para a previa
            // acontece depois de materializado (ver ListarAsync/ObterUmaAsync
            // acima). Conteudo esta cifrado em repouso (PR3): um Substring
            // traduzido para SQL operaria sobre o ciphertext, nao sobre o
            // texto em claro.
            db.Mensagens
                .Where(r => r.Id == m.MensagemRespondidaId)
                .Select(r => r.Conteudo)
                .FirstOrDefault(),

            // Sem ArquivoCaminho: a interface pede o binario pelo id da mensagem,
            // e publicar o caminho daria a quem lesse a resposta um mapa da pasta
            // de anexos do servidor.
            m.ArquivoNome,
            m.ArquivoTipoMime,
            m.ArquivoTamanhoBytes,
            m.GeradaPorIa));
}
