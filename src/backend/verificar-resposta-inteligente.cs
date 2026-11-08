#:project CrmCartorio.Api/CrmCartorio.Api.csproj
#:property PublishAot=false
#:package Microsoft.EntityFrameworkCore.InMemory@10.0.9

using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.RespostaInteligente.Application;
using CrmCartorio.Api.Modules.RespostaInteligente.Application.Cota;
using CrmCartorio.Api.Modules.RespostaInteligente.Application.Provedores;
using CrmCartorio.Api.Modules.RespostaInteligente.Domain.Entities;
using CrmCartorio.Api.Modules.RespostaInteligente.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// VERIFICADOR DA RESPOSTA INTELIGENTE — GEMINI + HAIKU
//
// Rode com: dotnet run verificar-resposta-inteligente.cs
//
// Banco descartavel em memoria (um nome unico por cenario, isolados entre si);
// os dois provedores sao FAKES (IProvedorDeIa) — nunca abre conexao de rede.
// Exercita o ORQUESTRADOR REAL (CrmCartorio.Api.Modules.RespostaInteligente.
// Application.OrquestradorDeProvedores) e as pecas reais de cota/disjuntor —
// nao uma copia da logica.
//
// O cenario "fluxo no roteiro nao chama IA" (item 9 do plano) NAO esta
// duplicado aqui: verificar-automacao.cs ja prova isso end-to-end, com um
// OrquestradorNuncaChamado que lanca excecao se for invocado — todos os
// caminhos de menu/triagem daquele arquivo passam sem tocar nele.

var falhas = 0;
var sequencial = 0;

void Checar(string nome, bool condicao, string? detalhe = null)
{
    if (condicao)
    {
        Console.WriteLine($"OK    {nome}");
        return;
    }

    falhas++;
    Console.WriteLine($"FALHA {nome}");
    if (detalhe is not null)
    {
        Console.WriteLine($"      {detalhe}");
    }
}

string NovoNomeDeBanco() => $"resposta-inteligente-{Interlocked.Increment(ref sequencial)}-{Guid.NewGuid():N}";

var tenantId = Guid.NewGuid();

// --- Ambiente de teste: db + scope factory + pecas reais + fakes -----------

(CrmDbContext Db, IServiceScopeFactory Escopos) NovoBanco()
{
    var nome = NovoNomeDeBanco();
    var servicos = new ServiceCollection();
    servicos.AddSingleton<ICifraDeDados>(FabricaDeCifraDeDados.CriarDormente());
    servicos.AddDbContext<CrmDbContext>(o => o.UseInMemoryDatabase(nome));
    var provedor = servicos.BuildServiceProvider();

    var opcoes = new DbContextOptionsBuilder<CrmDbContext>().UseInMemoryDatabase(nome).Options;
    var db = new CrmDbContext(opcoes, FabricaDeCifraDeDados.CriarDormente());

    return (db, provedor.GetRequiredService<IServiceScopeFactory>());
}

OpcoesDaIA NovasOpcoes(
    int limiteDiario = 300,
    int limiteDiarioHaiku = 60,
    int limiteRpd = 0,
    int limiteRpm = 0,
    int limiteTpm = 0,
    int percentualDeSeguranca = 85,
    int falhasParaAbrir = 3,
    int cooldownSegundos = 120,
    int cooldownMaximoSegundos = 900,
    int limiteRpmDeDescoberta = 5) => new()
    {
        LimiteDiario = limiteDiario,
        Gemini = new OpcoesDoGemini
        {
            Habilitado = true,
            ApiKey = "chave-fake-de-verificacao",
            Modelo = "gemini-3.7-flash",
            LimiteRpd = limiteRpd,
            LimiteRpm = limiteRpm,
            LimiteTpm = limiteTpm,
            LimiteRpmDeDescoberta = limiteRpmDeDescoberta,
            PercentualDeSeguranca = percentualDeSeguranca,
            FalhasParaAbrirDisjuntor = falhasParaAbrir,
            CooldownSegundos = cooldownSegundos,
            CooldownMaximoSegundos = cooldownMaximoSegundos
        },
        Anthropic = new OpcoesDoAnthropic
        {
            Habilitado = true,
            ApiKey = "chave-fake-de-verificacao",
            Modelo = "claude-haiku-4-5",
            LimiteDiarioHaiku = limiteDiarioHaiku
        }
    };

(OrquestradorDeProvedores Orquestrador, ProvedorFake Gemini, ProvedorFake Anthropic, DisjuntorDoGemini Disjuntor,
    ContadorDeCotaDoGemini Cota, ContadorDeLimiteDiario Limite, IServiceScopeFactory Escopos, CrmDbContext Db)
    MontarAmbiente(OpcoesDaIA? opcoesCustom = null, TimeSpan? tempoMaximoDeSonda = null)
{
    var (db, escopos) = NovoBanco();
    var opcoes = Options.Create(opcoesCustom ?? NovasOpcoes());

    var gemini = new ProvedorFake(ProvedorDeIa.Gemini, "gemini-3.7-flash");
    var anthropic = new ProvedorFake(ProvedorDeIa.Anthropic, "claude-haiku-4-5");

    var disjuntor = new DisjuntorDoGemini(tempoMaximoDeSonda);
    var cota = new ContadorDeCotaDoGemini(escopos);
    var limite = new ContadorDeLimiteDiario(escopos);
    var politica = new PoliticaDeSelecao(cota, disjuntor);
    var registrador = new RegistradorDeChamadas(escopos, NullLogger<RegistradorDeChamadas>.Instance);

    var orquestrador = new OrquestradorDeProvedores(
        gemini, anthropic, politica, disjuntor, cota, limite, registrador,
        opcoes, NullLogger<OrquestradorDeProvedores>.Instance);

    return (orquestrador, gemini, anthropic, disjuntor, cota, limite, escopos, db);
}

PedidoDeIa NovoPedido(string pergunta = "qual o horario de atendimento?") =>
    new("CATALOGO DE SERVICOS:\n- Certidao de nascimento\n", [], pergunta);

async Task<List<ChamadaDeIa>> LerChamadasAsync(IServiceScopeFactory escopos)
{
    using var escopo = escopos.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<CrmDbContext>();
    return await db.ChamadasDeIa.AsNoTracking().OrderBy(c => c.IniciadaEm).ToListAsync();
}

Console.WriteLine("Tenant .... " + tenantId);
Console.WriteLine();

// ============================================================================
// CENARIO 1 — Gemini normal: automacao nao resolve, Gemini responde, Haiku
// nunca e chamado.
// ============================================================================
Console.WriteLine("CENARIO 1 — Gemini responde, Haiku nao e chamado");
{
    var (orq, gemini, anthropic, _, _, _, escopos, _) = MontarAmbiente();
    gemini.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, "Atendemos das 8h as 17h.", ""), 100, 40, 0, 250));

    var resposta = await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);

    Checar("Gemini foi chamado exatamente uma vez", gemini.Chamadas == 1);
    Checar("Haiku NAO foi chamado", anthropic.Chamadas == 0);
    Checar("Resposta veio do Gemini", resposta.PodeResponder && resposta.Resposta.Contains("17h"));

    var linhas = await LerChamadasAsync(escopos);
    Checar("Uma linha registrada, do Gemini, com sucesso", linhas.Count == 1 && linhas[0].Provedor == ProvedorDeIa.Gemini && linhas[0].Sucesso);
    Checar("FoiFallback=false na linha do Gemini (e a tentativa principal)", !linhas[0].FoiFallback);
}
Console.WriteLine();

// ============================================================================
// CENARIO 2 — Gemini perto da margem configurada: proxima chamada usa Haiku.
// ============================================================================
Console.WriteLine("CENARIO 2 — margem de seguranca do RPD atingida, cai para Haiku");
{
    // RPD=10, margem=80% -> bloqueia a partir da 9a reserva (8 ja usadas).
    var opcoes = NovasOpcoes(limiteRpd: 10, percentualDeSeguranca: 80);
    var (orq, gemini, anthropic, _, _, _, _, _) = MontarAmbiente(opcoes);

    for (var i = 0; i < 8; i++)
    {
        gemini.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, $"resposta {i}", ""), 10, 10, 0, 50));
        await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);
    }

    Checar("8 chamadas ao Gemini, dentro da margem", gemini.Chamadas == 8);

    anthropic.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, "resposta do haiku", ""), 10, 10, 0, 50));
    var resposta = await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);

    Checar("A 9a chamada NAO foi ao Gemini (margem atingida)", gemini.Chamadas == 8);
    Checar("A 9a chamada foi ao Haiku", anthropic.Chamadas == 1);
    Checar("Resposta veio do Haiku", resposta.PodeResponder && resposta.Resposta == "resposta do haiku");
}
Console.WriteLine();

