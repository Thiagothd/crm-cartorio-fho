#:project CrmCartorio.Api/CrmCartorio.Api.csproj
#:property PublishAot=false

using System.Collections.Concurrent;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Atendimento.Application;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.CanalWhatsApp.Application;
using CrmCartorio.Api.Modules.CanalWhatsApp.Domain.Enums;
using CrmCartorio.Api.Modules.Clientes.Domain.Entities;
using CrmCartorio.Api.Modules.RespostaInteligente.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// BUG-WA-IN-02 — prova com SQL Server real, contexts/conexoes independentes.
// Rode com: dotnet run verificar-concorrencia-entrada-whatsapp.cs ["<connection string>"]
// Escreve somente em CRM_Cartorio_Dev e remove tudo que cria.

var conexao = args.Length > 0
    ? args[0]
    : "Server=.\\SQLEXPRESS;Database=CRM_Cartorio_Dev;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";
var construtor = new DbContextOptionsBuilder<CrmDbContext>();
ConfiguracaoDoBanco.Configurar(construtor, conexao);
var opcoes = construtor.Options;
var cifra = FabricaDeCifraDeDados.CriarDormente();

await using (var seguranca = new CrmDbContext(opcoes, cifra))
{
    if (!string.Equals(seguranca.Database.GetDbConnection().Database, "CRM_Cartorio_Dev", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Recusado: o verificador so escreve em CRM_Cartorio_Dev.");
    if (!await seguranca.Database.CanConnectAsync()) return 2;

    var colunas = await seguranca.Database.SqlQuery<int>($"""
        SELECT COUNT(*) AS Value
          FROM sys.columns
         WHERE object_id = OBJECT_ID(N'dbo.Mensagens')
           AND name IN (N'EstadoProcessamentoEntrada', N'ProcessamentoEntradaIniciadoEm',
                        N'ProcessamentoEntradaConcluidoEm', N'ProcessamentoEntradaDisponivelEm')
        """).SingleAsync();
    if (colunas != 4)
        throw new InvalidOperationException("A migration 032 deve ser aplicada em CRM_Cartorio_Dev antes deste verificador.");
}

var falhas = 0;
void Checar(string nome, bool ok)
{
    Console.WriteLine($"{(ok ? "OK   " : "FALHA")} {nome}");
    if (!ok) falhas++;
}

var sufixo = Guid.NewGuid().ToString("N")[..8];
var prefixoId = $"WAIN02-{sufixo}-";
var prefixoNome = $"[TESTE WA-IN-02 {sufixo}]";
var telefones = new ConcurrentBag<string>();
Guid tenantId;

await using (var db = new CrmDbContext(opcoes, cifra))
    tenantId = await db.Tenants.Select(t => t.Id).FirstAsync();

var notificador = new NotificadorSilencioso();
var anexos = new AnexosSilenciosos();

RecebimentoDeWhatsApp NovoRecebimento(CrmDbContext db) =>
    new(db, anexos, notificador, new SinalizadorDeEntradaWhatsapp(), cifra, NullLogger<RecebimentoDeWhatsApp>.Instance);

ProcessadorDeEntradaWhatsapp NovoProcessador(CrmDbContext db, IRespostaInteligenteService ia, IEnviadorDeCanal? canal = null) =>
    new(
        db,
        new DisparadorDeMensagemAutomatica(db, NullLogger<DisparadorDeMensagemAutomatica>.Instance),
        notificador,
        canal ?? new CanalSilencioso(),
        ia,
        Options.Create(new OpcoesDaIA()),
        NullLogger<ProcessadorDeEntradaWhatsapp>.Instance);

async Task<(Guid ClienteId, Guid ConversaId)> CriarConversaAsync(
    string marcador,
    bool humano = false,
    bool aguardandoIa = false,
    bool comMensagemAnterior = false,
    CanalOrigem canal = CanalOrigem.WhatsApp)
{
    await using var db = new CrmDbContext(opcoes, cifra);
    var telefone = $"5598{Random.Shared.NextInt64(100000000, 999999999)}";
    telefones.Add(telefone);
    var cliente = new Cliente
    {
        TenantId = tenantId,
        Nome = $"{prefixoNome} {marcador}",
        TelefoneWhatsapp = telefone,
        PrimeiroContatoEm = DateTime.UtcNow,
        Ativo = true
    };
    var conversa = new Conversa
    {
        TenantId = tenantId,
        ClienteId = cliente.Id,
        Protocolo = $"WI2-{Guid.NewGuid():N}"[..20],
        Canal = canal,
        Status = StatusConversa.Ativa,
        EmAtendimentoHumano = humano,
        AguardandoRespostaInteligente = aguardandoIa,
        IniciadaEm = DateTime.UtcNow.AddMinutes(-1),
        UltimaMensagemEm = DateTime.UtcNow.AddMinutes(-1)
    };
    db.AddRange(cliente, conversa);
    if (comMensagemAnterior)
    {
        db.Mensagens.Add(new Mensagem
        {
            TenantId = tenantId,
            ConversaId = conversa.Id,
            AutorTipo = AutorTipo.Cliente,
            Conteudo = "mensagem anterior",
            StatusEntrega = StatusEntrega.Entregue,
            IdExternoWhatsapp = $"{prefixoId}{marcador}-anterior",
            EstadoProcessamentoEntrada = EstadoProcessamentoEntradaWhatsapp.Processada,
            ProcessamentoEntradaConcluidoEm = DateTime.UtcNow.AddMinutes(-1),
            EnviadaEm = DateTime.UtcNow.AddMinutes(-1)
        });
    }
    await db.SaveChangesAsync();
    return (cliente.Id, conversa.Id);
}

async Task<Guid> AceitarAsync(string telefone, string id, string conteudo = "mensagem")
{
    await using var db = new CrmDbContext(opcoes, cifra);
    return await NovoRecebimento(db).ReceberAsync(
        tenantId,
        new MensagemDoCanal(telefone, "Cliente Teste", conteudo, TipoMensagem.Texto, id));
}

async Task<int> ContarAsync(Guid conversaId, string prefixo)
{
    await using var db = new CrmDbContext(opcoes, cifra);
    return await db.Mensagens.CountAsync(m => m.ConversaId == conversaId
        && m.AutorTipo == AutorTipo.Cliente
        && m.IdExternoWhatsapp!.StartsWith(prefixo));
}

async Task<int> ContarEntradaExataAsync(Guid conversaId, string idExterno)
{
    await using var db = new CrmDbContext(opcoes, cifra);
    return await db.Mensagens.CountAsync(m => m.ConversaId == conversaId
        && m.AutorTipo == AutorTipo.Cliente
        && m.IdExternoWhatsapp == idExterno);
}

Task ProcessarAsync(Guid conversaId, IRespostaInteligenteService ia, IEnviadorDeCanal? canal = null) =>
    Task.Run(async () =>
    {
        await using var db = new CrmDbContext(opcoes, cifra);
        await NovoProcessador(db, ia, canal).ProcessarConversaAsync(conversaId);
    });

async Task<Guid> ObterAtendenteAtivoAsync()
{
    await using var db = new CrmDbContext(opcoes, cifra);
    return await db.Atendentes.Where(a => a.TenantId == tenantId && a.Ativo)
        .Select(a => a.Id)
        .FirstAsync();
}

try
{
    // A/B/C/N/Q — a mesma conversa, 2/5/20 ids distintos, contexts separados.
    var baseConcorrencia = await CriarConversaAsync("concorrencia", humano: true);
    string telefoneBase;
    await using (var db = new CrmDbContext(opcoes, cifra))
        telefoneBase = await db.Clientes.Where(c => c.Id == baseConcorrencia.ClienteId).Select(c => c.TelefoneWhatsapp).SingleAsync();

    async Task RodarDistintas(int quantidade, string grupo)
    {
        var ids = Enumerable.Range(0, quantidade).Select(i => $"{prefixoId}{grupo}-{i:D2}").ToArray();
        var erros = new ConcurrentBag<Exception>();
        await Task.WhenAll(ids.Select(async id =>
        {
            try { await AceitarAsync(telefoneBase, id); }
            catch (Exception ex) { erros.Add(ex); }
        }));
        Checar($"{quantidade} mensagens distintas: zero falhas", erros.IsEmpty);
        Checar($"{quantidade} mensagens distintas: todas persistidas", await ContarAsync(baseConcorrencia.ConversaId, $"{prefixoId}{grupo}-") == quantidade);
    }

    await RodarDistintas(2, "A");
    await RodarDistintas(5, "B");
    await RodarDistintas(20, "C");

    await using (var db = new CrmDbContext(opcoes, cifra))
    {
        var naoLidas = await db.Conversas.Where(c => c.Id == baseConcorrencia.ConversaId).Select(c => c.MensagensNaoLidas).SingleAsync();
        Checar("incremento atomico de MensagensNaoLidas", naoLidas == 27);
    }

    // D — 20 redeliveries do mesmo id.
    var idRepetido = $"{prefixoId}D-mesmo";
    var retornos = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => AceitarAsync(telefoneBase, idRepetido)));
    Checar("20 redeliveries retornam a mesma conversa", retornos.All(id => id == baseConcorrencia.ConversaId));
    var quantidadeRepetida = await ContarEntradaExataAsync(baseConcorrencia.ConversaId, idRepetido);
    Checar($"20 redeliveries persistem 1 mensagem logica (obtidas: {quantidadeRepetida})", quantidadeRepetida == 1);

    // E — mistura de ids iguais e diferentes.
    var mistura = Enumerable.Repeat($"{prefixoId}E-mesmo", 5)
        .Concat(Enumerable.Range(0, 5).Select(i => $"{prefixoId}E-{i}"))
        .ToArray();
    await Task.WhenAll(mistura.Select(id => AceitarAsync(telefoneBase, id)));
    var quantidadeMistura = await ContarAsync(baseConcorrencia.ConversaId, $"{prefixoId}E-");
    Checar($"mistura de ids iguais e diferentes preserva 6 mensagens (obtidas: {quantidadeMistura})", quantidadeMistura == 6);

    // F — conversas diferentes podem aceitar em paralelo.
    var c1 = await CriarConversaAsync("duas-1", humano: true);
    var c2 = await CriarConversaAsync("duas-2", humano: true);
    string t1, t2;
    await using (var db = new CrmDbContext(opcoes, cifra))
    {
        t1 = await db.Clientes.Where(c => c.Id == c1.ClienteId).Select(c => c.TelefoneWhatsapp).SingleAsync();
        t2 = await db.Clientes.Where(c => c.Id == c2.ClienteId).Select(c => c.TelefoneWhatsapp).SingleAsync();
    }
    await Task.WhenAll(Enumerable.Range(0, 10).SelectMany(i => new[]
    {
        AceitarAsync(t1, $"{prefixoId}F1-{i}"),
        AceitarAsync(t2, $"{prefixoId}F2-{i}")
    }));
    Checar("duas conversas diferentes persistem 20 entradas", await ContarAsync(c1.ConversaId, $"{prefixoId}F1-") + await ContarAsync(c2.ConversaId, $"{prefixoId}F2-") == 20);

    // M — timestamp nunca volta.
    var futuro = DateTime.UtcNow.AddHours(1);
    DateTime futuroPersistido;
    await using (var db = new CrmDbContext(opcoes, cifra))
    {
        await db.Conversas.Where(c => c.Id == baseConcorrencia.ConversaId).ExecuteUpdateAsync(s => s.SetProperty(c => c.UltimaMensagemEm, futuro));
        futuroPersistido = await db.Conversas.Where(c => c.Id == baseConcorrencia.ConversaId)
            .Select(c => c.UltimaMensagemEm)
            .SingleAsync();
    }
    await AceitarAsync(telefoneBase, $"{prefixoId}M-antiga");
    await using (var db = new CrmDbContext(opcoes, cifra))
        Checar("UltimaMensagemEm e monotona", await db.Conversas.Where(c => c.Id == baseConcorrencia.ConversaId).Select(c => c.UltimaMensagemEm).SingleAsync() == futuroPersistido);

    // P — duas primeiras mensagens simultaneas: um cliente, uma conversa, duas mensagens.
    var telefonePrimeiro = $"5597{Random.Shared.NextInt64(100000000, 999999999)}";
    telefones.Add(telefonePrimeiro);
    var primeiras = await Task.WhenAll(
        AceitarAsync(telefonePrimeiro, $"{prefixoId}P-1"),
        AceitarAsync(telefonePrimeiro, $"{prefixoId}P-2"));
    Checar("primeiro contato concorrente usa uma conversa", primeiras[0] == primeiras[1]);
    await using (var db = new CrmDbContext(opcoes, cifra))
    {
        var clienteP = await db.Clientes.SingleAsync(c => c.TenantId == tenantId && c.TelefoneWhatsapp == telefonePrimeiro);
        Checar("primeiro contato concorrente cria um cliente", await db.Clientes.CountAsync(c => c.TenantId == tenantId && c.TelefoneWhatsapp == telefonePrimeiro) == 1);
        Checar("primeiro contato concorrente cria uma conversa ativa", await db.Conversas.CountAsync(c => c.TenantId == tenantId && c.ClienteId == clienteP.Id && c.Status == StatusConversa.Ativa) == 1);
        Checar("primeiro contato concorrente preserva as duas mensagens", await db.Mensagens.CountAsync(m => m.ConversaId == primeiras[0] && (m.IdExternoWhatsapp == $"{prefixoId}P-1" || m.IdExternoWhatsapp == $"{prefixoId}P-2")) == 2);
    }

    // P2 — Simulado e WhatsApp sao fios independentes do mesmo cliente.
    var outroCanal = await CriarConversaAsync("canal-simulado", humano: true, canal: CanalOrigem.Simulado);
    string telefoneOutroCanal;
    await using (var db = new CrmDbContext(opcoes, cifra))
        telefoneOutroCanal = await db.Clientes.Where(c => c.Id == outroCanal.ClienteId).Select(c => c.TelefoneWhatsapp).SingleAsync();
    var conversaWhatsapp = await AceitarAsync(telefoneOutroCanal, $"{prefixoId}P2-whatsapp");
    var gatewayP2 = new GatewayDeContagem();
    await using (var db = new CrmDbContext(opcoes, cifra))
    {
        var entrada = await db.Mensagens.SingleAsync(m => m.IdExternoWhatsapp == $"{prefixoId}P2-whatsapp");
        Checar("entrada WhatsApp cria conversa propria, sem reutilizar Simulado", conversaWhatsapp != outroCanal.ConversaId);
        Checar("conversa Simulado permanece intacta", await db.Conversas.AnyAsync(c => c.Id == outroCanal.ConversaId && c.Canal == CanalOrigem.Simulado && c.Status == StatusConversa.Ativa));
        Checar("entrada pertence a conversa WhatsApp ativa", entrada.ConversaId == conversaWhatsapp && await db.Conversas.CountAsync(c => c.TenantId == tenantId && c.ClienteId == outroCanal.ClienteId && c.Canal == CanalOrigem.WhatsApp && c.Status == StatusConversa.Ativa) == 1);

        db.Mensagens.AddRange(
            new Mensagem
            {
                TenantId = tenantId, ConversaId = conversaWhatsapp, AutorTipo = AutorTipo.Sistema,
                Conteudo = "automatica WhatsApp", EhAutomatica = true, StatusEntrega = StatusEntrega.Pendente, EnviadaEm = DateTime.UtcNow
            },
            new Mensagem
            {
                TenantId = tenantId, ConversaId = outroCanal.ConversaId, AutorTipo = AutorTipo.Sistema,
                Conteudo = "automatica Simulado", EhAutomatica = true, StatusEntrega = StatusEntrega.Pendente, EnviadaEm = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var enviador = new EnviadorPeloWhatsApp(
            db, gatewayP2, anexos, new RegrasPermissivas(),
            Options.Create(new OpcoesDoCanalWhatsApp
            {
                Habilitado = true, BaseUrl = "http://verificador.local", ApiKey = "teste",
                NomeDaInstancia = "verificador", TokenDoWebhook = "teste"
            }),
            NullLogger<EnviadorPeloWhatsApp>.Instance);
        await enviador.EnviarPendentesAsync(conversaWhatsapp);
        await enviador.EnviarPendentesAsync(outroCanal.ConversaId);

        Checar("automatica WhatsApp percorre a porta real de saida", gatewayP2.EnviosDeTexto == 1);
        Checar("automatica Simulado continua bloqueada na porta de saida", gatewayP2.EnviosDeTexto == 1 && await db.Mensagens.Where(m => m.ConversaId == outroCanal.ConversaId && m.Conteudo == "automatica Simulado").Select(m => m.StatusEntrega).SingleAsync() == StatusEntrega.Pendente);
    }

    // G — Human Takeover: processa a entrada, IA silenciosa.
    var humano = await CriarConversaAsync("humano", humano: true, aguardandoIa: true, comMensagemAnterior: true);
    string telefoneHumano;
    await using (var db = new CrmDbContext(opcoes, cifra)) telefoneHumano = await db.Clientes.Where(c => c.Id == humano.ClienteId).Select(c => c.TelefoneWhatsapp).SingleAsync();
    await AceitarAsync(telefoneHumano, $"{prefixoId}G-humano");
    var iaHumano = new IaFake();
    await using (var db = new CrmDbContext(opcoes, cifra)) await NovoProcessador(db, iaHumano).ProcessarConversaAsync(humano.ConversaId);
    await using (var db = new CrmDbContext(opcoes, cifra))
    {
        var estado = await db.Mensagens.Where(m => m.IdExternoWhatsapp == $"{prefixoId}G-humano").Select(m => m.EstadoProcessamentoEntrada).SingleAsync();
        Checar("Human Takeover mantem IA silenciosa", iaHumano.Chamadas == 0 && estado == EstadoProcessamentoEntradaWhatsapp.Processada);
        Checar("Human Takeover permanece ativo", await db.Conversas.Where(c => c.Id == humano.ConversaId).Select(c => c.EmAtendimentoHumano).SingleAsync());
    }

    // H/I — IA habilitada e lenta, com serializacao por conversa.
    var lenta = await CriarConversaAsync("ia-lenta", aguardandoIa: true, comMensagemAnterior: true);
    string telefoneLenta;
    await using (var db = new CrmDbContext(opcoes, cifra)) telefoneLenta = await db.Clientes.Where(c => c.Id == lenta.ClienteId).Select(c => c.TelefoneWhatsapp).SingleAsync();
    await Task.WhenAll(
        AceitarAsync(telefoneLenta, $"{prefixoId}I-1", "primeira pergunta"),
        AceitarAsync(telefoneLenta, $"{prefixoId}I-2", "segunda pergunta"));
    var iaLenta = new IaFake(TimeSpan.FromMilliseconds(200));
    await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
    {
        await using var db = new CrmDbContext(opcoes, cifra);
        await NovoProcessador(db, iaLenta).ProcessarConversaAsync(lenta.ConversaId);
    }));
    Checar("IA habilitada processa as duas entradas", iaLenta.Chamadas == 2);
    Checar("IA lenta nunca executa duas transicoes da conversa ao mesmo tempo", iaLenta.MaximoSimultaneo == 1);
    await using (var db = new CrmDbContext(opcoes, cifra))
        Checar("respostas da IA foram persistidas", await db.Mensagens.CountAsync(m => m.ConversaId == lenta.ConversaId && m.GeradaPorIa) == 2);

    // J — erro nao apaga a entrada; fica Falha com retomada agendada.
    var erroIa = await CriarConversaAsync("ia-erro", aguardandoIa: true, comMensagemAnterior: true);
    string telefoneErro;
    await using (var db = new CrmDbContext(opcoes, cifra)) telefoneErro = await db.Clientes.Where(c => c.Id == erroIa.ClienteId).Select(c => c.TelefoneWhatsapp).SingleAsync();
    await AceitarAsync(telefoneErro, $"{prefixoId}J-erro");
    await using (var db = new CrmDbContext(opcoes, cifra)) await NovoProcessador(db, new IaFake(falhar: true)).ProcessarConversaAsync(erroIa.ConversaId);
    await using (var db = new CrmDbContext(opcoes, cifra))
    {
        var entrada = await db.Mensagens.SingleAsync(m => m.IdExternoWhatsapp == $"{prefixoId}J-erro");
        Checar("erro de IA preserva a mensagem", entrada.EstadoProcessamentoEntrada == EstadoProcessamentoEntradaWhatsapp.Falha && entrada.ProcessamentoEntradaDisponivelEm != null);
    }

    // K — primeiro contato dispara a automacao cadastrada e conclui a entrada.
    await using (var db = new CrmDbContext(opcoes, cifra)) await NovoProcessador(db, new IaFake()).ProcessarConversaAsync(primeiras[0]);
    await using (var db = new CrmDbContext(opcoes, cifra))
    {
        Checar("automacao de primeiro contato foi persistida", await db.Mensagens.AnyAsync(m => m.ConversaId == primeiras[0] && m.AutorTipo == AutorTipo.Sistema && m.EhAutomatica));
        Checar("entradas do primeiro contato foram processadas em ordem", await db.Mensagens.CountAsync(m => m.ConversaId == primeiras[0] && m.AutorTipo == AutorTipo.Cliente && m.EstadoProcessamentoEntrada == EstadoProcessamentoEntradaWhatsapp.Processada) == 2);
    }

    // O — claim Processando abandonado e antigo e recuperado.
    var recuperacao = await CriarConversaAsync("recuperacao", humano: true, comMensagemAnterior: true);
    string telefoneRecuperacao;
    await using (var db = new CrmDbContext(opcoes, cifra)) telefoneRecuperacao = await db.Clientes.Where(c => c.Id == recuperacao.ClienteId).Select(c => c.TelefoneWhatsapp).SingleAsync();
    await AceitarAsync(telefoneRecuperacao, $"{prefixoId}O-crash");
    await using (var db = new CrmDbContext(opcoes, cifra))
        await db.Mensagens.Where(m => m.IdExternoWhatsapp == $"{prefixoId}O-crash").ExecuteUpdateAsync(s => s
            .SetProperty(m => m.EstadoProcessamentoEntrada, EstadoProcessamentoEntradaWhatsapp.Processando)
            .SetProperty(m => m.ProcessamentoEntradaIniciadoEm, DateTime.UtcNow.AddMinutes(-10)));
    await using (var db = new CrmDbContext(opcoes, cifra)) await NovoProcessador(db, new IaFake()).ProcessarConversaAsync(recuperacao.ConversaId);
    await using (var db = new CrmDbContext(opcoes, cifra))
    {
        var entrada = await db.Mensagens.SingleAsync(m => m.IdExternoWhatsapp == $"{prefixoId}O-crash");
        Checar("claim abandonado e recuperado", entrada.EstadoProcessamentoEntrada == EstadoProcessamentoEntradaWhatsapp.Processada && entrada.TentativasProcessamentoEntrada >= 1);
    }

    // R1 — IA em voo perde para takeover humano, em DbContexts independentes.
    var atendenteHumanoId = await ObterAtendenteAtivoAsync();
    var takeoverEmVoo = await CriarConversaAsync("takeover-em-voo", aguardandoIa: true, comMensagemAnterior: true);
    string telefoneTakeover;
    await using (var db = new CrmDbContext(opcoes, cifra)) telefoneTakeover = await db.Clientes.Where(c => c.Id == takeoverEmVoo.ClienteId).Select(c => c.TelefoneWhatsapp).SingleAsync();
    await AceitarAsync(telefoneTakeover, $"{prefixoId}R1-takeover");
    var iaControladaTakeover = new IaControlada();
    var processamentoTakeover = ProcessarAsync(takeoverEmVoo.ConversaId, iaControladaTakeover);
    await iaControladaTakeover.AguardarInicioAsync();
    await using (var db = new CrmDbContext(opcoes, cifra))
        await db.Conversas.Where(c => c.Id == takeoverEmVoo.ConversaId).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.AtendenteId, atendenteHumanoId)
            .SetProperty(c => c.EmAtendimentoHumano, true)
            .SetProperty(c => c.AtualizadoEm, DateTime.UtcNow));
    iaControladaTakeover.Liberar();
    await processamentoTakeover;
    await using (var db = new CrmDbContext(opcoes, cifra))
    {
        Checar("IA em voo nao persiste resposta apos takeover humano", await db.Mensagens.CountAsync(m => m.ConversaId == takeoverEmVoo.ConversaId && m.GeradaPorIa) == 0);
        Checar("takeover concorrente preserva a entrada processada", await db.Mensagens.Where(m => m.IdExternoWhatsapp == $"{prefixoId}R1-takeover").Select(m => m.EstadoProcessamentoEntrada).SingleAsync() == EstadoProcessamentoEntradaWhatsapp.Processada);
    }

    // R2 — encerrar durante IA em voo tambem vence, mesmo com EmAtendimentoHumano falso.
    var encerramentoEmVoo = await CriarConversaAsync("encerramento-em-voo", aguardandoIa: true, comMensagemAnterior: true);
    string telefoneEncerramento;
    await using (var db = new CrmDbContext(opcoes, cifra)) telefoneEncerramento = await db.Clientes.Where(c => c.Id == encerramentoEmVoo.ClienteId).Select(c => c.TelefoneWhatsapp).SingleAsync();
    await AceitarAsync(telefoneEncerramento, $"{prefixoId}R2-encerrar");
    var iaControladaEncerramento = new IaControlada();
    var processamentoEncerramento = ProcessarAsync(encerramentoEmVoo.ConversaId, iaControladaEncerramento);
    await iaControladaEncerramento.AguardarInicioAsync();
    await using (var db = new CrmDbContext(opcoes, cifra))
        await db.Conversas.Where(c => c.Id == encerramentoEmVoo.ConversaId).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.Status, StatusConversa.Encerrada)
            .SetProperty(c => c.EncerradaEm, DateTime.UtcNow)
            .SetProperty(c => c.EmAtendimentoHumano, false)
            .SetProperty(c => c.AguardandoRespostaInteligente, false)
            .SetProperty(c => c.AtualizadoEm, DateTime.UtcNow));
    iaControladaEncerramento.Liberar();
    await processamentoEncerramento;
    await using (var db = new CrmDbContext(opcoes, cifra))
    {
        var conversaEncerrada = await db.Conversas.SingleAsync(c => c.Id == encerramentoEmVoo.ConversaId);
        Checar("encerramento humano em voo permanece encerrado", conversaEncerrada.Status == StatusConversa.Encerrada && conversaEncerrada.EncerradaEm != null);
        Checar("encerramento humano descarta resposta automatica obsoleta", await db.Mensagens.CountAsync(m => m.ConversaId == encerramentoEmVoo.ConversaId && m.GeradaPorIa) == 0);
        Checar("encerramento humano preserva a entrada recebida", await db.Mensagens.Where(m => m.IdExternoWhatsapp == $"{prefixoId}R2-encerrar").Select(m => m.EstadoProcessamentoEntrada).SingleAsync() == EstadoProcessamentoEntradaWhatsapp.Processada);
    }

    // R3 — mover etapa e transferir durante IA em voo nao podem ser sobrescritos.
    var moverEmVoo = await CriarConversaAsync("mover-em-voo", aguardandoIa: true, comMensagemAnterior: true);
    Guid etapaHumanaId;
    await using (var db = new CrmDbContext(opcoes, cifra))
        etapaHumanaId = await db.EtapasFunil.Where(e => e.TenantId == tenantId && e.Ativo).Select(e => e.Id).FirstAsync();
    string telefoneMover;
    await using (var db = new CrmDbContext(opcoes, cifra)) telefoneMover = await db.Clientes.Where(c => c.Id == moverEmVoo.ClienteId).Select(c => c.TelefoneWhatsapp).SingleAsync();
    await AceitarAsync(telefoneMover, $"{prefixoId}R3-mover");
    var iaControladaMover = new IaControlada();
    var processamentoMover = ProcessarAsync(moverEmVoo.ConversaId, iaControladaMover);
    await iaControladaMover.AguardarInicioAsync();
    await using (var db = new CrmDbContext(opcoes, cifra))
        await db.Conversas.Where(c => c.Id == moverEmVoo.ConversaId).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.EtapaId, etapaHumanaId)
            .SetProperty(c => c.AtendenteId, atendenteHumanoId)
            .SetProperty(c => c.EmAtendimentoHumano, true)
            .SetProperty(c => c.AtualizadoEm, DateTime.UtcNow));
    iaControladaMover.Liberar();
    await processamentoMover;
    await using (var db = new CrmDbContext(opcoes, cifra))
    {
        var conversaMovida = await db.Conversas.SingleAsync(c => c.Id == moverEmVoo.ConversaId);
        Checar("movimentacao e transferencia humanas vencem o processamento", conversaMovida.EtapaId == etapaHumanaId && conversaMovida.AtendenteId == atendenteHumanoId && conversaMovida.EmAtendimentoHumano);
        Checar("movimentacao humana descarta resposta automatica", await db.Mensagens.CountAsync(m => m.ConversaId == moverEmVoo.ConversaId && m.GeradaPorIa) == 0);
    }

    // R4 — conflito somente dos agregados tecnicos permite concluir sem segunda IA.
    var tecnicoEmVoo = await CriarConversaAsync("tecnico-em-voo", aguardandoIa: true, comMensagemAnterior: true);
    string telefoneTecnico;
    await using (var db = new CrmDbContext(opcoes, cifra)) telefoneTecnico = await db.Clientes.Where(c => c.Id == tecnicoEmVoo.ClienteId).Select(c => c.TelefoneWhatsapp).SingleAsync();
    await AceitarAsync(telefoneTecnico, $"{prefixoId}R4-tecnico");
    var iaControladaTecnica = new IaControlada();
    int naoLidasAntes;
    DateTime ultimaMensagemAntes;
    await using (var db = new CrmDbContext(opcoes, cifra))
    {
        var antes = await db.Conversas.Where(c => c.Id == tecnicoEmVoo.ConversaId)
            .Select(c => new { c.MensagensNaoLidas, c.UltimaMensagemEm })
            .SingleAsync();
        naoLidasAntes = antes.MensagensNaoLidas;
        ultimaMensagemAntes = antes.UltimaMensagemEm;
    }
    var processamentoTecnico = ProcessarAsync(tecnicoEmVoo.ConversaId, iaControladaTecnica);
    await iaControladaTecnica.AguardarInicioAsync();
    var referenciaTecnica = DateTime.UtcNow > ultimaMensagemAntes
        ? DateTime.UtcNow
        : ultimaMensagemAntes;
    var instanteTecnicoComPrecisaoDoBanco = referenciaTecnica.AddMinutes(1);
    var instanteTecnico = new DateTime(
        instanteTecnicoComPrecisaoDoBanco.Ticks
            - instanteTecnicoComPrecisaoDoBanco.Ticks % TimeSpan.TicksPerMillisecond,
        DateTimeKind.Utc);
    await using (var db = new CrmDbContext(opcoes, cifra))
        await db.Conversas.Where(c => c.Id == tecnicoEmVoo.ConversaId).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.MensagensNaoLidas, c => c.MensagensNaoLidas + 1)
            .SetProperty(c => c.UltimaMensagemEm, instanteTecnico)
            .SetProperty(c => c.AtualizadoEm, instanteTecnico));
    iaControladaTecnica.Liberar();
    await processamentoTecnico;
    await using (var db = new CrmDbContext(opcoes, cifra))
    {
        var respostasIaPersistidas = await db.Mensagens.CountAsync(m => m.ConversaId == tecnicoEmVoo.ConversaId && m.GeradaPorIa);
        Checar("conflito apenas tecnico conclui sem repetir IA", iaControladaTecnica.Chamadas == 1 && respostasIaPersistidas == 1);
        Checar("conflito apenas tecnico conclui a entrada", await db.Mensagens.Where(m => m.IdExternoWhatsapp == $"{prefixoId}R4-tecnico").Select(m => m.EstadoProcessamentoEntrada).SingleAsync() == EstadoProcessamentoEntradaWhatsapp.Processada);
        var final = await db.Conversas.SingleAsync(c => c.Id == tecnicoEmVoo.ConversaId);
        Checar("rebase tecnico preserva incremento de nao lidas", final.MensagensNaoLidas == naoLidasAntes + 1);
        Checar("rebase tecnico preserva UltimaMensagemEm concorrente", final.UltimaMensagemEm >= instanteTecnico);
        Console.WriteLine($"R4 final: MensagensNaoLidas={final.MensagensNaoLidas}; UltimaMensagemEm={final.UltimaMensagemEm:O}; ChamadasIA={iaControladaTecnica.Chamadas}; RespostasIAPersistidas={respostasIaPersistidas}");
    }

    // R5 — poison message: seis falhas, diagnostico terminal e bloqueio explicito.
    var poison = await CriarConversaAsync("poison", aguardandoIa: true, comMensagemAnterior: true);
    string telefonePoison;
    await using (var db = new CrmDbContext(opcoes, cifra)) telefonePoison = await db.Clientes.Where(c => c.Id == poison.ClienteId).Select(c => c.TelefoneWhatsapp).SingleAsync();
    var poisonId = $"{prefixoId}R5-poison";
    await AceitarAsync(telefonePoison, poisonId);
    var iaPoison = new IaFake(falhar: true);
    for (var tentativa = 1; tentativa <= 6; tentativa++)
    {
        await ProcessarAsync(poison.ConversaId, iaPoison);
        if (tentativa < 6)
        {
            await using var db = new CrmDbContext(opcoes, cifra);
            await db.Mensagens.Where(m => m.IdExternoWhatsapp == poisonId).ExecuteUpdateAsync(s =>
                s.SetProperty(m => m.ProcessamentoEntradaDisponivelEm, DateTime.UtcNow.AddSeconds(-1)));
        }
    }
    await AceitarAsync(telefonePoison, $"{prefixoId}R5-posterior");
    await ProcessarAsync(poison.ConversaId, iaPoison);
    await using (var db = new CrmDbContext(opcoes, cifra))
    {
        var entradaPoison = await db.Mensagens.SingleAsync(m => m.IdExternoWhatsapp == poisonId);
        var posteriorPoison = await db.Mensagens.SingleAsync(m => m.IdExternoWhatsapp == $"{prefixoId}R5-posterior");
        Checar("poison chega ao limite sem retry infinito", iaPoison.Chamadas == 6 && entradaPoison.TentativasProcessamentoEntrada == 6);
        Checar("poison fica em Falha terminal diagnosticavel", entradaPoison.EstadoProcessamentoEntrada == EstadoProcessamentoEntradaWhatsapp.Falha && entradaPoison.ProcessamentoEntradaDisponivelEm == null && entradaPoison.ErroProcessamentoEntrada!.StartsWith("FALHA TERMINAL"));
        Checar("poison preserva posterior duravel e bloqueada em ordem", posteriorPoison.EstadoProcessamentoEntrada == EstadoProcessamentoEntradaWhatsapp.Pendente);
    }

    // R6 — varredura: pendente, backoff, stale e saida automatica.
    var pendenteVarredura = await CriarConversaAsync("varredura-pendente", humano: true);
    var falhaFuturaVarredura = await CriarConversaAsync("varredura-falha-futura", humano: true);
    var falhaVencidaVarredura = await CriarConversaAsync("varredura-falha-vencida", humano: true);
    var processandoRecenteVarredura = await CriarConversaAsync("varredura-processando-recente", humano: true);
    var processandoStaleVarredura = await CriarConversaAsync("varredura-processando-stale", humano: true);
    var saidaVarredura = await CriarConversaAsync("varredura-saida", humano: true);
    var saidaSimuladaVarredura = await CriarConversaAsync("varredura-saida-simulada", humano: true, canal: CanalOrigem.Simulado);
    string telefonePendente, telefoneFalhaFutura, telefoneFalhaVencida, telefoneRecente, telefoneStale;
    await using (var db = new CrmDbContext(opcoes, cifra))
    {
        telefonePendente = await db.Clientes.Where(c => c.Id == pendenteVarredura.ClienteId).Select(c => c.TelefoneWhatsapp).SingleAsync();
        telefoneFalhaFutura = await db.Clientes.Where(c => c.Id == falhaFuturaVarredura.ClienteId).Select(c => c.TelefoneWhatsapp).SingleAsync();
        telefoneFalhaVencida = await db.Clientes.Where(c => c.Id == falhaVencidaVarredura.ClienteId).Select(c => c.TelefoneWhatsapp).SingleAsync();
        telefoneRecente = await db.Clientes.Where(c => c.Id == processandoRecenteVarredura.ClienteId).Select(c => c.TelefoneWhatsapp).SingleAsync();
        telefoneStale = await db.Clientes.Where(c => c.Id == processandoStaleVarredura.ClienteId).Select(c => c.TelefoneWhatsapp).SingleAsync();
    }
    await AceitarAsync(telefonePendente, $"{prefixoId}R6-pendente");
    await AceitarAsync(telefoneFalhaFutura, $"{prefixoId}R6-falha-futura");
    await AceitarAsync(telefoneFalhaVencida, $"{prefixoId}R6-falha-vencida");
    await AceitarAsync(telefoneRecente, $"{prefixoId}R6-processando-recente");
    await AceitarAsync(telefoneStale, $"{prefixoId}R6-processando-stale");
    await using (var db = new CrmDbContext(opcoes, cifra))
    {
        await db.Mensagens.Where(m => m.IdExternoWhatsapp == $"{prefixoId}R6-falha-futura").ExecuteUpdateAsync(s => s
            .SetProperty(m => m.EstadoProcessamentoEntrada, EstadoProcessamentoEntradaWhatsapp.Falha)
            .SetProperty(m => m.ProcessamentoEntradaDisponivelEm, DateTime.UtcNow.AddMinutes(10)));
        await db.Mensagens.Where(m => m.IdExternoWhatsapp == $"{prefixoId}R6-falha-vencida").ExecuteUpdateAsync(s => s
            .SetProperty(m => m.EstadoProcessamentoEntrada, EstadoProcessamentoEntradaWhatsapp.Falha)
            .SetProperty(m => m.ProcessamentoEntradaDisponivelEm, DateTime.UtcNow.AddMinutes(-1)));
        await db.Mensagens.Where(m => m.IdExternoWhatsapp == $"{prefixoId}R6-processando-recente").ExecuteUpdateAsync(s => s
            .SetProperty(m => m.EstadoProcessamentoEntrada, EstadoProcessamentoEntradaWhatsapp.Processando)
            .SetProperty(m => m.ProcessamentoEntradaIniciadoEm, DateTime.UtcNow));
        await db.Mensagens.Where(m => m.IdExternoWhatsapp == $"{prefixoId}R6-processando-stale").ExecuteUpdateAsync(s => s
            .SetProperty(m => m.EstadoProcessamentoEntrada, EstadoProcessamentoEntradaWhatsapp.Processando)
            .SetProperty(m => m.ProcessamentoEntradaIniciadoEm, DateTime.UtcNow.AddMinutes(-10)));
        db.Mensagens.AddRange(
            new Mensagem
            {
                TenantId = tenantId,
                ConversaId = saidaVarredura.ConversaId,
                AutorTipo = AutorTipo.Sistema,
                Conteudo = "saida automatica WhatsApp pendente",
                EhAutomatica = true,
                StatusEntrega = StatusEntrega.Pendente,
                EnviadaEm = DateTime.UtcNow
            },
            new Mensagem
            {
                TenantId = tenantId,
                ConversaId = saidaSimuladaVarredura.ConversaId,
                AutorTipo = AutorTipo.Sistema,
                Conteudo = "saida automatica Simulado pendente",
                EhAutomatica = true,
                StatusEntrega = StatusEntrega.Pendente,
                EnviadaEm = DateTime.UtcNow
            });
        await db.SaveChangesAsync();
    }
    IReadOnlyList<Guid> conversasDaVarredura;
    await using (var db = new CrmDbContext(opcoes, cifra)) conversasDaVarredura = await NovoProcessador(db, new IaFake()).ListarConversasPendentesAsync();
    Checar("varredura encontra pendente, falha vencida, claim stale e saida WhatsApp pendente", conversasDaVarredura.Contains(pendenteVarredura.ConversaId) && conversasDaVarredura.Contains(falhaVencidaVarredura.ConversaId) && conversasDaVarredura.Contains(processandoStaleVarredura.ConversaId) && conversasDaVarredura.Contains(saidaVarredura.ConversaId));
    Checar("varredura respeita backoff e claim recente", !conversasDaVarredura.Contains(falhaFuturaVarredura.ConversaId) && !conversasDaVarredura.Contains(processandoRecenteVarredura.ConversaId));
    Checar("varredura nao acorda saida pendente de Simulado", !conversasDaVarredura.Contains(saidaSimuladaVarredura.ConversaId));

    // L — o processador chama a mesma porta de saida; WA-OUT-01 tem verificador proprio.
    var canal = new CanalContador();
    await using (var db = new CrmDbContext(opcoes, cifra)) await NovoProcessador(db, new IaFake(), canal).ProcessarConversaAsync(lenta.ConversaId);
    Checar("porta de saida WA-OUT-01 continua acionada apos commit", canal.Varreduras == 1);
}
finally
{
    await using var db = new CrmDbContext(opcoes, cifra);
    var telefonesCriados = telefones.ToArray();
    var clientes = await db.Clientes.Where(c => c.TenantId == tenantId && (c.Nome.StartsWith(prefixoNome) || telefonesCriados.Contains(c.TelefoneWhatsapp))).Select(c => c.Id).ToListAsync();
    var conversas = await db.Conversas.Where(c => clientes.Contains(c.ClienteId)).Select(c => c.Id).ToListAsync();
    await db.Mensagens.Where(m => conversas.Contains(m.ConversaId)).ExecuteDeleteAsync();
    await db.HistoricoEtapas.Where(h => conversas.Contains(h.ConversaId)).ExecuteDeleteAsync();
    await db.Conversas.Where(c => conversas.Contains(c.Id)).ExecuteDeleteAsync();
    await db.Clientes.Where(c => clientes.Contains(c.Id)).ExecuteDeleteAsync();
}

