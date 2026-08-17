#:project CrmCartorio.Api/CrmCartorio.Api.csproj
#:property PublishAot=false

using Anthropic;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Atendimento.Application;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.Clientes.Domain.Entities;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Entities;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Enums;
using CrmCartorio.Api.Modules.RespostaInteligente.Application;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// VERIFICADOR DE AUTOMACAO DE MENSAGENS
//
// Rode com:  dotnet run verificar-automacao.cs ["<connection string>"]
//
// Exercita o fluxo inteiro da automacao chamando os SERVICOS REAIS — nao uma copia
// da logica. Mesmo principio de verificar-concorrencia.cs: copia de codigo testa a
// copia, nao o sistema.
//
//   1. Boas-vindas e triagem no primeiro contato
//   2. Resposta que NAO corresponde a opcao nenhuma
//   3. Conversa aberta ja com setor nao recebe menu
//   4. Aviso por mudanca de etapa
//   5. Varredura de prazo, incluindo a garantia de nao avisar duas vezes
//   6. Transferencia entre atendentes (RF-013)
//
// ATENCAO: este verificador ESCREVE no banco. Rode SOMENTE contra base de
// desenvolvimento. Ao final remove tudo que criou, inclusive sobras de execucoes
// anteriores.
//
// NAO altera a configuracao existente. Os gatilhos de prazo nascem desativados no
// seed (decisao do cartório parceiro, docs/REQUISITOS.md 5.7) e continuam assim: o teste cria
// regras proprias, marcadas, e as remove no final.

const string PrefixoDeTeste = "[TESTE-AUTO] ";

var conexao = args.Length > 0
    ? args[0]
    : "Server=.\\SQLEXPRESS;Database=CRM_Cartorio_Dev;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

var construtor = new DbContextOptionsBuilder<CrmDbContext>();
ConfiguracaoDoBanco.Configurar(construtor, conexao);
var opcoes = construtor.Options;

await using (var checagem = new CrmDbContext(opcoes))
{
    if (!await checagem.Database.CanConnectAsync())
    {
        Console.WriteLine("Nao foi possivel conectar. Verifique a connection string.");
        return 1;
    }

    // SqlQuery<T> exige que a coluna se chame "Value" — sem o alias, o SQL Server
    // devolve coluna sem nome e o EF nao consegue materializar.
    var existe = await checagem.Database
        .SqlQuery<int>($"SELECT CASE WHEN OBJECT_ID('dbo.MensagensAutomaticas') IS NULL THEN 0 ELSE 1 END AS Value")
        .FirstAsync();

    if (existe == 0)
    {
        Console.WriteLine("Tabela MensagensAutomaticas ausente. Rode sql/005-mensagens-automaticas.sql antes.");
        return 1;
    }
}

Guid tenantId;
Guid atendenteId;
string atendenteNome;
Guid outroAtendenteId;
string outroAtendenteNome;
Guid etapaFinalId;
string etapaFinalNome;
Guid etapaInicialId;
Guid servicoId;
string servicoNome;
List<(Guid Id, string Nome, int Ordem)> setores;

await using (var db = new CrmDbContext(opcoes))
{
    tenantId = await db.Tenants.Select(t => t.Id).FirstAsync();

    var equipe = await db.Atendentes
        .Where(a => a.TenantId == tenantId && a.Ativo)
        .OrderBy(a => a.Nome)
        .Select(a => new { a.Id, a.Nome })
        .Take(2)
        .ToListAsync();

    if (equipe.Count < 2)
    {
        Console.WriteLine("Sao necessarios ao menos 2 atendentes ativos. Rode o seed antes.");
        return 1;
    }

    (atendenteId, atendenteNome) = (equipe[0].Id, equipe[0].Nome);
    (outroAtendenteId, outroAtendenteNome) = (equipe[1].Id, equipe[1].Nome);

    setores = await db.Setores
        .Where(s => s.TenantId == tenantId && s.Ativo)
        .OrderBy(s => s.Ordem).ThenBy(s => s.Nome)
        .Select(s => new ValueTuple<Guid, string, int>(s.Id, s.Nome, s.Ordem))
        .ToListAsync();

    var final = await db.EtapasFunil
        .Where(e => e.TenantId == tenantId && e.Ativo && e.EhFinal)
        .Select(e => new { e.Id, e.Nome })
        .FirstOrDefaultAsync();

    if (final is null || setores.Count < 2)
    {
        Console.WriteLine("Configuracao insuficiente: sao necessarios 2 setores ativos e 1 etapa final.");
        return 1;
    }

    (etapaFinalId, etapaFinalNome) = (final.Id, final.Nome);

    etapaInicialId = await db.EtapasFunil
        .Where(e => e.TenantId == tenantId && e.Ativo && !e.EhFinal)
        .OrderBy(e => e.Ordem)
        .Select(e => e.Id)
        .FirstAsync();

    var servico = await db.TiposServico
        .Where(t => t.TenantId == tenantId && t.Ativo)
        .OrderBy(t => t.Nome)
        .Select(t => new { t.Id, t.Nome })
        .FirstAsync();

    (servicoId, servicoNome) = (servico.Id, servico.Nome);
}