// ============================================================================
// CENARIO 3 — Rate limit (429): Gemini falha, Haiku responde.
// ============================================================================
Console.WriteLine("CENARIO 3 — 429 rate limit no Gemini, Haiku assume");
{
    var (orq, gemini, anthropic, disjuntor, _, _, escopos, _) = MontarAmbiente();
    gemini.Enfileirar(ResultadoDoProvedor.DeFalha(TipoDeFalhaDeIa.RateLimit, 429, 80, "429 rate_limit_exceeded"));
    anthropic.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, "resposta do haiku", ""), 10, 10, 0, 50));

    var resposta = await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);

    Checar("Gemini foi tentado", gemini.Chamadas == 1);
    Checar("Haiku assumiu", anthropic.Chamadas == 1 && resposta.PodeResponder);
    Checar("Disjuntor abriu depois do 429", disjuntor.Consultar(FingerprintDeChave.Calcular("chave-fake-de-verificacao")).Estado == EstadoDoDisjuntor.Aberto);

    var linhas = await LerChamadasAsync(escopos);
    Checar("Duas linhas: Gemini (falha) e Haiku (sucesso, fallback)", linhas.Count == 2);
    Checar("Linha do Gemini marca RateLimit", linhas.Any(l => l.Provedor == ProvedorDeIa.Gemini && !l.Sucesso && l.TipoDeFalha == TipoDeFalhaDeIa.RateLimit));
    Checar("Linha do Haiku marca FoiFallback com motivo do Gemini", linhas.Any(l => l.Provedor == ProvedorDeIa.Anthropic && l.FoiFallback && l.MotivoDoFallback != null && l.MotivoDoFallback.Contains("Gemini")));
}
Console.WriteLine();

// ============================================================================
// CENARIO 4 — Quota diaria esgotada: Haiku responde; disjuntor abre ate a
// virada do dia no Pacifico (nao o cooldown curto de rate limit comum).
// ============================================================================
Console.WriteLine("CENARIO 4 — quota diaria esgotada no Gemini, Haiku assume");
{
    var (orq, gemini, anthropic, disjuntor, _, _, _, _) = MontarAmbiente();
    gemini.Enfileirar(ResultadoDoProvedor.DeFalha(TipoDeFalhaDeIa.QuotaEsgotada, 429, 80, "429 RESOURCE_EXHAUSTED (quota diaria)"));
    anthropic.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, "resposta do haiku", ""), 10, 10, 0, 50));

    var resposta = await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);
    var chave = FingerprintDeChave.Calcular("chave-fake-de-verificacao");
    var situacao = disjuntor.Consultar(chave);

    Checar("Haiku assumiu", anthropic.Chamadas == 1 && resposta.PodeResponder);
    Checar("Disjuntor abriu", situacao.Estado == EstadoDoDisjuntor.Aberto);
    Checar(
        "Abertura dura mais que um cooldown comum (ate a meia-noite do Pacifico, nao 120s)",
        situacao.VoltaATentarEm.HasValue && situacao.VoltaATentarEm.Value - DateTime.UtcNow > TimeSpan.FromMinutes(5));
}
Console.WriteLine();

// ============================================================================
// CENARIO 5 — Indisponibilidade (503) e timeout: Haiku responde nos dois casos.
// ============================================================================
Console.WriteLine("CENARIO 5 — 503 e timeout no Gemini, Haiku assume nos dois");
{
    var (orq, gemini, anthropic, _, _, _, _, _) = MontarAmbiente();
    gemini.Enfileirar(ResultadoDoProvedor.DeFalha(TipoDeFalhaDeIa.Indisponibilidade, 503, 120, "503 Service Unavailable"));
    anthropic.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, "resposta 1", ""), 10, 10, 0, 50));
    var r1 = await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);
    Checar("503: Haiku assumiu", r1.PodeResponder && r1.Resposta == "resposta 1");

    gemini.Enfileirar(ResultadoDoProvedor.DeFalha(TipoDeFalhaDeIa.Timeout, null, 20000, "Timeout apos 20s"));
    anthropic.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, "resposta 2", ""), 10, 10, 0, 50));
    var r2 = await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);
    Checar("timeout: Haiku assumiu", r2.PodeResponder && r2.Resposta == "resposta 2");
}
Console.WriteLine();

// ============================================================================
// CENARIO 6 — Retorno automatico: disjuntor abre, cooldown vence, UMA sonda
// controlada, sonda funciona, Gemini volta a ser principal.
// ============================================================================
Console.WriteLine("CENARIO 6 — recuperacao automatica apos o cooldown");
{
    // Cooldown de 1s de proposito, para o teste nao esperar minutos. RPD
    // cadastrado e alto para NAO cair no freio local de descoberta (que usa
    // um intervalo de dezenas de segundos) — aqui quem esta sob teste e o
    // disjuntor, nao o modo de franquia nao cadastrada (isso e o cenario 13).
    var opcoes = NovasOpcoes(limiteRpd: 1000, falhasParaAbrir: 1, cooldownSegundos: 1, cooldownMaximoSegundos: 2);
    var (orq, gemini, anthropic, disjuntor, _, _, _, _) = MontarAmbiente(opcoes);
    var chave = FingerprintDeChave.Calcular("chave-fake-de-verificacao");

    gemini.Enfileirar(ResultadoDoProvedor.DeFalha(TipoDeFalhaDeIa.Indisponibilidade, 503, 50, "fora do ar"));
    anthropic.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, "haiku assumiu", ""), 10, 10, 0, 50));
    await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);
    Checar("Disjuntor abriu com 1 falha (limiar=1)", disjuntor.Consultar(chave).Estado == EstadoDoDisjuntor.Aberto);

    anthropic.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, "ainda no haiku", ""), 10, 10, 0, 50));
    var duranteOCooldown = await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);
    Checar("Enquanto o cooldown nao vence, continua no Haiku sem tentar o Gemini de novo", gemini.Chamadas == 1 && duranteOCooldown.Resposta == "ainda no haiku");

    await Task.Delay(TimeSpan.FromMilliseconds(1100));

    gemini.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, "gemini voltou", ""), 10, 10, 0, 50));
    var sonda = await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);
    Checar("Cooldown vencido: a sonda foi para o Gemini", gemini.Chamadas == 2);
    Checar("A sonda respondeu com sucesso", sonda.PodeResponder && sonda.Resposta == "gemini voltou");
    Checar("Disjuntor fechou depois da sonda", disjuntor.Consultar(chave).Estado == EstadoDoDisjuntor.Fechado);

    gemini.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, "gemini principal de novo", ""), 10, 10, 0, 50));
    var apos = await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);
    Checar("Gemini volta a ser o principal (chamado direto, sem sonda)", gemini.Chamadas == 3 && apos.Resposta == "gemini principal de novo");
}
Console.WriteLine();

// ============================================================================
// CENARIO 7 — Os dois falham: PodeResponder=false, motivo describe as duas
// falhas, e os tetos diarios sao devolvidos (nao gastos numa resposta que nao
// saiu).
// ============================================================================
Console.WriteLine("CENARIO 7 — Gemini e Haiku falham: comportamento seguro");
{
    var (orq, gemini, anthropic, _, _, limite, escopos, _) = MontarAmbiente();
    gemini.Enfileirar(ResultadoDoProvedor.DeFalha(TipoDeFalhaDeIa.Indisponibilidade, 500, 100, "erro interno"));
    anthropic.Enfileirar(ResultadoDoProvedor.DeFalha(TipoDeFalhaDeIa.Indisponibilidade, 500, 100, "erro interno tambem"));

    var resposta = await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);

    Checar("PodeResponder=false", !resposta.PodeResponder);
    Checar("Resposta vazia (nunca inventa)", resposta.Resposta == string.Empty);
    Checar("Motivo cita os dois provedores", resposta.Motivo.Contains("Gemini") && resposta.Motivo.Contains("Haiku"));

    var linhas = await LerChamadasAsync(escopos);
    Checar("Duas tentativas registradas, as duas falhas", linhas.Count == 2 && linhas.All(l => !l.Sucesso));

    var usadoGeral = await limite.ConsultarUsadoAsync(tenantId, CategoriaDeLimiteDiario.Geral, CancellationToken.None);
    var usadoHaiku = await limite.ConsultarUsadoAsync(tenantId, CategoriaDeLimiteDiario.Haiku, CancellationToken.None);
    Checar("Reserva do teto geral foi devolvida (nada foi de fato respondido)", usadoGeral == 0);
    Checar("Reserva do teto do Haiku foi devolvida", usadoHaiku == 0);
}
Console.WriteLine();

// ============================================================================
// CENARIO 8 — Concorrencia: N mensagens simultaneas perto do teto nao
// ultrapassam o limite nem duplicam resposta.
// ============================================================================
Console.WriteLine("CENARIO 8 — concorrencia nao ultrapassa o teto nem duplica resposta");
{
    var opcoes = NovasOpcoes(limiteDiario: 5);
    var (orq, gemini, _, _, _, limite, _, _) = MontarAmbiente(opcoes);

    for (var i = 0; i < 20; i++)
    {
        gemini.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, $"resposta {i}", ""), 10, 10, 0, 20));
    }

    var tarefas = Enumerable.Range(0, 20)
        .Select(_ => orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None))
        .ToArray();

    var resultados = await Task.WhenAll(tarefas);

    var respondidas = resultados.Count(r => r.PodeResponder);
    var usadoGeral = await limite.ConsultarUsadoAsync(tenantId, CategoriaDeLimiteDiario.Geral, CancellationToken.None);

    Checar("No maximo 5 respostas saem (teto geral=5) sob 20 chamadas simultaneas", respondidas <= 5, $"respondidas={respondidas}");
    Checar("O contador nao ultrapassa o teto", usadoGeral <= 5, $"usadoGeral={usadoGeral}");
    Checar("O contador bate exatamente com quantas respostas de fato saíram", usadoGeral == respondidas);
}
Console.WriteLine();

