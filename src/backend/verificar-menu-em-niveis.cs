#:project CrmCartorio.Api/CrmCartorio.Api.csproj
#:package Microsoft.EntityFrameworkCore.InMemory@10.0.9
#:property PublishAot=false
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Persistence.Seeding;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Atendimento.Application;
using CrmCartorio.Api.Modules.Atendimento.Domain;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.Clientes.Domain.Entities;
using CrmCartorio.Api.Modules.RespostaInteligente.Application;
using CrmCartorio.Api.Modules.Servicos.Application;
using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

// VERIFICADOR DO MENU EM NÍVEIS — o menu do Registro Civil pedido pelo cartório parceiro, em memória.
//
//   dotnet run verificar-menu-em-niveis.cs
//
// Monta o kit de partida, configura o menu PELO SERVIÇO DE CADASTRO (o mesmo da tela) e
// percorre a conversa com os aplicadores reais: ordem do menu, submenu da segunda via,
// dados picados → silêncio → Pix (pela varredura real) → comprovante com texto próprio → atendente, serviço sem sequência de dados, regressão da
// mensagem geral, recusas do cadastro, "menu" zerando tudo e chave geral desligada.

var falhas = new List<string>();
void Conferir(bool ok, string oque) { Console.WriteLine((ok ? "ok    " : "FALHA ") + oque); if (!ok) falhas.Add(oque); }

var opcoes = new DbContextOptionsBuilder<CrmDbContext>().UseInMemoryDatabase("menu-" + Guid.NewGuid()).Options;
await using var db = new CrmDbContext(opcoes, FabricaDeCifraDeDados.CriarDormente());
var tenantId = Guid.CreateVersion7();
db.Tenants.Add(new Tenant { Id = tenantId, RazaoSocial = "Cartório de Teste", NomeFantasia = "Cartório de Teste", Cnpj = "00000000000000" });
await db.SaveChangesAsync();
await KitDePartida.AplicarAsync(db, tenantId);

var ctx = new ContextoFixo(tenantId);
var cadastro = new TipoServicoService(db, ctx);
var automaticas = new DisparadorDeMensagemAutomatica(db, NullLogger<DisparadorDeMensagemAutomatica>.Instance);
var log = NullLogger.Instance;

var civil = await db.Setores.Where(s => s.TenantId == tenantId && s.Nome == "Registro Civil").Select(s => s.Id).SingleAsync();
var notas = await db.Setores.Where(s => s.TenantId == tenantId && s.Nome == "Tabelionato de Notas").Select(s => s.Id).SingleAsync();
async Task<Guid> Id(string nome) => await db.TiposServico.Where(t => t.TenantId == tenantId && t.Nome == nome).Select(t => t.Id).SingleAsync();

async Task Configurar(string nome, int ordem, Guid? pai = null, string? aoEscolher = null, string? aposDados = null, string? novoNome = null, string? aposComprovante = null)
{
    var atual = await cadastro.ObterAsync(await Id(nome));
    await cadastro.AtualizarAsync(atual.Id, new SalvarTipoServicoRequest(
        novoNome ?? atual.Nome, atual.Codigo, atual.Categoria, atual.Descricao, atual.Ativo,
        atual.Prazo is { } p ? new PrazoServicoRequest(p.PrazoHoras, p.PercentualAlerta, p.ApenasDiasUteis) : null,
        atual.SetorId, atual.DocumentosNecessarios, ordem, pai, aoEscolher, aposDados, aposComprovante));
}

const string Pix = "Obrigado! ✅ Recebemos os dados para solicitação da certidão.\nPara pagamento, utilize a chave Pix abaixo:\n\n00000000000000\n\nApós realizar o pagamento, envie o comprovante por aqui.";

