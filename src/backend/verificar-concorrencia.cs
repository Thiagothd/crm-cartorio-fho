#:project CrmCartorio.Api/CrmCartorio.Api.csproj
#:property PublishAot=false

using System.Collections.Concurrent;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Atendimento.Application;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.Clientes.Domain.Entities;
using CrmCartorio.Api.Modules.RespostaInteligente.Application;
using CrmCartorio.Api.Modules.RespostaInteligente.Application.Provedores;
using CrmCartorio.Api.Modules.Usuarios.Domain;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// VERIFICADOR DE CONCORRENCIA
//
// Rode com:  dotnet run verificar-concorrencia.cs ["<connection string>"]
//
// Exercita os dois pontos do sistema sensiveis a corrida, chamando os SERVICOS
// REAIS — nao uma copia da consulta:
//
//   1. ConversaService.CriarAsync — N atendimentos abertos no mesmo instante nao
//      podem receber o mesmo protocolo. A defesa e o UPDLOCK/HOLDLOCK na leitura
//      do ultimo numero do mes, com o indice unico como rede de seguranca.
//
//   2. ConversaService.AssumirAsync — N atendentes assumindo a mesma conversa ao
//      mesmo tempo. Exatamente UM ganha; os demais recebem
//      ConflitoDeAtribuicaoException, com o nome de quem ganhou.
//
// Chamar o servico real importa: a primeira versao deste arquivo copiava so o
// UPDATE condicional e passava, enquanto a API devolvia 500 — a transacao
// explicita do servico era incompativel com a estrategia de retry configurada.
// Copia de consulta testa a copia, nao o sistema.
//
// ATENCAO: este verificador ESCREVE no banco. Rode SOMENTE contra base de
// desenvolvimento. Ao final remove tudo que criou.
//
// As conversas criadas aqui nascem sem setor, entao disparam a mensagem de
// boas-vindas se houver regra ativa — e por isso o disparador real e injetado, e
// nao um dublê. O que se mede continua sendo protocolo e trava; a automacao entra
// junto de proposito, para que a corrida seja exercitada com o mesmo trabalho que
// a API faz de verdade. Quem testa a automacao em si e verificar-automacao.cs.

const int Simultaneas = 12;
const string PrefixoDeTeste = "[TESTE] ";

var conexao = args.Length > 0
    ? args[0]
    : "Server=.\\SQLEXPRESS;Database=CRM_Cartorio_Dev;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

// Mesma configuracao da API — de proposito. Este verificador ja deixou passar um
// erro por montar o DbContext de um jeito diferente do de producao.
var construtor = new DbContextOptionsBuilder<CrmDbContext>();
ConfiguracaoDoBanco.Configurar(construtor, conexao);
var opcoes = construtor.Options;

await using (var checagem = new CrmDbContext(opcoes))
{
    if (!string.Equals(
            checagem.Database.GetDbConnection().Database,
            "CRM_Cartorio_Dev",
            StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            "Recusado: o verificador so escreve em CRM_Cartorio_Dev.");
    }

    if (!await checagem.Database.CanConnectAsync())
    {
        Console.WriteLine("Nao foi possivel conectar. Verifique a connection string.");
        return 1;
    }
}

Guid tenantId;
(Guid Id, string Nome, PerfilDeAtendente Perfil)[] atendentes;
List<Guid> clientesDeTeste = [];

await using (var db = new CrmDbContext(opcoes))
{
    tenantId = await db.Tenants.Select(t => t.Id).FirstAsync();

    atendentes = await db.Atendentes
        .Where(a => a.Ativo)
        .OrderBy(a => a.Nome)
        .Select(a => new ValueTuple<Guid, string, PerfilDeAtendente>(a.Id, a.Nome, a.Perfil))
        .Take(4)
        .ToArrayAsync();

    // Um cliente por conversa: CriarAsync recusa abrir um segundo atendimento
    // para quem ja tem um em aberto, e essa regra nao e o que estamos testando.
    //
    // Simultaneas para o teste 1, mais um para cada um dos testes 2 a 6.
    for (var i = 0; i < Simultaneas + 5; i++)
    {
        var telefone = $"559999900{i:D3}";

        var existente = await db.Clientes
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.TelefoneWhatsapp == telefone);

        if (existente is null)
        {
            var cliente = new Cliente
            {
                TenantId = tenantId,
                Nome = $"{PrefixoDeTeste}Concorrencia {i:D2}",
                TelefoneWhatsapp = telefone,
                PrimeiroContatoEm = DateTime.UtcNow,
                Ativo = true
            };

            db.Clientes.Add(cliente);
            await db.SaveChangesAsync();
            clientesDeTeste.Add(cliente.Id);
        }
        else
        {
            clientesDeTeste.Add(existente.Id);
        }
    }
}