// ============================================================================
// CENARIO 10 — podeResponder=false do Gemini e resposta valida, NAO fallback.
// ============================================================================
Console.WriteLine("CENARIO 10 — Gemini diz 'nao sei responder': nao aciona o Haiku");
{
    var (orq, gemini, anthropic, _, _, _, escopos, _) = MontarAmbiente();
    gemini.Enfileirar(ResultadoDoProvedor.DeSucesso(
        new RespostaDaIA(false, "", "Pergunta fora do catalogo cadastrado."), 30, 15, 0, 90));

    var resposta = await orq.ResponderAsync(tenantId, null, NovoPedido("posso comprar meu terreno?"), CancellationToken.None);

    Checar("Gemini foi chamado uma vez", gemini.Chamadas == 1);
    Checar("Haiku NAO foi chamado — podeResponder=false e resultado valido, nao falha", anthropic.Chamadas == 0);
    Checar("PodeResponder=false propagado como veio do modelo", !resposta.PodeResponder);

    var linhas = await LerChamadasAsync(escopos);
    Checar("Uma linha, Sucesso=true (a chamada funcionou, so nao teve o que dizer)", linhas.Count == 1 && linhas[0].Sucesso);
    Checar("PodeResponder=false gravado na linha", linhas[0].PodeResponder == false);
}
Console.WriteLine();

// ============================================================================
// CENARIO 11 — Gemini falha + Haiku responde conta 1 no teto geral e 1 no teto
// do Haiku — nunca 2 no geral.
// ============================================================================
Console.WriteLine("CENARIO 11 — contagem correta dos dois tetos diarios no fallback");
{
    var (orq, gemini, anthropic, _, _, limite, escopos, _) = MontarAmbiente();
    gemini.Enfileirar(ResultadoDoProvedor.DeFalha(TipoDeFalhaDeIa.RateLimit, 429, 60, "429"));
    anthropic.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, "resposta do haiku", ""), 10, 10, 0, 50));

    await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);

    var usadoGeral = await limite.ConsultarUsadoAsync(tenantId, CategoriaDeLimiteDiario.Geral, CancellationToken.None);
    var usadoHaiku = await limite.ConsultarUsadoAsync(tenantId, CategoriaDeLimiteDiario.Haiku, CancellationToken.None);

    Checar("Teto geral soma exatamente 1 (nao 2)", usadoGeral == 1, $"usadoGeral={usadoGeral}");
    Checar("Teto do Haiku soma exatamente 1", usadoHaiku == 1, $"usadoHaiku={usadoHaiku}");

    var linhas = await LerChamadasAsync(escopos);
    Checar("Duas linhas: uma Sucesso=0 (Gemini) e uma Sucesso=1 (Haiku)",
        linhas.Count(l => !l.Sucesso) == 1 && linhas.Count(l => l.Sucesso) == 1);
}
Console.WriteLine();

// ============================================================================
// CENARIO 12 — Persistencia independente: tentativa fica registrada mesmo
// quando nenhuma resposta final e criada (os dois falham) e mesmo quando o
// fluxo de negocio que chamou nunca chega a salvar nada.
// ============================================================================
Console.WriteLine("CENARIO 12 — persistencia sobrevive mesmo sem commit do fluxo de negocio");
{
    var (orq, gemini, anthropic, _, _, _, escopos, dbDoFluxoDeNegocio) = MontarAmbiente();

    // Simula um fluxo de negocio que criou uma entidade em memoria mas NUNCA
    // chamou SaveChangesAsync — o dbDoFluxoDeNegocio nunca persiste nada.
    // A gravacao da ChamadaDeIa usa um escopo/DbContext PROPRIO (o mesmo banco
    // InMemory, mas outra instancia), entao ela nao depende do commit do
    // primeiro em nenhum momento.
    gemini.Enfileirar(ResultadoDoProvedor.DeFalha(TipoDeFalhaDeIa.Indisponibilidade, 500, 80, "fora do ar"));
    anthropic.Enfileirar(ResultadoDoProvedor.DeFalha(TipoDeFalhaDeIa.Indisponibilidade, 500, 80, "fora do ar tambem"));

    var resposta = await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);
    Checar("Nenhuma resposta final foi criada", !resposta.PodeResponder);

    // O DbContext do "fluxo de negocio" nunca foi salvo — confere que ele
    // continua vazio, provando que o registro nao veio por ali.
    var pendencias = dbDoFluxoDeNegocio.ChangeTracker.Entries().Count();
    Checar("O DbContext do fluxo de negocio nunca foi salvo (nada pendente por SaveChanges nele)", pendencias == 0);

    var linhas = await LerChamadasAsync(escopos);
    Checar("Mesmo assim, DUAS tentativas ficaram registradas (escopo proprio do registrador)", linhas.Count == 2);
    Checar("As duas marcam falha de indisponibilidade", linhas.All(l => l.TipoDeFalha == TipoDeFalhaDeIa.Indisponibilidade));
}
Console.WriteLine();

// ============================================================================
// CENARIO 13 — Limites nao cadastrados (0): sem barra de percentual, Gemini
// continua sendo usado com o freio de descoberta, e 429 de quota diaria
// registra sugestao de LimiteRpd.
// ============================================================================
Console.WriteLine("CENARIO 13 — franquia nao cadastrada: modo de descoberta");
{
    var opcoes = NovasOpcoes(limiteRpd: 0, limiteRpm: 0, limiteTpm: 0, limiteRpmDeDescoberta: 2);
    var (orq, gemini, anthropic, _, cota, _, _, _) = MontarAmbiente(opcoes);
    var chave = FingerprintDeChave.Calcular("chave-fake-de-verificacao");

    var situacaoInicial = await cota.ConsultarAsync(chave, opcoes.Gemini, CancellationToken.None);
    Checar("LimitesCadastrados=false com os tres em zero", !situacaoInicial.LimitesCadastrados);

    gemini.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, "resposta 1", ""), 10, 10, 0, 40));
    await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);
    Checar("Primeira chamada foi ao Gemini mesmo sem limites cadastrados", gemini.Chamadas == 1);

    // LimiteRpmDeDescoberta=2 -> intervalo minimo de 30s entre chamadas do
    // freio local. A proxima chamada, imediata, deve ser barrada e cair no Haiku.
    anthropic.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, "resposta do freio", ""), 10, 10, 0, 40));
    var segunda = await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);
    Checar("Freio local de descoberta bloqueou a chamada imediata seguinte", gemini.Chamadas == 1 && anthropic.Chamadas == 1);
    Checar("Resposta ainda saiu, pelo Haiku", segunda.PodeResponder && segunda.Resposta == "resposta do freio");

    gemini.Enfileirar(ResultadoDoProvedor.DeFalha(TipoDeFalhaDeIa.QuotaEsgotada, 429, 60, "quota diaria"));
    anthropic.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, "resposta 3", ""), 10, 10, 0, 40));

    // O freio de descoberta bloquearia de novo — espera passar o intervalo so
    // para garantir que desta vez o Gemini e de fato tentado.
    await Task.Delay(TimeSpan.FromSeconds(31));
    await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);
    await cota.RegistrarEstouroDeQuotaDiariaAsync(chave, CancellationToken.None);

    var situacaoFinal = await cota.ConsultarAsync(chave, opcoes.Gemini, CancellationToken.None);
    Checar("Sugestao de LimiteRpd registrada apos o 429 de quota diaria", situacaoFinal.SugestaoDeLimiteRpd.HasValue);
}
Console.WriteLine();

