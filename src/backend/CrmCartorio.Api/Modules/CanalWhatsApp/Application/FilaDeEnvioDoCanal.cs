using System.Threading.Channels;
using CrmCartorio.Api.Modules.Atendimento.Application;

namespace CrmCartorio.Api.Modules.CanalWhatsApp.Application;

/// <inheritdoc cref="IFilaDeEnvioDoCanal"/>
/// <remarks>
/// Fila em memoria, singleton: some se o processo reiniciar, e isso e
/// aceitavel — a mensagem continua <c>Pendente</c> no banco, e a proxima
/// mensagem do cliente ou a proxima mudanca de etapa naquela conversa chama
/// <c>EnviarPendentesAsync</c> de novo (mesma logica de retentativa que ja
/// existe para <c>StatusEntrega.Falha</c>). Persistir a fila exigiria uma
/// tabela e um scheduler so para isto — peso que nao se paga para o unico
/// efeito colateral de um restart, que e' a mensagem sair minutos depois em
/// vez de na hora, e nunca se perder.
/// </remarks>
public sealed class FilaDeEnvioDoCanal : IFilaDeEnvioDoCanal
{
    private readonly Channel<Guid> _fila = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    public void Agendar(Guid conversaId) => _fila.Writer.TryWrite(conversaId);

    /// <summary>Consumida so pelo hospede em segundo plano.</summary>
    public IAsyncEnumerable<Guid> LerAsync(CancellationToken ct) => _fila.Reader.ReadAllAsync(ct);
}