if (atendentes.Length < 2)
{
    Console.WriteLine("Sao necessarios ao menos 2 atendentes ativos. Rode o seed antes.");
    return 1;
}

Console.WriteLine($"Tenant .... {tenantId}");
Console.WriteLine($"Atendentes  {atendentes.Length}");
Console.WriteLine($"Disparos .. {Simultaneas} simultaneos por teste");
Console.WriteLine();

var problemas = 0;

ConversaService Servico(CrmDbContext db, int indiceDoAtendente)
{
    var (id, nome, perfil) = atendentes[indiceDoAtendente % atendentes.Length];

    return new ConversaService(
        db,
        new ContextoFixo(tenantId, id, nome, perfil),
        new GeradorDeProtocolo(db),
        new NotificadorSilencioso(),
        new DisparadorDeMensagemAutomatica(db, NullLogger<DisparadorDeMensagemAutomatica>.Instance),
        new CanalSilencioso(),
        new FilaSilenciosa(),
        NullLogger<ConversaService>.Instance);
}

// ---------------------------------------------------------------------------
// TESTE 1 — protocolos simultaneos, via ConversaService.CriarAsync
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 1 — geracao de protocolo sob concorrencia");

var largada1 = new TaskCompletionSource();
var protocolos = new ConcurrentBag<string>();
var errosCriacao = new ConcurrentBag<string>();

