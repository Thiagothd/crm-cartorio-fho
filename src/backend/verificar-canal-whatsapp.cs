#:project CrmCartorio.Api/CrmCartorio.Api.csproj
#:property PublishAot=false
#:package Microsoft.EntityFrameworkCore.InMemory@10.0.9

using CrmCartorio.Api.Infrastructure.Auditoria;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using CrmCartorio.Api.Modules.Atendimento.Application;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.CanalWhatsApp.Application;
using CrmCartorio.Api.Modules.CanalWhatsApp.Domain.Entities;
using CrmCartorio.Api.Modules.CanalWhatsApp.Domain.Enums;
using CrmCartorio.Api.Modules.Clientes.Application;
using CrmCartorio.Api.Modules.Clientes.Domain.Entities;
using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
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

// Regressao do bug ao vivo em 2026-08-22: ownerJid do fetchInstances devolveu
// "553898000901" (forma legada) para o numero real "+55 38 99800-0901", e o
// perfil pareado apareceu na tela com um digito a menos.
Checar(
    "Canonicalizar completa o nono digito da forma legada",
    NormalizadorDeTelefone.CanonicalizarMovelBrasileiro("553898000901") == "5538998000901");
Checar(
    "Canonicalizar nao mexe num numero ja canonico",
    NormalizadorDeTelefone.CanonicalizarMovelBrasileiro("5538998000901") == "5538998000901");
Checar(
    "Canonicalizar nao mexe em DDD brasileiro inexistente",
    NormalizadorDeTelefone.CanonicalizarMovelBrasileiro("550098000901") == "550098000901");
Checar(
    "Canonicalizar nao mexe em numero de 12 digitos fora do Brasil (nao comeca com 55)",
    NormalizadorDeTelefone.CanonicalizarMovelBrasileiro("441234567890") == "441234567890");

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

var (provedorDeChave, cifra) = FabricaDeCifraDeDados.CriarParDormente();
var nomeBancoPareamento = $"verificar-pareamento-{Guid.NewGuid():N}";
var opcoesDoPareamento = new DbContextOptionsBuilder<CrmDbContext>()
    .UseInMemoryDatabase(nomeBancoPareamento)
    .Options;
