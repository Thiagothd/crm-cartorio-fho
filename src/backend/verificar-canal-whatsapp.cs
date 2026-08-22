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
using System.Net;
using System.Text;
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
    "JID legado sem o nono digito casa somente com o mesmo celular brasileiro",
    canalDeTeste.NumeroPermitido("551990001488"));
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

var testeComJidLegadoNaAllowlist = new OpcoesDoCanalWhatsApp
{
    NumerosDeTeste = ["551990001488"]
};
Checar(
    "Ambiente Teste exige a forma canonica com nono digito na allowlist",
    !testeComJidLegadoNaAllowlist.RestricaoDeTesteValida);

var testeComMovelCanonicoForaDaFaixaLegada = new OpcoesDoCanalWhatsApp
{
    NumerosDeTeste = ["5519912345678"]
};
Checar(
    "Movel canonico 9XXXXXXXX e valido mesmo sem alias legado elegivel",
    testeComMovelCanonicoForaDaFaixaLegada.RestricaoDeTesteValida
    && !testeComMovelCanonicoForaDaFaixaLegada.NumeroPermitido("551912345678"));

var testeComDddInvalido = new OpcoesDoCanalWhatsApp
{
    NumerosDeTeste = ["5520990001488"]
};
Checar(
    "Ambiente Teste rejeita DDD brasileiro inexistente",
    !testeComDddInvalido.RestricaoDeTesteValida);

var testeComCanonicoEAlias = new OpcoesDoCanalWhatsApp
{
    NumerosDeTeste = ["5519990001488", "551990001488"]
};
Checar(
    "Canonico e alias continuam sendo duas entradas e invalidam a restricao",
    !testeComCanonicoEAlias.RestricaoDeTesteValida);

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
    "Bloqueio canonico tambem barra o JID legado real do numero de teste",
    !producaoProtegida.NumeroPermitido("553898000901"));
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

var bloqueioLegadoVenceAllowlist = new OpcoesDoCanalWhatsApp
{
    NumerosDeTeste = ["5519990001488"],
    NumerosBloqueados = ["551990001488"]
};
Checar(
    "Bloqueio legado tem precedencia sobre a allowlist canonica",
    !bloqueioLegadoVenceAllowlist.NumeroPermitido("5519990001488")
    && !bloqueioLegadoVenceAllowlist.RestricaoDeTesteValida);

foreach (var naoEquivalente in new[]
         {
             "19990001488",
             "1990001488",
             "1551990001488",
             "3519990001488",
             "5951990001488",
             "005519990001488",
             "5520990001488",
             "5519290001488"
         })
{
    Checar(
        $"Numero nao equivalente {naoEquivalente} nao atravessa a allowlist",
        !canalDeTeste.NumeroPermitido(naoEquivalente));
}

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

using var payloadLidLegado = JsonDocument.Parse("""
    {
      "key": {
        "remoteJid": "116909060165867@lid",
        "remoteJidAlt": "551990001488@s.whatsapp.net",
        "fromMe": false,
        "id": "MENSAGEM-LID-LEGADO"
      },
      "message": {
        "conversation": "mensagem pelo JID legado"
      }
    }
    """);

var traduzidaDoLidLegado = TradutorDePayload.LerMensagem(payloadLidLegado.RootElement);
Checar(
    "remoteJidAlt legado do contato autorizado atravessa a allowlist",
    traduzidaDoLidLegado?.TelefoneE164 == "551990001488"
    && canalDeTeste.NumeroPermitido(traduzidaDoLidLegado.TelefoneE164));

using var payloadComDispositivo = JsonDocument.Parse("""
    {
      "key": {
        "remoteJid": "5519990001488:12@s.whatsapp.net",
        "fromMe": false,
        "id": "MENSAGEM-DISPOSITIVO"
      },
      "message": {
        "conversation": "mensagem de aparelho vinculado"
      }
    }
    """);

Checar(
    "Sufixo de dispositivo do JID nao e concatenado ao telefone",
    TradutorDePayload.LerMensagem(payloadComDispositivo.RootElement)?.TelefoneE164
        == "5519990001488");