// ---- o menu do cartório parceiro, pelo cadastro
await Configurar("Segunda via de certidão", 1, aoEscolher: "Certo, {NomeCliente}! ✅ Qual certidão você precisa?\n{MenuServicos}\nSe preferir, descreva o que precisa que um de nossos atendentes dará continuidade.");
await Configurar("Registro de nascimento", 2, aoEscolher: "REGISTRO DE NASCIMENTO — documentos necessários…");
await Configurar("Habilitação de casamento", 3, aoEscolher: "HABILITAÇÃO DE CASAMENTO — documentos dos noivos…");
await Configurar("Registro de óbito", 4, aoEscolher: "REGISTRO DE ÓBITO — documentos necessários…");
await Configurar("Averbação em registro civil", 5, aoEscolher: "AVERBAÇÕES — principais situações…", novoNome: "Averbações");
var segundaVia = await Id("Segunda via de certidão");
await Configurar("Certidão de nascimento", 1, segundaVia, "Para solicitar a segunda via da Certidão de Nascimento, informe…", Pix,
    aposComprovante: "Recebemos seu comprovante, {NomeCliente}! ✅ Um de nossos atendentes vai conferir e dar continuidade.");
await Configurar("Certidão de casamento", 2, segundaVia, "Para solicitar a segunda via da Certidão de Casamento, informe…", Pix);
await Configurar("Certidão de óbito", 3, segundaVia, "Para solicitar a segunda via da Certidão de Óbito, informe…", Pix);

var menuCivil = (await automaticas.ServicosDoMenuAsync(tenantId, civil)).Select(o => o.Nome).ToList();
Conferir(menuCivil.SequenceEqual(["Segunda via de certidão", "Registro de nascimento", "Habilitação de casamento", "Registro de óbito", "Averbações"]),
    $"menu do Registro Civil na ordem pedida: {string.Join(" | ", menuCivil)}");
var submenu = (await automaticas.ServicosDoSubmenuAsync(tenantId, segundaVia)).Select(o => o.Nome).ToList();
Conferir(submenu.SequenceEqual(["Certidão de nascimento", "Certidão de casamento", "Certidão de óbito"]), $"submenu da segunda via: {string.Join(" | ", submenu)}");
var menuNotas = (await automaticas.ServicosDoMenuAsync(tenantId, notas)).Select(o => o.Nome).ToList();
Conferir(menuNotas.SequenceEqual(menuNotas.OrderBy(n => n, StringComparer.Ordinal)), "Notas, sem posição definida, continua em ordem alfabética");

// ---- recusas do cadastro
async Task<bool> Recusa(Func<Task> acao) { try { await acao(); return false; } catch (ServicosServiceException) { return true; } }
Conferir(await Recusa(async () => await Configurar("Certidão de casamento", 2, await Id("Certidão de nascimento"))), "recusa submenu de dois níveis");
Conferir(await Recusa(async () => await Configurar("Certidão de casamento", 2, await Id("Procuração pública"))), "recusa agrupar serviço de outro setor");
Conferir(await Recusa(async () => await Configurar("Segunda via de certidão", 1, await Id("Registro de nascimento"))), "recusa pôr dentro de outro quem já agrupa");
Conferir(await Recusa(async () => await Configurar("Registro de óbito", 4, aoEscolher: "Olá {NomeClinte}")), "recusa marcador desconhecido");
Conferir(await Recusa(async () => await Configurar("Registro de óbito", 1000)), "recusa posição fora de 0 a 999");
Conferir(await Recusa(async () => await Configurar("Registro de óbito", 4, aoEscolher: new string('a', 4100))),
    "recusa parte única acima de 4000 caracteres (limite de uma mensagem do WhatsApp)");
Conferir(await Recusa(async () => await Configurar("Registro de óbito", 4, aoEscolher: new string('a', 3000) + "\n---\n" + new string('b', 4100))),
    "recusa a segunda parte acima de 4000");

// ---- conversa: segunda via → nascimento → dados → Pix → comprovante → atendente
var cliente = new Cliente { TenantId = tenantId, Nome = "Adriana Alves", TelefoneWhatsapp = "5538999990000" };
db.Clientes.Add(cliente);
Conversa NovaConversa(Guid setor)
{
    var c = new Conversa { TenantId = tenantId, ClienteId = cliente.Id, Protocolo = $"202609-{Random.Shared.Next(1000, 9999)}", SetorId = setor, AguardandoEscolhaDeServico = true, Canal = CanalOrigem.Simulado };
    db.Conversas.Add(c);
    return c;
}
async Task<List<string>> Geradas(Func<Task> passo)
{
    await passo();
    var novas = db.ChangeTracker.Entries<Mensagem>().Where(e => e.State == EntityState.Added && e.Entity.EhAutomatica).Select(e => e.Entity.Conteudo).ToList();
    await db.SaveChangesAsync();
    return novas;
}
Task Escolher(Conversa c, string resposta) =>
    AplicadorDeEscolhaDeServico.AplicarAsync(db, automaticas, null!, new OpcoesDaIA(), c, resposta, DateTime.UtcNow, log, default);