await using var dbPareamento = new CrmDbContext(opcoesDoPareamento, cifra);
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
    new RegrasDoCanal(dbPareamento, new ContextoFake(tenantPareamento.Id), Options.Create(canalDeTeste), NullLogger<RegrasDoCanal>.Instance),
    new ControleDeTentativasDePareamento(),
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
        using var verificacao = new CrmDbContext(opcoesDoPareamento, cifra);
        vinculoPersistidoAntesDoQr = verificacao.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantPareamento.Id)
            .Select(t => t.WhatsappInstancia)
            .Single() == "cartorio-teste";
    }
};
var pareamentoFechado = new PareamentoDoCanal(
    dbPareamento,
    gatewayPareamentoFechado,
    new RegrasDoCanal(dbPareamento, new ContextoFake(tenantPareamento.Id), Options.Create(canalDeTeste), NullLogger<RegrasDoCanal>.Instance),
    new ControleDeTentativasDePareamento(),
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
    new RegrasDoCanal(dbPareamento, new ContextoFake(tenantPareamento.Id), Options.Create(canalDeTeste), NullLogger<RegrasDoCanal>.Instance),
    new ControleDeTentativasDePareamento(),
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
    new RegrasDoCanal(dbPareamento, new ContextoFake(tenantPareamento.Id), Options.Create(canalDeTeste), NullLogger<RegrasDoCanal>.Instance),
    new ControleDeTentativasDePareamento(),
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

await using var db = new CrmDbContext(opcoesDoBanco, cifra);

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
var regrasDoProcessador = new RegrasDoCanal(
    db, new ContextoFake(tenant.Id), Options.Create(canalDeTeste), NullLogger<RegrasDoCanal>.Instance);
var processador = new ProcessadorDeWebhook(
    db,
    recebimentoFake,
    gatewayFake,
    regrasDoProcessador,
    new IdentificadorDeEco(db, NullLogger<IdentificadorDeEco>.Instance),
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
var regrasDoProcessadorDesligado = new RegrasDoCanal(
    db, new ContextoFake(tenant.Id), Options.Create(canalDesligado), NullLogger<RegrasDoCanal>.Instance);
var processadorDesligado = new ProcessadorDeWebhook(
    db,
    recebimentoFake,
    gatewayFake,
    regrasDoProcessadorDesligado,
    new IdentificadorDeEco(db, NullLogger<IdentificadorDeEco>.Instance),
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

// CORRECAO (achado ao vivo, 2026-09-09): "Somente os liberados" e "Todos,
// menos os bloqueados" comparavam por igualdade exata de digitos. Uma
// atendente cadastrada com o numero completo (13 digitos, com o nono) teve
// mensagens dela mesma descartadas como "fora da lista" so' porque o
// WhatsApp entregou o remetente na forma legada (12 digitos, sem o nono).
// Estes cenarios chamam RegrasDoCanal.PodeFalarComAsync diretamente, no modo
// BANCO (Tenant.WhatsappModoDeRecebimento setado) — nao no fallback de
// appsettings ja coberto acima.
tenant.WhatsappModoDeRecebimento = ModoDeRecebimento.SomenteLiberados;
db.CanalWhatsappNumeros.Add(new NumeroDoCanal
{
    TenantId = tenant.Id,
    Telefone = "5538988000271", // forma canonica, com o nono digito
    Tipo = TipoDeNumero.Liberado
});
await db.SaveChangesAsync();

var veredictoComNono = await regrasDoProcessador.PodeFalarComAsync(tenant.Id, "5538988000271");
var veredictoSemNono = await regrasDoProcessador.PodeFalarComAsync(tenant.Id, "553888000271");
var veredictoOutroNumero = await regrasDoProcessador.PodeFalarComAsync(tenant.Id, "5538999990000");

Checar(
    "Somente liberados: numero cadastrado com o nono digito passa na forma canonica",
    veredictoComNono.Permitido);
Checar(
    "Somente liberados: MESMO numero, entregue sem o nono digito, tambem passa (o bug)",
    veredictoSemNono.Permitido);
Checar(
    "Somente liberados: numero de fato diferente continua descartado como fora da lista",
    !veredictoOutroNumero.Permitido && veredictoOutroNumero.Motivo == MotivoDoDescarte.ForaDaLista);

tenant.WhatsappModoDeRecebimento = ModoDeRecebimento.TodosMenosBloqueados;
const string bloqueadoCanonico = "5538974000711"; // forma canonica, com o nono digito
var bloqueadoFormaLegada = bloqueadoCanonico.Remove(4, 1); // mesmo numero, sem o nono
db.CanalWhatsappNumeros.Add(new NumeroDoCanal
{
    TenantId = tenant.Id,
    Telefone = bloqueadoCanonico,
    Tipo = TipoDeNumero.Bloqueado
});
await db.SaveChangesAsync();

var veredictoBloqueadoCanonico = await regrasDoProcessador.PodeFalarComAsync(tenant.Id, bloqueadoCanonico);
var veredictoBloqueadoFormaLegada = await regrasDoProcessador.PodeFalarComAsync(tenant.Id, bloqueadoFormaLegada);
var veredictoNaoBloqueado = await regrasDoProcessador.PodeFalarComAsync(tenant.Id, "5538999990000");

Checar(
    "Todos menos bloqueados: numero bloqueado na forma canonica e recusado",
    !veredictoBloqueadoCanonico.Permitido && veredictoBloqueadoCanonico.Motivo == MotivoDoDescarte.Bloqueado);
Checar(
    "Todos menos bloqueados: MESMO numero bloqueado, entregue sem o nono digito, tambem e recusado",
    !veredictoBloqueadoFormaLegada.Permitido && veredictoBloqueadoFormaLegada.Motivo == MotivoDoDescarte.Bloqueado);
Checar(
    "Todos menos bloqueados: numero nao bloqueado continua passando",
    veredictoNaoBloqueado.Permitido);

tenant.WhatsappModoDeRecebimento = null;
await db.SaveChangesAsync();

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
    regrasDoProcessador,
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

// O caminho autorizado agora depende de ExecuteUpdate atomico. EF InMemory nao
// implementa essa operacao; a prova de envio e concorrencia usa SQL Server real
// em verificar-concorrencia-saida-whatsapp.cs. Este verificador continua
// responsável pelo gate que impede qualquer efeito externo quando ha bloqueio.

var gatewayDeFotos = new GatewayFake();
using var cache = new MemoryCache(new MemoryCacheOptions());
var clienteService = new ClienteService(
    db,
    new ContextoFake(tenant.Id),
    gatewayDeFotos,
    new HttpClientFactoryFake(),
    cache,
    Options.Create(canalDeTeste),
    new RegistradorDeAcesso(db, new ContextoFake(tenant.Id), new HttpContextAccessor(), cache),
    provedorDeChave, new IndiceCego(provedorDeChave),
    NullLogger<ClienteService>.Instance);

var fotoBloqueada = await clienteService.ObterFotoPerfilAsync(clienteBloqueado.Id);
Checar(
    "Ficha fora da allowlist não consulta foto no gateway",
    fotoBloqueada is null && gatewayDeFotos.ConsultasDeFoto == 0);

var fotoPermitida = await clienteService.ObterFotoPerfilAsync(clientePermitido.Id);
Checar(
    "Ficha do contato autorizado pode consultar foto no gateway",
    fotoPermitida is null && gatewayDeFotos.ConsultasDeFoto == 1);

// ---------------------------------------------------------------------------
// Correcao e apagamento de mensagem JA ENTREGUE (2026-09-22).
//
// O que se prova aqui e o lado do CANAL: quando o adaptador chega ao gateway,
// quando nao chega, e com que texto. As travas de dominio (janela de 15
// minutos, so texto, so o autor) vivem no MensagemService e nao dependem de
// canal nenhum.
//
// A assimetria entre as duas acoes e deliberada e e o cenario mais importante
// do bloco: corrigir ENTREGA texto novo ao aparelho do cliente e por isso passa
// pela regra do canal; apagar RETIRA o que ja esta la e por isso nao passa —
// senao um numero que saiu da allowlist depois do envio ficaria com a mensagem
// errada na tela para sempre.
// ---------------------------------------------------------------------------

var gatewayDeCorrecao = new GatewayFake();
var enviadorDeCorrecao = new EnviadorPeloWhatsApp(
    db,
    gatewayDeCorrecao,
    anexosFake,
    regrasDoProcessador,
    Options.Create(canalDeTeste),
    NullLogger<EnviadorPeloWhatsApp>.Instance);

var atendenteDaCorrecao = new Atendente
{
    TenantId = tenant.Id,
    Nome = "Maria Rezende",
    Email = "maria.correcao@cartoriogameleiras.com.br",
    SenhaHash = "x",
    Perfil = PerfilDeAtendente.Atendente
};

var mensagemEntregue = new Mensagem
{
    TenantId = tenant.Id,
    ConversaId = conversaPermitida.Id,
    AutorTipo = AutorTipo.Atendente,
    AtendenteId = atendenteDaCorrecao.Id,
    Conteudo = "A certidão fica pronta na terça.",
    StatusEntrega = StatusEntrega.Entregue,
    IdExternoWhatsapp = "WA-ENTREGUE-1"
};

// Mesma mensagem, mas na conversa do contato que esta FORA da allowlist.
var mensagemEntregueBloqueada = new Mensagem
{
    TenantId = tenant.Id,
    ConversaId = conversaBloqueada.Id,
    AutorTipo = AutorTipo.Atendente,
    AtendenteId = atendenteDaCorrecao.Id,
    Conteudo = "Texto que saiu errado.",
    StatusEntrega = StatusEntrega.Entregue,
    IdExternoWhatsapp = "WA-ENTREGUE-2"
};

// Nunca chegou ao gateway: sem id externo nao ha o que corrigir la fora.
var mensagemSemIdExterno = new Mensagem
{
    TenantId = tenant.Id,
    ConversaId = conversaPermitida.Id,
    AutorTipo = AutorTipo.Atendente,
    AtendenteId = atendenteDaCorrecao.Id,
    Conteudo = "Ainda na fila.",
    StatusEntrega = StatusEntrega.Pendente
};

db.AddRange(atendenteDaCorrecao, mensagemEntregue, mensagemEntregueBloqueada, mensagemSemIdExterno);
await db.SaveChangesAsync();

var correcaoPermitida = await enviadorDeCorrecao.CorrigirAsync(
    mensagemEntregue.Id, "A certidão fica pronta na quinta.");

Checar(
    "Correcao chega ao gateway com o id externo da mensagem original",
    correcaoPermitida == ResultadoNoCanal.Feito
    && gatewayDeCorrecao.Edicoes == 1
    && gatewayDeCorrecao.IdDaUltimaEdicao == "WA-ENTREGUE-1");

Checar(
    "Texto corrigido sai assinado com quem ESCREVEU, nao com quem corrigiu",
    gatewayDeCorrecao.TextoDaUltimaEdicao == "*Maria Rezende*\nA certidão fica pronta na quinta.");

var correcaoBloqueada = await enviadorDeCorrecao.CorrigirAsync(
    mensagemEntregueBloqueada.Id, "tentativa de corrigir quem esta fora da regra");

Checar(
    "Correcao para numero fora da regra do canal nao chega ao gateway",
    correcaoBloqueada == ResultadoNoCanal.NaoSeAplica && gatewayDeCorrecao.Edicoes == 1);

var apagamentoBloqueado = await enviadorDeCorrecao.ApagarParaTodosAsync(mensagemEntregueBloqueada.Id);

Checar(
    "Apagar NAO passa pela regra do canal: retirar o que ja esta no aparelho continua possivel",
    apagamentoBloqueado == ResultadoNoCanal.Feito
    && gatewayDeCorrecao.Apagamentos == 1
    && gatewayDeCorrecao.IdDoUltimoApagamento == "WA-ENTREGUE-2");

var correcaoSemId = await enviadorDeCorrecao.CorrigirAsync(mensagemSemIdExterno.Id, "qualquer coisa");

Checar(
    "Mensagem sem id externo nao chega ao gateway (nao ha o que corrigir la fora)",
    correcaoSemId == ResultadoNoCanal.NaoSeAplica && gatewayDeCorrecao.Edicoes == 1);

// Conversa de demonstracao: o MESMO gate do envio. Sem isto, corrigir uma das 11
// conversas semeadas tentaria falar com telefone ficticio — e alguns sao numeros
// reais de terceiros.
var clienteSimulado = new Cliente
{
    TenantId = tenant.Id,
    Nome = "Contato de demonstração",
    TelefoneWhatsapp = "5519990001488"
};
var conversaSimulada = new Conversa
{
    TenantId = tenant.Id,
    ClienteId = clienteSimulado.Id,
    Protocolo = "TESTE-0003",
    Canal = CanalOrigem.Simulado
};
var mensagemSimulada = new Mensagem
{
    TenantId = tenant.Id,
    ConversaId = conversaSimulada.Id,
    AutorTipo = AutorTipo.Atendente,
    AtendenteId = atendenteDaCorrecao.Id,
    Conteudo = "mensagem de demonstração",
    StatusEntrega = StatusEntrega.Enviada,
    IdExternoWhatsapp = "WA-NUNCA-EXISTIU"
};

db.AddRange(clienteSimulado, conversaSimulada, mensagemSimulada);
await db.SaveChangesAsync();

var correcaoSimulada = await enviadorDeCorrecao.CorrigirAsync(mensagemSimulada.Id, "texto novo");
var apagamentoSimulado = await enviadorDeCorrecao.ApagarParaTodosAsync(mensagemSimulada.Id);

Checar(
    "Conversa de demonstracao nao corrige nem apaga por WhatsApp nenhum",
    correcaoSimulada == ResultadoNoCanal.NaoSeAplica
    && apagamentoSimulado == ResultadoNoCanal.NaoSeAplica
    && gatewayDeCorrecao.Edicoes == 1
    && gatewayDeCorrecao.Apagamentos == 1);

// O WhatsApp recusando (janela vencida do lado dele) e resposta legitima, nao
// falha de sistema: vira Recusado, e o servico nao grava nada.
var gatewayQueRecusa = new GatewayFake { RecusarEdicao = true, RecusarApagamento = true };
var enviadorComRecusa = new EnviadorPeloWhatsApp(
    db,
    gatewayQueRecusa,
    anexosFake,
    regrasDoProcessador,
    Options.Create(canalDeTeste),
    NullLogger<EnviadorPeloWhatsApp>.Instance);

var correcaoRecusada = await enviadorComRecusa.CorrigirAsync(mensagemEntregue.Id, "outro texto");
var apagamentoRecusado = await enviadorComRecusa.ApagarParaTodosAsync(mensagemEntregue.Id);

Checar(
    "Recusa do WhatsApp vira Recusado, nao excecao",
    correcaoRecusada == ResultadoNoCanal.Recusado && apagamentoRecusado == ResultadoNoCanal.Recusado);

// ---------------------------------------------------------------------------
// Resposta dada pelo celular (2026-09-23).
//
// A mensagem "enviada por mim" chega pelo webhook de duas origens que parecem
// iguais: o eco do que o PAINEL mandou, e a resposta que alguem da serventia deu
// direto pelo celular. Confundir o eco com resposta desligaria o robo sozinho —
// toda mensagem automatica viraria "atendimento assumido pelo celular". Por isso
// o bloco prova, antes de tudo, que eco NUNCA vira resposta; e so depois que a
// resposta de verdade chega ao recebimento.
// ---------------------------------------------------------------------------

static JsonElement DoProprioNumero(string id, string telefone, JsonElement mensagem) =>
    JsonSerializer.SerializeToElement(new
    {
        key = new { remoteJid = $"{telefone}@s.whatsapp.net", fromMe = true, id },
        pushName = "Cartório de Gameleiras",
        message = mensagem
    });

static JsonElement TextoSimples(string texto) =>
    JsonSerializer.SerializeToElement(new { conversation = texto });

// --- Tradutor --------------------------------------------------------------

var payloadDoCelular = DoProprioNumero("CEL-TRADUTOR", "5519990001488", TextoSimples("Oi, já estou vendo aqui."));
var lidaPeloCaminhoDoCliente = TradutorDePayload.LerMensagem(payloadDoCelular);
var lidaPeloCaminhoDoAparelho = TradutorDePayload.LerMensagemEnviadaPeloAparelho(payloadDoCelular);

Checar(
    "Mensagem do proprio numero continua fora do caminho do cliente (LerMensagem devolve null)",
    lidaPeloCaminhoDoCliente is null);

Checar(
    "Mensagem do proprio numero e lida pelo caminho do aparelho, com o contato como destinatario",
    lidaPeloCaminhoDoAparelho is { TelefoneE164: "5519990001488", Conteudo: "Oi, já estou vendo aqui.", IdExterno: "CEL-TRADUTOR" });

Checar(
    "O pushName da mensagem do proprio numero (perfil do CARTORIO) nunca vira nome de cliente",
    lidaPeloCaminhoDoAparelho is { NomeDoPerfil: null });

var payloadDoClienteComum = JsonSerializer.SerializeToElement(new
{
    key = new { remoteJid = "5519990001488@s.whatsapp.net", fromMe = false, id = "CLIENTE-COMUM" },
    pushName = "Cliente",
    message = new { conversation = "Bom dia" }
});

Checar(
    "Mensagem do cliente nao entra pelo caminho do aparelho",
    TradutorDePayload.LerMensagemEnviadaPeloAparelho(payloadDoClienteComum) is null);

var edicaoFeitaNoCelular = DoProprioNumero(
    "CEL-EDICAO",
    "5519990001488",
    JsonSerializer.SerializeToElement(new
    {
        protocolMessage = new { type = 14, editedMessage = new { conversation = "texto editado" } }
    }));

Checar(
    "Edicao ou apagamento feitos no celular (protocolMessage) nao viram mensagem",
    TradutorDePayload.LerMensagemEnviadaPeloAparelho(edicaoFeitaNoCelular) is null);

var paraGrupo = JsonSerializer.SerializeToElement(new
{
    key = new { remoteJid = "120363000000000000@g.us", fromMe = true, id = "CEL-GRUPO" },
    message = new { conversation = "mensagem no grupo" }
});

Checar(
    "Mensagem do proprio numero para grupo nao vira atendimento",
    TradutorDePayload.LerMensagemEnviadaPeloAparelho(paraGrupo) is null);

// --- Roteamento no webhook -------------------------------------------------

tenant.WhatsappNumeroExibicao = "+55 38 99977-8910";
await db.SaveChangesAsync();

var respostasAntes = recebimentoFake.RespostasPeloAparelho;
var recebimentosAntes = recebimentoFake.Recebimentos;

await processador.ProcessarAsync(new PayloadDoWebhook(
    "messages.upsert", "cartorio-teste",
    DoProprioNumero("CEL-SI-MESMO", "5538999778910", TextoSimples("lembrete para mim"))));

Checar(
    "Conversa consigo mesmo (\"mensagem para mim\") nao vira atendimento",
    recebimentoFake.RespostasPeloAparelho == respostasAntes);

await processador.ProcessarAsync(new PayloadDoWebhook(
    "messages.upsert", "cartorio-teste",
    DoProprioNumero("CEL-FORA-DA-REGRA", "5538991234567", TextoSimples("para quem nao esta liberado"))));

Checar(
    "Resposta pelo celular para numero fora da regra do canal nao entra no painel",
    recebimentoFake.RespostasPeloAparelho == respostasAntes);

// O eco de uma mensagem que o painel ja registrou (mesmo id).
await processador.ProcessarAsync(new PayloadDoWebhook(
    "messages.upsert", "cartorio-teste",
    DoProprioNumero("WA-ENTREGUE-1", "5519990001488", TextoSimples("*Maria Rezende*\nA certidão fica pronta na quinta."))));

Checar(
    "Eco de mensagem que o painel ja registrou nao vira resposta pelo celular",
    recebimentoFake.RespostasPeloAparelho == respostasAntes);

await processador.ProcessarAsync(new PayloadDoWebhook(
    "messages.upsert", "cartorio-teste",
    DoProprioNumero("CEL-TESTE-DE-ENVIO", "5519990001488", TextoSimples(PareamentoDoCanal.TextoDoTeste))));

Checar(
    "Eco do \"testar envio\" da tela do canal (unico envio sem linha no fio) nao vira atendimento",
    recebimentoFake.RespostasPeloAparelho == respostasAntes);

await processador.ProcessarAsync(new PayloadDoWebhook(
    "messages.upsert", "cartorio-teste",
    DoProprioNumero("CEL-DE-VERDADE", "5519990001488", TextoSimples("Oi! Aqui é o cartório parceiro, já vou te ajudar."))));

Checar(
    "Resposta de verdade pelo celular chega ao recebimento, sem nome de perfil e sem passar pelo caminho do cliente",
    recebimentoFake.RespostasPeloAparelho == respostasAntes + 1
    && recebimentoFake.Recebimentos == recebimentosAntes
    && recebimentoFake.UltimaPeloAparelho is
    {
        TelefoneE164: "5519990001488",
        IdExterno: "CEL-DE-VERDADE",
        NomeDoPerfil: null,
        Conteudo: "Oi! Aqui é o cartório parceiro, já vou te ajudar."
    });

// --- Identificador de eco: a janela em que o painel ainda nao gravou o id ----
//
// Contexto proprio para o identificador: o cenario de confirmacao durante a
// espera escreve pelo `db` enquanto o identificador le.

await using var dbDoIdentificador = new CrmDbContext(opcoesDoBanco, cifra);
var identificador = new IdentificadorDeEco(
    dbDoIdentificador,
    NullLogger<IdentificadorDeEco>.Instance,
    esperaMaxima: TimeSpan.FromMilliseconds(600),
    intervaloDaEspera: TimeSpan.FromMilliseconds(40),
    janelaDoEnvioEmVoo: TimeSpan.FromMinutes(2));

MensagemRecebidaDoCanal Eco(string id, string texto) =>
    TradutorDePayload.LerMensagemEnviadaPeloAparelho(DoProprioNumero(id, "5519990001488", TextoSimples(texto)))!;

Checar(
    "Sem nada saindo do painel, a mensagem do proprio numero e resposta pelo celular",
    await identificador.ClassificarAsync(tenant.Id, Eco("CEL-SEM-VOO", "qualquer resposta"))
        == OrigemDaMensagemDoProprioNumero.RespostaPeloAparelho);

// A fila de saida disparou e ainda nao gravou o id — o eco chega nessa janela.
var envioEmVoo = new Mensagem
{
    TenantId = tenant.Id,
    ConversaId = conversaPermitida.Id,
    AutorTipo = AutorTipo.Atendente,
    AtendenteId = atendenteDaCorrecao.Id,
    Conteudo = "Sua certidão está pronta.",
    StatusEntrega = StatusEntrega.Enviando,
    EnvioIniciadoEm = DateTime.UtcNow,
    EnvioDisparadoEm = DateTime.UtcNow
};
db.Mensagens.Add(envioEmVoo);
await db.SaveChangesAsync();

var cronometro = System.Diagnostics.Stopwatch.StartNew();
var ecoAntesDoId = await identificador.ClassificarAsync(
    tenant.Id, Eco("ECO-ANTES-DO-ID", "*Maria Rezende*\nSua certidão está pronta."));
cronometro.Stop();

Checar(
    "Eco que chega ANTES de o painel gravar o id e reconhecido pelo texto assinado, sem esperar",
    ecoAntesDoId == OrigemDaMensagemDoProprioNumero.JaEstaNoFio
    && cronometro.Elapsed < TimeSpan.FromMilliseconds(400));

// Texto diferente com envio em voo que NUNCA confirma: na duvida, e eco.
cronometro.Restart();
var duvidaQueNaoSeResolve = await identificador.ClassificarAsync(
    tenant.Id, Eco("DUVIDA-1", "texto que nao e o do envio em voo"));
cronometro.Stop();

Checar(
    "Envio em voo que nao confirma a tempo: a duvida vai para o lado seguro (eco), depois da espera",
    duvidaQueNaoSeResolve == OrigemDaMensagemDoProprioNumero.JaEstaNoFio
    && cronometro.Elapsed >= TimeSpan.FromMilliseconds(600));

// Texto diferente com envio em voo que confirma com OUTRO id durante a espera:
// agora da para saber — a mensagem do proprio numero nao era aquele eco.
var classificacaoEmCurso = identificador.ClassificarAsync(
    tenant.Id, Eco("CEL-DURANTE-O-VOO", "Oi, sou eu pelo celular"));

await Task.Delay(120);
envioEmVoo.StatusEntrega = StatusEntrega.Enviada;
envioEmVoo.IdExternoWhatsapp = "ID-DO-ENVIO-DO-PAINEL";
envioEmVoo.EnvioIniciadoEm = null;
envioEmVoo.EnvioDisparadoEm = null;
await db.SaveChangesAsync();

Checar(
    "Envio em voo confirmado com outro id durante a espera: a mensagem do proprio numero e resposta pelo celular",
    await classificacaoEmCurso == OrigemDaMensagemDoProprioNumero.RespostaPeloAparelho);

// O mesmo envio, agora confirmado, e o eco dele chegando atrasado: o id resolve.
Checar(
    "Eco que chega DEPOIS de o painel gravar o id e reconhecido pelo id",
    await identificador.ClassificarAsync(tenant.Id, Eco("ID-DO-ENVIO-DO-PAINEL", "*Maria Rezende*\nSua certidão está pronta."))
        == OrigemDaMensagemDoProprioNumero.JaEstaNoFio);

// Envio travado ha muito tempo (resultado ambiguo, WA-OUT-01): nao pode segurar
// toda resposta pelo celular dali em diante.
var envioTravado = new Mensagem
{
    TenantId = tenant.Id,
    ConversaId = conversaPermitida.Id,
    AutorTipo = AutorTipo.Atendente,
    AtendenteId = atendenteDaCorrecao.Id,
    Conteudo = "mensagem de resultado ambiguo",
    StatusEntrega = StatusEntrega.Enviando,
    EnvioIniciadoEm = DateTime.UtcNow.AddMinutes(-30),
    EnvioDisparadoEm = DateTime.UtcNow.AddMinutes(-30)
};
db.Mensagens.Add(envioTravado);
await db.SaveChangesAsync();

cronometro.Restart();
var comEnvioTravado = await identificador.ClassificarAsync(
    tenant.Id, Eco("CEL-COM-ENVIO-TRAVADO", "resposta enquanto ha envio travado"));
cronometro.Stop();

Checar(
    "Envio travado ha mais de dois minutos nao conta como em voo: a resposta pelo celular passa, sem esperar",
    comEnvioTravado == OrigemDaMensagemDoProprioNumero.RespostaPeloAparelho
    && cronometro.Elapsed < TimeSpan.FromMilliseconds(400));

// Midia em voo: casa pelo tipo (a regra da legenda tem casos demais para
// reproduzir, e casar "demais" so erra para o lado seguro).
var midiaEmVoo = new Mensagem
{
    TenantId = tenant.Id,
    ConversaId = conversaPermitida.Id,
    AutorTipo = AutorTipo.Atendente,
    AtendenteId = atendenteDaCorrecao.Id,
    Conteudo = "[imagem]",
    Tipo = TipoMensagem.Imagem,
    StatusEntrega = StatusEntrega.Enviando,
    EnvioIniciadoEm = DateTime.UtcNow,
    EnvioDisparadoEm = DateTime.UtcNow,
    ArquivoCaminho = "anexo-em-voo.jpg",
    ArquivoNome = "anexo-em-voo.jpg",
    ArquivoTipoMime = "image/jpeg",
    ArquivoTamanhoBytes = 10
};
db.Mensagens.Add(midiaEmVoo);
await db.SaveChangesAsync();

var ecoDaImagem = TradutorDePayload.LerMensagemEnviadaPeloAparelho(DoProprioNumero(
    "ECO-DA-IMAGEM",
    "5519990001488",
    JsonSerializer.SerializeToElement(new { imageMessage = new { mimetype = "image/jpeg" } })))!;

Checar(
    "Eco de imagem com imagem saindo do painel e reconhecido como eco",
    await identificador.ClassificarAsync(tenant.Id, ecoDaImagem) == OrigemDaMensagemDoProprioNumero.JaEstaNoFio);

// Nada daqui pode vazar para os cenarios seguintes como "envio em voo".
midiaEmVoo.StatusEntrega = StatusEntrega.Enviada;
midiaEmVoo.IdExternoWhatsapp = "ID-DA-IMAGEM";
envioTravado.StatusEntrega = StatusEntrega.Falha;
await db.SaveChangesAsync();

// ---------------------------------------------------------------------------
// AssinaturaDeAutoria — funcao pura, sem banco nem gateway. Prova o que o
// cliente le no WhatsApp quando um atendente responde, a IA responde, ou uma
// regra cadastrada dispara. O caminho feliz completo (gateway real + prova de
// nao-persistencia no banco) esta em verificar-concorrencia-saida-whatsapp.cs,
// que usa SQL Server de verdade — o InMemory nao implementa ExecuteUpdate.
// ---------------------------------------------------------------------------

var autoriaAtendente = new AutoriaDaMensagem(AutorTipo.Atendente, false, false, "Maria Rezende");
Checar(
    "Atendente ganha cabecalho em negrito com o nome projetado do AtendenteId, conteudo abaixo",
    AssinaturaDeAutoria.Decorar("Bom dia", autoriaAtendente) == "*Maria Rezende*\nBom dia");

var autoriaIa = new AutoriaDaMensagem(AutorTipo.Sistema, true, true, null);
Checar(
    "Resposta da IA leva o cabecalho 'Assistente Virtual'",
    AssinaturaDeAutoria.Decorar("O valor varia conforme a tabela do TJ.", autoriaIa)
        == "*Assistente Virtual*\nO valor varia conforme a tabela do TJ.");

var autoriaRegraCadastrada = new AutoriaDaMensagem(AutorTipo.Sistema, true, false, null);
Checar(
    "Automatica de REGRA CADASTRADA nao recebe cabecalho nenhum (decisao do Thiago, 2026-09-04)",
    AssinaturaDeAutoria.Decorar("Certo! Um atendente vai continuar seu atendimento.", autoriaRegraCadastrada)
        == "Certo! Um atendente vai continuar seu atendimento.");

var autoriaAnotacaoInterna = new AutoriaDaMensagem(AutorTipo.Sistema, false, false, null);
Checar(
    "Anotacao interna (Sistema, EhAutomatica=false) nao e decorada — defensivo, nunca deveria chegar aqui",
    AssinaturaDeAutoria.Decorar("Conversa assumida por Fulano.", autoriaAnotacaoInterna)
        == "Conversa assumida por Fulano.");

var autoriaCliente = new AutoriaDaMensagem(AutorTipo.Cliente, false, false, null);
Checar(
    "Mensagem do cliente nao e decorada — defensivo",
    AssinaturaDeAutoria.Decorar("Oi, bom dia", autoriaCliente) == "Oi, bom dia");

var autoriaAtendenteSemNome = new AutoriaDaMensagem(AutorTipo.Atendente, false, false, null);
Checar(
    "Atendente sem nome utilizavel nao produz cabecalho vazio (nunca '**')",
    AssinaturaDeAutoria.Decorar("Bom dia", autoriaAtendenteSemNome) == "Bom dia");

var autoriaAtendenteNomeSoEspacos = new AutoriaDaMensagem(AutorTipo.Atendente, false, false, "   ");
Checar(
    "Atendente com nome so de espacos tambem nao produz cabecalho",
    AssinaturaDeAutoria.Decorar("Bom dia", autoriaAtendenteNomeSoEspacos) == "Bom dia");

var autoriaNomeComFormatacao = new AutoriaDaMensagem(
    AutorTipo.Atendente, false, false, "Ma*ria_ `Re~zende\ndo Carmo");
Checar(
    "Nome com caracteres de formatacao do WhatsApp e quebra de linha e higienizado",
    AssinaturaDeAutoria.Decorar("Bom dia", autoriaNomeComFormatacao)
        == "*Maria Rezende do Carmo*\nBom dia");

var nomeMuitoLongo = new string('A', 120);
var autoriaNomeLongo = new AutoriaDaMensagem(AutorTipo.Atendente, false, false, nomeMuitoLongo);
var cabecalhoDoNomeLongo = AssinaturaDeAutoria.Cabecalho(autoriaNomeLongo);
Checar(
    "Nome acima do limite e cortado (cabecalho nao cresce sem controle)",
    cabecalhoDoNomeLongo is not null && cabecalhoDoNomeLongo.Length < nomeMuitoLongo.Length);

Checar(
    "Legenda de midia escrita por gente ganha o mesmo cabecalho",
    AssinaturaDeAutoria.DecorarLegenda("Segue a certidão digitalizada.", autoriaAtendente)
        == "*Maria Rezende*\nSegue a certidão digitalizada.");

Checar(
    "Legenda nula continua nula — foto sem legenda nao ganha cabecalho (decisao do Thiago, 2026-09-04)",
    AssinaturaDeAutoria.DecorarLegenda(null, autoriaAtendente) is null);

Checar(
    "Reenvio da mesma mensagem produz string identica (nao ha estado escondido na decoracao)",
    AssinaturaDeAutoria.Decorar("Bom dia", autoriaAtendente) == AssinaturaDeAutoria.Decorar("Bom dia", autoriaAtendente));

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

    public int RespostasPeloAparelho { get; private set; }
    public MensagemDoCanal? UltimaPeloAparelho { get; private set; }

    public Task<Guid?> RegistrarEnviadaPeloAparelhoAsync(
        Guid tenantId,
        MensagemDoCanal mensagem,
        CancellationToken ct = default)
    {
        RespostasPeloAparelho++;
        UltimaPeloAparelho = mensagem;
        return Task.FromResult<Guid?>(Guid.NewGuid());
    }
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

    /// <summary>Texto que o gateway recebeu na ultima edicao — ja decorado com a autoria.</summary>
    public string? TextoDaUltimaEdicao { get; private set; }

    public string? IdDaUltimaEdicao { get; private set; }

    public string? IdDoUltimoApagamento { get; private set; }

    public int Edicoes { get; private set; }

    public int Apagamentos { get; private set; }

    /// <summary>Faz o gateway recusar, como o WhatsApp faz quando a janela dele fecha.</summary>
    public bool RecusarEdicao { get; init; }

    public bool RecusarApagamento { get; init; }

    public Task<bool> EditarTextoAsync(
        string telefoneE164,
        string idExternoWhatsapp,
        string conteudo,
        CancellationToken ct)
    {
        Edicoes++;
        IdDaUltimaEdicao = idExternoWhatsapp;
        TextoDaUltimaEdicao = conteudo;

        return Task.FromResult(!RecusarEdicao);
    }

    public Task<bool> ApagarParaTodosAsync(
        string telefoneE164,
        string idExternoWhatsapp,
        CancellationToken ct)
    {
        Apagamentos++;
        IdDoUltimoApagamento = idExternoWhatsapp;

        return Task.FromResult(!RecusarApagamento);
    }

    public Task<string?> ObterFotoPerfilUrlAsync(string telefoneE164, CancellationToken ct)
    {
        ConsultasDeFoto++;
        return Task.FromResult<string?>(null);
    }

    public int Desconexoes { get; private set; }

    public Task DesconectarAsync(CancellationToken ct)
    {
        Desconexoes++;
        return Task.CompletedTask;
    }

    public Task<PerfilDaInstancia?> ObterPerfilDaInstanciaAsync(CancellationToken ct) =>
        Task.FromResult<PerfilDaInstancia?>(null);

    public Task<IReadOnlyList<ContatoDoWhatsApp>> ObterContatosAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ContatoDoWhatsApp>>([]);
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
    public bool Pode(PermissaoDoSistema permissao) => true;
    public Guid? AutorizacaoDeAcessoTecnicoId => null;
    public Guid? EscopoDeConversaId => null;
}

file sealed class HttpClientFactoryFake : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new();
}