return falhas == 0 ? 0 : 1;

file sealed class IaFake(TimeSpan? atraso = null, bool falhar = false) : IRespostaInteligenteService
{
    private int _chamadas;
    private int _simultaneas;
    private int _maximo;
    public int Chamadas => _chamadas;
    public int MaximoSimultaneo => _maximo;

    public async Task<RespostaDaIA> ResponderAsync(Guid tenantId, IReadOnlyList<TrocaAnterior> historico, string mensagemDoCliente, ContextoDaChamada? contexto = null, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _chamadas);
        var atuais = Interlocked.Increment(ref _simultaneas);
        int observado;
        while (atuais > (observado = _maximo)) Interlocked.CompareExchange(ref _maximo, atuais, observado);
        try
        {
            if (atraso is { } tempo) await Task.Delay(tempo, ct);
            if (falhar) throw new InvalidOperationException("Falha de IA simulada.");
            return new RespostaDaIA(true, $"Resposta para: {mensagemDoCliente}", "ok");
        }
        finally
        {
            Interlocked.Decrement(ref _simultaneas);
        }
    }
}

file sealed class IaControlada : IRespostaInteligenteService
{
    private readonly TaskCompletionSource _iniciada = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _liberada = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _chamadas;

    public int Chamadas => _chamadas;

    public Task AguardarInicioAsync() => _iniciada.Task.WaitAsync(TimeSpan.FromSeconds(15));

    public void Liberar() => _liberada.TrySetResult();

    public async Task<RespostaDaIA> ResponderAsync(
        Guid tenantId,
        IReadOnlyList<TrocaAnterior> historico,
        string mensagemDoCliente,
        ContextoDaChamada? contexto = null,
        CancellationToken ct = default)
    {
        Interlocked.Increment(ref _chamadas);
        _iniciada.TrySetResult();
        await _liberada.Task.WaitAsync(ct);
        return new RespostaDaIA(true, $"Resposta controlada para: {mensagemDoCliente}", "ok");
    }
}