Task Dados(Conversa c) => AplicadorDeDadosDoServico.AplicarAsync(db, automaticas, c, DateTime.UtcNow, log, default);

var conversa = NovaConversa(civil);
await db.SaveChangesAsync();

var m1 = await Geradas(() => Escolher(conversa, "1"));
Conferir(conversa.ServicoDoSubmenuId == segundaVia && conversa.AguardandoEscolhaDeServico, "\"1\" abre o submenu da segunda via");
Conferir(m1.Count == 1 && m1[0].StartsWith("Certo, Adriana Alves! ✅ Qual certidão") && m1[0].Contains("1 - Certidão de nascimento") && m1[0].Contains("3 - Certidão de óbito"),
    "a pergunta do submenu sai com o nome e a lista de dentro");

var m2 = await Geradas(() => Escolher(conversa, "1"));
Conferir(conversa.TipoServicoId == await Id("Certidão de nascimento") && conversa.ServicoDoSubmenuId is null, "\"1\" no submenu escolhe Certidão de nascimento");
Conferir(conversa.AguardandoDadosDoServico && !conversa.AguardandoMenuDeContinuacao, "passa a esperar os dados, sem menu de continuação no meio");
Conferir(m2.Count == 1 && m2[0].StartsWith("Para solicitar a segunda via da Certidão de Nascimento"), "manda o texto próprio da certidão");

// Os dados chegam picados, em três mensagens: nenhuma é respondida na hora.
var m3 = await Geradas(() => Dados(conversa));
var comecouEm = conversa.DadosDoServicoRecebidosEm;
await Geradas(() => Dados(conversa));
var m3c = await Geradas(() => Dados(conversa));
Conferir(m3.Count == 0 && m3c.Count == 0 && conversa.AguardandoDadosDoServico && !conversa.AguardandoComprovante,
    "dados em várias mensagens: o robô não responde no meio da digitação");
Conferir(comecouEm is not null && conversa.DadosDoServicoRecebidosEm == comecouEm, "marca quando os dados começaram, só na primeira mensagem");
Conferir(await db.Mensagens.CountAsync(m => m.ConversaId == conversa.Id && m.AutorTipo == AutorTipo.Sistema && !m.EhAutomatica && m.Conteudo.StartsWith("Cliente começou a enviar")) == 1,
    "uma anotação só para o painel, não uma por mensagem");

// A varredura real, num contexto próprio como o do serviço de fundo.
async Task<int> Varrer(TimeSpan silencio)
{
    await using var dbDoFundo = new CrmDbContext(opcoes, FabricaDeCifraDeDados.CriarDormente());
    var varredor = new VarredorDeDadosDoServico(dbDoFundo,
        new DisparadorDeMensagemAutomatica(dbDoFundo, NullLogger<DisparadorDeMensagemAutomatica>.Instance),
        new NotificadorSilencioso(), new CanalSilencioso(), NullLogger<VarredorDeDadosDoServico>.Instance);
    var n = await varredor.ExecutarAsync(silencio);
    await db.Entry(conversa).ReloadAsync();
    return n;
}

conversa.UltimaMensagemEm = DateTime.UtcNow;
await db.SaveChangesAsync();
Conferir(await Varrer(TimeSpan.FromSeconds(45)) == 0 && conversa.AguardandoDadosDoServico, "cliente ainda digitando (última mensagem agora): a varredura espera");

conversa.UltimaMensagemEm = DateTime.UtcNow.AddSeconds(-60);
await db.SaveChangesAsync();
var respondidas = await Varrer(TimeSpan.FromSeconds(45));
var pix = await db.Mensagens.Where(m => m.ConversaId == conversa.Id && m.EhAutomatica && m.Conteudo.Contains("chave Pix")).ToListAsync();
Conferir(respondidas == 1 && pix.Count == 1 && pix[0].StatusEntrega == StatusEntrega.Pendente, "depois do silêncio, a varredura manda a chave Pix uma vez, na fila de envio");
Conferir(conversa.AguardandoComprovante && !conversa.AguardandoDadosDoServico && conversa.DadosDoServicoRecebidosEm is null, "e passa a esperar o comprovante");
Conferir(await Varrer(TimeSpan.FromSeconds(45)) == 0, "a passada seguinte não repete o Pix");

