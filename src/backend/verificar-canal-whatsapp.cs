#:project CrmCartorio.Api/CrmCartorio.Api.csproj
#:property PublishAot=false
#:package Microsoft.EntityFrameworkCore.InMemory@10.0.9

using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Atendimento.Application;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.CanalWhatsApp.Application;
using CrmCartorio.Api.Modules.Clientes.Application;
using CrmCartorio.Api.Modules.Clientes.Domain.Entities;
using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;

// VERIFICADOR DA RESTRICAO DE NUMEROS DO CANAL WHATSAPP
//
// Rode com: dotnet run verificar-canal-whatsapp.cs
//
// Nao abre conexao nem chama gateway. Usa um banco descartavel somente em memoria
// para provar que os adaptadores reais de entrada e saida aplicam a politica.

var falhas = 0;

void Checar(string nome, bool condicao)
{
    if (condicao)
    {
        Console.WriteLine($"OK    {nome}");
        return;
    }

    falhas++;
    Console.WriteLine($"FALHA {nome}");
}

var canalPublico = new OpcoesDoCanalWhatsApp();
Checar(
    "Lista de teste vazia mantém canal público sem restrição",
    canalPublico.NumeroPermitido("5538991234567"));

var canalDeTeste = new OpcoesDoCanalWhatsApp
{
    Habilitado = true,
    BaseUrl = "http://gateway-inexistente",
    ApiKey = "api-key-de-verificacao",
    NomeDaInstancia = "cartorio-teste",
    TokenDoWebhook = "token-de-verificacao",
    NumerosDeTeste = ["+55 (19) 99000-1488"]
};

Checar(
    "Contato autorizado casa no E.164 produzido pelo webhook",
    canalDeTeste.NumeroPermitido("5519990001488"));
Checar(
    "Formatação é removida dos dois lados",
    canalDeTeste.NumeroPermitido("+55 19 99000-1488"));
Checar(
    "Outro contato é bloqueado",
    !canalDeTeste.NumeroPermitido("5538991234567"));
Checar(
    "Número sem DDI não ganha 55 silenciosamente",
    !canalDeTeste.NumeroPermitido("19 99000-1488"));
Checar(
    "Valor vazio não é aceito",
    !canalDeTeste.NumeroPermitido("---"));
Checar(
    "Allowlist E.164 brasileira é válida para o ambiente Teste",
    canalDeTeste.RestricaoDeTesteValida);
Checar(
    "Instância configurada é aceita",
    canalDeTeste.InstanciaConfere("cartorio-teste"));
Checar(
    "Nome da instância é o único aceito no ambiente Teste",
    canalDeTeste.InstanciaDeTesteValida);
Checar(
    "Instância de produção é rejeitada no ambiente de teste",
    !canalDeTeste.InstanciaConfere("cartorio-piloto"));

var testeApontandoParaProducao = new OpcoesDoCanalWhatsApp
{
    NomeDaInstancia = "cartorio-piloto",
    NumerosDeTeste = ["5519990001488"]
};
Checar(
    "Configuração de Teste não pode apontar para cartorio-piloto",
    !testeApontandoParaProducao.InstanciaDeTesteValida);

var testeSemDdi = new OpcoesDoCanalWhatsApp
{
    NumerosDeTeste = ["19 99000-1488"]
};
Checar(
    "Ambiente Teste rejeita allowlist sem DDI 55",
    !testeSemDdi.RestricaoDeTesteValida);

var testeSemAllowlist = new OpcoesDoCanalWhatsApp();
Checar(
    "Ambiente Teste rejeita allowlist vazia",
    !testeSemAllowlist.RestricaoDeTesteValida);

var testeSemContatoEfetivo = new OpcoesDoCanalWhatsApp
{
    NumerosDeTeste = ["5519990001488"],
    NumerosBloqueados = ["+55 (19) 99000-1488"]
};
Checar(
    "Ambiente Teste rejeita contato permitido que também esteja bloqueado",
    !testeSemContatoEfetivo.RestricaoDeTesteValida);

var testeComListaMista = new OpcoesDoCanalWhatsApp
{
    NumerosDeTeste = ["5519990001488", "19 99000-1488"]
};
Checar(
    "Ambiente Teste rejeita allowlist parcialmente inválida",
    !testeComListaMista.RestricaoDeTesteValida);