Console.WriteLine($"Tenant ....... {tenantId}");
Console.WriteLine($"Atendentes ... {atendenteNome} / {outroAtendenteNome}");
Console.WriteLine($"Setores ...... {string.Join(", ", setores.Select((s, i) => $"{i + 1}={s.Nome}"))}");
Console.WriteLine($"Etapa final .. {etapaFinalNome}");

// Limpeza de ENTRADA, alem da de saida. Uma execucao que aborta no meio deixa
// cliente e conversa para tras, e o CriarAsync recusa abrir um segundo atendimento
// para quem ja tem um aberto — sem isto, a primeira falha travaria todas as
// execucoes seguintes num erro que nao tem nada a ver com o que se quer testar.
var residuo = await LimparAsync();

if (residuo > 0)
{
    Console.WriteLine($"Resíduo ...... {residuo} registro(s) de execucao anterior removidos");
}

Console.WriteLine();

var problemas = 0;
var sequencial = 0;

void Checar(string descricao, bool condicao, string? detalhe = null)
{
    if (condicao)
    {
        Console.WriteLine($"    OK    {descricao}");
        return;
    }

    problemas++;
    Console.WriteLine($"    FALHA {descricao}");

    if (detalhe is not null)
    {
        Console.WriteLine($"          {detalhe}");
    }
}

ConversaService NovaConversaService(CrmDbContext db, Guid quem, string nome) =>
    new(db,
        new ContextoFixo(tenantId, quem, nome, PerfilDeAtendente.Administrador),
        new GeradorDeProtocolo(db),
        new NotificadorSilencioso(),
        new DisparadorDeMensagemAutomatica(db, NullLogger<DisparadorDeMensagemAutomatica>.Instance),
        new CanalSilencioso(),
        NullLogger<ConversaService>.Instance);

MensagemService NovaMensagemService(CrmDbContext db, Guid quem, string nome)
{
    var ctx = new ContextoFixo(tenantId, quem, nome, PerfilDeAtendente.Administrador);

    // Habilitada=false: o servico real de RespostaInteligente cai direto no
    // "nao respondeu" sem tentar rede nenhuma (mesma contingencia de producao),
    // entao o verificador exercita o caminho de sempre — triagem sem
    // correspondencia escala para humano — sem precisar de chave da Anthropic.
    var opcoesDaIA = Options.Create(new OpcoesDaIA { Habilitada = false });
    var respostaInteligente = new RespostaInteligenteService(
        db, new AnthropicClient(), opcoesDaIA, NullLogger<RespostaInteligenteService>.Instance);

    return new MensagemService(
        db,
        ctx,
        NovaConversaService(db, quem, nome),
        new NotificadorSilencioso(),
        new DisparadorDeMensagemAutomatica(db, NullLogger<DisparadorDeMensagemAutomatica>.Instance),
        new CanalSilencioso(),
        new AnexosEmMemoria(),
        respostaInteligente,
        opcoesDaIA,
        NullLogger<MensagemService>.Instance);
}