using var payloadLidLegadoComDispositivo = JsonDocument.Parse("""
    {
      "key": {
        "remoteJid": "116909060165867@lid",
        "remoteJidAlt": "551990001488:12@s.whatsapp.net",
        "fromMe": false,
        "id": "MENSAGEM-LID-LEGADO-DISPOSITIVO"
      },
      "message": {
        "conversation": "mensagem pelo JID legado multi-dispositivo"
      }
    }
    """);

Checar(
    "remoteJidAlt legado remove sufixo numerico de dispositivo",
    TradutorDePayload.LerMensagem(payloadLidLegadoComDispositivo.RootElement)?.TelefoneE164
        == "551990001488");

using var payloadComDispositivoInvalido = JsonDocument.Parse("""
    {
      "key": {
        "remoteJid": "5519990001488:abc@s.whatsapp.net",
        "fromMe": false,
        "id": "MENSAGEM-DISPOSITIVO-INVALIDO"
      },
      "message": {
        "conversation": "nao deve entrar"
      }
    }
    """);

Checar(
    "Sufixo de dispositivo nao numerico e rejeitado",
    TradutorDePayload.LerMensagem(payloadComDispositivoInvalido.RootElement) is null);

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

// O GET de situacao precisa ser uma leitura pura. Estes cenarios exercitam o
// cliente HTTP real contra respostas controladas, sem abrir conexao externa.
var opcoesGateway = Options.Create(new OpcoesDoCanalWhatsApp
{
    Habilitado = true,
    BaseUrl = "https://evolution.invalid",
    ApiKey = "api-key-fake",
    NomeDaInstancia = "cartorio-teste",
    TokenDoWebhook = "token-fake"
});

var handlerConsulta = new EvolutionHttpHandlerFake(
    new RespostaHttpFake(
        HttpStatusCode.OK,
        """{"instance":{"instanceName":"cartorio-teste","state":"close"}}"""));
using var httpConsulta = new HttpClient(handlerConsulta);
var evolutionConsulta = new EvolutionClient(
    httpConsulta,
    opcoesGateway,
    NullLogger<EvolutionClient>.Instance);
var estadoFechado = await evolutionConsulta.ObterEstadoAsync(CancellationToken.None);

Checar(
    "Consulta de estado fechado faz somente leitura e nunca pede QR",
    estadoFechado.Estado == "close"
    && estadoFechado.QrCodeBase64 is null
    && handlerConsulta.Chamadas.Count == 1
    && handlerConsulta.Chamadas[0].Caminho
        == "/instance/connectionState/cartorio-teste");

var handlerEstadoInvalido = new EvolutionHttpHandlerFake(
    new RespostaHttpFake(HttpStatusCode.OK, """{"instance":{"status":"open"}}"""));
using var httpEstadoInvalido = new HttpClient(handlerEstadoInvalido);
var evolutionEstadoInvalido = new EvolutionClient(
    httpEstadoInvalido,
    opcoesGateway,
    NullLogger<EvolutionClient>.Instance);
var rejeitouEstadoInvalido = false;

try
{
    await evolutionEstadoInvalido.IniciarConexaoAsync(CancellationToken.None);
}
catch (CanalWhatsAppServiceException)
{
    rejeitouEstadoInvalido = true;
}

Checar(
    "Resposta sem state falha fechada e nao e convertida em novo connect",
    rejeitouEstadoInvalido && handlerEstadoInvalido.Chamadas.Count == 1);

var handlerConectando = new EvolutionHttpHandlerFake(
    new RespostaHttpFake(
        HttpStatusCode.OK,
        """{"instance":{"instanceName":"cartorio-teste","state":"connecting"}}"""));
using var httpConectando = new HttpClient(handlerConectando);
var evolutionConectando = new EvolutionClient(
    httpConectando,
    opcoesGateway,
    NullLogger<EvolutionClient>.Instance);
var estadoConectando = await evolutionConectando.IniciarConexaoAsync(CancellationToken.None);

Checar(
    "Comando explicito nao substitui uma sessao que ja esta conectando",
    estadoConectando.Estado == "connecting"
    && handlerConectando.Chamadas.Count == 1
    && handlerConectando.Chamadas.All(c =>
        !c.Caminho.StartsWith("/instance/connect/", StringComparison.Ordinal)));

var handlerInicio = new EvolutionHttpHandlerFake(
    new RespostaHttpFake(
        HttpStatusCode.OK,
        """{"instance":{"instanceName":"cartorio-teste","state":"close"}}"""),
    new RespostaHttpFake(
        HttpStatusCode.OK,
        """{"base64":"data:image/png;base64,QR-UNICO"}"""));
