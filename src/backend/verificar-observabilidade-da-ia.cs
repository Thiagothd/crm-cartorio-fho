#:project CrmCartorio.Api/CrmCartorio.Api.csproj
#:property PublishAot=false
#:package Microsoft.EntityFrameworkCore.InMemory@10.0.9

using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.CentralDeIa.Application;
using CrmCartorio.Api.Modules.RespostaInteligente.Application;
using CrmCartorio.Api.Modules.RespostaInteligente.Application.Cota;
using CrmCartorio.Api.Modules.RespostaInteligente.Domain.Entities;
using CrmCartorio.Api.Modules.RespostaInteligente.Domain.Enums;
using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

// VERIFICADOR DA OBSERVABILIDADE DA IA — ETAPA C (/central-ia, desenvolvedor)
//
// Rode com: dotnet run verificar-observabilidade-da-ia.cs
//
// Banco descartavel em memoria (um nome unico por cenario). Exercita o
// SERVICO REAL (ObservabilidadeDaIaService), nao uma copia da consulta.
// DisjuntorDoGemini e' a classe real (singleton em producao, aqui uma
// instancia nova por cenario) — nao ha razao para fake-la, e' pura logica.
//
// Prova o que distingue esta tela da AnaliseDaIa (Etapa B): ve TODOS os
// tenants por padrao (o oposto de isolamento por tenant), filtros globais
// aplicam em todo bloco, percentil por histograma fecha com dado conhecido,
// paginacao/ordenacao/busca de tenants funcionam, e o historico nunca expoe
// ponteiro para conteudo do cliente.

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

CrmDbContext NovoBanco()
{
    var nome = $"observabilidade-da-ia-{Interlocked.Increment(ref sequencial)}-{Guid.NewGuid():N}";
    var opcoes = new DbContextOptionsBuilder<CrmDbContext>().UseInMemoryDatabase(nome).Options;
    return new CrmDbContext(opcoes);
}

ChamadaDeIa NovaLinha(
    Guid tenantId,
    DateTime iniciadaEm,
    bool sucesso = true,
    bool? podeResponder = true,
    Guid? rodadaId = null,
    TipoDeFallbackDeIa tipoDeFallback = TipoDeFallbackDeIa.NaoEhFallback,
    TipoDeFalhaDeIa tipoDeFalha = TipoDeFalhaDeIa.Nenhuma,
    int? codigoHttp = null,
    int latenciaMs = 800,
    ProvedorDeIa provedor = ProvedorDeIa.Anthropic,
    string modelo = "claude-haiku-4-5",
    decimal? custoEstimadoUsd = 0.001m) => new()
{
    TenantId = tenantId,
    Provedor = provedor,
    Modelo = modelo,
    IniciadaEm = iniciadaEm,
    LatenciaMs = latenciaMs,
    Sucesso = sucesso,
    PodeResponder = sucesso ? podeResponder : null,
    TipoDeFalha = sucesso ? TipoDeFalhaDeIa.Nenhuma : tipoDeFalha,
    CodigoHttp = codigoHttp,
    FoiFallback = tipoDeFallback != TipoDeFallbackDeIa.NaoEhFallback,
    TipoDeFallback = tipoDeFallback,
    RodadaId = rodadaId,
    CustoEstimadoUsd = provedor == ProvedorDeIa.Gemini ? null : custoEstimadoUsd,
    TokensEntrada = 50,
    TokensSaida = 30,
    TokensPensamento = 0,
};

OpcoesDaIA OpcoesComGeminiConfigurado(string chave = "chave-de-teste-gemini") => new()
{
    Habilitada = true,
    Gemini = new OpcoesDoGemini { Habilitado = true, ApiKey = chave, LimiteRpd = 1000 },
    Anthropic = new OpcoesDoAnthropic { Habilitado = true, ApiKey = "chave-de-teste-anthropic" },
};

OpcoesDaIA OpcoesComGeminiDesligado() => new()
{
    Habilitada = true,
    Gemini = new OpcoesDoGemini { Habilitado = false },
    Anthropic = new OpcoesDoAnthropic { Habilitado = true, ApiKey = "chave-de-teste-anthropic" },
};

ObservabilidadeDaIaService NovoServico(CrmDbContext db, OpcoesDaIA? opcoes = null, DisjuntorDoGemini? disjuntor = null) =>
    new(db, Options.Create(opcoes ?? OpcoesComGeminiConfigurado()), disjuntor ?? new DisjuntorDoGemini());