var criacoes = Enumerable.Range(0, Simultaneas).Select(async i =>
{
    // Todas param aqui ate a largada: sem isso elas se enfileiram naturalmente
    // e o teste nao exercita corrida nenhuma.
    await largada1.Task;

    try
    {
        await using var db = new CrmDbContext(opcoes);
        var servico = Servico(db, i);

        var conversa = await servico.CriarAsync(
            new CriarConversaRequest(clientesDeTeste[i], null, null, "Mensagem do verificador."));

        protocolos.Add(conversa.Protocolo);
    }
    catch (Exception ex)
    {
        errosCriacao.Add($"{ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
    }
}).ToArray();

largada1.SetResult();
await Task.WhenAll(criacoes);

var lista = protocolos.ToList();
var duplicados = lista.GroupBy(p => p).Where(g => g.Count() > 1).ToList();

Console.WriteLine($"  protocolos gerados ... {lista.Count}");
Console.WriteLine($"  distintos ............ {lista.Distinct().Count()}");
Console.WriteLine($"  duplicados ........... {duplicados.Count}");
Console.WriteLine($"  erros ................ {errosCriacao.Count}");

foreach (var erro in errosCriacao.Take(5))
{
    Console.WriteLine($"    ! {erro}");
}

if (lista.Count > 0)
{
    Console.WriteLine($"  faixa ................ {lista.Min()} .. {lista.Max()}");
}

if (duplicados.Count > 0)
{
    problemas++;
    Console.WriteLine("  RESULTADO: FALHOU — protocolo duplicado:");
    foreach (var d in duplicados)
    {
        Console.WriteLine($"    {d.Key} x{d.Count()}");
    }
}
else if (lista.Count != Simultaneas)
{
    problemas++;
    Console.WriteLine($"  RESULTADO: FALHOU — {Simultaneas - lista.Count} chamada(s) nao concluiram.");
}
else
{
    Console.WriteLine("  RESULTADO: OK — nenhum protocolo duplicado, nenhuma chamada perdida.");
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 2 — trava de conversa, via ConversaService.AssumirAsync
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 2 — trava de conversa sob concorrencia");

Guid alvo;

await using (var db = new CrmDbContext(opcoes))
{
    var criada = await Servico(db, 0).CriarAsync(
        new CriarConversaRequest(clientesDeTeste[Simultaneas], null, null, "Conversa disputada."));

    alvo = criada.Id;
}

var largada2 = new TaskCompletionSource();
var vencedores = new ConcurrentBag<string>();
var conflitos = new ConcurrentBag<string>();
var errosInesperados = new ConcurrentBag<string>();

var disputa = Enumerable.Range(0, Simultaneas).Select(async i =>
{
    await largada2.Task;

    try
    {
        await using var db = new CrmDbContext(opcoes);
        var resultado = await Servico(db, i).AssumirAsync(alvo);

        vencedores.Add(resultado.AtendenteNome ?? "?");
    }
    catch (ConflitoDeAtribuicaoException ex)
    {
        conflitos.Add(ex.AtendenteNome);
    }
    catch (Exception ex)
    {
        errosInesperados.Add($"{ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
    }
}).ToArray();

largada2.SetResult();
await Task.WhenAll(disputa);

// Quem repetiu o atendente que ja tinha ganho recebe a conversa de volta em vez
// de conflito — AssumirAsync e idempotente. Por isso o esperado nao e "1 vitoria"
// e sim "um unico atendente vitorioso".
var distintos = vencedores.Distinct().ToList();

Console.WriteLine($"  tentativas ............... {Simultaneas}");
Console.WriteLine($"  respostas de sucesso ..... {vencedores.Count}");
Console.WriteLine($"  atendentes vitoriosos .... {distintos.Count} ({string.Join(", ", distintos)})");
Console.WriteLine($"  conflitos tratados ....... {conflitos.Count}");
Console.WriteLine($"  erros inesperados ........ {errosInesperados.Count}");

foreach (var erro in errosInesperados.Take(5))
{
    Console.WriteLine($"    ! {erro}");
}

if (errosInesperados.Count > 0)
{
    problemas++;
    Console.WriteLine("  RESULTADO: FALHOU — houve erro nao tratado.");
}
else if (distintos.Count != 1)
{
    problemas++;
    Console.WriteLine($"  RESULTADO: FALHOU — esperado 1 atendente vitorioso, houve {distintos.Count}.");
}
else if (conflitos.Count + vencedores.Count != Simultaneas)
{
    problemas++;
    Console.WriteLine("  RESULTADO: FALHOU — alguma chamada nao terminou nem em sucesso nem em conflito.");
}
else
{
    Console.WriteLine("  RESULTADO: OK — um unico atendente assumiu; o resto virou conflito tratado.");
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 3 — envio simultaneo em conversa SEM DONO
//
// Responder uma conversa da fila assume junto, e a atribuicao passou a nascer
// dentro de MensagemService.EnviarAsync. Se a trava tivesse enfraquecido nessa
// mudanca, N atendentes escrevendo ao mesmo tempo gravariam N mensagens numa
// conversa que deveria pertencer a um so.
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 3 — envio simultaneo assume sem furar a trava");

Guid disputada;

await using (var db = new CrmDbContext(opcoes))
{
    var criada = await Servico(db, 0).CriarAsync(
        new CriarConversaRequest(clientesDeTeste[Simultaneas + 1], null, null, "Cliente escreveu primeiro."));

    // CriarAsync ja deixa a conversa sem dono; e esse o estado sob teste.
    disputada = criada.Id;
}

var largada3 = new TaskCompletionSource();
var enviosAceitos = new ConcurrentBag<string>();
var conflitosNoEnvio = new ConcurrentBag<string>();
var errosNoEnvio = new ConcurrentBag<string>();

var envios = Enumerable.Range(0, Simultaneas).Select(async i =>
{
    await largada3.Task;

    try
    {
        await using var db = new CrmDbContext(opcoes);
        var (id, nome, _) = atendentes[i % atendentes.Length];

        // Perfil Atendente para TODOS, ignorando o perfil semeado de proposito.
        //
        // Tres dos quatro atendentes da base sao Supervisor ou Administrador, e
        // para esses o MensagemService PERMITE escrever em conversa de terceiro
        // (PermissaoDoSistema.AtendimentoMexerNoDeTerceiro) — comportamento
        // desejado, nao furo. Com o
        // perfil real, quem lesse a conversa depois de outro ja ter assumido
        // passaria direto pela checagem de responsavel e gravaria, fazendo este
        // teste falhar de forma intermitente conforme o escalonamento do dia.
        //
        // O que o teste mede e a TRAVA, nao o privilegio de supervisao. Nivelar o
        // perfil isola uma coisa da outra.
        var ctx = new ContextoFixo(tenantId, id, nome, PerfilDeAtendente.Atendente);

        // Habilitada=false: nao e o que este verificador mede, e assim nao precisa
        // de chave da Anthropic para rodar. Ver o mesmo comentario em
        // verificar-automacao.cs.
        var opcoesDaIA = Options.Create(new OpcoesDaIA { Habilitada = false });

        var mensagens = new MensagemService(
            db,
            ctx,
            Servico(db, i),
            new NotificadorSilencioso(),
            new DisparadorDeMensagemAutomatica(db, NullLogger<DisparadorDeMensagemAutomatica>.Instance),
            new CanalSilencioso(),
            new AnexosEmMemoria(),
            new RespostaInteligenteService(
                db, new OrquestradorNuncaChamado(), opcoesDaIA, NullLogger<RespostaInteligenteService>.Instance),
            opcoesDaIA,
            NullLogger<MensagemService>.Instance);

        var enviada = await mensagens.EnviarAsync(
            disputada, new EnviarMensagemRequest($"Resposta simultanea {i}."));

        enviosAceitos.Add(enviada.AtendenteNome ?? "?");
    }
    catch (ConflitoDeAtribuicaoException ex)
    {
        conflitosNoEnvio.Add(ex.AtendenteNome);
    }
    catch (AtendimentoServiceException ex) when (ex.Codigo == "nao_eh_o_responsavel")
    {
        // Perdeu a corrida por um caminho diferente: assumiu depois de outro, e a
        // checagem de responsavel barrou antes de gravar. Tambem e recusa correta.
        conflitosNoEnvio.Add(ex.Message);
    }
    catch (Exception ex)
    {
        errosNoEnvio.Add($"{ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
    }
}).ToArray();

largada3.SetResult();
await Task.WhenAll(envios);

int mensagensGravadas;
Guid? donoFinal;

await using (var db = new CrmDbContext(opcoes))
{
    mensagensGravadas = await db.Mensagens
        .CountAsync(m => m.ConversaId == disputada && m.AutorTipo == AutorTipo.Atendente);

    donoFinal = await db.Conversas
        .Where(c => c.Id == disputada)
        .Select(c => c.AtendenteId)
        .FirstAsync();
}

var autoresDistintos = enviosAceitos.Distinct().ToList();

Console.WriteLine($"  tentativas de envio ...... {Simultaneas}");
Console.WriteLine($"  envios aceitos ........... {enviosAceitos.Count}");
Console.WriteLine($"  autores distintos ........ {autoresDistintos.Count} ({string.Join(", ", autoresDistintos)})");
Console.WriteLine($"  recusas tratadas ......... {conflitosNoEnvio.Count}");
Console.WriteLine($"  erros inesperados ........ {errosNoEnvio.Count}");
Console.WriteLine($"  mensagens gravadas ....... {mensagensGravadas}");
Console.WriteLine($"  conversa tem dono ........ {(donoFinal is null ? "NAO" : "sim")}");

foreach (var erro in errosNoEnvio.Take(5))
{
    Console.WriteLine($"    ! {erro}");
}

if (errosNoEnvio.Count > 0)
{
    problemas++;
    Console.WriteLine("  RESULTADO: FALHOU — houve erro nao tratado.");
}
else if (autoresDistintos.Count != 1)
{
    problemas++;
    Console.WriteLine($"  RESULTADO: FALHOU — esperado 1 autor, houve {autoresDistintos.Count}.");
}
else if (mensagensGravadas != enviosAceitos.Count)
{
    problemas++;
    Console.WriteLine("  RESULTADO: FALHOU — mensagem gravada sem envio aceito correspondente.");
}
else if (donoFinal is null)
{
    problemas++;
    Console.WriteLine("  RESULTADO: FALHOU — a conversa continuou sem dono depois do envio.");
}
else
{
    Console.WriteLine("  RESULTADO: OK — um unico atendente assumiu e escreveu; o resto foi recusado.");
}

// ---------------------------------------------------------------------------
// TESTE 4 — Human Takeover (BUG-ATD-01): IA em voo quando o atendente assume
// — a resposta gerada ANTES do takeover nao pode ser entregue DEPOIS.
//
// Cenarios B/G do plano: a chamada a IA pode levar segundos; nesse meio
// tempo um atendente pode assumir a conversa. AplicadorDeRespostaInteligente
// releu' o estado (recheck) logo apos a IA responder, exatamente para fechar
// esta corrida — este teste teve prova real, sob concorrencia de verdade
// (dois DbContext/conexoes distintas), nao so a logica isolada.
//
// Duas conclusoes SAO aceitas como corretas, e o teste distingue qual
// aconteceu: (a) o recheck descarta a resposta, SimularRecebimentoAsync
// termina normalmente, zero mensagem da IA gravada; ou (b) o SaveChanges
// final de SimularRecebimentoAsync colide com o RowVersion que o Assumir
// concorrente ja avancou (colisao PRE-EXISTENTE, documentada no plano —
// nao e' o que este teste foi desenhado para fechar) e a chamada inteira
// e desfeita, o que tambem satisfaz a garantia central: nenhuma resposta
// da IA e' entregue por cima do atendente. So (b) NAO seria aceitavel se a
// mensagem do cliente tambem se perdesse SEM nenhuma indicacao — por isso o
// teste loga qual caminho ocorreu, em vez de esconder a diferenca.
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 4 — IA em voo quando o atendente assume; resposta nao pode ser entregue depois");

Guid conversaEmVoo;

await using (var db = new CrmDbContext(opcoes))
{
    var criada = await Servico(db, 0).CriarAsync(
        new CriarConversaRequest(clientesDeTeste[Simultaneas + 2], null, null, "Oi"));

    conversaEmVoo = criada.Id;

    // Pula direto para "ja passou da triagem, a IA esta ouvindo" — o mesmo
    // atalho usado em verificar-automacao.cs para este cenario.
    var c = await db.Conversas.FirstAsync(x => x.Id == conversaEmVoo);
    c.AguardandoTriagem = false;
    c.AguardandoRespostaInteligente = true;
    await db.SaveChangesAsync();
}

var iaEmVoo = new TaskCompletionSource();
var liberaResposta = new TaskCompletionSource();
var atendenteQueAssumiu = atendentes[0];
Exception? excecaoDaSimulacao = null;

var tarefaDaIa = Task.Run(async () =>
{
    await using var db = new CrmDbContext(opcoes);
    var ctx = new ContextoFixo(tenantId, atendenteQueAssumiu.Id, atendenteQueAssumiu.Nome, PerfilDeAtendente.Atendente);
    var opcoesDaIA = Options.Create(new OpcoesDaIA { Habilitada = true });

    var mensagens = new MensagemService(
        db,
        ctx,
        Servico(db, 0),
        new NotificadorSilencioso(),
        new DisparadorDeMensagemAutomatica(db, NullLogger<DisparadorDeMensagemAutomatica>.Instance),
        new CanalSilencioso(),
        new AnexosEmMemoria(),
        new RespostaInteligenteAtrasada(iaEmVoo, liberaResposta.Task),
        opcoesDaIA,
        NullLogger<MensagemService>.Instance);

    try
    {
        await mensagens.SimularRecebimentoAsync(conversaEmVoo, new SimularMensagemDeClienteRequest("uma pergunta qualquer"));
    }
    catch (Exception ex)
    {
        excecaoDaSimulacao = ex;
    }
});

// So dispara o Assumir DEPOIS que a IA de fato comecou a "responder" — senao
// o teste nao prova nada sobre a corrida.
await iaEmVoo.Task;

await using (var db = new CrmDbContext(opcoes))
{
    await Servico(db, 0).AssumirAsync(conversaEmVoo);
}

// So agora libera a resposta da IA — DEPOIS que o takeover ja aconteceu de
// verdade no banco.
liberaResposta.SetResult();
await tarefaDaIa;

int mensagensDaIaGravadas;
bool emAtendimentoHumanoApos;
Guid? atendenteApos;

await using (var db = new CrmDbContext(opcoes))
{
    mensagensDaIaGravadas = await db.Mensagens
        .CountAsync(m => m.ConversaId == conversaEmVoo && m.AutorTipo == AutorTipo.Sistema && m.GeradaPorIa);

    var conversaApos = await db.Conversas.AsNoTracking().FirstAsync(x => x.Id == conversaEmVoo);
    emAtendimentoHumanoApos = conversaApos.EmAtendimentoHumano;
    atendenteApos = conversaApos.AtendenteId;
}

Console.WriteLine($"  mensagens da IA gravadas .. {mensagensDaIaGravadas}");
Console.WriteLine($"  EmAtendimentoHumano ....... {emAtendimentoHumanoApos}");
Console.WriteLine($"  atendente que assumiu ..... {(atendenteApos == atendenteQueAssumiu.Id ? "confirmado" : "ERRADO")}");
Console.WriteLine(excecaoDaSimulacao is DbUpdateConcurrencyException
    ? "  caminho ................... (b) colisao de RowVersion desfez a chamada inteira (aceitavel, ver comentario acima)"
    : excecaoDaSimulacao is null
        ? "  caminho ................... (a) recheck descartou a resposta, chamada terminou normalmente"
        : $"  caminho ................... EXCECAO INESPERADA: {excecaoDaSimulacao.GetType().Name}: {excecaoDaSimulacao.Message.Split('\n')[0]}");

if (excecaoDaSimulacao is not null and not DbUpdateConcurrencyException)
{
    problemas++;
    Console.WriteLine("  RESULTADO: FALHOU — excecao inesperada (nao e a colisao de RowVersion conhecida).");
}
else if (mensagensDaIaGravadas > 0)
{
    problemas++;
    Console.WriteLine("  RESULTADO: FALHOU — a resposta da IA gerada ANTES do takeover foi gravada mesmo assim.");
}
else if (!emAtendimentoHumanoApos || atendenteApos != atendenteQueAssumiu.Id)
{
    problemas++;
    Console.WriteLine("  RESULTADO: FALHOU — estado da conversa inconsistente apos o takeover.");
}
else
{
    Console.WriteLine("  RESULTADO: OK — a resposta da IA em voo nunca foi entregue; o takeover venceu a corrida.");
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 5 — reabrir nao atravessa conversa ativa no mesmo cliente e canal.
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 5 — reabrir retorna conflito funcional quando ja existe conversa ativa");

Guid conversaFechadaComOutraAtiva;
await using (var db = new CrmDbContext(opcoes))
{
    var fechada = await Servico(db, 0).CriarAsync(
        new CriarConversaRequest(clientesDeTeste[Simultaneas + 3], null, null, "Conversa que sera fechada."));
    conversaFechadaComOutraAtiva = fechada.Id;
    await db.Conversas.Where(c => c.Id == fechada.Id).ExecuteUpdateAsync(s => s
        .SetProperty(c => c.Status, StatusConversa.Encerrada)
        .SetProperty(c => c.EncerradaEm, DateTime.UtcNow));
    await Servico(db, 1).CriarAsync(
        new CriarConversaRequest(clientesDeTeste[Simultaneas + 3], null, null, "Nova conversa ativa."));
}

string? codigoDaReabertura = null;
Exception? erroDaReabertura = null;
try
{
    await using var db = new CrmDbContext(opcoes);
    await Servico(db, 0).ReabrirAsync(conversaFechadaComOutraAtiva);
}
catch (AtendimentoServiceException ex)
{
    codigoDaReabertura = ex.Codigo;
}
catch (Exception ex)
{
    erroDaReabertura = ex;
}

Console.WriteLine($"  codigo devolvido .......... {codigoDaReabertura ?? "(nenhum)"}");
Console.WriteLine($"  erro inesperado ........... {(erroDaReabertura is null ? "nenhum" : erroDaReabertura.GetType().Name)}");
if (erroDaReabertura is not null || codigoDaReabertura != "cliente_ja_tem_conversa_ativa")
{
    problemas++;
    Console.WriteLine("  RESULTADO: FALHOU — reabrir nao devolveu o conflito funcional esperado.");
}
else
{
    Console.WriteLine("  RESULTADO: OK — reabrir foi bloqueado com cliente_ja_tem_conversa_ativa.");
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 6 — Criar e Reabrir concorrem pela mesma vaga do mesmo canal.
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 6 — criar e reabrir concorrentes devolvem conflito funcional");

Guid conversaFechadaParaCorrida;
var clienteDaCorrida = clientesDeTeste[Simultaneas + 4];
await using (var db = new CrmDbContext(opcoes))
{
    var fechada = await Servico(db, 0).CriarAsync(
        new CriarConversaRequest(clienteDaCorrida, null, null, "Conversa para corrida de reabertura."));
    conversaFechadaParaCorrida = fechada.Id;
    await db.Conversas.Where(c => c.Id == fechada.Id).ExecuteUpdateAsync(s => s
        .SetProperty(c => c.Status, StatusConversa.Encerrada)
        .SetProperty(c => c.EncerradaEm, DateTime.UtcNow));
}

var largada6 = new TaskCompletionSource();
var sucessosDaCorrida = new ConcurrentBag<string>();
var conflitosDaCorrida = new ConcurrentBag<string>();
var errosDaCorrida = new ConcurrentBag<string>();

var criarOuReabrir = new[]
{
    Task.Run(async () =>
    {
        await largada6.Task;
        try
        {
            await using var db = new CrmDbContext(opcoes);
            await Servico(db, 0).ReabrirAsync(conversaFechadaParaCorrida);
            sucessosDaCorrida.Add("reabrir");
        }
        catch (AtendimentoServiceException ex) when (ex.Codigo == "cliente_ja_tem_conversa_ativa")
        {
            conflitosDaCorrida.Add("reabrir");
        }
        catch (Exception ex)
        {
            errosDaCorrida.Add($"reabrir: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
        }
    }),
    Task.Run(async () =>
    {
        await largada6.Task;
        try
        {
            await using var db = new CrmDbContext(opcoes);
            await Servico(db, 1).CriarAsync(
                new CriarConversaRequest(clienteDaCorrida, null, null, "Criacao concorrente."));
            sucessosDaCorrida.Add("criar");
        }
        catch (AtendimentoServiceException ex) when (ex.Codigo == "cliente_ja_tem_conversa_ativa")
        {
            conflitosDaCorrida.Add("criar");
        }
        catch (Exception ex)
        {
            errosDaCorrida.Add($"criar: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
        }
    })
};

largada6.SetResult();
await Task.WhenAll(criarOuReabrir);

int ativasDaCorrida;
await using (var db = new CrmDbContext(opcoes))
    ativasDaCorrida = await db.Conversas.CountAsync(c => c.ClienteId == clienteDaCorrida
        && c.Canal == CanalOrigem.Simulado && c.Status == StatusConversa.Ativa);

Console.WriteLine($"  sucessos .................. {sucessosDaCorrida.Count}");
Console.WriteLine($"  conflitos funcionais ...... {conflitosDaCorrida.Count}");
Console.WriteLine($"  erros inesperados ......... {errosDaCorrida.Count}");
Console.WriteLine($"  conversas ativas .......... {ativasDaCorrida}");
foreach (var erro in errosDaCorrida) Console.WriteLine($"    ! {erro}");

if (sucessosDaCorrida.Count != 1 || conflitosDaCorrida.Count != 1 || errosDaCorrida.Count != 0 || ativasDaCorrida != 1)
{
    problemas++;
    Console.WriteLine("  RESULTADO: FALHOU — a corrida nao terminou em uma vaga e um conflito funcional.");
}
else
{
    Console.WriteLine("  RESULTADO: OK — uma operacao venceu; a outra recebeu cliente_ja_tem_conversa_ativa.");
}

// ---------------------------------------------------------------------------
// Limpeza
//
// Aqui SIM ha DELETE, e e a unica parte do repositorio que apaga alguma coisa.
// O produto nunca apaga conversa — mas o que este verificador cria nao e
// atendimento: e fixture. Deixar dezenas de conversas "[TESTE]" na base sujaria
// a demonstracao. O escopo se limita aos clientes criados aqui.
//
// A limpeza tambem apanha sobras de execucoes anteriores.
// ---------------------------------------------------------------------------

await using (var db = new CrmDbContext(opcoes))
{
    var clientes = db.Clientes
        .Where(c => c.TenantId == tenantId && c.Nome.StartsWith(PrefixoDeTeste))
        .Select(c => c.Id);

    var conversas = db.Conversas.Where(c => clientes.Contains(c.ClienteId)).Select(c => c.Id);

    var m = await db.Mensagens.Where(x => conversas.Contains(x.ConversaId)).ExecuteDeleteAsync();
    var h = await db.HistoricoEtapas.Where(x => conversas.Contains(x.ConversaId)).ExecuteDeleteAsync();
    var c = await db.Conversas.Where(x => clientes.Contains(x.ClienteId)).ExecuteDeleteAsync();
    var cl = await db.Clientes
        .Where(x => x.TenantId == tenantId && x.Nome.StartsWith(PrefixoDeTeste))
        .ExecuteDeleteAsync();

    Console.WriteLine();
    Console.WriteLine($"Limpeza: {c} conversa(s), {m} mensagem(ns), {h} evento(s), {cl} cliente(s).");
}

Console.WriteLine();
Console.WriteLine(problemas == 0 ? "TODOS OS TESTES PASSARAM." : $"{problemas} TESTE(S) FALHARAM.");

return problemas == 0 ? 0 : 1;

// ---------------------------------------------------------------------------

/// <summary>Identidade fixa, no lugar das claims do token.</summary>
file sealed class ContextoFixo(Guid tenantId, Guid atendenteId, string nome, PerfilDeAtendente perfil)
    : IContextoDoUsuario
{
    public bool Autenticado => true;
    public Guid TenantId => tenantId;
    public Guid AtendenteId => atendenteId;
    public string Nome => nome;
    public PerfilDeAtendente Perfil => perfil;

    /// <remarks>
    /// AtendimentoSimularCliente entra a mais, fora de <see cref="CatalogoDePermissoes.ConjuntoDaClasse"/>
    /// (excluida de proposito de toda classe): este verificador usa
    /// <c>MensagemService.SimularRecebimentoAsync</c> como harness para injetar a
    /// mensagem do cliente sob concorrencia. Nao afeta o gate real.
    /// </remarks>
    public bool Pode(PermissaoDoSistema permissao) =>
        permissao == PermissaoDoSistema.AtendimentoSimularCliente
        || CatalogoDePermissoes.ConjuntoDaClasse(perfil).Contains(permissao);
}

/// <summary>Sem SignalR aqui: o que esta sob teste e o banco, nao o hub.</summary>
/// <summary>
/// Canal que nao entrega nada. O verificador exercita a TRAVA de conversa, nao o
/// gateway: sair para o WhatsApp real aqui mandaria doze mensagens de teste para
/// telefone de gente a cada execucao.
/// </summary>
file sealed class CanalSilencioso : IEnviadorDeCanal
{
    public Task<string?> EnviarAsync(Guid m, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    public Task EnviarPendentesAsync(Guid c, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>
/// Mesmo papel de <see cref="CanalSilencioso"/> para o novo caminho assincrono de
/// ConversaService.MudarEtapaAsync: o verificador exercita a trava, nao a fila.
/// </summary>
file sealed class FilaSilenciosa : IFilaDeEnvioDoCanal
{
    public void Agendar(Guid conversaId) { }
}

/// <summary>
/// Anexo em memoria. Nenhum teste daqui anexa arquivo; existe para satisfazer o
/// construtor sem escrever no disco de quem roda o verificador.
/// </summary>
file sealed class AnexosEmMemoria : IArmazenamentoDeAnexos
{
    private readonly Dictionary<string, byte[]> _guardados = [];

    public long TamanhoMaximoBytes => 16 * 1024 * 1024;

    public bool TipoPermitido(string? tipoMime) => !string.IsNullOrWhiteSpace(tipoMime);

    public Task<AnexoGravado> GravarAsync(
        Guid tenantId, Guid mensagemId, DateTime quando, byte[] conteudo,
        string? nomeOriginal, string? tipoMime, CancellationToken ct = default)
    {
        var caminho = $"memoria/{mensagemId:N}";
        _guardados[caminho] = conteudo;

        return Task.FromResult(new AnexoGravado(
            caminho,
            nomeOriginal ?? "anexo",
            tipoMime ?? "application/octet-stream",
            conteudo.LongLength,
            TipoMensagem.Documento));
    }

    public Task<byte[]?> LerAsync(string caminhoRelativo, CancellationToken ct = default) =>
        Task.FromResult(_guardados.GetValueOrDefault(caminhoRelativo));
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

/// <summary>Nunca deveria ser chamado — Habilitada=false aqui. Ver o mesmo comentario em verificar-automacao.cs.</summary>
file sealed class OrquestradorNuncaChamado : IOrquestradorDeProvedores
{
    public Task<RespostaDaIA> ResponderAsync(Guid tenantId, ContextoDaChamada? contexto, PedidoDeIa pedido, CancellationToken ct) =>
        throw new InvalidOperationException(
            "OrquestradorDeProvedores nao deveria ser chamado com RespostaInteligente:Habilitada=false.");
}

/// <summary>
/// Para o TESTE 4 (BUG-ATD-01): simula a IA "em voo" sob concorrencia real —
/// sinaliza <paramref name="sinalDeEntrada"/> assim que e' chamada (o teste
/// usa isso para so entao disparar o Assumir concorrente) e so devolve a
/// resposta depois que <paramref name="liberar"/> completar (o teste controla
/// esse instante, DEPOIS de confirmar que o takeover ja aconteceu no banco).
/// </summary>
file sealed class RespostaInteligenteAtrasada(TaskCompletionSource sinalDeEntrada, Task liberar)
    : IRespostaInteligenteService
{
    public async Task<RespostaDaIA> ResponderAsync(
        Guid tenantId,
        IReadOnlyList<TrocaAnterior> historico,
        string mensagemDoCliente,
        ContextoDaChamada? contexto = null,
        CancellationToken ct = default)
    {
        sinalDeEntrada.TrySetResult();
        await liberar;

        return new RespostaDaIA(
            true, "Resposta gerada ANTES do takeover — nao deveria ser entregue.", "");
    }
}