// ============================================================================
// CENARIO 14 — Cota global do Gemini compartilhada entre tenants; tetos
// diarios e metricas continuam separados por tenant.
// ============================================================================
Console.WriteLine("CENARIO 14 — cota do Gemini e global; tetos diarios sao por tenant");
{
    var opcoes = NovasOpcoes(limiteRpd: 3, percentualDeSeguranca: 100); // margem 100% = bloqueia so ao ATINGIR o limite
    var (orq, gemini, anthropic, _, cota, limite, _, _) = MontarAmbiente(opcoes);

    var tenantA = Guid.NewGuid();
    var tenantB = Guid.NewGuid();

    for (var i = 0; i < 3; i++)
    {
        gemini.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, $"A{i}", ""), 10, 10, 0, 20));
        await orq.ResponderAsync(tenantA, null, NovoPedido(), CancellationToken.None);
    }

    Checar("Tenant A sozinho ja consumiu as 3 requisicoes do RPD global", gemini.Chamadas == 3);

    anthropic.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, "B caiu no haiku", ""), 10, 10, 0, 20));
    var respostaB = await orq.ResponderAsync(tenantB, null, NovoPedido(), CancellationToken.None);

    Checar(
        "Tenant B, mesmo sem ter usado nada, cai no Haiku (RPD e do PROJETO, nao do tenant)",
        gemini.Chamadas == 3 && anthropic.Chamadas == 1 && respostaB.Resposta == "B caiu no haiku");

    var usadoA = await limite.ConsultarUsadoAsync(tenantA, CategoriaDeLimiteDiario.Geral, CancellationToken.None);
    var usadoB = await limite.ConsultarUsadoAsync(tenantB, CategoriaDeLimiteDiario.Geral, CancellationToken.None);

    Checar("Teto diario GERAL de A e 3 (so as respostas de A)", usadoA == 3);
    Checar("Teto diario GERAL de B e 1 (so a resposta de B) — nao herda o consumo de A", usadoB == 1);
}
Console.WriteLine();

// ============================================================================
// CENARIO 15 — cancelamento da requisicao DURANTE a sonda do meio-aberto nao
// trava o disjuntor para sempre (achado ao vivo, 2026-08-26). Sonda tem
// validade curta neste ambiente so para o teste nao esperar 60s de verdade.
// ============================================================================
Console.WriteLine("CENARIO 15 — cancelamento durante a sonda nao trava o Gemini para sempre");
{
    var opcoes = NovasOpcoes(limiteRpd: 1000, falhasParaAbrir: 1, cooldownSegundos: 1, cooldownMaximoSegundos: 2);
    var (orq, gemini, anthropic, disjuntor, _, limite, _, _) =
        MontarAmbiente(opcoes, tempoMaximoDeSonda: TimeSpan.FromMilliseconds(300));
    var chave = FingerprintDeChave.Calcular("chave-fake-de-verificacao");

    // Abre o disjuntor (mesma receita do cenario 6) e espera o cooldown vencer.
    // Esta primeira chamada e' respondida com sucesso pelo Haiku — CONSOME
    // legitimamente 1 unidade do teto geral, que fica reservada (nao e um bug;
    // e' o mesmo comportamento provado no cenario 11).
    gemini.Enfileirar(ResultadoDoProvedor.DeFalha(TipoDeFalhaDeIa.Indisponibilidade, 503, 50, "fora do ar"));
    anthropic.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, "haiku assumiu", ""), 10, 10, 0, 50));
    await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);
    await Task.Delay(TimeSpan.FromMilliseconds(1100));

    var usadoGeralAntesDaSonda = await limite.ConsultarUsadoAsync(tenantId, CategoriaDeLimiteDiario.Geral, CancellationToken.None);

    // A sonda chega ao Gemini, mas o CALLER cancela a requisicao antes de
    // qualquer provedor responder — simula o webhook do WhatsApp/gateway
    // desistindo em pleno voo.
    var ctsDaSonda = new CancellationTokenSource();
    gemini.EnfileirarCancelamento(ctsDaSonda);

    var lancouCancelamento = false;
    try
    {
        await orq.ResponderAsync(tenantId, null, NovoPedido(), ctsDaSonda.Token);
    }
    catch (OperationCanceledException)
    {
        lancouCancelamento = true;
    }

    Checar("O cancelamento propagou (nao foi engolido como falha comum)", lancouCancelamento);
    Checar("A sonda foi de fato tentada no Gemini", gemini.Chamadas == 2);
    Checar(
        "Disjuntor fica em MeioAberto logo apos o cancelamento (sonda perdida, nao fechada nem reaberta)",
        disjuntor.Consultar(chave).Estado == EstadoDoDisjuntor.MeioAberto);

    // A reserva feita PARA A SONDA CANCELADA tem que voltar exatamente ao mesmo
    // patamar de antes dela (a unidade legitima da primeira chamada continua
    // contada — so a reserva da tentativa cancelada e' que precisa ser devolvida).
    var usadoGeralLogoApos = await limite.ConsultarUsadoAsync(tenantId, CategoriaDeLimiteDiario.Geral, CancellationToken.None);
    Checar(
        "A reserva do teto geral foi devolvida mesmo com o token ja cancelado (CancellationToken.None no Liberar)",
        usadoGeralLogoApos == usadoGeralAntesDaSonda,
        $"antes={usadoGeralAntesDaSonda} depois={usadoGeralLogoApos}");

    // Sem o fix, o proximo PodeTentar cairia sempre no ramo
    // "MeioAberto quando SondaEmAndamento" e devolveria falso PARA SEMPRE — aqui
    // so esperamos a sonda expirar (300ms configurados) para provar que uma
    // NOVA tentativa e' liberada, em vez de bloqueada eternamente.
    await Task.Delay(TimeSpan.FromMilliseconds(400));

    gemini.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, "gemini recuperou", ""), 10, 10, 0, 40));
    var resposta = await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);

    Checar("Apos a sonda expirar, uma NOVA tentativa ao Gemini acontece (nao ficou travado)", gemini.Chamadas == 3);
    Checar("E essa tentativa teve sucesso", resposta.PodeResponder && resposta.Resposta == "gemini recuperou");
    Checar("Disjuntor fechou depois do sucesso", disjuntor.Consultar(chave).Estado == EstadoDoDisjuntor.Fechado);
}
Console.WriteLine();

// ============================================================================
// CENARIO 16 — motivo de fallback maior que a coluna (NVARCHAR(200) em
// dbo.ChamadasDeIa.MotivoDoFallback) e' truncado no codigo, nao perde a linha
// inteira. NOTA: o provedor InMemory do EF nao aplica MaxLength — este cenario
// prova que o TRUNCAMENTO ACONTECE (a mensagem gravada cabe em 200), nao que o
// SaveChanges falharia sem ele; a falha real so aparece contra SQL Server de
// verdade.
// ============================================================================
Console.WriteLine("CENARIO 16 — motivo de fallback gigante e truncado, nao perde a linha");
{
    var (orq, gemini, anthropic, _, _, _, escopos, _) = MontarAmbiente();

    // Maior que a mensagem real do 429 do free tier do Gemini (~170 caracteres,
    // ver comentario em ProvedorGemini.ClassificarErro) — aqui exagerado de
    // proposito para nao depender do tamanho exato de um texto real.
    var detalheGigante = new string('x', 500);
    gemini.Enfileirar(ResultadoDoProvedor.DeFalha(TipoDeFalhaDeIa.RateLimit, 429, 80, detalheGigante));
    anthropic.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, "resposta do haiku", ""), 10, 10, 0, 50));

    var resposta = await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);
    Checar("Resposta ainda saiu pelo Haiku, apesar do motivo gigante", resposta.PodeResponder);

    var linhas = await LerChamadasAsync(escopos);
    var linhaDoHaiku = linhas.SingleOrDefault(l => l.Provedor == ProvedorDeIa.Anthropic);

    Checar("A linha do Haiku foi gravada (nao se perdeu por causa do motivo gigante)", linhaDoHaiku is not null);
    Checar(
        "MotivoDoFallback coube no limite da coluna (<=200 caracteres)",
        linhaDoHaiku?.MotivoDoFallback is { Length: <= 200 },
        $"tamanho={linhaDoHaiku?.MotivoDoFallback?.Length}");
    Checar(
        "MotivoDoFallback truncado preserva o inicio da descricao original",
        linhaDoHaiku?.MotivoDoFallback?.StartsWith("Gemini: RateLimit") == true);
}
Console.WriteLine();

// ============================================================================
// CENARIO 17 — o retryDelay do 429 do Gemini e' interpretado em cultura
// invariante, nao na cultura do processo (achado ao vivo, 2026-08-25/26: em
// pt-BR, "30.5" sem CultureInfo.InvariantCulture vira 305). Via reflection
// porque ExtrairRetryDelayDoTexto e' privado — nao existe caminho publico para
// isto sem literalmente sofrer um 429 de verdade contra a rede.
// ============================================================================
Console.WriteLine("CENARIO 17 — retryDelay do Gemini em cultura invariante (nao pt-BR)");
{
    var metodo = typeof(ProvedorGemini).GetMethod(
        "ExtrairRetryDelayDoTexto", BindingFlags.NonPublic | BindingFlags.Static);

    if (metodo is null)
    {
        Checar("Metodo ExtrairRetryDelayDoTexto encontrado via reflection (verificador desatualizado?)", false);
    }
    else
    {
        var resultado = (TimeSpan?)metodo.Invoke(null, ["Please retry in 30.5s."]);
        Checar(
            "\"retry in 30.5s\" vira 30,5 segundos — nao 305 nem 30500 (parse em cultura invariante)",
            resultado == TimeSpan.FromSeconds(30.5), $"resultado={resultado}");
    }
}
Console.WriteLine();