var testeComDoisContatos = new OpcoesDoCanalWhatsApp
{
    NumerosDeTeste = ["5519990001488", "5538991234567"]
};
Checar(
    "Ambiente Teste rejeita mais de um contato autorizado",
    !testeComDoisContatos.RestricaoDeTesteValida);

var listasNulas = new OpcoesDoCanalWhatsApp
{
    NumerosDeTeste = null!,
    NumerosBloqueados = null!
};
Checar(
    "Configuração desserializada com listas nulas falha de forma segura",
    listasNulas.NumeroPermitido("5519990001488") && !listasNulas.RestricaoDeTesteValida);

var producaoProtegida = new OpcoesDoCanalWhatsApp
{
    NumerosBloqueados = ["+55 (38) 99800-0901"]
};

Checar(
    "Bloqueio pontual funciona com canal público",
    !producaoProtegida.NumeroPermitido("5538998000901"));
Checar(
    "Bloqueio pontual não fecha produção para outros contatos",
    producaoProtegida.NumeroPermitido("5538991234567"));

var bloqueioVenceAllowlist = new OpcoesDoCanalWhatsApp
{
    NumerosDeTeste = ["5519990001488"],
    NumerosBloqueados = ["5519990001488"]
};

Checar(
    "Lista de bloqueio tem precedência sobre a allowlist",
    !bloqueioVenceAllowlist.NumeroPermitido("5519990001488"));

using var payloadLid = JsonDocument.Parse("""
    {
      "key": {
        "remoteJid": "116909060165867@lid",
        "remoteJidAlt": "5519990001488@s.whatsapp.net",
        "fromMe": false,
        "id": "MENSAGEM-LID-1"
      },
      "pushName": "Contato autorizado",
      "message": {
        "conversation": "mensagem de teste"
      }
    }
    """);

var traduzidaDoLid = TradutorDePayload.LerMensagem(payloadLid.RootElement);
Checar(
    "Payload @lid usa remoteJidAlt como telefone real",
    traduzidaDoLid?.TelefoneE164 == "5519990001488");
Checar(
    "Contato @lid traduzido passa pela mesma allowlist",
    traduzidaDoLid is not null && canalDeTeste.NumeroPermitido(traduzidaDoLid.TelefoneE164));

using var payloadTradicional = JsonDocument.Parse("""
    {
      "key": {
        "remoteJid": "5538991234567@s.whatsapp.net",
        "remoteJidAlt": "5519990001488@s.whatsapp.net",
        "fromMe": false,
        "id": "MENSAGEM-JID-1"
      },
      "message": {
        "conversation": "mensagem tradicional"
      }
    }
    """);

Checar(
    "remoteJidAlt não sobrescreve um JID tradicional",
    TradutorDePayload.LerMensagem(payloadTradicional.RootElement)?.TelefoneE164
        == "5538991234567");

using var payloadFromMe = JsonDocument.Parse("""
    {
      "key": {
        "remoteJid": "5519990001488@s.whatsapp.net",
        "fromMe": true,
        "id": "MENSAGEM-ECO"
      },
      "message": {
        "conversation": "eco"
      }
    }
    """);

Checar(
    "Eco fromMe é descartado",
    TradutorDePayload.LerMensagem(payloadFromMe.RootElement) is null);

using var payloadLidSemAlternativo = JsonDocument.Parse("""
    {
      "key": {
        "remoteJid": "116909060165867@lid",
        "fromMe": false,
        "id": "MENSAGEM-LID-SEM-ALT"
      },
      "message": {
        "conversation": "não deve entrar"
      }
    }
    """);

Checar(
    "@lid sem remoteJidAlt individual é descartado",
    TradutorDePayload.LerMensagem(payloadLidSemAlternativo.RootElement) is null);

using var payloadLidComAlternativoInvalido = JsonDocument.Parse("""
    {
      "key": {
        "remoteJid": "116909060165867@lid",
        "remoteJidAlt": "5519990001488@newsletter",
        "fromMe": false,
        "id": "MENSAGEM-LID-ALT-INVALIDO"
      },
      "message": {
        "conversation": "não deve entrar"
      }
    }
    """);

Checar(
    "@lid com remoteJidAlt não individual é descartado",
    TradutorDePayload.LerMensagem(payloadLidComAlternativoInvalido.RootElement) is null);

