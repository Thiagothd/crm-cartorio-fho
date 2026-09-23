using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.CanalWhatsApp.Application;

/// <summary>O que e uma mensagem que saiu do proprio numero do cartorio.</summary>
public enum OrigemDaMensagemDoProprioNumero
{
    /// <summary>
    /// Ja esta no fio: eco do que o painel enviou, ou reentrega de uma resposta
    /// pelo celular ja registrada. Nao grava nada.
    /// </summary>
    JaEstaNoFio = 0,

    /// <summary>Alguem da serventia respondeu fora do painel. Vai para o fio.</summary>
    RespostaPeloAparelho = 1
}

public interface IIdentificadorDeEco
{
    /// <summary>
    /// Separa o eco do que o painel enviou da resposta dada direto pelo celular.
    /// </summary>
    Task<OrigemDaMensagemDoProprioNumero> ClassificarAsync(
        Guid tenantId,
        MensagemRecebidaDoCanal mensagem,
        CancellationToken ct = default);
}

/// <summary>
/// Decide se uma mensagem "enviada por mim" e eco do painel ou resposta pelo
/// celular.
/// </summary>
/// <remarks>
/// <b>O erro que este tipo existe para impedir:</b> tomar o eco de uma mensagem
/// do robo por resposta do celular. O efeito seria o robo se desligar sozinho —
/// toda mensagem automatica viraria "atendimento assumido pelo celular", e a
/// automacao pausaria a si mesma a cada envio. Por isso a regra inteira e
/// assimetrica: na duvida, e ECO. Errar para esse lado perde uma resposta do
/// celular, que e exatamente o comportamento de antes desta funcionalidade;
/// errar para o outro quebraria o atendimento automatico de todos os clientes.
///
/// Tres camadas, da mais forte para a mais fraca:
/// <list type="number">
///   <item><b>Id da mensagem.</b> O WhatsApp devolve no envio o mesmo id que o eco
///   traz; se alguma linha do tenant ja tem esse id, e eco (ou reentrega de uma
///   resposta pelo celular ja gravada — as duas coisas ja estao no fio).</item>
///   <item><b>Conteudo do que esta saindo agora.</b> O eco pode chegar ANTES de a
///   fila de saida gravar o id (a Evolution avisa o webhook enquanto ainda
///   responde ao envio). Nessa janela a mensagem de origem esta em
///   <see cref="StatusEntrega.Enviando"/>, com o disparo carimbado e sem id; se o
///   texto do eco e o texto que ela mandou, e eco.</item>
///   <item><b>Espera curta.</b> Havia envio em voo e o texto nao bateu: espera a
///   fila de saida confirmar. Se o id aparecer, e eco; se todo envio em voo for
///   confirmado com OUTRO id, e resposta pelo celular; se o tempo acabar com
///   envio ainda em voo, e eco — a duvida vai para o lado seguro.</item>
/// </list>
///
/// <b>Nao filtra pelo telefone do contato, de proposito.</b> O WhatsApp devolve o
/// numero ora com, ora sem o nono digito (ver <c>NormalizadorDeTelefone</c>), e
/// uma comparacao por telefone falharia exatamente no cliente cadastrado a mao —
/// deixando de achar o envio em voo e tomando o eco por resposta. Envio em voo e
/// janela de segundos: no cartorio inteiro, sao poucas mensagens por vez.
/// </remarks>
public sealed class IdentificadorDeEco : IIdentificadorDeEco
{
    /// <summary>Quanto esperar pela confirmacao de um envio em voo.</summary>
    /// <remarks>
    /// Folgado para o caminho normal (a confirmacao sai milissegundos depois da
    /// resposta do gateway) e curto o bastante para nao prender o webhook: esta
    /// espera so acontece no caso raro de um envio em voo com texto diferente.
    /// </remarks>
    public static readonly TimeSpan EsperaMaximaPadrao = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan IntervaloDaEsperaPadrao = TimeSpan.FromMilliseconds(250);