// ============================================================================
// CENARIO 18 — cancelamento DURANTE a chamada ao HAIKU (nao ao Gemini/sonda)
// tambem devolve a reserva do teto do Haiku, alem da do teto Geral. Achado
// numa revisao critica em 2026-08-26, depois do fix do cenario 15: aquele
// cenario so cancelava durante o Gemini — cancelar durante o Haiku seguia um
// caminho diferente (a liberacao ficava inline, DEPOIS da chamada, e por isso
// nunca era alcancada quando a chamada em si era cancelada).
// ============================================================================
Console.WriteLine("CENARIO 18 — cancelamento durante a chamada ao Haiku devolve as duas reservas");
{
    var (orq, gemini, anthropic, _, _, limite, escopos, _) = MontarAmbiente();

    var usadoGeralAntes = await limite.ConsultarUsadoAsync(tenantId, CategoriaDeLimiteDiario.Geral, CancellationToken.None);
    var usadoHaikuAntes = await limite.ConsultarUsadoAsync(tenantId, CategoriaDeLimiteDiario.Haiku, CancellationToken.None);

    // Gemini falha NORMALMENTE (nao cancelado) — precisa disso para o fluxo
    // chegar ao Haiku e conseguir a reserva do teto do Haiku antes do
    // cancelamento acontecer.
    gemini.Enfileirar(ResultadoDoProvedor.DeFalha(TipoDeFalhaDeIa.Indisponibilidade, 500, 60, "gemini fora do ar"));

    // O CALLER cancela DURANTE a chamada ao Haiku — simula o webhook/gateway
    // desistindo enquanto o Haiku ainda estava respondendo.
    var ctsDoHaiku = new CancellationTokenSource();
    anthropic.EnfileirarCancelamento(ctsDoHaiku);

    var lancouCancelamento = false;
    try
    {
        await orq.ResponderAsync(tenantId, null, NovoPedido(), ctsDoHaiku.Token);
    }
    catch (OperationCanceledException)
    {
        lancouCancelamento = true;
    }

    Checar("O cancelamento propagou (mesma semantica do cenario 15)", lancouCancelamento);
    Checar("O Gemini foi tentado e falhou normalmente antes do cancelamento", gemini.Chamadas == 1);
    Checar("O Haiku foi de fato tentado (a reserva chegou a ser concedida)", anthropic.Chamadas == 1);

    var usadoGeralDepois = await limite.ConsultarUsadoAsync(tenantId, CategoriaDeLimiteDiario.Geral, CancellationToken.None);
    var usadoHaikuDepois = await limite.ConsultarUsadoAsync(tenantId, CategoriaDeLimiteDiario.Haiku, CancellationToken.None);

    Checar(
        "Teto GERAL voltou a linha de base (nao consumiu unidade sem resposta entregue)",
        usadoGeralDepois == usadoGeralAntes, $"antes={usadoGeralAntes} depois={usadoGeralDepois}");
    Checar(
        "Teto HAIKU voltou a linha de base (a reserva concedida antes do cancelamento foi devolvida)",
        usadoHaikuDepois == usadoHaikuAntes, $"antes={usadoHaikuAntes} depois={usadoHaikuDepois}");

    // Divida de observabilidade conhecida (registrada na revisao, item 5): a
    // tentativa cancelada do Haiku NAO gera ChamadaDeIa — TentarProvedorAsync
    // relanca antes de montar a linha. So a falha normal do Gemini fica
    // registrada. Nao e' o que este cenario existe para corrigir, so para nao
    // fingir que a linha existe.
    var linhasAposCancelamento = await LerChamadasAsync(escopos);
    Checar(
        "So a linha do Gemini (falha normal) foi gravada; a tentativa cancelada do Haiku nao gera linha",
        linhasAposCancelamento.Count == 1 && linhasAposCancelamento[0].Provedor == ProvedorDeIa.Gemini);

    // Prova final: nada ficou preso. Uma tentativa NORMAL subsequente ainda
    // consegue reservar e responder pelos dois tetos.
    gemini.Enfileirar(ResultadoDoProvedor.DeFalha(TipoDeFalhaDeIa.Indisponibilidade, 500, 60, "gemini ainda fora do ar"));
    anthropic.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, "haiku respondeu normalmente", ""), 10, 10, 0, 50));

    var respostaNormal = await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);
    Checar(
        "Tentativa subsequente reserva e responde normalmente (nada ficou preso)",
        respostaNormal.PodeResponder && respostaNormal.Resposta == "haiku respondeu normalmente");

    var usadoGeralFinal = await limite.ConsultarUsadoAsync(tenantId, CategoriaDeLimiteDiario.Geral, CancellationToken.None);
    var usadoHaikuFinal = await limite.ConsultarUsadoAsync(tenantId, CategoriaDeLimiteDiario.Haiku, CancellationToken.None);
    Checar(
        "Essa resposta bem-sucedida soma +1 no teto geral em relacao a linha de base",
        usadoGeralFinal == usadoGeralAntes + 1, $"usadoGeralFinal={usadoGeralFinal}");
    Checar(
        "E +1 no teto do Haiku em relacao a linha de base",
        usadoHaikuFinal == usadoHaikuAntes + 1, $"usadoHaikuFinal={usadoHaikuFinal}");
}
Console.WriteLine();

// ============================================================================
// CENARIO 19 — variante ESTRUTURADA do retryDelay (details[].RetryInfo.retryDelay,
// nao o texto livre da mensagem) tambem precisa de cultura invariante — o
// mesmo fix do cenario 17 cobre os DOIS pontos de parse alterados em
// ProvedorGemini.cs, mas so o de texto livre tinha prova direta ate agora.
// Via HttpMessageHandler fake + ProvedorGemini.GerarAsync PUBLICO (nao
// reflection): exercita tambem ClassificarErro e a desserializacao real dos
// DTOs de erro do Google. CurrentCulture fica EXPLICITAMENTE pt-BR durante o
// teste — nao depende da cultura do ambiente onde o verificador roda.
// ============================================================================
Console.WriteLine("CENARIO 19 — retryDelay ESTRUTURADO (RetryInfo) em cultura invariante, sob pt-BR");
{
    var culturaOriginal = CultureInfo.CurrentCulture;
    CultureInfo.CurrentCulture = new CultureInfo("pt-BR");

    try
    {
        var opcoesDaIA = NovasOpcoes();

        async Task<ResultadoDoProvedor> ChamarComRetryDelayAsync(string retryDelay)
        {
            // Sem interpolacao no raw string: JSON tem varios "}}" literais
            // seguidos, que colidiriam com o delimitador de interpolacao "$$".
            // Placeholder + Replace evita a confusao.
            var corpoDoErro = """
                {"error":{"code":429,"message":"quota exceeded","status":"RESOURCE_EXHAUSTED",
                "details":[{"@type":"type.googleapis.com/google.rpc.RetryInfo","retryDelay":"__RETRY_DELAY__"}]}}
                """.Replace("__RETRY_DELAY__", retryDelay);

            using var handler = new HandlerFake(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent(corpoDoErro, Encoding.UTF8, "application/json")
            });
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://fake.gemini.local/v1/") };
            var provedor = new ProvedorGemini(http, Options.Create(opcoesDaIA), NullLogger<ProvedorGemini>.Instance);

            return await provedor.GerarAsync(NovoPedido(), CancellationToken.None);
        }

        var normal = await ChamarComRetryDelayAsync("1.5s");
        Checar(
            "\"1.5s\" estruturado vira 1,5 segundo — nao 15 (decimal com ponto, cultura invariante)",
            normal.RetryApos == TimeSpan.FromSeconds(1.5), $"RetryApos={normal.RetryApos}");
        Checar(
            "Classificado como falha 429 (RateLimit/QuotaEsgotada), nunca sucesso",
            !normal.Sucesso && normal.Falha is TipoDeFalhaDeIa.RateLimit or TipoDeFalhaDeIa.QuotaEsgotada);

        var absurdo = await ChamarComRetryDelayAsync("999999s");
        Checar(
            "retryDelay estruturado absurdo (>3600s) e' descartado pela mesma protecao (SanidadeDoRetryDelay)",
            absurdo.RetryApos is null, $"RetryApos={absurdo.RetryApos}");
    }
    finally
    {
        CultureInfo.CurrentCulture = culturaOriginal;
    }
}
Console.WriteLine();