foreach (var jidColetivo in new[]
         {
             "5511999999999@g.us",
             "5511999999999@broadcast",
             "5511999999999@newsletter"
         })
{
    using var payloadColetivo = JsonDocument.Parse($$"""
        {
          "key": {
            "remoteJid": "{{jidColetivo}}",
            "fromMe": false,
            "id": "MENSAGEM-COLETIVA"
          },
          "message": {
            "conversation": "não deve entrar"
          }
        }
        """);

    Checar(
        $"JID coletivo {jidColetivo.Split('@')[1]} é descartado",
        TradutorDePayload.LerMensagem(payloadColetivo.RootElement) is null);
}

// Exercita os adaptadores reais com doubles apenas nas portas externas. Se o
// Processador deixar de chamar NumeroPermitido/InstanciaConfere, estes cenários
// chegam ao recebimento. Se o Enviador perder a guarda, o gateway ou o storage
// fake registra a tentativa.
var opcoesDoBanco = new DbContextOptionsBuilder<CrmDbContext>()
    .UseInMemoryDatabase($"verificar-canal-{Guid.NewGuid():N}")
    .Options;

await using var db = new CrmDbContext(opcoesDoBanco);

var tenant = new Tenant
{
    RazaoSocial = "Tenant efêmero do verificador",
    Cnpj = "00000000000000",
    WhatsappInstancia = "cartorio-teste"
};

db.Tenants.Add(tenant);
await db.SaveChangesAsync();

var gatewayFake = new GatewayFake();
var recebimentoFake = new RecebimentoFake();
var processador = new ProcessadorDeWebhook(
    db,
    recebimentoFake,
    gatewayFake,
    Options.Create(canalDeTeste),
    NullLogger<ProcessadorDeWebhook>.Instance);

await processador.ProcessarAsync(new PayloadDoWebhook(
    "messages.upsert",
    "cartorio-teste",
    payloadTradicional.RootElement.Clone()));

Checar(
    "Processador não entrega entrada fora da allowlist",
    recebimentoFake.Recebimentos == 0
    && gatewayFake.ConsultasDeFoto == 0
    && gatewayFake.DownloadsDeMidia == 0);

using var payloadMidiaBloqueada = JsonDocument.Parse("""
    {
      "key": {
        "remoteJid": "5538991234567@s.whatsapp.net",
        "fromMe": false,
        "id": "MIDIA-BLOQUEADA"
      },
      "message": {
        "imageMessage": {
          "url": "https://midia-inexistente.invalid/arquivo",
          "mimetype": "image/jpeg",
          "caption": "não deve baixar"
        }
      }
    }
    """);

await processador.ProcessarAsync(new PayloadDoWebhook(
    "messages.upsert",
    "cartorio-teste",
    payloadMidiaBloqueada.RootElement.Clone()));

Checar(
    "Processador bloqueia mídia antes de consultar ou baixar no gateway",
    recebimentoFake.Recebimentos == 0
    && gatewayFake.ConsultasDeFoto == 0
    && gatewayFake.DownloadsDeMidia == 0);

await processador.ProcessarAsync(new PayloadDoWebhook(
    "messages.upsert",
    "cartorio-piloto",
    payloadLid.RootElement.Clone()));

Checar(
    "Processador ignora webhook da instância de produção",
    recebimentoFake.Recebimentos == 0 && gatewayFake.ConsultasDeFoto == 0);

var canalDesligado = new OpcoesDoCanalWhatsApp
{
    Habilitado = false,
    BaseUrl = canalDeTeste.BaseUrl,
    ApiKey = canalDeTeste.ApiKey,
    NomeDaInstancia = canalDeTeste.NomeDaInstancia,
    TokenDoWebhook = canalDeTeste.TokenDoWebhook,
    NumerosDeTeste = canalDeTeste.NumerosDeTeste
};
var processadorDesligado = new ProcessadorDeWebhook(
    db,
    recebimentoFake,
    gatewayFake,
    Options.Create(canalDesligado),
    NullLogger<ProcessadorDeWebhook>.Instance);

await processadorDesligado.ProcessarAsync(new PayloadDoWebhook(
    "messages.upsert",
    "cartorio-teste",
    payloadLid.RootElement.Clone()));

Checar(
    "Processador desligado não entrega nem consulta o gateway",
    recebimentoFake.Recebimentos == 0 && gatewayFake.ConsultasDeFoto == 0);

await processador.ProcessarAsync(new PayloadDoWebhook(
    "messages.upsert",
    "cartorio-teste",
    payloadLid.RootElement.Clone()));