using var httpInicio = new HttpClient(handlerInicio);
var evolutionInicio = new EvolutionClient(
    httpInicio,
    opcoesGateway,
    NullLogger<EvolutionClient>.Instance);
var estadoIniciado = await evolutionInicio.IniciarConexaoAsync(CancellationToken.None);

Checar(
    "Comando explicito pede exatamente um QR quando a instancia esta fechada",
    estadoIniciado.Estado == "connecting"
    && estadoIniciado.QrCodeBase64 == "data:image/png;base64,QR-UNICO"
    && handlerInicio.Chamadas.Count(c =>
        c.Caminho.StartsWith("/instance/connect/", StringComparison.Ordinal)) == 1);

const string urlWebhookFake =
    "https://testecrm.example/api/whatsapp/webhook?token=token-fake";

// Incidente ao vivo em 2026-08-22: repetir instance/create com a sessao ja
// aberta forcou um LOGOUT de verdade no gateway. Instancia existente agora
// NUNCA chama instance/create — so consulta, confirma webhook e le o estado.
var handlerGarantia = new EvolutionHttpHandlerFake(
    new RespostaHttpFake(
        HttpStatusCode.OK,
        """{"instance":{"instanceName":"cartorio-teste","state":"open"}}"""),
    new RespostaHttpFake(HttpStatusCode.OK),
    new RespostaHttpFake(
        HttpStatusCode.OK,
        $$"""{"enabled":true,"url":"{{urlWebhookFake}}"}"""),
    new RespostaHttpFake(
        HttpStatusCode.OK,
        """{"instance":{"instanceName":"cartorio-teste","state":"open"}}"""));
using var httpGarantia = new HttpClient(handlerGarantia);
var evolutionGarantia = new EvolutionClient(
    httpGarantia,
    opcoesGateway,
    NullLogger<EvolutionClient>.Instance);
var estadoGarantido = await evolutionGarantia.GarantirInstanciaAsync(
    urlWebhookFake,
    CancellationToken.None);

Checar(
    "Instancia existente confirma webhook sem nunca chamar instance/create",
    estadoGarantido.Conectado
    && handlerGarantia.Chamadas.Count == 4
    && handlerGarantia.Chamadas[0].Caminho == "/instance/connectionState/cartorio-teste"
    && handlerGarantia.Chamadas.All(c => c.Caminho != "/instance/create"));

var handlerCriacao = new EvolutionHttpHandlerFake(
    new RespostaHttpFake(HttpStatusCode.NotFound, "{}"),
    new RespostaHttpFake(HttpStatusCode.OK),
    new RespostaHttpFake(HttpStatusCode.OK),
    new RespostaHttpFake(
        HttpStatusCode.OK,
        $$"""{"enabled":true,"url":"{{urlWebhookFake}}"}"""),
    new RespostaHttpFake(
        HttpStatusCode.OK,
        """{"instance":{"instanceName":"cartorio-teste","state":"close"}}"""));
using var httpCriacao = new HttpClient(handlerCriacao);
var evolutionCriacao = new EvolutionClient(
    httpCriacao,
    opcoesGateway,
    NullLogger<EvolutionClient>.Instance);
var estadoCriado = await evolutionCriacao.GarantirInstanciaAsync(
    urlWebhookFake,
    CancellationToken.None);

Checar(
    "Instancia inexistente (404) e criada sem QR antes do webhook",
    !estadoCriado.Conectado
    && handlerCriacao.Chamadas.Count == 5
    && handlerCriacao.Chamadas[0].Caminho == "/instance/connectionState/cartorio-teste"
    && handlerCriacao.Chamadas[1].Caminho == "/instance/create"
    && handlerCriacao.Chamadas[1].Corpo?.Contains(
        "\"qrcode\":false",
        StringComparison.Ordinal) == true);

var handlerWebhookRecusado = new EvolutionHttpHandlerFake(
    new RespostaHttpFake(
        HttpStatusCode.OK,
        """{"instance":{"instanceName":"cartorio-teste","state":"open"}}"""),
    new RespostaHttpFake(HttpStatusCode.InternalServerError, "{}"));