// ============================================================================
// CENARIO 20 — Haiku sem cota disponivel (teto diario do Haiku ja esgotado):
// TentarReservarAsync devolve falso, o Haiku nunca chega a ser chamado, e a
// reserva do teto GERAL feita para essa tentativa e' devolvida. Fecha a ultima
// linha da matriz de consumo pedida na revisao — a unica sem cobertura direta
// ate agora. limiteRpd alto de proposito: tira o Gemini do modo de descoberta
// (que tem um freio de ~12s entre tentativas) para as duas chamadas do teste
// nao dependerem de tempo de espera nenhum.
// ============================================================================
Console.WriteLine("CENARIO 20 — Haiku sem cota disponivel: Geral devolvido, Haiku nunca tentado");
{
    var opcoes = NovasOpcoes(limiteDiarioHaiku: 1, limiteRpd: 1000);
    var (orq, gemini, anthropic, _, _, limite, escopos, _) = MontarAmbiente(opcoes);

    // Primeira chamada consome a UNICA vaga do teto do Haiku (Gemini falha, Haiku responde).
    gemini.Enfileirar(ResultadoDoProvedor.DeFalha(TipoDeFalhaDeIa.Indisponibilidade, 500, 60, "fora do ar"));
    anthropic.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, "primeira resposta", ""), 10, 10, 0, 40));
    await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);

    var usadoHaikuAposPrimeira = await limite.ConsultarUsadoAsync(tenantId, CategoriaDeLimiteDiario.Haiku, CancellationToken.None);
    Checar("Teto do Haiku (=1) ja esta no limite apos a primeira chamada", usadoHaikuAposPrimeira == 1);

    var usadoGeralAntes = await limite.ConsultarUsadoAsync(tenantId, CategoriaDeLimiteDiario.Geral, CancellationToken.None);

    // Segunda chamada: Gemini falha de novo (RPD cadastrado e alto — sem freio
    // de descoberta, entao o Gemini E' tentado de novo, sem esperar). O Haiku
    // NAO TEM MAIS VAGA — TentarReservarAsync devolve falso e GerarAsync do
    // Haiku nunca chega a ser chamado.
    gemini.Enfileirar(ResultadoDoProvedor.DeFalha(TipoDeFalhaDeIa.Indisponibilidade, 500, 60, "ainda fora do ar"));
    var resposta = await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);

    Checar("Resposta final e' negativa (nem Gemini nem Haiku responderam)", !resposta.PodeResponder);
    Checar("Gemini foi tentado nas duas chamadas", gemini.Chamadas == 2);
    Checar("O Haiku foi chamado SO na primeira vez (sem vaga na segunda, nunca tentado)", anthropic.Chamadas == 1);

    var usadoGeralDepois = await limite.ConsultarUsadoAsync(tenantId, CategoriaDeLimiteDiario.Geral, CancellationToken.None);
    var usadoHaikuDepois = await limite.ConsultarUsadoAsync(tenantId, CategoriaDeLimiteDiario.Haiku, CancellationToken.None);

    Checar(
        "Teto GERAL volta a linha de base (a reserva da segunda tentativa foi devolvida)",
        usadoGeralDepois == usadoGeralAntes, $"antes={usadoGeralAntes} depois={usadoGeralDepois}");
    Checar(
        "Teto HAIKU permanece EXATAMENTE no mesmo nivel de antes (nada foi reservado para a segunda tentativa)",
        usadoHaikuDepois == usadoHaikuAposPrimeira, $"antes={usadoHaikuAposPrimeira} depois={usadoHaikuDepois}");

    var linhas = await LerChamadasAsync(escopos);
    Checar(
        "3 linhas no total (2 falhas do Gemini + 1 sucesso do Haiku) — nenhuma tentativa fantasma ao Haiku sem vaga",
        linhas.Count == 3 && linhas.Count(l => l.Provedor == ProvedorDeIa.Anthropic) == 1);
}
Console.WriteLine();

// ============================================================================
// CENARIOS 21-30 — migration 042: TipoDeFallback, RodadaId, MotivoDaNaoResposta,
// ContextoDaChamada (TipoServicoId/SetorId/MensagemId) e IniciadaEm como INICIO
// da chamada, nao o fim. Cada um prova exatamente um campo novo; os cenarios
// 1-20 acima continuam provando o comportamento de selecao/cota/disjuntor sem
// tocar nesses campos.
// ============================================================================

Console.WriteLine("CENARIO 21 — fallback EXECUTADO: TipoDeFallback e mesma RodadaId nas duas linhas");
{
    var (orq, gemini, anthropic, _, _, _, escopos, _) = MontarAmbiente();
    gemini.Enfileirar(ResultadoDoProvedor.DeFalha(TipoDeFalhaDeIa.RateLimit, 429, 80, "429 rate_limit_exceeded"));
    anthropic.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, "resposta do haiku", ""), 10, 10, 0, 50));

    await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);

    var linhas = await LerChamadasAsync(escopos);
    var linhaGemini = linhas.Single(l => l.Provedor == ProvedorDeIa.Gemini);
    var linhaHaiku = linhas.Single(l => l.Provedor == ProvedorDeIa.Anthropic);

    Checar("Linha do Gemini (a que falhou): TipoDeFallback=NaoEhFallback", linhaGemini.TipoDeFallback == TipoDeFallbackDeIa.NaoEhFallback);
    Checar("Linha do Haiku (a que assumiu): TipoDeFallback=Executado", linhaHaiku.TipoDeFallback == TipoDeFallbackDeIa.Executado);
    Checar(
        "Mesma RodadaId nas duas linhas (a mesma pergunta)",
        linhaGemini.RodadaId != null && linhaGemini.RodadaId == linhaHaiku.RodadaId);
}
Console.WriteLine();

Console.WriteLine("CENARIO 22 — Gemini desligado: TipoDeFallback=PreventivoDesligado, nenhuma linha do Gemini");
{
    var opcoes = NovasOpcoes();
    opcoes.Gemini.Habilitado = false;
    var (orq, gemini, anthropic, _, _, _, escopos, _) = MontarAmbiente(opcoes);
    anthropic.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, "resposta do haiku", ""), 10, 10, 0, 50));

    var resposta = await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);

    Checar("Gemini NUNCA foi discado", gemini.Chamadas == 0);
    Checar("Haiku respondeu", anthropic.Chamadas == 1 && resposta.PodeResponder);

    var linhas = await LerChamadasAsync(escopos);
    Checar("UMA linha so — nao ha linha do Gemini para uma chamada que nunca saiu", linhas.Count == 1);
    Checar(
        "A linha e do Haiku, TipoDeFallback=PreventivoDesligado",
        linhas[0].Provedor == ProvedorDeIa.Anthropic && linhas[0].TipoDeFallback == TipoDeFallbackDeIa.PreventivoDesligado);
    Checar(
        "Motivo comeca com 'Gemini desligado'",
        linhas[0].MotivoDoFallback != null && linhas[0].MotivoDoFallback.StartsWith("Gemini desligado"));
    Checar(
        "Nenhuma linha com Provedor=Gemini — o coracao do bug original (Caidas no Haiku vs Nenhum fallback)",
        !linhas.Any(l => l.Provedor == ProvedorDeIa.Gemini));
}
Console.WriteLine();

Console.WriteLine("CENARIO 23 — disjuntor aberto: segunda chamada pula o Gemini preventivamente");
{
    var (orq, gemini, anthropic, disjuntor, _, _, escopos, _) = MontarAmbiente();

    // Primeira chamada abre o disjuntor.
    gemini.Enfileirar(ResultadoDoProvedor.DeFalha(TipoDeFalhaDeIa.RateLimit, 429, 80, "429 rate_limit_exceeded"));
    anthropic.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, "primeira", ""), 10, 10, 0, 50));
    await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);

    Checar(
        "Disjuntor abriu apos a primeira falha",
        disjuntor.Consultar(FingerprintDeChave.Calcular("chave-fake-de-verificacao")).Estado == EstadoDoDisjuntor.Aberto);

    // Segunda chamada, imediatamente depois: cooldown nao venceu ainda -> o
    // Gemini e pulado sem ser discado, nao por ter falhado de novo.
    anthropic.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, "segunda", ""), 10, 10, 0, 50));
    await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);

    Checar("Gemini NAO foi discado na segunda chamada (disjuntor aberto)", gemini.Chamadas == 1);

    var linhaDaSegunda = (await LerChamadasAsync(escopos)).Last();
    Checar(
        "Segunda chamada: linha do Haiku com TipoDeFallback=PreventivoDisjuntor",
        linhaDaSegunda.Provedor == ProvedorDeIa.Anthropic && linhaDaSegunda.TipoDeFallback == TipoDeFallbackDeIa.PreventivoDisjuntor);
    Checar(
        "Motivo comeca com 'Disjuntor'",
        linhaDaSegunda.MotivoDoFallback != null && linhaDaSegunda.MotivoDoFallback.StartsWith("Disjuntor"));
}
Console.WriteLine();

Console.WriteLine("CENARIO 24 — margem de cota atingida: TipoDeFallback=PreventivoMargemDeCota");
{
    // RPD=10, margem=80% -> bloqueia a partir da 9a reserva (8 ja usadas) — mesma
    // configuracao do CENARIO 2, so acrescentando as asserções dos campos novos.
    var opcoes = NovasOpcoes(limiteRpd: 10, percentualDeSeguranca: 80);
    var (orq, gemini, anthropic, _, _, _, escopos, _) = MontarAmbiente(opcoes);

    for (var i = 0; i < 8; i++)
    {
        gemini.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, $"resposta {i}", ""), 10, 10, 0, 50));
        await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);
    }

    anthropic.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, "resposta do haiku", ""), 10, 10, 0, 50));
    await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);

    var linhaDoHaiku = (await LerChamadasAsync(escopos)).Single(l => l.Provedor == ProvedorDeIa.Anthropic);
    Checar("TipoDeFallback=PreventivoMargemDeCota", linhaDoHaiku.TipoDeFallback == TipoDeFallbackDeIa.PreventivoMargemDeCota);
    Checar(
        "Motivo comeca com 'Margem de seguranca'",
        linhaDoHaiku.MotivoDoFallback != null && linhaDoHaiku.MotivoDoFallback.StartsWith("Margem de seguran"));
}
Console.WriteLine();