FiltroDeObservabilidadeDaIa Filtro(
    DateTime de, DateTime ate, Guid? tenantId = null, ProvedorDeIa? provedor = null, string? modelo = null,
    ResultadoDaChamadaDeIa? resultado = null, TipoDeFallbackDeIa? tipoDeFallback = null,
    TipoDeFalhaDeIa? tipoDeFalha = null, int? codigoHttp = null) =>
    new(de, ate, tenantId, provedor, modelo, resultado, tipoDeFallback, tipoDeFalha, codigoHttp);

Console.WriteLine();

// ============================================================================
// CENARIO 1 — Ao contrario da AnaliseDaIa (tela do cartorio), a Central VE
// todos os tenants por padrao — isso e' o proposito dela existir.
// ============================================================================
Console.WriteLine("CENARIO 1 — sem filtro de tenant, a Central consolida TODOS os tenants");
{
    using var db = NovoBanco();
    var tenantA = Guid.NewGuid();
    var tenantB = Guid.NewGuid();
    var agora = DateTime.UtcNow;

    db.ChamadasDeIa.AddRange(
        NovaLinha(tenantA, agora.AddHours(-1), rodadaId: Guid.NewGuid()),
        NovaLinha(tenantB, agora.AddHours(-1), rodadaId: Guid.NewGuid()),
        NovaLinha(tenantB, agora.AddHours(-1), rodadaId: Guid.NewGuid()));
    await db.SaveChangesAsync();

    var servico = NovoServico(db);
    var resposta = await servico.ObterAsync(Filtro(agora.AddDays(-1), agora.AddHours(1)));

    Checar("Sem filtro: as 3 tentativas dos 2 tenants aparecem juntas", resposta.Kpis.Tentativas == 3);
    Checar("TenantsAtivos = 2", resposta.Kpis.TenantsAtivos == 2);

    var soTenantA = await servico.ObterAsync(Filtro(agora.AddDays(-1), agora.AddHours(1), tenantId: tenantA));
    Checar("Filtrando por TenantId=A, so a linha de A aparece", soTenantA.Kpis.Tentativas == 1);
}
Console.WriteLine();

// ============================================================================
// CENARIO 2 — KPIs fecham: Tentativas = Entregues + NaoSoube + Falhas, e a
// soma da linha do tempo bate com os mesmos totais (mesma identidade da
// AnaliseDaIa, agora cross-tenant).
// ============================================================================
Console.WriteLine("CENARIO 2 — fechamento: KPIs = soma da linha do tempo");
{
    using var db = NovoBanco();
    var tenantId = Guid.NewGuid();
    var agora = DateTime.UtcNow;

    db.ChamadasDeIa.AddRange(
        NovaLinha(tenantId, agora.AddHours(-1), sucesso: true, podeResponder: true, provedor: ProvedorDeIa.Anthropic),
        NovaLinha(tenantId, agora.AddHours(-2), sucesso: true, podeResponder: true, provedor: ProvedorDeIa.Gemini),
        NovaLinha(tenantId, agora.AddHours(-3), sucesso: true, podeResponder: false, provedor: ProvedorDeIa.Anthropic),
        NovaLinha(tenantId, agora.AddHours(-4), sucesso: false, provedor: ProvedorDeIa.Gemini, tipoDeFalha: TipoDeFalhaDeIa.Timeout));
    await db.SaveChangesAsync();

    var servico = NovoServico(db);
    var resposta = await servico.ObterAsync(Filtro(agora.AddDays(-1), agora.AddHours(1)));

    Checar(
        "KPIs: 2 entregues, 1 nao soube, 1 falha",
        resposta.Kpis is { Entregues: 2, NaoSoubeResponder: 1, Falhas: 1, Tentativas: 4 });

    var somaEntregues = resposta.LinhaDoTempo.Sum(p => p.Entregues);
    var somaNaoSoube = resposta.LinhaDoTempo.Sum(p => p.NaoSoubeResponder);
    var somaFalhas = resposta.LinhaDoTempo.Sum(p => p.Falhas);
    var somaGemini = resposta.LinhaDoTempo.Sum(p => p.TentativasGemini);
    var somaAnthropic = resposta.LinhaDoTempo.Sum(p => p.TentativasAnthropic);

    Checar("Soma da linha do tempo (Entregues) bate com o KPI", somaEntregues == resposta.Kpis.Entregues);
    Checar("Soma da linha do tempo (NaoSoube) bate com o KPI", somaNaoSoube == resposta.Kpis.NaoSoubeResponder);
    Checar("Soma da linha do tempo (Falhas) bate com o KPI", somaFalhas == resposta.Kpis.Falhas);
    Checar("Soma Gemini + Anthropic na linha do tempo = Tentativas", somaGemini + somaAnthropic == resposta.Kpis.Tentativas);
    Checar("1 timeout contabilizado", resposta.Kpis.TotalTimeouts == 1);
}
Console.WriteLine();

