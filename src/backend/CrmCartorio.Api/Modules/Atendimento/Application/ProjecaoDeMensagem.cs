using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;

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

    /// <param name="clienteNome">
    /// Nome do cliente da conversa. Vem de fora porque a citacao pode ser de uma
    /// mensagem do cliente, e resolver isso por subconsulta obrigaria a passar
    /// pela conversa a cada linha.
    /// </param>
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
            m.MensagemRespondidaId,

            db.Mensagens
                .Where(r => r.Id == m.MensagemRespondidaId)
                .Select(r => r.AutorTipo == AutorTipo.Atendente
                    ? db.Atendentes.Where(a => a.Id == r.AtendenteId).Select(a => a.Nome).FirstOrDefault()
                    : r.AutorTipo == AutorTipo.Cliente
                        ? clienteNome
                        : "Sistema")
                .FirstOrDefault(),

            db.Mensagens
                .Where(r => r.Id == m.MensagemRespondidaId)
                .Select(r => r.Conteudo.Length > TamanhoDaPrevia
                    ? r.Conteudo.Substring(0, TamanhoDaPrevia)
                    : r.Conteudo)
                .FirstOrDefault()));
}