Console.WriteLine("CENARIO 25 — Gemini responde direto: TipoDeFallback=NaoEhFallback, RodadaId preenchida");
{
    var (orq, gemini, _, _, _, _, escopos, _) = MontarAmbiente();
    gemini.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, "Atendemos das 8h as 17h.", ""), 100, 40, 0, 250));

    await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);

    var linha = (await LerChamadasAsync(escopos)).Single();
    Checar("TipoDeFallback=NaoEhFallback numa resposta direta do Gemini", linha.TipoDeFallback == TipoDeFallbackDeIa.NaoEhFallback);
    Checar("RodadaId preenchida mesmo numa unica tentativa (sem fallback)", linha.RodadaId != null);
}
Console.WriteLine();

Console.WriteLine("CENARIO 26 — categoria da nao-resposta: so persistida quando podeResponder=false");
{
    // Limites CADASTRADOS (RPD/RPM/TPM > 0): desliga o freio local de descoberta
    // (CENARIO 13 ja prova que ele bloqueia chamadas consecutivas sem 12s de
    // espaco quando a franquia nao esta cadastrada) — aqui o que se quer testar
    // e' a categoria da nao-resposta em tres chamadas seguidas, nao o freio.
    var opcoes = NovasOpcoes(limiteRpd: 1000, limiteRpm: 1000, limiteTpm: 1_000_000);
    var (orq, gemini, _, _, _, _, escopos, _) = MontarAmbiente(opcoes);
    gemini.Enfileirar(ResultadoDoProvedor.DeSucesso(
        new RespostaDaIA(false, "", "Pergunta sobre preco de escritura de doacao.", MotivoDaNaoRespostaDeIa.PrecoNaoCadastrado),
        30, 15, 0, 90));

    var resposta = await orq.ResponderAsync(tenantId, null, NovoPedido("quanto custa uma doacao?"), CancellationToken.None);
    Checar("Resposta negativa (nao sabe)", !resposta.PodeResponder);

    var primeiraLinha = (await LerChamadasAsync(escopos)).Single();
    Checar(
        "Sucesso=true, PodeResponder=false, categoria persistida",
        primeiraLinha.Sucesso && primeiraLinha.PodeResponder == false
            && primeiraLinha.MotivoDaNaoResposta == MotivoDaNaoRespostaDeIa.PrecoNaoCadastrado);

    // Segunda chamada: agora sabe responder -> categoria fica nula, nunca "zero" disfarcado.
    gemini.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, "R$ 250,00.", ""), 20, 10, 0, 60));
    await orq.ResponderAsync(tenantId, null, NovoPedido("quanto custa uma doacao?"), CancellationToken.None);

    var linhaDeSucesso = (await LerChamadasAsync(escopos)).Last();
    Checar(
        "Categoria fica NULA quando podeResponder=true",
        linhaDeSucesso.PodeResponder == true && linhaDeSucesso.MotivoDaNaoResposta == null);

    // Terceira chamada: provedor fake construido SEM categoria explicita (usa o
    // default do record) -> cai no piso seguro, nunca fica nulo por omissao.
    gemini.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(false, "", "sem categoria enviada"), 10, 5, 0, 40));
    await orq.ResponderAsync(tenantId, null, NovoPedido("pergunta sem categoria"), CancellationToken.None);

    var linhaSemCategoria = (await LerChamadasAsync(escopos)).Last();
    Checar(
        "Sem categoria explicita cai em NaoClassificado (nunca nulo quando podeResponder=false)",
        linhaSemCategoria.PodeResponder == false && linhaSemCategoria.MotivoDaNaoResposta == MotivoDaNaoRespostaDeIa.NaoClassificado);
}
Console.WriteLine();

Console.WriteLine("CENARIO 27 — ContextoDaChamada completo chega identico na linha gravada");
{
    var (orq, gemini, _, _, _, _, escopos, _) = MontarAmbiente();
    gemini.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, "ok", ""), 10, 10, 0, 40));

    var conversaId = Guid.NewGuid();
    var mensagemId = Guid.NewGuid();
    var tipoServicoId = Guid.NewGuid();
    var setorId = Guid.NewGuid();
    var contexto = new ContextoDaChamada(conversaId, mensagemId, tipoServicoId, setorId);

    await orq.ResponderAsync(tenantId, contexto, NovoPedido(), CancellationToken.None);

    var linha = (await LerChamadasAsync(escopos)).Single();
    Checar("ConversaId chega identico", linha.ConversaId == conversaId);
    Checar("MensagemId chega identico", linha.MensagemId == mensagemId);
    Checar("TipoServicoId chega identico (snapshot do instante da chamada)", linha.TipoServicoId == tipoServicoId);
    Checar("SetorId chega identico (snapshot do instante da chamada)", linha.SetorId == setorId);
}
Console.WriteLine();

Console.WriteLine("CENARIO 28 — ContextoDaChamada nulo: as quatro colunas ficam nulas, nada quebra");
{
    var (orq, gemini, _, _, _, _, escopos, _) = MontarAmbiente();
    gemini.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, "ok", ""), 10, 10, 0, 40));

    var resposta = await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);
    Checar("Resposta normal, sem contexto (ex.: chamada do painel de teste)", resposta.PodeResponder);

    var linha = (await LerChamadasAsync(escopos)).Single();
    Checar("ConversaId nulo", linha.ConversaId == null);
    Checar("MensagemId nulo", linha.MensagemId == null);
    Checar("TipoServicoId nulo", linha.TipoServicoId == null);
    Checar("SetorId nulo", linha.SetorId == null);
}
Console.WriteLine();

Console.WriteLine("CENARIO 29 — IniciadaEm marca o INICIO da chamada, nao o fim");
{
    var (orq, gemini, _, _, _, _, escopos, _) = MontarAmbiente();
    var atraso = TimeSpan.FromMilliseconds(300);
    gemini.EnfileirarComAtraso(
        atraso, ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, "ok", ""), 10, 10, 0, (int)atraso.TotalMilliseconds));

    var antes = DateTime.UtcNow;
    await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);
    var depois = DateTime.UtcNow;

    Checar(
        "Pre-condicao do cenario: a chamada realmente levou o atraso configurado",
        depois - antes >= atraso, $"decorrido={(depois - antes).TotalMilliseconds}ms");

    var linha = (await LerChamadasAsync(escopos)).Single();
    Checar(
        "IniciadaEm cai perto de ANTES (inicio) — nao mais no antigo 'depois' (fim)",
        linha.IniciadaEm >= antes.AddMilliseconds(-50) && linha.IniciadaEm <= antes.AddMilliseconds(150),
        $"antes={antes:O} iniciadaEm={linha.IniciadaEm:O} depois={depois:O}");
    Checar(
        "IniciadaEm fica ANTES de (depois - metade do atraso) — prova que nao esta grudado no fim",
        linha.IniciadaEm < depois - TimeSpan.FromMilliseconds(atraso.TotalMilliseconds / 2));
}
Console.WriteLine();

Console.WriteLine("CENARIO 30 — os dois provedores falham: RodadaId igual, TipoDeFallback correto em cada linha");
{
    var (orq, gemini, anthropic, _, _, _, escopos, _) = MontarAmbiente();
    gemini.Enfileirar(ResultadoDoProvedor.DeFalha(TipoDeFalhaDeIa.Indisponibilidade, 500, 100, "erro interno"));
    anthropic.Enfileirar(ResultadoDoProvedor.DeFalha(TipoDeFalhaDeIa.Indisponibilidade, 500, 100, "erro interno tambem"));

    var resposta = await orq.ResponderAsync(tenantId, null, NovoPedido(), CancellationToken.None);
    Checar("Resposta final negativa", !resposta.PodeResponder);

    var linhas = await LerChamadasAsync(escopos);
    Checar("Duas linhas, nenhuma com Sucesso=true", linhas.Count == 2 && linhas.All(l => !l.Sucesso));

    var linhaGemini = linhas.Single(l => l.Provedor == ProvedorDeIa.Gemini);
    var linhaHaiku = linhas.Single(l => l.Provedor == ProvedorDeIa.Anthropic);
    Checar(
        "Mesma RodadaId nas duas linhas, mesmo as duas tendo falhado",
        linhaGemini.RodadaId != null && linhaGemini.RodadaId == linhaHaiku.RodadaId);
    Checar("Linha do Gemini: TipoDeFallback=NaoEhFallback (e a tentativa principal, falhou ou nao)", linhaGemini.TipoDeFallback == TipoDeFallbackDeIa.NaoEhFallback);
    Checar("Linha do Haiku: TipoDeFallback=Executado (o Gemini FOI discado antes)", linhaHaiku.TipoDeFallback == TipoDeFallbackDeIa.Executado);
}
Console.WriteLine();

