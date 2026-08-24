using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.Clientes.Domain;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <summary>
/// Le a resposta do cliente a pergunta "como posso te chamar?" e, capturado o
/// nome ou nao, abre o atendimento com as boas-vindas.
/// </summary>
/// <remarks>
/// Mesma forma dos outros <c>Aplicador*</c>, e pelos mesmos motivos: estatico e
/// sem estado, dois chamadores (webhook real e simulacao do painel), tenant vindo
/// da propria conversa em vez de <c>IContextoDoUsuario</c>.
///
/// <b>Diferente dos vizinhos, este GRAVA no banco</b> — ver
/// <see cref="AplicarAsync"/> para o porque. E a unica excecao a regra "enfileira,
/// nao grava" em todo o modulo, e ela existe por uma razao mecanica, nao por
/// conveniencia.
/// </remarks>
public static class AplicadorDePedidoDeNome
{
    /// <summary>
    /// Trata a mensagem que responde ao pedido de nome. <b>Consome a mensagem em
    /// todos os caminhos</b> e sempre termina abrindo o atendimento.
    /// </summary>
    /// <remarks>
    /// <b>ATENCAO — o retorno NAO e condicao de <c>else if</c>.</b> Diferente de
    /// <see cref="AplicadorDeEncerramentoPorInatividade.TratarRespostaAsync"/>,
    /// que devolve <c>false</c> para a mensagem seguir a cadeia, aqui devolver
    /// <c>false</c> e deixar passar seria um bug: neste ponto o cliente ainda NAO
    /// recebeu o menu, entao <see cref="Conversa.AguardandoTriagem"/> esta
    /// desligada e a mensagem cairia no <c>else</c> final do despacho — o piso da
    /// IA, feito para conversa que ja acabou. O primeiro contato de alguem seria
    /// respondido com "nao tenho essa informacao especifica", que e pior que o
    /// problema original. O <c>bool</c> serve a log e a teste, e nada mais.
    ///
    /// <b>Por que grava com <c>ExecuteUpdateAsync</c> em vez de mexer na entidade
    /// rastreada.</b> <c>DisparadorDeMensagemAutomatica</c> resolve
    /// <c>{NomeCliente}</c> com um SELECT de verdade, e o recebimento ja gastou o
    /// <c>SaveChangesAsync</c> dele ANTES da cadeia de despacho. Um
    /// <c>cliente.Nome = ...</c> ficaria so no change tracker e as boas-vindas
    /// sairiam com o nome velho — o mesmo "Ola, !" que o comentario do recebimento
    /// documenta. E <c>SaveChangesAsync</c> aqui tambem nao serve: a simulacao do
    /// painel nao abre transacao, e um flush parcial deixaria a mensagem do
    /// cliente gravada sem resposta se o passo seguinte falhasse. O
    /// <c>ExecuteUpdate</c> roda dentro da transacao de quem chamou quando ela
    /// existe, e sozinho quando nao existe.
    ///
    /// Efeito colateral conhecido: a entidade <c>Cliente</c> rastreada fica velha
    /// em memoria. Inofensivo — ninguem le o nome dela depois deste ponto.
    /// </remarks>
    /// <returns><c>true</c> se o nome foi capturado. Nunca use como condicao de despacho.</returns>
    public static async Task<bool> AplicarAsync(
        CrmDbContext db,
        IDisparadorDeMensagemAutomatica automaticas,
        Conversa conversa,
        string? conteudo,
        DateTime agora,
        ILogger log,
        CancellationToken ct)
    {
        // Primeira linha, todos os caminhos — mesma disciplina de
        // AplicadorDeTriagem: uma tentativa e acabou. Insistir por um dado
        // OPCIONAL seria ainda menos defensavel que insistir pelo menu.
        conversa.AguardandoNome = false;

        var nome = NomeDeContato.LerRespostaDePergunta(conteudo);

        if (nome is not null)
        {
            await db.Clientes
                .Where(c => c.Id == conversa.ClienteId)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(c => c.Nome, nome)
                        .SetProperty(c => c.AtualizadoEm, agora),
                    ct);

            // Anotacao interna, nao mensagem ao cliente: EhAutomatica fica falso e
            // LidaEm ja vem preenchido, convencao dos outros aplicadores.
            db.Mensagens.Add(new Mensagem
            {
                TenantId = conversa.TenantId,
                ConversaId = conversa.Id,
                AutorTipo = AutorTipo.Sistema,
                Conteudo = $"O cliente informou o nome: {nome}.",
                Tipo = TipoMensagem.Texto,
                StatusEntrega = StatusEntrega.Entregue,
                EnviadaEm = agora,
                LidaEm = agora
            });

            log.LogInformation("Conversa {ConversaId}: nome capturado pelo pedido automatico.", conversa.Id);
        }
        else
        {
            // Resposta que nao parece nome nao vira nome. Ver o vies documentado em
            // NomeDeContato: gravar "quero uma certidao" seria permanente e sairia
            // em toda mensagem automatica dali em diante.
            log.LogInformation(
                "Conversa {ConversaId}: resposta ao pedido de nome nao foi reconhecida como nome.", conversa.Id);
        }

        // Aconteca o que acontecer, o atendimento abre agora — o cliente ja gastou
        // um turno e nao pode ficar sem o menu.
        var boasVindas = await automaticas.EnfileirarAsync(conversa, GatilhoDeMensagem.NovoAtendimento, ct: ct);
        conversa.AguardandoTriagem = boasVindas.Count > 0;

        return nome is not null;
    }
}
