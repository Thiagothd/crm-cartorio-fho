using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.RespostaInteligente.Application.Provedores;
using Microsoft.Extensions.Options;

namespace CrmCartorio.Api.Modules.RespostaInteligente.Application;

/// <inheritdoc cref="IRespostaInteligenteService"/>
/// <remarks>
/// Monta o pedido (contexto + histórico) uma vez e delega a escolha de provedor,
/// o fallback e o registro de observabilidade ao
/// <see cref="IOrquestradorDeProvedores"/> — este serviço continua sendo a única
/// porta pública do módulo, e o contrato "nunca lança" (exceto tenant vazio ou
/// cancelamento de quem chamou) continua valendo, agora atrás de dois provedores
/// em vez de um.
/// </remarks>
public class RespostaInteligenteService(
    CrmDbContext db,
    IOrquestradorDeProvedores orquestrador,
    IOptions<OpcoesDaIA> opcoes,
    ILogger<RespostaInteligenteService> log) : IRespostaInteligenteService
{
    private readonly OpcoesDaIA _opcoes = opcoes.Value;

    public async Task<RespostaDaIA> ResponderAsync(
        Guid tenantId,
        IReadOnlyList<TrocaAnterior> historico,
        string mensagemDoCliente,
        ContextoDaChamada? contexto = null,
        CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
        {
            throw RespostaInteligenteServiceException.TenantObrigatorio();
        }

        if (!_opcoes.EstaConfigurada)
        {
            return new RespostaDaIA(false, string.Empty, "IA desligada ou sem provedor configurado.");
        }

        try
        {
            var contextoDoGrounding = await MontadorDeContexto.MontarAsync(db, tenantId, ct);
            var pedido = new PedidoDeIa(contextoDoGrounding, historico, mensagemDoCliente);

            return await orquestrador.ResponderAsync(tenantId, contexto, pedido, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Quem desistiu foi quem chamou, nao a IA — o cancelamento sobe, como em
            // qualquer outro ponto do sistema.
            throw;
        }
        catch (Exception falha)
        {
            // Rede de seguranca final: montar o contexto (consulta ao banco) ou o
            // orquestrador em si podem falhar por motivo que nenhum dos dois
            // provedores cobre (banco fora do ar, etc.) — mesmo assim nunca
            // propaga. Quem chamou so olha PodeResponder e cai para o atendimento
            // humano de sempre.
            log.LogWarning(falha, "Falha ao consultar a IA; caindo para atendimento humano.");
            return new RespostaDaIA(false, string.Empty, "Falha ao consultar a IA.");
        }
    }
}