// ============================================================================
// CENARIO 3 — Fallback executado x preventivo (os 3 sub-motivos) contam
// separado — o mesmo bug que a Etapa A corrigiu no resto do sistema.
// ============================================================================
Console.WriteLine("CENARIO 3 — fallback executado e os 3 preventivos contam em baldes separados");
{
    using var db = NovoBanco();
    var tenantId = Guid.NewGuid();
    var agora = DateTime.UtcNow;
    var rodada1 = Guid.NewGuid();

    db.ChamadasDeIa.AddRange(
        // Rodada 1: Gemini discou e falhou, Haiku assumiu — DUAS linhas.
        NovaLinha(tenantId, agora.AddHours(-1), sucesso: false, provedor: ProvedorDeIa.Gemini, rodadaId: rodada1),
        NovaLinha(tenantId, agora.AddHours(-1), sucesso: true, provedor: ProvedorDeIa.Anthropic, rodadaId: rodada1, tipoDeFallback: TipoDeFallbackDeIa.Executado),
        // Gemini nem foi discado (desligado por config) — UMA linha so.
        NovaLinha(tenantId, agora.AddHours(-2), sucesso: true, provedor: ProvedorDeIa.Anthropic, tipoDeFallback: TipoDeFallbackDeIa.PreventivoDesligado),
        NovaLinha(tenantId, agora.AddHours(-3), sucesso: true, provedor: ProvedorDeIa.Anthropic, tipoDeFallback: TipoDeFallbackDeIa.PreventivoDisjuntor),
        NovaLinha(tenantId, agora.AddHours(-4), sucesso: true, provedor: ProvedorDeIa.Anthropic, tipoDeFallback: TipoDeFallbackDeIa.PreventivoMargemDeCota));
    await db.SaveChangesAsync();

    var servico = NovoServico(db);
    var resposta = await servico.ObterAsync(Filtro(agora.AddDays(-1), agora.AddHours(1)));

    Checar("1 fallback executado", resposta.Kpis.FallbackExecutado == 1);
    Checar("1 preventivo desligado", resposta.Kpis.FallbackPreventivoDesligado == 1);
    Checar("1 preventivo disjuntor", resposta.Kpis.FallbackPreventivoDisjuntor == 1);
    Checar("1 preventivo margem de cota", resposta.Kpis.FallbackPreventivoMargemDeCota == 1);

    var fallbacksPorTipo = resposta.FallbacksPorTipo.ToDictionary(f => f.Tipo, f => f.Total);
    Checar(
        "FallbacksPorTipo bate com os KPIs (mesma fonte, nunca diverge)",
        fallbacksPorTipo.GetValueOrDefault("Executado") == 1
            && fallbacksPorTipo.GetValueOrDefault("PreventivoDesligado") == 1
            && fallbacksPorTipo.GetValueOrDefault("PreventivoDisjuntor") == 1
            && fallbacksPorTipo.GetValueOrDefault("PreventivoMargemDeCota") == 1);
}
Console.WriteLine();

// ============================================================================
// CENARIO 4 — Percentis de latencia (histograma, balde de 250ms) fecham com
// dado conhecido, com precisao de +-125ms.
// ============================================================================
Console.WriteLine("CENARIO 4 — percentis de latencia batem com dado conhecido");
{
    using var db = NovoBanco();
    var tenantId = Guid.NewGuid();
    var agora = DateTime.UtcNow;

    // 10 chamadas com sucesso, latencias 100..1000ms de 100 em 100 — a
    // mediana (p50) tem que cair perto de 500-600ms.
    for (var i = 1; i <= 10; i++)
    {
        db.ChamadasDeIa.Add(NovaLinha(tenantId, agora.AddMinutes(-i), sucesso: true, latenciaMs: i * 100));
    }
    await db.SaveChangesAsync();

    var servico = NovoServico(db);
    var resposta = await servico.ObterAsync(Filtro(agora.AddDays(-1), agora.AddHours(1)));

    Checar(
        "P50 cai dentro de +-1 balde do centro da distribuicao (500ms)",
        resposta.Kpis.P50LatenciaMs is { } p50 && Math.Abs(p50 - 500) <= 250,
        $"P50={resposta.Kpis.P50LatenciaMs}");
    Checar(
        "P99 e' maior ou igual ao P50 (distribuicao monotona)",
        resposta.Kpis.P99LatenciaMs >= resposta.Kpis.P50LatenciaMs);
    Checar("LarguraDoBaldeDeLatenciaMs = 250 (contrato declara a precisao)", resposta.Kpis.LarguraDoBaldeDeLatenciaMs == 250);

    var totalNoHistograma = resposta.HistogramaDeLatencia.Sum(h => h.Baldes.Sum(b => b.Total));
    Checar("Soma dos baldes do histograma = total de tentativas com sucesso (10)", totalNoHistograma == 10);
}
Console.WriteLine();