var m4 = await Geradas(() => Dados(conversa));
Conferir(conversa.EmAtendimentoHumano && !conversa.AguardandoComprovante, "o comprovante passa o atendimento para a equipe");
Conferir(m4.Count == 1 && m4[0] == "Recebemos seu comprovante, Adriana Alves! ✅ Um de nossos atendentes vai conferir e dar continuidade.",
    "avisa o cliente com o texto próprio do comprovante, com o nome resolvido");

// ---- serviço com texto próprio mas sem sequência de dados
var conversa2 = NovaConversa(civil);
await db.SaveChangesAsync();
var h = await Geradas(() => Escolher(conversa2, "3"));
Conferir(conversa2.TipoServicoId == await Id("Habilitação de casamento"), "\"3\" é Habilitação de casamento");
Conferir(h.Count == 2 && h.Any(x => x.StartsWith("HABILITAÇÃO")) && h.Any(x => x.StartsWith("Ficou alguma dúvida, Adriana Alves?")),
    "serviço que só orienta: texto próprio e, em seguida, a pergunta de dúvida");
Conferir(conversa2.AguardandoDuvidaDoServico && !conversa2.AguardandoMenuDeContinuacao && !conversa2.AguardandoDadosDoServico,
    "espera a resposta à pergunta, sem menu de continuação e sem esperar dados");

// ---- a dúvida vai para a IA, que tem o texto do serviço no contexto, e a pergunta volta
var iaFalsa = new IaFalsa();
Task Duvida(Conversa c, string resposta) =>
    AplicadorDeDuvidaDoServico.AplicarAsync(db, iaFalsa, automaticas, new OpcoesDaIA(), c, resposta, DateTime.UtcNow, log, default);

var du1 = await Geradas(() => Duvida(conversa2, "posso dar entrada em outra cidade?"));
Conferir(iaFalsa.Chamadas == 1 && du1.Count == 2 && du1.Contains(IaFalsa.Resposta) && du1.Any(x => x.StartsWith("Ficou alguma dúvida")),
    "dúvida: a IA responde e a pergunta de dúvida volta");
Conferir(conversa2.AguardandoDuvidaDoServico && !conversa2.AguardandoRespostaInteligente && conversa2.Status == StatusConversa.Ativa,
    "depois da resposta da IA, continua esperando a resposta à pergunta");

var contextoComServico = await CrmCartorio.Api.Modules.RespostaInteligente.Application.MontadorDeContexto.MontarAsync(db, tenantId, conversa2.TipoServicoId, default);
var contextoSemServico = await CrmCartorio.Api.Modules.RespostaInteligente.Application.MontadorDeContexto.MontarAsync(db, tenantId, default);
Conferir(contextoComServico.Contains("ORIENTACAO DO CARTORIO SOBRE O SERVICO QUE O CLIENTE ESCOLHEU (Habilitação de casamento)")
         && contextoComServico.Contains("HABILITAÇÃO DE CASAMENTO — documentos dos noivos")
         && !contextoSemServico.Contains("ORIENTACAO DO CARTORIO"),
    "a IA recebe o texto do serviço da conversa, e só dele");

var du2 = await Geradas(() => Duvida(conversa2, "Não, obrigado!"));
Conferir(conversa2.Status == StatusConversa.Encerrada && !conversa2.AguardandoDuvidaDoServico && du2.Count == 1 && du2[0].Contains("Foi um prazer"),
    "\"não, obrigado\": despedida e encerra");

var conversa8 = NovaConversa(civil);
await db.SaveChangesAsync();
await Geradas(() => Escolher(conversa8, "2"));
var du3 = await Geradas(() => Duvida(conversa8, "2"));
Conferir(conversa8.EmAtendimentoHumano && !conversa8.AguardandoDuvidaDoServico && du3.Count == 1 && du3[0].Contains("atendentes vai continuar"),
    "\"2\": passa para a equipe");

