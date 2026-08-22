using System.Collections.Concurrent;

namespace CrmCartorio.Api.Modules.CanalWhatsApp.Application;

/// <summary>
/// Espaça pedidos de pareamento (gerar QR) no tempo, por instância.
/// </summary>
/// <remarks>
/// A Meta bloqueia temporariamente 2-3 pareamentos seguidos em pouco tempo
/// (docs/DEPLOY-WHATSAPP.md, seção 7). Recusar aqui evita gastar uma tentativa
/// que já sabemos que vai falhar.
///
/// Registrado como <b>singleton</b> de propósito: <see cref="PareamentoDoCanal"/>
/// é Scoped (usa <c>CrmDbContext</c>), e uma instância nova a cada requisição
/// perderia o controle na hora seguinte. Em memória, por processo — perder no
/// restart é aceitável, e mais barato que abrir uma coluna só para isto.
/// </remarks>
public class ControleDeTentativasDePareamento
{
    private const int IntervaloMinimoSegundos = 30;

    private readonly ConcurrentDictionary<string, DateTime> _ultimaTentativa = new();

    /// <summary>Segundos que faltam para a próxima tentativa ser aceita. Zero se pode tentar agora.</summary>
    public int SegundosRestantes(string instancia)
    {
        if (!_ultimaTentativa.TryGetValue(instancia, out var ultima))
        {
            return 0;
        }

        var passado = (DateTime.UtcNow - ultima).TotalSeconds;
        var restante = IntervaloMinimoSegundos - passado;

        return restante > 0 ? (int)Math.Ceiling(restante) : 0;
    }

    public void RegistrarTentativa(string instancia) =>
        _ultimaTentativa[instancia] = DateTime.UtcNow;
}