// ============================================================================
// CENARIO 5 — Filtros (resultado, provedor, tipo de falha, codigo HTTP)
// aplicam a TODO bloco, nao so a um.
// ============================================================================
Console.WriteLine("CENARIO 5 — filtros globais aplicam a KPIs e a linha do tempo igualmente");
{
    using var db = NovoBanco();
    var tenantId = Guid.NewGuid();
    var agora = DateTime.UtcNow;

    db.ChamadasDeIa.AddRange(
        NovaLinha(tenantId, agora.AddHours(-1), sucesso: true, podeResponder: true, provedor: ProvedorDeIa.Gemini),
        NovaLinha(tenantId, agora.AddHours(-2), sucesso: true, podeResponder: true, provedor: ProvedorDeIa.Anthropic),
        NovaLinha(tenantId, agora.AddHours(-3), sucesso: false, provedor: ProvedorDeIa.Gemini, codigoHttp: 429, tipoDeFalha: TipoDeFalhaDeIa.RateLimit),
        NovaLinha(tenantId, agora.AddHours(-4), sucesso: false, provedor: ProvedorDeIa.Gemini, codigoHttp: 503, tipoDeFalha: TipoDeFalhaDeIa.Indisponibilidade));
    await db.SaveChangesAsync();

    var servico = NovoServico(db);

    var soGemini = await servico.ObterAsync(Filtro(agora.AddDays(-1), agora.AddHours(1), provedor: ProvedorDeIa.Gemini));
    Checar("Filtro por Provedor=Gemini: 3 das 4 tentativas", soGemini.Kpis.Tentativas == 3);

    var so429 = await servico.ObterAsync(Filtro(agora.AddDays(-1), agora.AddHours(1), codigoHttp: 429));
    Checar("Filtro por CodigoHttp=429: 1 tentativa", so429.Kpis.Tentativas == 1);

    var soEntregues = await servico.ObterAsync(Filtro(agora.AddDays(-1), agora.AddHours(1), resultado: ResultadoDaChamadaDeIa.Entregue));
    Checar("Filtro por Resultado=Entregue: 2 tentativas, 0 falhas", soEntregues.Kpis is { Tentativas: 2, Falhas: 0 });

    var soRateLimit = await servico.ObterAsync(Filtro(agora.AddDays(-1), agora.AddHours(1), tipoDeFalha: TipoDeFalhaDeIa.RateLimit));
    Checar("Filtro por TipoDeFalha=RateLimit: 1 tentativa", soRateLimit.Kpis.Tentativas == 1);
}
Console.WriteLine();