Conferir(TriagemAutomatica.LerRespostaDeDuvida("1") == TriagemAutomatica.RespostaDeDuvida.SemDuvida
         && TriagemAutomatica.LerRespostaDeDuvida("Entendi, obrigada!") == TriagemAutomatica.RespostaDeDuvida.SemDuvida
         && TriagemAutomatica.LerRespostaDeDuvida("quero falar com um atendente") == TriagemAutomatica.RespostaDeDuvida.Atendente
         && TriagemAutomatica.LerRespostaDeDuvida("Não, obrigado. Tudo certo!") == TriagemAutomatica.RespostaDeDuvida.SemDuvida
         && TriagemAutomatica.LerRespostaDeDuvida("não entendi a parte da DNV") == TriagemAutomatica.RespostaDeDuvida.Duvida
         && TriagemAutomatica.LerRespostaDeDuvida("ok, mas e o prazo") == TriagemAutomatica.RespostaDeDuvida.Duvida
         && TriagemAutomatica.LerRespostaDeDuvida("tenho duvida") == TriagemAutomatica.RespostaDeDuvida.Duvida
         && TriagemAutomatica.LerRespostaDeDuvida("Obrigado, e precisa levar testemunha?") == TriagemAutomatica.RespostaDeDuvida.Duvida,
    "leitura da resposta: \"não entendi a parte da DNV\" é dúvida, não encerramento");

// ---- texto longo em duas partes (a habilitação do cartório parceiro tem 4217 caracteres)
var parte1 = "💍 *HABILITAÇÃO* — parte 1 " + new string('x', 2300);
var parte2 = "💒 *TIPOS DE CASAMENTO* — parte 2 " + new string('y', 1900);
await Configurar("Registro de óbito", 4, aoEscolher: parte1 + "\n---\n" + parte2);
Conferir((await cadastro.ObterAsync(await Id("Registro de óbito"))).MensagemAoEscolher!.Length > 4000, "o cadastro aceita texto de mais de 4000 dividido em partes");
var conversa7 = NovaConversa(civil);
await db.SaveChangesAsync();
var o = await Geradas(() => Escolher(conversa7, "4"));
var fioO = await db.Mensagens.Where(m => m.ConversaId == conversa7.Id && m.EhAutomatica).OrderBy(m => m.EnviadaEm).Select(m => m.Conteudo).ToListAsync();
Conferir(o.Count == 3 && fioO.Count == 3 && fioO[0] == parte1 && fioO[1] == parte2 && fioO[2].StartsWith("Ficou alguma dúvida"),
    "texto em duas partes sai em duas mensagens, na ordem, e depois a pergunta de dúvida");
Conferir(fioO.All(x => x.Length <= 4000), "nenhuma mensagem gerada passa de 4000");

// ---- regressão: serviço sem nada configurado usa a mensagem geral de sempre
var conversa3 = NovaConversa(notas);
await db.SaveChangesAsync();
var n = await Geradas(() => Escolher(conversa3, "1"));
Conferir(n.Count == 2 && n.Any(x => x.Contains("Localizamos seu atendimento de")) && n.Any(x => x.Contains("Posso ajudar em mais alguma coisa")), "Notas continua com a mensagem geral e o menu de continuação");

// ---- "menu" zera o submenu e a espera de dados
var conversa4 = NovaConversa(civil);
await db.SaveChangesAsync();
await Geradas(() => Escolher(conversa4, "1"));
await Geradas(() => Escolher(conversa4, "2"));
Conferir(conversa4.AguardandoDadosDoServico, "casamento também espera os dados");
await Geradas(() => Dados(conversa4));
Conferir(conversa4.DadosDoServicoRecebidosEm is not null, "casamento marcou o começo dos dados");
await Geradas(() => AplicadorDePosAtendimento.ReabrirDoZeroAsync(db, automaticas, conversa4, DateTime.UtcNow, default));
Conferir(!conversa4.AguardandoDadosDoServico && !conversa4.AguardandoComprovante && conversa4.DadosDoServicoRecebidosEm is null && conversa4.ServicoDoSubmenuId is null && conversa4.AguardandoTriagem, "\"menu\" zera tudo e volta à triagem");