Checar(
    "Processador entrega o contato autorizado",
    recebimentoFake.Recebimentos == 1
    && recebimentoFake.Ultima?.TelefoneE164 == "5519990001488");

var clienteBloqueado = new Cliente
{
    TenantId = tenant.Id,
    Nome = "Contato fora da allowlist",
    TelefoneWhatsapp = "5538991234567"
};
var conversaBloqueada = new Conversa
{
    TenantId = tenant.Id,
    ClienteId = clienteBloqueado.Id,
    Protocolo = "TESTE-0001",
    Canal = CanalOrigem.WhatsApp
};
var mensagemBloqueada = new Mensagem
{
    TenantId = tenant.Id,
    ConversaId = conversaBloqueada.Id,
    AutorTipo = AutorTipo.Sistema,
    Conteudo = "arquivo que não pode sair",
    Tipo = TipoMensagem.Documento,
    StatusEntrega = StatusEntrega.Pendente,
    EhAutomatica = true,
    ArquivoCaminho = "arquivo-inexistente.pdf",
    ArquivoNome = "arquivo.pdf",
    ArquivoTipoMime = "application/pdf",
    ArquivoTamanhoBytes = 10
};

db.AddRange(clienteBloqueado, conversaBloqueada, mensagemBloqueada);
await db.SaveChangesAsync();

var anexosFake = new ArmazenamentoFake();
var enviador = new EnviadorPeloWhatsApp(
    db,
    gatewayFake,
    anexosFake,
    Options.Create(canalDeTeste),
    NullLogger<EnviadorPeloWhatsApp>.Instance);

var idExternoBloqueado = await enviador.EnviarAsync(mensagemBloqueada.Id);

Checar(
    "Enviador bloqueia saída antes de ler anexo ou chamar gateway",
    idExternoBloqueado is null
    && anexosFake.Leituras == 0
    && gatewayFake.EnviosDeTexto == 0
    && gatewayFake.EnviosDeMidia == 0
    && mensagemBloqueada.StatusEntrega == StatusEntrega.Pendente);

var clientePermitido = new Cliente
{
    TenantId = tenant.Id,
    Nome = "Contato autorizado",
    TelefoneWhatsapp = "5519990001488"
};
var conversaPermitida = new Conversa
{
    TenantId = tenant.Id,
    ClienteId = clientePermitido.Id,
    Protocolo = "TESTE-0002",
    Canal = CanalOrigem.WhatsApp
};
var mensagemPermitida = new Mensagem
{
    TenantId = tenant.Id,
    ConversaId = conversaPermitida.Id,
    AutorTipo = AutorTipo.Sistema,
    Conteudo = "mensagem autorizada",
    StatusEntrega = StatusEntrega.Pendente,
    EhAutomatica = true
};

db.AddRange(clientePermitido, conversaPermitida, mensagemPermitida);
await db.SaveChangesAsync();

// Cancela dentro do double depois de contar a chamada. Isso prova que o envio
// autorizado atravessa o gate sem depender de ExecuteUpdate, que o provider em
// memória deliberadamente não implementa.
var gatewayPermitido = new GatewayFake { CancelarEnvios = true };
var enviadorPermitido = new EnviadorPeloWhatsApp(
    db,
    gatewayPermitido,
    anexosFake,
    Options.Create(canalDeTeste),
    NullLogger<EnviadorPeloWhatsApp>.Instance);
var cancelamentoEsperado = false;

try
{
    await enviadorPermitido.EnviarAsync(mensagemPermitida.Id);
}
catch (OperationCanceledException)
{
    cancelamentoEsperado = true;
}

Checar(
    "Enviador deixa exatamente um envio de texto chegar ao gateway para o contato autorizado",
    cancelamentoEsperado
    && gatewayPermitido.EnviosDeTexto == 1
    && gatewayPermitido.EnviosDeMidia == 0);

var gatewayDeFotos = new GatewayFake();
using var cache = new MemoryCache(new MemoryCacheOptions());
var clienteService = new ClienteService(
    db,
    new ContextoFake(tenant.Id),
    gatewayDeFotos,
    new HttpClientFactoryFake(),
    cache,
    Options.Create(canalDeTeste),
    NullLogger<ClienteService>.Instance);

var fotoBloqueada = await clienteService.ObterFotoPerfilAsync(clienteBloqueado.Id);
Checar(
    "Ficha fora da allowlist não consulta foto no gateway",
    fotoBloqueada is null && gatewayDeFotos.ConsultasDeFoto == 0);