// ============================================================================
// CENARIO 6 — Tabela de tenants: ordenacao por custo, paginacao estavel,
// busca por nome.
// ============================================================================
Console.WriteLine("CENARIO 6 — tenants: ordenacao, paginacao e busca por nome");
{
    using var db = NovoBanco();
    var agora = DateTime.UtcNow;

    var tenantBarato = new Tenant { RazaoSocial = "Cartorio Alfa Ltda", NomeFantasia = "Cartorio Alfa", Cnpj = "11111111000101" };
    var tenantCaro = new Tenant { RazaoSocial = "Cartorio Gameleiras Ltda", NomeFantasia = "Cartorio Gameleiras", Cnpj = "22222222000102" };
    db.Tenants.AddRange(tenantBarato, tenantCaro);

    db.ChamadasDeIa.AddRange(
        NovaLinha(tenantBarato.Id, agora.AddHours(-1), custoEstimadoUsd: 0.001m),
        NovaLinha(tenantCaro.Id, agora.AddHours(-1), custoEstimadoUsd: 0.050m),
        NovaLinha(tenantCaro.Id, agora.AddHours(-2), custoEstimadoUsd: 0.050m));
    await db.SaveChangesAsync();

    var servico = NovoServico(db);
    var filtro = Filtro(agora.AddDays(-1), agora.AddHours(1));

    var porCusto = await servico.ObterTenantsAsync(filtro, null, OrdenarTenantsPor.Custo, decrescente: true, pagina: 1, tamanhoDaPagina: 10);
    Checar("2 tenants no total", porCusto.Total == 2);
    Checar("Ordenado por custo decrescente: Gameleiras primeiro", porCusto.Itens[0].Nome == "Cartorio Gameleiras");

    var pagina1 = await servico.ObterTenantsAsync(filtro, null, OrdenarTenantsPor.Custo, decrescente: true, pagina: 1, tamanhoDaPagina: 1);
    var pagina2 = await servico.ObterTenantsAsync(filtro, null, OrdenarTenantsPor.Custo, decrescente: true, pagina: 2, tamanhoDaPagina: 1);
    Checar("Paginacao: pagina 1 traz 1 item, pagina 2 traz o outro", pagina1.Itens.Count == 1 && pagina2.Itens.Count == 1 && pagina1.Itens[0].TenantId != pagina2.Itens[0].TenantId);

    var busca = await servico.ObterTenantsAsync(filtro, "gameleiras", OrdenarTenantsPor.Custo, decrescente: true, pagina: 1, tamanhoDaPagina: 10);
    Checar("Busca por nome (case-insensitive) encontra so o Gameleiras", busca.Total == 1 && busca.Itens[0].Nome == "Cartorio Gameleiras");
}
Console.WriteLine();

// ============================================================================
// CENARIO 7 — Historico: buscar por RodadaId traz as DUAS tentativas lado a
// lado, e o contrato NUNCA expoe ponteiro para conteudo do cliente.
// ============================================================================
Console.WriteLine("CENARIO 7 — historico por RodadaId e privacidade do contrato");
{
    using var db = NovoBanco();
    var tenantId = Guid.NewGuid();
    var agora = DateTime.UtcNow;
    var rodada = Guid.NewGuid();

    db.ChamadasDeIa.AddRange(
        NovaLinha(tenantId, agora.AddMinutes(-2), sucesso: false, provedor: ProvedorDeIa.Gemini, rodadaId: rodada),
        NovaLinha(tenantId, agora.AddMinutes(-1), sucesso: true, provedor: ProvedorDeIa.Anthropic, rodadaId: rodada, tipoDeFallback: TipoDeFallbackDeIa.Executado),
        NovaLinha(tenantId, agora.AddHours(-5), rodadaId: Guid.NewGuid()));
    await db.SaveChangesAsync();

    var servico = NovoServico(db);
    var filtro = Filtro(agora.AddDays(-1), agora.AddHours(1));

    var porRodada = await servico.ObterHistoricoAsync(filtro, rodada, pagina: 1, tamanhoDaPagina: 10);
    Checar("Buscar por RodadaId traz as 2 tentativas da mesma pergunta", porRodada.Total == 2);
    Checar(
        "As 2 linhas sao Gemini (falhou) e Anthropic (resolveu), na mesma RodadaId",
        porRodada.Itens.All(i => i.RodadaId == rodada) && porRodada.Itens.Select(i => i.Provedor).OrderBy(p => p).SequenceEqual(["Anthropic", "Gemini"]));

    var todasAsLinhas = await servico.ObterHistoricoAsync(filtro, null, pagina: 1, tamanhoDaPagina: 10);
    Checar("Sem filtro de RodadaId, as 3 linhas aparecem", todasAsLinhas.Total == 3);

    var propriedades = typeof(LinhaDoHistoricoResponse).GetProperties().Select(p => p.Name).ToHashSet();
    Checar(
        "O contrato do historico NUNCA expoe ConversaId, MensagemId ou MotivoDoFallback (texto livre)",
        !propriedades.Contains("ConversaId") && !propriedades.Contains("MensagemId") && !propriedades.Contains("MotivoDoFallback"));
}
Console.WriteLine();