async Task<Guid> NovoClienteAsync(string apelido)
{
    await using var db = new CrmDbContext(opcoes);

    var telefone = $"5538000{++sequencial:D6}";

    var existente = await db.Clientes
        .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.TelefoneWhatsapp == telefone);

    if (existente is not null)
    {
        return existente.Id;
    }

    var cliente = new Cliente
    {
        TenantId = tenantId,
        Nome = $"{PrefixoDeTeste}{apelido}",
        TelefoneWhatsapp = telefone,
        PrimeiroContatoEm = DateTime.UtcNow,
        Ativo = true
    };

    db.Clientes.Add(cliente);
    await db.SaveChangesAsync();

    return cliente.Id;
}

async Task<List<(AutorTipo Autor, bool Automatica, string Conteudo, StatusEntrega Status)>> FioAsync(Guid conversaId)
{
    await using var db = new CrmDbContext(opcoes);

    return await db.Mensagens.AsNoTracking()
        .Where(m => m.ConversaId == conversaId)
        .OrderBy(m => m.EnviadaEm).ThenBy(m => m.Id)
        .Select(m => new ValueTuple<AutorTipo, bool, string, StatusEntrega>(
            m.AutorTipo, m.EhAutomatica, m.Conteudo, m.StatusEntrega))
        .ToListAsync();
}

// ---------------------------------------------------------------------------
// TESTE 1 — boas-vindas e triagem que ACERTA a opcao
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 1 — primeiro contato: menu sai e a resposta certa roteia sozinha");

Guid conversa1;

await using (var db = new CrmDbContext(opcoes))
{
    var criada = await NovaConversaService(db, atendenteId, atendenteNome).CriarAsync(
        new CriarConversaRequest(await NovoClienteAsync("Triagem acerta"), null, null, "Bom dia!"));

    conversa1 = criada.Id;

    Checar("conversa nasce aguardando triagem", criada.AguardandoTriagem);
    Checar("conversa nasce sem setor", criada.SetorId is null);
}

var fio1 = await FioAsync(conversa1);
var boasVindas = fio1.FirstOrDefault(m => m.Automatica);

Checar("mensagem de boas-vindas foi gerada", boasVindas.Conteudo is not null);
Checar("boas-vindas e do Sistema", boasVindas.Autor == AutorTipo.Sistema);
Checar("boas-vindas nasce Pendente (Fase 1 nao entrega nada)", boasVindas.Status == StatusEntrega.Pendente);
Checar("nenhum marcador sobrou sem resolver",
    boasVindas.Conteudo?.Contains('{') != true,
    boasVindas.Conteudo);

foreach (var (id, nome, _) in setores)
{
    _ = id;
    Checar($"menu cita o setor \"{nome}\"", boasVindas.Conteudo?.Contains(nome, StringComparison.Ordinal) == true);
}

// O cliente responde o numero da SEGUNDA opcao. Testar a segunda, e nao a
// primeira, e o que separa "leu a resposta" de "sempre cai no primeiro setor".
var setorEsperado = setores[1];

await using (var db = new CrmDbContext(opcoes))
{
    await NovaMensagemService(db, atendenteId, atendenteNome).SimularRecebimentoAsync(
        conversa1, new SimularMensagemDeClienteRequest("2"));
}

await using (var db = new CrmDbContext(opcoes))
{
    var depois = await db.Conversas.AsNoTracking().FirstAsync(c => c.Id == conversa1);

    Checar($"setor atribuido = \"{setorEsperado.Nome}\"", depois.SetorId == setorEsperado.Id,
        $"veio {depois.SetorId}");
    Checar("flag de triagem foi limpa", !depois.AguardandoTriagem);

    var trilha = await db.HistoricoEtapas.AsNoTracking()
        .Where(h => h.ConversaId == conversa1 && h.TipoMudanca == TipoMudancaHistorico.Setor)
        .ToListAsync();

    Checar("historico registrou a mudanca de setor", trilha.Count == 1);
    Checar("historico marca autoria automatica (AtendenteId nulo)",
        trilha.Count == 1 && trilha[0].AtendenteId is null);

    Checar("aguarda escolha de servico (setor tem servico vinculado)", depois.AguardandoEscolhaDeServico);
}

var perguntaServico = (await FioAsync(conversa1)).LastOrDefault(m => m.Automatica);