var fotoPermitida = await clienteService.ObterFotoPerfilAsync(clientePermitido.Id);
Checar(
    "Ficha do contato autorizado pode consultar foto no gateway",
    fotoPermitida is null && gatewayDeFotos.ConsultasDeFoto == 1);

Console.WriteLine();
Console.WriteLine(falhas == 0
    ? "Todas as restrições do canal WhatsApp passaram."
    : $"{falhas} restrição(ões) do canal WhatsApp falharam.");

return falhas == 0 ? 0 : 1;

file sealed class RecebimentoFake : IRecebimentoDeWhatsApp
{
    public int Recebimentos { get; private set; }
    public MensagemDoCanal? Ultima { get; private set; }

    public Task<Guid> ReceberAsync(
        Guid tenantId,
        MensagemDoCanal mensagem,
        CancellationToken ct = default)
    {
        Recebimentos++;
        Ultima = mensagem;
        return Task.FromResult(Guid.NewGuid());
    }

    public Task AtualizarEntregaAsync(
        Guid tenantId,
        string idExternoWhatsapp,
        StatusEntrega status,
        CancellationToken ct = default) => Task.CompletedTask;
}

file sealed class GatewayFake : IEvolutionClient
{
    public bool CancelarEnvios { get; init; }
    public int ConsultasDeFoto { get; private set; }
    public int DownloadsDeMidia { get; private set; }
    public int EnviosDeTexto { get; private set; }
    public int EnviosDeMidia { get; private set; }

    public Task<EstadoDaInstancia> GarantirInstanciaAsync(string urlDoWebhook, CancellationToken ct) =>
        Task.FromResult(new EstadoDaInstancia("open", null));

    public Task<EstadoDaInstancia> ObterEstadoAsync(CancellationToken ct) =>
        Task.FromResult(new EstadoDaInstancia("open", null));

    public Task<string> EnviarTextoAsync(
        string telefoneE164,
        string conteudo,
        CancellationToken ct)
    {
        EnviosDeTexto++;

        if (CancelarEnvios)
        {
            throw new OperationCanceledException("Cancelamento intencional do verificador.");
        }

        return Task.FromResult("ENVIO-TEXTO-FAKE");
    }

    public Task<string> EnviarMidiaAsync(
        string telefoneE164,
        byte[] conteudo,
        string tipoMime,
        string nomeDoArquivo,
        string? legenda,
        CancellationToken ct)
    {
        EnviosDeMidia++;

        if (CancelarEnvios)
        {
            throw new OperationCanceledException("Cancelamento intencional do verificador.");
        }

        return Task.FromResult("ENVIO-MIDIA-FAKE");
    }

    public Task<byte[]?> BaixarMidiaAsync(string idExternoWhatsapp, CancellationToken ct)
    {
        DownloadsDeMidia++;
        return Task.FromResult<byte[]?>([1, 2, 3]);
    }

    public Task<string?> ObterFotoPerfilUrlAsync(string telefoneE164, CancellationToken ct)
    {
        ConsultasDeFoto++;
        return Task.FromResult<string?>(null);
    }
}

file sealed class ArmazenamentoFake : IArmazenamentoDeAnexos
{
    public int Leituras { get; private set; }
    public long TamanhoMaximoBytes => 10_000_000;

    public bool TipoPermitido(string? tipoMime) => true;

    public Task<AnexoGravado> GravarAsync(
        Guid tenantId,
        Guid mensagemId,
        DateTime quando,
        byte[] conteudo,
        string? nomeOriginal,
        string? tipoMime,
        CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<byte[]?> LerAsync(string caminhoRelativo, CancellationToken ct = default)
    {
        Leituras++;
        return Task.FromResult<byte[]?>([1, 2, 3]);
    }
}

file sealed class ContextoFake(Guid tenantId) : IContextoDoUsuario
{
    public bool Autenticado => true;
    public Guid TenantId => tenantId;
    public Guid AtendenteId => Guid.Empty;
    public string Nome => "Verificador";
    public PerfilDeAtendente Perfil => PerfilDeAtendente.Administrador;
    public bool PodeGerirCadastros => true;
    public bool PodeGerirAtendentes => true;
    public bool PodeOperarConversaDeTerceiro => true;
}

file sealed class HttpClientFactoryFake : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new();
}