// ============================================================================
// CENARIO 8 — Saude agora: independente do filtro de periodo (sempre "hoje"
// local), reflete config e disjuntor de verdade.
// ============================================================================
Console.WriteLine("CENARIO 8 — saude agora: config, disjuntor e independencia do filtro de periodo");
{
    using var db = NovoBanco();
    var tenantId = Guid.NewGuid();
    var agora = DateTime.UtcNow;

    db.ChamadasDeIa.Add(NovaLinha(tenantId, agora.AddDays(-10), sucesso: true, provedor: ProvedorDeIa.Gemini));
    await db.SaveChangesAsync();

    var servicoSemChave = NovoServico(db, OpcoesComGeminiDesligado());
    var respostaSemChave = await servicoSemChave.ObterAsync(Filtro(agora.AddDays(-30), agora));
    Checar("Gemini desligado por config -> EstadoDoCircuito=SemChave", respostaSemChave.SaudeAgora.Gemini.EstadoDoCircuito == "SemChave");
    Checar("Gemini desligado -> Configurado=false", !respostaSemChave.SaudeAgora.Gemini.Configurado);
    Checar("Anthropic configurado -> Configurado=true", respostaSemChave.SaudeAgora.Anthropic.Configurado);

    var chave = "chave-disjuntor-teste";
    var disjuntor = new DisjuntorDoGemini();
    var chaveDeProjeto = FingerprintDeChave.Calcular(chave);
    var opcoesGemini = new OpcoesDoGemini { Habilitado = true, ApiKey = chave, FalhasParaAbrirDisjuntor = 1 };
    disjuntor.RegistrarFalha(chaveDeProjeto, TipoDeFalhaDeIa.Indisponibilidade, 503, TimeSpan.FromMinutes(5), eraSonda: false, opcoesGemini);

    var opcoesComDisjuntorAberto = OpcoesComGeminiConfigurado(chave);
    opcoesComDisjuntorAberto.Gemini.FalhasParaAbrirDisjuntor = 1;
    var servicoComDisjuntorAberto = NovoServico(db, opcoesComDisjuntorAberto, disjuntor);
    var respostaComDisjuntorAberto = await servicoComDisjuntorAberto.ObterAsync(Filtro(agora.AddDays(-30), agora));
    Checar(
        "Disjuntor aberto de verdade -> EstadoDoCircuito=Aberto, com VoltaATentarEm preenchido",
        respostaComDisjuntorAberto.SaudeAgora.Gemini is { EstadoDoCircuito: "Aberto", CircuitoVoltaATentarEm: not null });

    // A unica linha de dado existente e' de ha 10 dias — "hoje" tem que ficar
    // zerado, mesmo com um filtro de periodo de 30 dias pedido no ObterAsync.
    Checar(
        "Saude agora conta so 'hoje' (linha de ha 10 dias nao entra), independente do filtro de periodo pedido",
        respostaSemChave.SaudeAgora.Gemini.RequisicoesHoje == 0);
}
Console.WriteLine();

// ============================================================================
// CENARIO 9 — Periodo sem nenhum dado nao quebra: zeros honestos, listas
// vazias, nunca excecao.
// ============================================================================
Console.WriteLine("CENARIO 9 — periodo vazio nao quebra");
{
    using var db = NovoBanco();
    var agora = DateTime.UtcNow;

    var servico = NovoServico(db);
    var resposta = await servico.ObterAsync(Filtro(agora.AddDays(-1), agora));

    Checar("KPIs todos zero", resposta.Kpis is { Tentativas: 0, Entregues: 0, Falhas: 0, TenantsAtivos: 0 });
    Checar("Percentis nulos (sem amostra, nao zero fabricado)", resposta.Kpis.P50LatenciaMs == null && resposta.Kpis.P95LatenciaMs == null);
    Checar("Nenhum tenant no top de custo", resposta.TopTenantsPorCusto.Count == 0);
    Checar("Nenhum erro por categoria", resposta.ErrosPorCategoria.Count == 0);

    var tenants = await servico.ObterTenantsAsync(Filtro(agora.AddDays(-1), agora), null, OrdenarTenantsPor.Tentativas, true, 1, 20);
    Checar("Tabela de tenants vazia, total=0", tenants is { Total: 0, Itens.Count: 0 });

    var historico = await servico.ObterHistoricoAsync(Filtro(agora.AddDays(-1), agora), null, 1, 20);
    Checar("Historico vazio, total=0", historico is { Total: 0, Itens.Count: 0 });
}
Console.WriteLine();

Console.WriteLine(falhas == 0 ? "TODOS OS CENARIOS PASSARAM." : $"{falhas} CENARIO(S) COM FALHA.");
return falhas == 0 ? 0 : 1;
