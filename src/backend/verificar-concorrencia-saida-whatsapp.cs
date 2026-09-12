#:project CrmCartorio.Api/CrmCartorio.Api.csproj
#:property PublishAot=false

using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Atendimento.Application;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.CanalWhatsApp.Application;
using CrmCartorio.Api.Modules.CanalWhatsApp.Domain.Enums;
using CrmCartorio.Api.Modules.Clientes.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// WA-OUT-01 — prova o claim usando SQL Server real. EF InMemory nao implementa
// ExecuteUpdate e nao demonstra atomicidade entre conexoes.
// Rode com: dotnet run verificar-concorrencia-saida-whatsapp.cs ["<connection string>"]
// Escreve somente em CRM_Cartorio_Dev e remove tudo que cria.

var conexao = args.Length > 0
    ? args[0]
    : "Server=.\\SQLEXPRESS;Database=CRM_Cartorio_Dev;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

var builder = new DbContextOptionsBuilder<CrmDbContext>();
ConfiguracaoDoBanco.Configurar(builder, conexao);
var opcoes = builder.Options;
var cifra = FabricaDeCifraDeDados.CriarDormente();
await using (var seguranca = new CrmDbContext(opcoes, cifra))
{
    if (!string.Equals(seguranca.Database.GetDbConnection().Database, "CRM_Cartorio_Dev", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Recusado: o verificador so escreve em CRM_Cartorio_Dev.");
    if (!await seguranca.Database.CanConnectAsync()) return 2;
}

var falhas = 0;
void Checar(string nome, bool ok)
{
    Console.WriteLine($"{(ok ? "OK   " : "FALHA")} {nome}");
    if (!ok) falhas++;
}

var opcoesCanal = Options.Create(new OpcoesDoCanalWhatsApp
{
    Habilitado = true, BaseUrl = "http://fake", ApiKey = "fake",
    NomeDaInstancia = "fake", TokenDoWebhook = "fake"
});
var regras = new RegrasFake();
var anexos = new AnexosFake();
Guid tenantId, clienteId, conversaId;
var ids = new List<Guid>();

await using (var db = new CrmDbContext(opcoes, cifra))
{
    tenantId = await db.Tenants.Select(t => t.Id).FirstAsync();
    var cliente = new Cliente { TenantId = tenantId, Nome = "[TESTE WA-OUT-01]", TelefoneWhatsapp = $"559{Random.Shared.NextInt64(100000000, 999999999)}" };
    var conversa = new Conversa { TenantId = tenantId, ClienteId = cliente.Id, Protocolo = $"WOUT-{Guid.NewGuid():N}"[..20], Canal = CanalOrigem.WhatsApp };
    db.AddRange(cliente, conversa);
    await db.SaveChangesAsync();
    clienteId = cliente.Id; conversaId = conversa.Id;
}

async Task<Guid> NovaMensagem(StatusEntrega status = StatusEntrega.Pendente, bool automatica = true, DateTime? iniciada = null, DateTime? disparada = null)
{
    await using var db = new CrmDbContext(opcoes, cifra);
    var m = new Mensagem { TenantId = tenantId, ConversaId = conversaId, AutorTipo = automatica ? AutorTipo.Sistema : AutorTipo.Atendente,
        AtendenteId = automatica ? null : await db.Atendentes.Where(a => a.TenantId == tenantId).Select(a => (Guid?)a.Id).FirstAsync(),
        Conteudo = $"[TESTE WA-OUT-01] {Guid.NewGuid():N}", EhAutomatica = automatica, StatusEntrega = status,
        EnvioIniciadoEm = iniciada, EnvioDisparadoEm = disparada, EnviadaEm = DateTime.UtcNow };
    db.Mensagens.Add(m); await db.SaveChangesAsync(); ids.Add(m.Id); return m.Id;
}

EnviadorPeloWhatsApp Novo(CrmDbContext db, GatewayFake gateway) => new(db, gateway, anexos, regras, opcoesCanal, NullLogger<EnviadorPeloWhatsApp>.Instance);
async Task Disputar(Guid id, int quantidade, GatewayFake gateway, bool pendentes = false)
{
    await Task.WhenAll(Enumerable.Range(0, quantidade).Select(async _ =>
    {
        await using var db = new CrmDbContext(opcoes, cifra);
        if (pendentes) await Novo(db, gateway).EnviarPendentesAsync(conversaId);
        else await Novo(db, gateway).EnviarAsync(id);
    }));
}
async Task<Mensagem> Ler(Guid id)
{
    await using var db = new CrmDbContext(opcoes, cifra);
    return await db.Mensagens.AsNoTracking().SingleAsync(m => m.Id == id);
}

try
{
    var id = await NovaMensagem(); var gw = new GatewayFake(); await Disputar(id, 2, gw);
    Checar("2 EnviarAsync simultaneos fazem exatamente 1 chamada", gw.Envios == 1);

    id = await NovaMensagem(); gw = new(); await Disputar(id, 8, gw);
    Checar("8 EnviarAsync simultaneos fazem exatamente 1 chamada", gw.Envios == 1);

    id = await NovaMensagem(); gw = new(); await Disputar(id, 2, gw, pendentes: true);
    Checar("2 EnviarPendentesAsync simultaneos fazem exatamente 1 chamada", gw.Envios == 1);

    id = await NovaMensagem(automatica: false); gw = new(); await Disputar(id, 5, gw);
    Checar("envio manual concorrente faz exatamente 1 chamada", gw.Envios == 1);

    id = await NovaMensagem(); gw = new(); await Disputar(id, 5, gw);
    Checar("automatica concorrente faz exatamente 1 chamada", gw.Envios == 1);

    id = await NovaMensagem(StatusEntrega.Falha); gw = new(); await Disputar(id, 5, gw);
    Checar("retry concorrente de Falha faz exatamente 1 chamada", gw.Envios == 1);

    id = await NovaMensagem(StatusEntrega.Enviada); gw = new(); await Disputar(id, 1, gw);
    Checar("segunda tentativa depois de Enviada nao chama gateway", gw.Envios == 0);

    var diferente1 = await NovaMensagem(automatica: false); var diferente2 = await NovaMensagem(automatica: false); gw = new();
    await Task.WhenAll(Disputar(diferente1, 1, gw), Disputar(diferente2, 1, gw));
    Checar("duas mensagens diferentes continuam funcionando", gw.Envios == 2);

    var barreira = await NovaMensagem(StatusEntrega.Enviando, iniciada: DateTime.UtcNow, disparada: DateTime.UtcNow);
    var posterior = await NovaMensagem(); gw = new(); await Disputar(posterior, 0, gw); // mantem ids usados
    await using (var db = new CrmDbContext(opcoes, cifra)) await Novo(db, gw).EnviarPendentesAsync(conversaId);
    Checar("automatica posterior nao ultrapassa anterior Enviando", gw.Envios == 0 && (await Ler(posterior)).StatusEntrega == StatusEntrega.Pendente);

    await using (var db = new CrmDbContext(opcoes, cifra)) await db.Conversas.Where(c => c.Id == conversaId).ExecuteUpdateAsync(s => s.SetProperty(c => c.EmAtendimentoHumano, true));
    id = await NovaMensagem(automatica: false); gw = new(); await Disputar(id, 1, gw);
    await using (var db = new CrmDbContext(opcoes, cifra)) Checar("Human Takeover permanece funcional", gw.Envios == 1 && await db.Conversas.Where(c => c.Id == conversaId).Select(c => c.EmAtendimentoHumano).SingleAsync());

    id = await NovaMensagem(); gw = new GatewayFake { FalharDepoisDoDisparo = true }; await Disputar(id, 1, gw);
    var ambiguo = await Ler(id);
    Checar("timeout depois do disparo permanece Enviando", ambiguo.StatusEntrega == StatusEntrega.Enviando && ambiguo.EnvioDisparadoEm != null);
    await Disputar(id, 1, gw);
    Checar("resultado ambiguo nao e retentado", gw.Envios == 1);

    id = await NovaMensagem(StatusEntrega.Enviando, iniciada: DateTime.UtcNow.AddMinutes(-10)); gw = new(); await Disputar(id, 1, gw);
    Checar("claim antigo sem disparo e recuperado", gw.Envios == 1 && (await Ler(id)).StatusEntrega == StatusEntrega.Enviada);

    id = await NovaMensagem(StatusEntrega.Enviando, iniciada: DateTime.UtcNow.AddMinutes(-10), disparada: DateTime.UtcNow.AddMinutes(-10)); gw = new(); await Disputar(id, 1, gw);
    Checar("claim antigo com disparo nao e reenviado", gw.Envios == 0 && (await Ler(id)).StatusEntrega == StatusEntrega.Enviando);
}
finally
{
    await using var db = new CrmDbContext(opcoes, cifra);
    await db.Mensagens.Where(m => ids.Contains(m.Id)).ExecuteDeleteAsync();
    await db.Conversas.Where(c => c.Id == conversaId).ExecuteDeleteAsync();
    await db.Clientes.Where(c => c.Id == clienteId).ExecuteDeleteAsync();
}

return falhas == 0 ? 0 : 1;

file sealed class GatewayFake : IEvolutionClient
{
    private int _envios;
    public int Envios => _envios;
    public bool FalharDepoisDoDisparo { get; init; }
    public Task<string> EnviarTextoAsync(string telefoneE164, string conteudo, CancellationToken ct)
    {
        Interlocked.Increment(ref _envios);
        if (FalharDepoisDoDisparo) throw new TimeoutException("Timeout ambiguo simulado.");
        return Task.FromResult(Guid.NewGuid().ToString("N"));
    }
    public Task<string> EnviarMidiaAsync(string a, byte[] b, string c, string d, string? e, CancellationToken ct) => EnviarTextoAsync(a, d, ct);
    public Task<EstadoDaInstancia> GarantirInstanciaAsync(string a, CancellationToken ct) => throw new NotSupportedException();
    public Task<EstadoDaInstancia> ObterEstadoAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<EstadoDaInstancia> IniciarConexaoAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task DesconectarAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<PerfilDaInstancia?> ObterPerfilDaInstanciaAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<byte[]?> BaixarMidiaAsync(string a, CancellationToken ct) => throw new NotSupportedException();
    public Task<string?> ObterFotoPerfilUrlAsync(string a, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<ContatoDoWhatsApp>> ObterContatosAsync(CancellationToken ct) => throw new NotSupportedException();
}

file sealed class AnexosFake : IArmazenamentoDeAnexos
{
    public long TamanhoMaximoBytes => long.MaxValue;
    public bool TipoPermitido(string? tipoMime) => true;
    public Task<AnexoGravado> GravarAsync(Guid a, Guid b, DateTime c, byte[] d, string? e, string? f, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<byte[]?> LerAsync(string a, CancellationToken ct = default) => Task.FromResult<byte[]?>([1]);
}

file sealed class RegrasFake : IRegrasDoCanal
{
    public Task<VeredictoDoNumero> PodeFalarComAsync(Guid a, string b, CancellationToken ct = default) => Task.FromResult(new VeredictoDoNumero(true, null));
    public Task<RegrasDoCanalResponse> ObterAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<RegrasDoCanalResponse> DefinirModoAsync(DefinirModoRequest a, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<NumeroDoCanalResponse> AdicionarAsync(AdicionarNumeroRequest a, CancellationToken ct = default) => throw new NotSupportedException();
    public Task RemoverAsync(Guid a, CancellationToken ct = default) => throw new NotSupportedException();
    public Task RegistrarDescarteAsync(Guid a, string b, MotivoDoDescarte c, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<DescartesResponse> DescartesRecentesAsync(CancellationToken ct = default) => throw new NotSupportedException();
}