    /// <summary>Ate quando um disparo sem confirmacao ainda conta como "em voo".</summary>
    /// <remarks>
    /// Sem este corte, um envio de resultado ambiguo — o processo caiu depois do
    /// disparo, e a fila de saida o deixa em <see cref="StatusEntrega.Enviando"/>
    /// para sempre, de proposito (WA-OUT-01) — faria TODA resposta pelo celular
    /// dali em diante cair na espera e sair como eco. O eco de um envio chega em
    /// segundos; dois minutos sobram com folga e esquecem o envio travado.
    /// </remarks>
    public static readonly TimeSpan JanelaDoEnvioEmVooPadrao = TimeSpan.FromMinutes(2);

    private readonly CrmDbContext _db;
    private readonly ILogger<IdentificadorDeEco> _log;
    private readonly TimeSpan _esperaMaxima;
    private readonly TimeSpan _intervaloDaEspera;
    private readonly TimeSpan _janelaDoEnvioEmVoo;

    /// <summary>O construtor que a injecao de dependencia usa: os tempos padrao.</summary>
    public IdentificadorDeEco(CrmDbContext db, ILogger<IdentificadorDeEco> log)
        : this(db, log, EsperaMaximaPadrao, IntervaloDaEsperaPadrao, JanelaDoEnvioEmVooPadrao)
    {
    }

    /// <summary>
    /// Tempos explicitos — existe para o verificador provar a espera sem esperar
    /// cinco segundos por cenario. O container nao sabe resolver
    /// <see cref="TimeSpan"/>, entao nunca escolhe este.
    /// </summary>
    public IdentificadorDeEco(
        CrmDbContext db,
        ILogger<IdentificadorDeEco> log,
        TimeSpan esperaMaxima,
        TimeSpan intervaloDaEspera,
        TimeSpan janelaDoEnvioEmVoo)
    {
        _db = db;
        _log = log;
        _esperaMaxima = esperaMaxima;
        _intervaloDaEspera = intervaloDaEspera;
        _janelaDoEnvioEmVoo = janelaDoEnvioEmVoo;
    }

    public async Task<OrigemDaMensagemDoProprioNumero> ClassificarAsync(
        Guid tenantId,
        MensagemRecebidaDoCanal mensagem,
        CancellationToken ct = default)
    {
        // "Testar envio" (tela do canal) e o unico envio que nao passa pela fila de
        // saida — nao ha linha para o id casar. Reconhecido pelo texto, que e fixo.
        if (Normalizar(mensagem.Conteudo) == Normalizar(PareamentoDoCanal.TextoDoTeste))
        {
            return OrigemDaMensagemDoProprioNumero.JaEstaNoFio;
        }

        // Camada 1.
        if (await IdJaEstaNoFioAsync(tenantId, mensagem.IdExterno, ct))
        {
            return OrigemDaMensagemDoProprioNumero.JaEstaNoFio;
        }

        // Camada 2.
        var emVoo = await EnviosEmVooAsync(tenantId, ct);

        if (emVoo.Count == 0)
        {
            // O painel nao tem nada saindo agora: nada que ele mandou pode ter
            // produzido este eco. E gente, pelo celular.
            return OrigemDaMensagemDoProprioNumero.RespostaPeloAparelho;
        }

        if (emVoo.Any(envio => CasaComOEco(envio, mensagem)))
        {
            return OrigemDaMensagemDoProprioNumero.JaEstaNoFio;
        }

        // Camada 3.
        var idsEmVoo = emVoo.Select(e => e.Id).ToList();
        var limite = DateTime.UtcNow + _esperaMaxima;

        while (DateTime.UtcNow < limite)
        {
            await Task.Delay(_intervaloDaEspera, ct);

            if (await IdJaEstaNoFioAsync(tenantId, mensagem.IdExterno, ct))
            {
                return OrigemDaMensagemDoProprioNumero.JaEstaNoFio;
            }

            var aindaEmVoo = await _db.Mensagens.AsNoTracking()
                .AnyAsync(m => idsEmVoo.Contains(m.Id)
                            && m.StatusEntrega == StatusEntrega.Enviando
                            && m.IdExternoWhatsapp == null, ct);

            if (!aindaEmVoo)
            {
                // Todo envio em voo foi resolvido e nenhum levou este id. A
                // checagem de id repete depois de saber disso para fechar a
                // corrida entre as duas leituras acima.
                return await IdJaEstaNoFioAsync(tenantId, mensagem.IdExterno, ct)
                    ? OrigemDaMensagemDoProprioNumero.JaEstaNoFio
                    : OrigemDaMensagemDoProprioNumero.RespostaPeloAparelho;
            }
        }

        _log.LogWarning(
            "Mensagem {IdExterno} do proprio numero ficou sem classificacao: havia envio em voo sem confirmar " +
            "depois de {Segundos}s. Tratada como eco — uma resposta pelo celular pode ter ficado fora do fio.",
            mensagem.IdExterno, _esperaMaxima.TotalSeconds);

        return OrigemDaMensagemDoProprioNumero.JaEstaNoFio;
    }