// ---- sem texto próprio do comprovante, sai o aviso geral de pedido de atendente
var conversa6 = NovaConversa(civil);
await db.SaveChangesAsync();
await Geradas(() => Escolher(conversa6, "1"));
await Geradas(() => Escolher(conversa6, "3"));
await Geradas(() => Dados(conversa6));
conversa6.AguardandoDadosDoServico = false; conversa6.DadosDoServicoRecebidosEm = null; conversa6.AguardandoComprovante = true;
var m6 = await Geradas(() => Dados(conversa6));
Conferir(conversa6.EmAtendimentoHumano && m6.Count == 1 && m6[0].Contains("atendentes vai continuar"), "óbito, sem texto do comprovante: aviso geral de pedido de atendente");

// ---- chave geral desligada: nada é gerado e nada fica esperando
(await db.Tenants.SingleAsync(t => t.Id == tenantId)).MensagensAutomaticasHabilitadas = false;
await db.SaveChangesAsync();
var conversa5 = NovaConversa(civil);
await db.SaveChangesAsync();
var d1 = await Geradas(() => Escolher(conversa5, "1"));
Conferir(d1.Count == 0 && conversa5.ServicoDoSubmenuId is null && !conversa5.AguardandoEscolhaDeServico, "chave desligada: não abre submenu mudo");

Console.WriteLine();
Console.WriteLine(falhas.Count == 0 ? "TUDO OK" : $"{falhas.Count} FALHA(S)");
return falhas.Count == 0 ? 0 : 1;

file sealed class CanalSilencioso : IEnviadorDeCanal
{
    public Task<string?> EnviarAsync(Guid m, CancellationToken ct = default) => Task.FromResult<string?>(null);
    public Task EnviarPendentesAsync(Guid c, CancellationToken ct = default) => Task.CompletedTask;
    public Task<ResultadoNoCanal> CorrigirAsync(Guid m, string conteudo, CancellationToken ct = default) => Task.FromResult(ResultadoNoCanal.NaoSeAplica);
    public Task<ResultadoNoCanal> ApagarParaTodosAsync(Guid m, CancellationToken ct = default) => Task.FromResult(ResultadoNoCanal.NaoSeAplica);
}

file sealed class NotificadorSilencioso : INotificadorDeAtendimento
{
    public Task ConversaAtribuidaAsync(Guid t, ConversaResumoResponse c, CancellationToken ct = default) => Task.CompletedTask;
    public Task ConversaLiberadaAsync(Guid t, ConversaResumoResponse c, CancellationToken ct = default) => Task.CompletedTask;
    public Task ConversaTransferidaAsync(Guid t, ConversaResumoResponse c, CancellationToken ct = default) => Task.CompletedTask;
    public Task ConversaAtualizadaAsync(Guid t, ConversaResumoResponse c, CancellationToken ct = default) => Task.CompletedTask;
    public Task ConversaEncerradaAsync(Guid t, ConversaResumoResponse c, CancellationToken ct = default) => Task.CompletedTask;
    public Task ConversaReabertaAsync(Guid t, ConversaResumoResponse c, CancellationToken ct = default) => Task.CompletedTask;
    public Task MensagemRecebidaAsync(Guid t, MensagemResponse m, ConversaResumoResponse c, CancellationToken ct = default) => Task.CompletedTask;
}

file sealed class IaFalsa : IRespostaInteligenteService
{
    public const string Resposta = "Pode sim: a habilitação é feita no cartório da cidade onde mora um dos noivos.";
    public int Chamadas { get; private set; }

    public Task<RespostaDaIA> ResponderAsync(Guid tenantId, IReadOnlyList<TrocaAnterior> historico, string mensagemDoCliente,
        ContextoDaChamada? contexto = null, CancellationToken ct = default)
    {
        Chamadas++;
        return Task.FromResult(new RespostaDaIA(true, Resposta, "ok"));
    }
}

file sealed class ContextoFixo(Guid tenantId) : IContextoDoUsuario
{
    public bool Autenticado => true;
    public Guid TenantId => tenantId;
    public Guid AtendenteId { get; } = Guid.CreateVersion7();
    public string Nome => "Administrador";
    public PerfilDeAtendente Perfil => PerfilDeAtendente.Administrador;
    public bool Pode(PermissaoDoSistema permissao) => CatalogoDePermissoes.ConjuntoDaClasse(Perfil).Contains(permissao);
    public Guid? AutorizacaoDeAcessoTecnicoId => null;
    public Guid? EscopoDeConversaId => null;
}