file sealed class CanalContador : IEnviadorDeCanal
{
    private int _varreduras;
    public int Varreduras => _varreduras;
    public Task<string?> EnviarAsync(Guid mensagemId, CancellationToken ct = default) => Task.FromResult<string?>(null);
    public Task EnviarPendentesAsync(Guid conversaId, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _varreduras);
        return Task.CompletedTask;
    }
}

file sealed class CanalSilencioso : IEnviadorDeCanal
{
    public Task<string?> EnviarAsync(Guid mensagemId, CancellationToken ct = default) => Task.FromResult<string?>(null);
    public Task EnviarPendentesAsync(Guid conversaId, CancellationToken ct = default) => Task.CompletedTask;
}

file sealed class GatewayDeContagem : IEvolutionClient
{
    public int EnviosDeTexto { get; private set; }

    public Task<string> EnviarTextoAsync(string telefoneE164, string conteudo, CancellationToken ct)
    {
        EnviosDeTexto++;
        return Task.FromResult("ENVIO-VERIFICADOR");
    }

    public Task<EstadoDaInstancia> GarantirInstanciaAsync(string urlDoWebhook, CancellationToken ct) => throw new NotSupportedException();
    public Task<EstadoDaInstancia> ObterEstadoAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<EstadoDaInstancia> IniciarConexaoAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task DesconectarAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<PerfilDaInstancia?> ObterPerfilDaInstanciaAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<string> EnviarMidiaAsync(string telefoneE164, byte[] conteudo, string tipoMime, string nomeDoArquivo, string? legenda, CancellationToken ct) => throw new NotSupportedException();
    public Task<byte[]?> BaixarMidiaAsync(string idExternoWhatsapp, CancellationToken ct) => throw new NotSupportedException();
    public Task<string?> ObterFotoPerfilUrlAsync(string telefoneE164, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<ContatoDoWhatsApp>> ObterContatosAsync(CancellationToken ct) => throw new NotSupportedException();
}

file sealed class RegrasPermissivas : IRegrasDoCanal
{
    public Task<VeredictoDoNumero> PodeFalarComAsync(Guid tenantId, string telefoneE164, CancellationToken ct = default) => Task.FromResult(new VeredictoDoNumero(true, null));
    public Task<RegrasDoCanalResponse> ObterAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<RegrasDoCanalResponse> DefinirModoAsync(DefinirModoRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<NumeroDoCanalResponse> AdicionarAsync(AdicionarNumeroRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    public Task RemoverAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task RegistrarDescarteAsync(Guid tenantId, string telefoneE164, MotivoDoDescarte motivo, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<DescartesResponse> DescartesRecentesAsync(CancellationToken ct = default) => throw new NotSupportedException();
}

file sealed class AnexosSilenciosos : IArmazenamentoDeAnexos
{
    public long TamanhoMaximoBytes => long.MaxValue;
    public bool TipoPermitido(string? tipoMime) => true;
    public Task<AnexoGravado> GravarAsync(Guid tenantId, Guid mensagemId, DateTime data, byte[] conteudo, string? nome, string? tipoMime, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<byte[]?> LerAsync(string caminhoRelativo, CancellationToken ct = default) => Task.FromResult<byte[]?>(null);
}

file sealed class NotificadorSilencioso : INotificadorDeAtendimento
{
    public Task ConversaAtribuidaAsync(Guid a, ConversaResumoResponse b, CancellationToken ct = default) => Task.CompletedTask;
    public Task ConversaLiberadaAsync(Guid a, ConversaResumoResponse b, CancellationToken ct = default) => Task.CompletedTask;
    public Task ConversaTransferidaAsync(Guid a, ConversaResumoResponse b, CancellationToken ct = default) => Task.CompletedTask;
    public Task ConversaAtualizadaAsync(Guid a, ConversaResumoResponse b, CancellationToken ct = default) => Task.CompletedTask;
    public Task ConversaEncerradaAsync(Guid a, ConversaResumoResponse b, CancellationToken ct = default) => Task.CompletedTask;
    public Task ConversaReabertaAsync(Guid a, ConversaResumoResponse b, CancellationToken ct = default) => Task.CompletedTask;
    public Task MensagemRecebidaAsync(Guid a, MensagemResponse b, ConversaResumoResponse c, CancellationToken ct = default) => Task.CompletedTask;
}