using var httpWebhookRecusado = new HttpClient(handlerWebhookRecusado);
var evolutionWebhookRecusado = new EvolutionClient(
    httpWebhookRecusado,
    opcoesGateway,
    NullLogger<EvolutionClient>.Instance);
var recusouWebhook = false;

try
{
    await evolutionWebhookRecusado.GarantirInstanciaAsync(
        urlWebhookFake,
        CancellationToken.None);
}
catch (CanalWhatsAppServiceException)
{
    recusouWebhook = true;
}

Checar(
    "Falha ao registrar webhook interrompe o fluxo antes de consultar ou conectar",
    recusouWebhook && handlerWebhookRecusado.Chamadas.Count == 2);

var nomeBancoPareamento = $"verificar-pareamento-{Guid.NewGuid():N}";
var opcoesDoPareamento = new DbContextOptionsBuilder<CrmDbContext>()
    .UseInMemoryDatabase(nomeBancoPareamento)
    .Options;
await using var dbPareamento = new CrmDbContext(opcoesDoPareamento);
var tenantPareamento = new Tenant
{
    RazaoSocial = "Tenant do pareamento",
    Cnpj = "11111111111111",
    WhatsappInstancia = "cartorio-piloto"
};
dbPareamento.Tenants.Add(tenantPareamento);
await dbPareamento.SaveChangesAsync();

var gatewayPareamentoAberto = new GatewayFake
{
    EstadoAoGarantir = new EstadoDaInstancia("open", null)
};
var pareamentoAberto = new PareamentoDoCanal(
    dbPareamento,
    gatewayPareamentoAberto,
    Options.Create(canalDeTeste),
    NullLogger<PareamentoDoCanal>.Instance);

var situacaoDesvinculada = await pareamentoAberto.ConsultarAsync(tenantPareamento.Id);
Checar(
    "GET com tenant de outro ambiente nao toca o gateway",
    situacaoDesvinculada.Estado == "nao_vinculado"
    && !situacaoDesvinculada.Conectado
    && gatewayPareamentoAberto.ConsultasDeEstado == 0
    && gatewayPareamentoAberto.IniciosDeConexao == 0);

var situacaoAberta = await pareamentoAberto.PrepararAsync(
    tenantPareamento.Id,
    "https://testecrm.example");
Checar(
    "POST recupera vinculo de sessao aberta sem gerar novo QR",
    situacaoAberta.Conectado
    && tenantPareamento.WhatsappInstancia == "cartorio-teste"
    && gatewayPareamentoAberto.GarantiasDeInstancia == 1
    && gatewayPareamentoAberto.IniciosDeConexao == 0);

var situacaoConsultada = await pareamentoAberto.ConsultarAsync(tenantPareamento.Id);
Checar(
    "GET vinculado consulta estado uma vez e nunca inicia conexao",
    situacaoConsultada.Conectado
    && gatewayPareamentoAberto.ConsultasDeEstado == 1
    && gatewayPareamentoAberto.IniciosDeConexao == 0);

tenantPareamento.WhatsappInstancia = "cartorio-piloto";
await dbPareamento.SaveChangesAsync();
var vinculoPersistidoAntesDoQr = false;
var gatewayPareamentoFechado = new GatewayFake
{
    EstadoAoGarantir = new EstadoDaInstancia("close", null),
    AoIniciar = () =>
    {
        using var verificacao = new CrmDbContext(opcoesDoPareamento);
        vinculoPersistidoAntesDoQr = verificacao.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantPareamento.Id)
            .Select(t => t.WhatsappInstancia)
            .Single() == "cartorio-teste";
    }
};
var pareamentoFechado = new PareamentoDoCanal(
    dbPareamento,
    gatewayPareamentoFechado,
    Options.Create(canalDeTeste),
    NullLogger<PareamentoDoCanal>.Instance);
var situacaoComQr = await pareamentoFechado.PrepararAsync(
    tenantPareamento.Id,
    "https://testecrm.example");

Checar(
    "QR so e iniciado depois de o vinculo estar persistido",
    vinculoPersistidoAntesDoQr
    && situacaoComQr.Estado == "connecting"
    && situacaoComQr.QrCodeBase64 is not null
    && gatewayPareamentoFechado.IniciosDeConexao == 1);