    private Task<bool> IdJaEstaNoFioAsync(Guid tenantId, string idExterno, CancellationToken ct) =>
        _db.Mensagens.AsNoTracking()
            .AnyAsync(m => m.TenantId == tenantId && m.IdExternoWhatsapp == idExterno, ct);

    /// <summary>
    /// O que o painel disparou e o gateway ainda nao confirmou: o unico estado em
    /// que um eco pode chegar sem id para casar.
    /// </summary>
    private async Task<List<EnvioEmVoo>> EnviosEmVooAsync(Guid tenantId, CancellationToken ct)
    {
        var disparadoDesde = DateTime.UtcNow - _janelaDoEnvioEmVoo;

        return await _db.Mensagens.AsNoTracking()
            .Where(m => m.TenantId == tenantId
                     && m.AutorTipo != AutorTipo.Cliente
                     && !m.EnviadaPeloAparelho
                     && m.StatusEntrega == StatusEntrega.Enviando
                     && m.EnvioDisparadoEm != null
                     && m.EnvioDisparadoEm >= disparadoDesde
                     && m.IdExternoWhatsapp == null)
            .Select(m => new EnvioEmVoo(
                m.Id,
                m.Conteudo,
                m.Tipo,
                m.ArquivoCaminho != null,
                m.AutorTipo,
                m.EhAutomatica,
                m.GeradaPorIa,
                _db.Atendentes.Where(a => a.Id == m.AtendenteId).Select(a => a.Nome).FirstOrDefault()))
            .ToListAsync(ct);
    }

    /// <summary>
    /// O eco traz o texto como o cliente o recebeu — ja com o cabecalho de
    /// autoria. A comparacao remonta esse texto a partir do banco, pela mesma
    /// funcao que o envio usou.
    /// </summary>
    /// <remarks>
    /// Midia casa pelo tipo, sem olhar a legenda: a regra de quando a legenda sai
    /// (ver <c>EnviadorPeloWhatsApp.PrepararArquivoAsync</c>) tem casos demais para
    /// reproduzir aqui, e casar "demais" so erra para o lado seguro.
    /// </remarks>
    private static bool CasaComOEco(EnvioEmVoo envio, MensagemRecebidaDoCanal eco)
    {
        if (envio.TemArquivo)
        {
            return eco.TemMidia && eco.Tipo == envio.Tipo;
        }

        if (eco.TemMidia)
        {
            return false;
        }

        var autoria = new AutoriaDaMensagem(
            envio.AutorTipo, envio.EhAutomatica, envio.GeradaPorIa, envio.AtendenteNome);

        return Normalizar(AssinaturaDeAutoria.Decorar(envio.Conteudo, autoria)) == Normalizar(eco.Conteudo)
            // Defensivo: se algum dia o eco chegar sem o cabecalho, o texto puro
            // tambem conta.
            || Normalizar(envio.Conteudo) == Normalizar(eco.Conteudo);
    }

    private static string Normalizar(string texto) =>
        texto.Replace("\r\n", "\n").Trim();

    private sealed record EnvioEmVoo(
        Guid Id,
        string Conteudo,
        TipoMensagem Tipo,
        bool TemArquivo,
        AutorTipo AutorTipo,
        bool EhAutomatica,
        bool GeradaPorIa,
        string? AtendenteNome);
}
