using System.Threading.Channels;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <summary>
/// Acorda os workers locais logo apos o commit. Nao e a garantia da fila: se o
/// processo cair, o estado persistido em Mensagens sera reencontrado pela varredura.
/// </summary>
public sealed class SinalizadorDeEntradaWhatsapp
{
    private readonly Channel<Guid> _canal = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = false,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });

    public ChannelReader<Guid> Leitor => _canal.Reader;

    public void Sinalizar(Guid conversaId) => _canal.Writer.TryWrite(conversaId);
}