tenantPareamento.WhatsappInstancia = "cartorio-piloto";
await dbPareamento.SaveChangesAsync();
var gatewayPareamentoComFalha = new GatewayFake { FalharAoGarantir = true };
var pareamentoComFalha = new PareamentoDoCanal(
    dbPareamento,
    gatewayPareamentoComFalha,
    Options.Create(canalDeTeste),
    NullLogger<PareamentoDoCanal>.Instance);
var falhouAntesDoVinculo = false;

try
{
    await pareamentoComFalha.PrepararAsync(
        tenantPareamento.Id,
        "https://testecrm.example");
}
catch (CanalWhatsAppServiceException)
{
    falhouAntesDoVinculo = true;
}

Checar(
    "Falha na garantia do webhook preserva vinculo antigo e nao inicia QR",
    falhouAntesDoVinculo
    && tenantPareamento.WhatsappInstancia == "cartorio-piloto"
    && gatewayPareamentoComFalha.IniciosDeConexao == 0);

var gatewayTenantAusente = new GatewayFake();
var pareamentoTenantAusente = new PareamentoDoCanal(
    dbPareamento,
    gatewayTenantAusente,
    Options.Create(canalDeTeste),
    NullLogger<PareamentoDoCanal>.Instance);
var rejeitouTenantAusente = false;

try
{
    await pareamentoTenantAusente.PrepararAsync(
        Guid.NewGuid(),
        "https://testecrm.example");
}
catch (CanalWhatsAppServiceException)
{
    rejeitouTenantAusente = true;
}

Checar(
    "Tenant ausente falha antes de qualquer chamada externa",
    rejeitouTenantAusente
    && gatewayTenantAusente.GarantiasDeInstancia == 0
    && gatewayTenantAusente.IniciosDeConexao == 0);

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

file sealed record RespostaHttpFake(HttpStatusCode Status, string? Corpo = null);

file sealed record ChamadaHttpFake(HttpMethod Metodo, string Caminho, string? Corpo);

file sealed class EvolutionHttpHandlerFake(params RespostaHttpFake[] respostas)
    : HttpMessageHandler
{
    private readonly Queue<RespostaHttpFake> _respostas = new(respostas);

    public List<ChamadaHttpFake> Chamadas { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var corpo = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        Chamadas.Add(new ChamadaHttpFake(
            request.Method,
            request.RequestUri?.AbsolutePath ?? string.Empty,
            corpo));

        if (_respostas.Count == 0)
        {
            throw new InvalidOperationException("O cliente HTTP fez uma chamada inesperada.");
        }

        var proxima = _respostas.Dequeue();

        return new HttpResponseMessage(proxima.Status)
        {
            Content = new StringContent(
                proxima.Corpo ?? string.Empty,
                Encoding.UTF8,
                "application/json")
        };
    }
}

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
    public bool FalharAoGarantir { get; init; }
    public EstadoDaInstancia EstadoAoGarantir { get; init; } = new("open", null);
    public EstadoDaInstancia EstadoAoConsultar { get; init; } = new("open", null);
    public EstadoDaInstancia EstadoAoIniciar { get; init; } =
        new("connecting", "data:image/png;base64,FAKE");
    public Action? AoIniciar { get; init; }
    public int GarantiasDeInstancia { get; private set; }
    public int ConsultasDeEstado { get; private set; }
    public int IniciosDeConexao { get; private set; }
    public int ConsultasDeFoto { get; private set; }
    public int DownloadsDeMidia { get; private set; }
    public int EnviosDeTexto { get; private set; }
    public int EnviosDeMidia { get; private set; }

    public Task<EstadoDaInstancia> GarantirInstanciaAsync(string urlDoWebhook, CancellationToken ct)
    {
        GarantiasDeInstancia++;

        if (FalharAoGarantir)
        {
            throw CanalWhatsAppServiceException.GatewayIndisponivel();
        }

        return Task.FromResult(EstadoAoGarantir);
    }

    public Task<EstadoDaInstancia> ObterEstadoAsync(CancellationToken ct)
    {
        ConsultasDeEstado++;
        return Task.FromResult(EstadoAoConsultar);
    }

    public Task<EstadoDaInstancia> IniciarConexaoAsync(CancellationToken ct)
    {
        IniciosDeConexao++;
        AoIniciar?.Invoke();
        return Task.FromResult(EstadoAoIniciar);
    }

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