Console.WriteLine("CENARIO 31 — blindagem LGPD: CPF/CNPJ/telefone/e-mail/CEP mascarados antes do pedido sair para o provedor");
{
    // Nivel do RespostaInteligenteService, nao do orquestrador: e' ali que o
    // PedidoDeIa e' montado e mascarado, uma camada acima do que
    // MontarAmbiente()/NovoPedido() deste arquivo exercitam.
    var (db, _) = NovoBanco();
    var capturador = new CapturadorDePedido();
    var servico = new RespostaInteligenteService(db, capturador, Options.Create(NovasOpcoes()), NullLogger<RespostaInteligenteService>.Instance);

    var historicoComDadoPessoal = new List<TrocaAnterior>
    {
        new(true, "meu CPF e 123.456.789-00, pode confirmar meu cadastro?"),
        new(false, "Claro, so um instante."),
    };

    await servico.ResponderAsync(
        tenantId,
        historicoComDadoPessoal,
        "meu telefone e (38) 99900-0045 e meu email e cliente@exemplo.com, o CEP e 39500-000",
        contexto: null,
        ct: CancellationToken.None);

    var pedido = capturador.UltimoPedido!;
    Checar("CPF do historico mascarado", pedido.Historico[0].Conteudo.Contains("[CPF]") && !pedido.Historico[0].Conteudo.Contains("123.456.789"));
    Checar("Troca sem dado pessoal passa intacta", pedido.Historico[1].Conteudo == "Claro, so um instante.");
    Checar("Telefone da pergunta mascarado", pedido.MensagemDoCliente.Contains("[TELEFONE]") && !pedido.MensagemDoCliente.Contains("99900-0045"));
    Checar("E-mail da pergunta mascarado", pedido.MensagemDoCliente.Contains("[EMAIL]") && !pedido.MensagemDoCliente.Contains("cliente@exemplo.com"));
    Checar("CEP da pergunta mascarado", pedido.MensagemDoCliente.Contains("[CEP]") && !pedido.MensagemDoCliente.Contains("39500-000"));

    var capturadorCnpj = new CapturadorDePedido();
    var servicoCnpj = new RespostaInteligenteService(db, capturadorCnpj, Options.Create(NovasOpcoes()), NullLogger<RespostaInteligenteService>.Instance);
    await servicoCnpj.ResponderAsync(tenantId, [], "o CNPJ da empresa e 12.345.678/0001-95", contexto: null, ct: CancellationToken.None);
    var pedidoCnpj = capturadorCnpj.UltimoPedido!;
    Checar("CNPJ mascarado (nao confundido com CPF)", pedidoCnpj.MensagemDoCliente.Contains("[CNPJ]") && !pedidoCnpj.MensagemDoCliente.Contains("12.345.678"));
}
Console.WriteLine();

Console.WriteLine("CENARIO 33 — formatacao do WhatsApp: Markdown da IA nunca chega cru ao cliente");
{
    // Teste ao vivo de 2026-09-30: a IA respondeu "**Alteracao de nome**" e o cliente leu os
    // asteriscos. A conversao roda no servico, para qualquer provedor.
    var (orq, gemini, _, _, _, _, _, db) = MontarAmbiente();
    var servico = new RespostaInteligenteService(
        db, orq, Options.Create(NovasOpcoes()), NullLogger<RespostaInteligenteService>.Instance);
    var doModelo = "Sim, exatamente! 📝\n\nPor exemplo:\n- **Alteração de nome** — exige documentação diferente de **reconhecimento de paternidade**\n"
        + "* **Divórcio** — tem procedimento distinto\n\n### Próximo passo\n➡️ **Qual registro deseja alterar:** nascimento, casamento ou óbito?\n"
        + "Veja [o site](https://exemplo.com.br).";
    gemini.Enfileirar(ResultadoDoProvedor.DeSucesso(new RespostaDaIA(true, doModelo, ""), 100, 40, 0, 250));

    var r = await servico.ResponderAsync(tenantId, [], "cada averbacao tem documento proprio?", null, CancellationToken.None);

    Checar("Nenhum asterisco duplo sobra", r.PodeResponder && !r.Resposta.Contains("**"), r.Resposta);
    Checar("Negrito vira um asterisco de cada lado", r.Resposta.Contains("*Alteração de nome*") && r.Resposta.Contains("*Qual registro deseja alterar:*"), r.Resposta);
    Checar("Itens de lista viram \"•\"", r.Resposta.Contains("• *Alteração de nome*") && r.Resposta.Contains("• *Divórcio*"), r.Resposta);
    Checar("Titulo com # vira negrito", r.Resposta.Contains("*Próximo passo*") && !r.Resposta.Contains("#"), r.Resposta);
    Checar("Link fica legivel", r.Resposta.Contains("o site: https://exemplo.com.br"), r.Resposta);

    var jaCerto = "Atendemos das *8h às 17h*. _Traga_ o documento:\n• RG\n• CPF";
    Checar("Texto ja no formato do WhatsApp passa igual", FormatacaoDeWhatsapp.Ajustar(jaCerto) == jaCerto);
}
Console.WriteLine();

Console.WriteLine(falhas == 0 ? "TODOS OS CENARIOS PASSARAM." : $"{falhas} CENARIO(S) COM FALHA.");
return falhas == 0 ? 0 : 1;

// ============================================================================
// Blindagem LGPD (CENARIO 31): captura o PedidoDeIa que o RespostaInteligenteService
// monta, sem exercitar Gemini/Haiku/cota — o que este cenario mede e' o texto
// que SAIRIA para qualquer provedor, nao a escolha de qual provedor responde.
// ============================================================================
file sealed class CapturadorDePedido : IOrquestradorDeProvedores
{
    public PedidoDeIa? UltimoPedido { get; private set; }

    public Task<RespostaDaIA> ResponderAsync(Guid tenantId, ContextoDaChamada? contexto, PedidoDeIa pedido, CancellationToken ct)
    {
        UltimoPedido = pedido;
        return Task.FromResult(new RespostaDaIA(true, "resposta de teste", "ok"));
    }
}

// ============================================================================
// Fake de provedor — fila de resultados configurados; conta chamadas.
// ============================================================================
file sealed class ProvedorFake(ProvedorDeIa identidade, string modelo) : IProvedorDeIa
{
    // Func em vez de ResultadoDoProvedor puro: permite enfileirar tanto uma
    // resposta normal quanto um cancelamento simulado (CENARIO 15).
    private readonly Queue<Func<CancellationToken, ResultadoDoProvedor>> _fila = new();

    public ProvedorDeIa Identidade => identidade;
    public string Modelo => modelo;
    public bool EstaConfigurado { get; set; } = true;
    public int Chamadas { get; private set; }

    public void Enfileirar(ResultadoDoProvedor resultado) => _fila.Enqueue(_ => resultado);

    /// <summary>
    /// Enfileira um resultado com atraso REAL (<see cref="Thread.Sleep"/>) antes de
    /// devolver — usado só pelo CENARIO 29 para provar que <c>IniciadaEm</c> marca
    /// o INÍCIO da chamada, não o fim. Os demais cenários não precisam de tempo
    /// real nenhum (por isso <see cref="Enfileirar"/> devolve instantâneo).
    /// </summary>
    public void EnfileirarComAtraso(TimeSpan atraso, ResultadoDoProvedor resultado) => _fila.Enqueue(_ =>
    {
        Thread.Sleep(atraso);
        return resultado;
    });

    /// <summary>
    /// Simula o request HTTP real sendo abortado (gateway do WhatsApp desiste, ou
    /// o webhook cancela) ENQUANTO o provedor estava em voo — cancela o
    /// <paramref name="cts"/> compartilhado com quem chama <c>ResponderAsync</c> e
    /// lança <see cref="OperationCanceledException"/> referenciando o mesmo token,
    /// exatamente como <c>HttpClient.SendAsync</c> faria.
    /// </summary>
    public void EnfileirarCancelamento(CancellationTokenSource cts) => _fila.Enqueue(_ =>
    {
        cts.Cancel();
        throw new OperationCanceledException("Cancelado pelo chamador (simulado).", cts.Token);
    });

    public Task<ResultadoDoProvedor> GerarAsync(PedidoDeIa pedido, CancellationToken ct)
    {
        Chamadas++;

        if (_fila.Count == 0)
        {
            throw new InvalidOperationException(
                $"ProvedorFake({identidade}) chamado sem resultado enfileirado — o cenario de teste esta incompleto.");
        }

        return Task.FromResult(_fila.Dequeue()(ct));
    }
}

// ============================================================================
// Handler HTTP fake para o CENARIO 19 — devolve a resposta configurada sem
// abrir conexao nenhuma, exercitando o ProvedorGemini PUBLICO de verdade
// (leitura do corpo, desserializacao do DTO de erro, ClassificarErro).
// ============================================================================
file sealed class HandlerFake(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(responder(request));
}