Checar("pergunta qual servico foi gerada", perguntaServico.Conteudo is not null);
Checar("pergunta de servico nao deixou marcador sem resolver",
    perguntaServico.Conteudo?.Contains('{') != true, perguntaServico.Conteudo);

// Fase 2D: o setor tem servico vinculado (migracao 014), entao a triagem
// pergunta QUAL antes de fechar — mesmo menu que ServicosDoMenuAsync monta.
List<(Guid Id, string Nome)> servicosDoSetorEsperado;

await using (var db = new CrmDbContext(opcoes))
{
    servicosDoSetorEsperado = await db.TiposServico.AsNoTracking()
        .Where(t => t.TenantId == tenantId && t.SetorId == setorEsperado.Id && t.Ativo)
        .OrderBy(t => t.Nome)
        .Select(t => new ValueTuple<Guid, string>(t.Id, t.Nome))
        .ToListAsync();
}

Checar("setor escolhido tem ao menos 1 servico vinculado (migracao 014)", servicosDoSetorEsperado.Count > 0);

if (servicosDoSetorEsperado.Count > 0)
{
    var servicoEsperado = servicosDoSetorEsperado[0];

    await using (var db = new CrmDbContext(opcoes))
    {
        await NovaMensagemService(db, atendenteId, atendenteNome).SimularRecebimentoAsync(
            conversa1, new SimularMensagemDeClienteRequest("1"));
    }

    await using (var db = new CrmDbContext(opcoes))
    {
        var depoisDoServico = await db.Conversas.AsNoTracking().FirstAsync(c => c.Id == conversa1);

        Checar($"servico atribuido = \"{servicoEsperado.Nome}\"",
            depoisDoServico.TipoServicoId == servicoEsperado.Id, $"veio {depoisDoServico.TipoServicoId}");
        Checar("flag de escolha de servico foi limpa", !depoisDoServico.AguardandoEscolhaDeServico);
    }

    var confirmacaoDoServico = (await FioAsync(conversa1)).LastOrDefault(m => m.Automatica);

    Checar("confirmacao com checklist/prazo foi gerada", confirmacaoDoServico.Conteudo is not null);
    Checar("confirmacao nao deixou marcador sem resolver",
        confirmacaoDoServico.Conteudo?.Contains('{') != true, confirmacaoDoServico.Conteudo);
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 2 — resposta que NAO corresponde a opcao nenhuma
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 2 — resposta fora do menu cai para atendimento humano");

Guid conversa2;

await using (var db = new CrmDbContext(opcoes))
{
    var criada = await NovaConversaService(db, atendenteId, atendenteNome).CriarAsync(
        new CriarConversaRequest(await NovoClienteAsync("Triagem erra"), null, null, null));

    conversa2 = criada.Id;
}

await using (var db = new CrmDbContext(opcoes))
{
    await NovaMensagemService(db, atendenteId, atendenteNome).SimularRecebimentoAsync(
        conversa2, new SimularMensagemDeClienteRequest("quero falar com alguem sobre um assunto"));
}

await using (var db = new CrmDbContext(opcoes))
{
    var depois = await db.Conversas.AsNoTracking().FirstAsync(c => c.Id == conversa2);

    Checar("setor continua vazio", depois.SetorId is null);
    Checar("flag de triagem foi limpa mesmo sem acerto", !depois.AguardandoTriagem);
}

var fio2 = await FioAsync(conversa2);

Checar("saiu boas-vindas + fallback (2 automaticas)",
    fio2.Count(m => m.Automatica) == 2,
    $"automaticas: {fio2.Count(m => m.Automatica)}");

Checar("nenhum marcador sobrou no fallback",
    fio2.Where(m => m.Automatica).All(m => !m.Conteudo.Contains('{')));

// Segunda mensagem do cliente NAO pode disparar o menu de novo.
await using (var db = new CrmDbContext(opcoes))
{
    await NovaMensagemService(db, atendenteId, atendenteNome).SimularRecebimentoAsync(
        conversa2, new SimularMensagemDeClienteRequest("alguem ai?"));
}

Checar("cliente nao fica preso em loop de robo",
    (await FioAsync(conversa2)).Count(m => m.Automatica) == 2);

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 3 — conversa aberta JA com setor nao recebe menu
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 3 — atendente ja escolheu o setor: nao faz sentido perguntar");

Guid conversa3;

await using (var db = new CrmDbContext(opcoes))
{
    var criada = await NovaConversaService(db, atendenteId, atendenteNome).CriarAsync(
        new CriarConversaRequest(await NovoClienteAsync("Com setor"), setores[0].Id, null, "Oi"));

    conversa3 = criada.Id;

    Checar("nao entra em triagem", !criada.AguardandoTriagem);
}

Checar("nenhuma mensagem automatica foi gerada",
    (await FioAsync(conversa3)).Count(m => m.Automatica) == 0);

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 4 — aviso por mudanca de etapa
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 4 — mover para a etapa final avisa o cliente");

string protocolo4;

await using (var db = new CrmDbContext(opcoes))
{
    var servico = NovaConversaService(db, atendenteId, atendenteNome);

    var criada = await servico.CriarAsync(
        new CriarConversaRequest(await NovoClienteAsync("Etapa"), setores[0].Id, etapaInicialId, "Oi"));

    protocolo4 = criada.Protocolo;

    await servico.AssumirAsync(criada.Id);
    await servico.VincularServicoAsync(criada.Id, new VincularServicoRequest(servicoId));

    // Etapa intermediaria: nao ha regra para ela, entao nada deve sair.
    await servico.MoverAsync(criada.Id, new MoverConversaRequest(etapaInicialId, null, null));

    var antes = (await FioAsync(criada.Id)).Count(m => m.Automatica);

    await servico.MoverAsync(criada.Id, new MoverConversaRequest(etapaFinalId, null, null));

    var fio = await FioAsync(criada.Id);
    var aviso = fio.LastOrDefault(m => m.Automatica);

    Checar("etapa sem regra nao dispara nada", antes == 0);
    Checar("etapa final disparou exatamente 1 aviso", fio.Count(m => m.Automatica) == 1);
    Checar("aviso resolveu {Protocolo}",
        aviso.Conteudo?.Contains(protocolo4, StringComparison.Ordinal) == true, aviso.Conteudo);
    Checar("aviso resolveu {Servico}",
        aviso.Conteudo?.Contains(servicoNome, StringComparison.Ordinal) == true);
    Checar("nenhum marcador sobrou", aviso.Conteudo?.Contains('{') != true);
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 5 — varredura de prazo
//
// Cria regra PROPRIA para o gatilho: as do seed nascem desativadas por decisao do
// cartório parceiro, e ligar a configuracao dele para rodar um teste seria mudar o produto
// para caber no verificador.
//
// A varredura e por TENANT, nao por conversa: enquanto a regra de teste esta ativa,
// qualquer conversa da demonstracao ja vencida tambem recebe aviso. Por isso
// primeiraPassada pode vir > 1, e por isso a limpeza precisa desfazer o respingo —
// ver LimparAsync.
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 5 — varredura de prazo avisa uma vez e so uma");

var regraDeTesteId = Guid.CreateVersion7();
Guid conversa5;

await using (var db = new CrmDbContext(opcoes))
{
    db.MensagensAutomaticas.Add(new MensagemAutomatica
    {
        Id = regraDeTesteId,
        TenantId = tenantId,
        Gatilho = GatilhoDeMensagem.PrazoVencido,
        Conteudo = $"{PrefixoDeTeste}Protocolo {{Protocolo}} passou do prazo.",
        Ativo = true
    });

    await db.SaveChangesAsync();
}

await using (var db = new CrmDbContext(opcoes))
{
    var servico = NovaConversaService(db, atendenteId, atendenteNome);

    var criada = await servico.CriarAsync(
        new CriarConversaRequest(await NovoClienteAsync("Prazo"), setores[0].Id, etapaInicialId, "Oi"));

    conversa5 = criada.Id;

    await servico.AssumirAsync(conversa5);
    await servico.VincularServicoAsync(conversa5, new VincularServicoRequest(servicoId));
}

// Empurra o prazo para o passado. E fixture: o que esta sob teste e a varredura,
// nao a CalculadoraDePrazo.
await using (var db = new CrmDbContext(opcoes))
{
    var passado = DateTime.UtcNow.AddHours(-2);

    await db.Conversas.Where(c => c.Id == conversa5)
        .ExecuteUpdateAsync(s => s
            .SetProperty(c => c.PrazoLimite, passado)
            .SetProperty(c => c.PrazoAlertaEm, passado.AddHours(-1)));
}

int primeiraPassada;
int segundaPassada;

await using (var db = new CrmDbContext(opcoes))
{
    primeiraPassada = await NovoVarredor(db).ExecutarAsync();
}

await using (var db = new CrmDbContext(opcoes))
{
    segundaPassada = await NovoVarredor(db).ExecutarAsync();
}

await using (var db = new CrmDbContext(opcoes))
{
    var depois = await db.Conversas.AsNoTracking().FirstAsync(c => c.Id == conversa5);
    var fio = await FioAsync(conversa5);

    Checar("primeira passada avisou", primeiraPassada >= 1);
    Checar("marcador de aviso foi gravado", depois.AvisoDeVencimentoEnviadoEm is not null);
    Checar("segunda passada NAO reavisa a mesma conversa", segundaPassada == 0,
        $"segunda passada avisou {segundaPassada}");
    Checar("exatamente 1 aviso no fio", fio.Count(m => m.Automatica) == 1);
    Checar("aviso do gatilho de prazo resolveu o protocolo",
        fio.Any(m => m.Automatica && !m.Conteudo.Contains('{')));

    // UltimaMensagemEm nao pode ter sido tocado: aviso automatico nao reordena a
    // inbox (README do modulo Atendimento).
    Checar("aviso automatico nao reordenou a inbox",
        depois.UltimaMensagemEm < depois.AvisoDeVencimentoEnviadoEm);
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 6 — transferencia entre atendentes (RF-013)
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 6 — transferencia entre atendentes");

await using (var db = new CrmDbContext(opcoes))
{
    var servico = NovaConversaService(db, atendenteId, atendenteNome);

    var criada = await servico.CriarAsync(
        new CriarConversaRequest(await NovoClienteAsync("Transferencia"), setores[0].Id, null, "Oi"));

    await servico.AssumirAsync(criada.Id);

    var transferida = await servico.TransferirAsync(
        criada.Id, new TransferirConversaRequest(outroAtendenteId, "Assunto do outro setor."));

    Checar($"responsavel passou a ser {outroAtendenteNome}", transferida.AtendenteId == outroAtendenteId);

    var fio = await FioAsync(criada.Id);
    var anotacao = fio.LastOrDefault(m => m.Autor == AutorTipo.Sistema && !m.Automatica);

    Checar("anotacao de sistema registrou a transferencia",
        anotacao.Conteudo?.Contains(outroAtendenteNome, StringComparison.Ordinal) == true,
        anotacao.Conteudo);
    Checar("anotacao interna NAO e marcada como automatica",
        fio.Count(m => m.Autor == AutorTipo.Sistema && m.Automatica) == 0);
}

// ---------------------------------------------------------------------------
// Limpeza
// ---------------------------------------------------------------------------

Console.WriteLine();
Console.WriteLine($"Limpeza: {await LimparAsync()} registro(s) removidos.");

Console.WriteLine();
Console.WriteLine(problemas == 0 ? "TODOS OS TESTES PASSARAM." : $"{problemas} VERIFICACAO(OES) FALHARAM.");

return problemas == 0 ? 0 : 1;

/// <summary>
/// Remove tudo que este verificador cria — inclusive o que ele deixou em conversas
/// que NAO sao dele. Roda na entrada e na saida.
/// </summary>
/// <remarks>
/// Aqui SIM ha DELETE, e junto com verificar-concorrencia.cs e a unica parte do
/// repositorio que apaga alguma coisa. O produto nunca apaga conversa — mas o que
/// este arquivo cria nao e atendimento: e fixture.
///
/// ATENCAO ao segundo bloco. A varredura de prazo varre o TENANT INTEIRO, nao a
/// conversa do teste: enquanto a regra de teste esta ativa, qualquer conversa da
/// demonstracao que ja estivesse vencida tambem recebe aviso e tem o marcador
/// gravado. Uma limpeza que so olhasse os clientes de teste deixaria essas
/// mensagens no fio da demonstracao — e, pior, deixaria o marcador preenchido,
/// impedindo o aviso de VERDADE de sair quando o cartório parceiro ativar a regra.
///
/// Por isso a limpeza e por CONTEUDO, no tenant todo, e desfaz o marcador
/// exatamente nas conversas de onde removeu uma mensagem de teste.
/// </remarks>
async Task<int> LimparAsync()
{
    await using var db = new CrmDbContext(opcoes);

    // --- 1. Conversas do proprio verificador -------------------------------
    var clientes = db.Clientes
        .Where(c => c.TenantId == tenantId && c.Nome.StartsWith(PrefixoDeTeste))
        .Select(c => c.Id);

    var conversas = db.Conversas.Where(c => clientes.Contains(c.ClienteId)).Select(c => c.Id);

    var total = await db.Mensagens.Where(x => conversas.Contains(x.ConversaId)).ExecuteDeleteAsync();
    total += await db.HistoricoEtapas.Where(x => conversas.Contains(x.ConversaId)).ExecuteDeleteAsync();
    total += await db.Conversas.Where(x => clientes.Contains(x.ClienteId)).ExecuteDeleteAsync();
    total += await db.Clientes
        .Where(x => x.TenantId == tenantId && x.Nome.StartsWith(PrefixoDeTeste))
        .ExecuteDeleteAsync();

    // --- 2. Respingo em conversas de terceiros ------------------------------
    var contaminadas = await db.Mensagens.AsNoTracking()
        .Where(m => m.TenantId == tenantId && m.Conteudo.StartsWith(PrefixoDeTeste))
        .Select(m => m.ConversaId)
        .Distinct()
        .ToListAsync();

    if (contaminadas.Count > 0)
    {
        total += await db.Mensagens
            .Where(m => m.TenantId == tenantId && m.Conteudo.StartsWith(PrefixoDeTeste))
            .ExecuteDeleteAsync();

        // Sem isto a conversa ficaria marcada como "ja avisada" para sempre, por
        // causa de um aviso que so existiu dentro de um teste.
        total += await db.Conversas
            .Where(c => contaminadas.Contains(c.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.AvisoDeVencimentoEnviadoEm, (DateTime?)null)
                .SetProperty(c => c.AvisoDeAlertaEnviadoEm, (DateTime?)null), default);
    }

    // --- 3. Regras de teste. A configuracao do cartório parceiro nao e tocada. ----------
    total += await db.MensagensAutomaticas
        .Where(x => x.TenantId == tenantId && x.Conteudo.StartsWith(PrefixoDeTeste))
        .ExecuteDeleteAsync();

    return total;
}

VarredorDePrazos NovoVarredor(CrmDbContext db) =>
    new(db,
        new DisparadorDeMensagemAutomatica(db, NullLogger<DisparadorDeMensagemAutomatica>.Instance),
        new NotificadorSilencioso(),
        new CanalSilencioso(),
        NullLogger<VarredorDePrazos>.Instance);

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

    public bool PodeGerirCadastros =>
        perfil is PerfilDeAtendente.Supervisor or PerfilDeAtendente.Administrador;

    public bool PodeGerirAtendentes => perfil is PerfilDeAtendente.Administrador;

    public bool PodeOperarConversaDeTerceiro =>
        perfil is PerfilDeAtendente.Supervisor or PerfilDeAtendente.Administrador;
}

/// <summary>Sem SignalR aqui: o que esta sob teste e a automacao, nao o hub.</summary>
/// <summary>
/// Canal que nao entrega nada. O verificador exercita a REGRA de atendimento,
/// nao o gateway: sair para o WhatsApp real aqui mandaria mensagem de teste para
/// telefone de gente.
/// </summary>
file sealed class CanalSilencioso : IEnviadorDeCanal
{
    public Task<string?> EnviarAsync(Guid m, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    public Task EnviarPendentesAsync(Guid c, CancellationToken ct = default) => Task.CompletedTask;
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
