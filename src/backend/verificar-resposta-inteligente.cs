#:project CrmCartorio.Api/CrmCartorio.Api.csproj
#:property PublishAot=false
#:package Microsoft.EntityFrameworkCore.InMemory@10.0.9

using CrmCartorio.Api.Infrastructure.Persistence;
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
    servicos.AddDbContext<CrmDbContext>(o => o.UseInMemoryDatabase(nome));
    var provedor = servicos.BuildServiceProvider();

    var opcoes = new DbContextOptionsBuilder<CrmDbContext>().UseInMemoryDatabase(nome).Options;
    var db = new CrmDbContext(opcoes);

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
        Habilitada = true,
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
    MontarAmbiente(OpcoesDaIA? opcoesCustom = null)
{
    var (db, escopos) = NovoBanco();
    var opcoes = Options.Create(opcoesCustom ?? NovasOpcoes());

    var gemini = new ProvedorFake(ProvedorDeIa.Gemini, "gemini-3.7-flash");
    var anthropic = new ProvedorFake(ProvedorDeIa.Anthropic, "claude-haiku-4-5");

    var disjuntor = new DisjuntorDoGemini();
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

Console.WriteLine(falhas == 0 ? "TODOS OS CENARIOS PASSARAM." : $"{falhas} CENARIO(S) COM FALHA.");
return falhas == 0 ? 0 : 1;

// ============================================================================
// Fake de provedor — fila de resultados configurados; conta chamadas.
// ============================================================================
file sealed class ProvedorFake(ProvedorDeIa identidade, string modelo) : IProvedorDeIa
{
    private readonly Queue<ResultadoDoProvedor> _fila = new();

    public ProvedorDeIa Identidade => identidade;
    public string Modelo => modelo;
    public bool EstaConfigurado { get; set; } = true;
    public int Chamadas { get; private set; }

    public void Enfileirar(ResultadoDoProvedor resultado) => _fila.Enqueue(resultado);

    public Task<ResultadoDoProvedor> GerarAsync(PedidoDeIa pedido, CancellationToken ct)
    {
        Chamadas++;

        if (_fila.Count == 0)
        {
            throw new InvalidOperationException(
                $"ProvedorFake({identidade}) chamado sem resultado enfileirado — o cenario de teste esta incompleto.");
        }

        return Task.FromResult(_fila.Dequeue());
    }
}
