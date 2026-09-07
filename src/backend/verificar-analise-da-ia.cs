#:project CrmCartorio.Api/CrmCartorio.Api.csproj
#:property PublishAot=false
#:package Microsoft.EntityFrameworkCore.InMemory@10.0.9

using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.RespostaInteligente.Application;
using CrmCartorio.Api.Modules.RespostaInteligente.Domain.Entities;
using CrmCartorio.Api.Modules.RespostaInteligente.Domain.Enums;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.EntityFrameworkCore;

// VERIFICADOR DA ANALISE DA IA — ETAPA B (/automacao/ia, dono/gestor)
//
// Rode com: dotnet run verificar-analise-da-ia.cs
//
// Banco descartavel em memoria (um nome unico por cenario). Exercita o SERVICO
// REAL (CrmCartorio.Api.Modules.RespostaInteligente.Application.AnaliseDaIa),
// nao uma copia da consulta. ISituacaoDaIa e' fake (constrói-lo de verdade
// exigiria disjuntor/cota/opcoes reais, que nao sao o que este verificador
// prova) — cada cenario configura o status de Gemini/Anthropic que precisa.
//
// Prova as identidades do plano da reformulacao: Tentativas = Entregues +
// NaoSoube + Falhas; Sigma da serie temporal = os cards; isolamento por
// tenant; linha antiga (RodadaId nulo) nao infla "rodadas"; balde "Nao
// identificado" nunca desaparece; insights so aparecem com amostra minima.

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
    var nome = $"analise-da-ia-{Interlocked.Increment(ref sequencial)}-{Guid.NewGuid():N}";
    var opcoes = new DbContextOptionsBuilder<CrmDbContext>().UseInMemoryDatabase(nome).Options;
    return new CrmDbContext(opcoes);
}

/// <summary>
/// Uma linha de <see cref="ChamadaDeIa"/> com defaults sensatos — cada cenario
/// so sobrescreve o que importa para ele. <paramref name="iniciadaEm"/> default
/// e' "agora" para nao depender de relogio entre cenarios distintos.
/// </summary>
ChamadaDeIa NovaLinha(
    Guid tenantId,
    DateTime iniciadaEm,
    bool sucesso = true,
    bool? podeResponder = true,
    Guid? rodadaId = null,
    Guid? tipoServicoId = null,
    MotivoDaNaoRespostaDeIa? motivoDaNaoResposta = null,
    TipoDeFallbackDeIa tipoDeFallback = TipoDeFallbackDeIa.NaoEhFallback,
    int latenciaMs = 800,
    ProvedorDeIa provedor = ProvedorDeIa.Anthropic) => new()
{
    TenantId = tenantId,
    Provedor = provedor,
    Modelo = "claude-haiku-4-5",
    IniciadaEm = iniciadaEm,
    LatenciaMs = latenciaMs,
    Sucesso = sucesso,
    PodeResponder = sucesso ? podeResponder : null,
    TipoDeFalha = sucesso ? TipoDeFalhaDeIa.Nenhuma : TipoDeFalhaDeIa.Indisponibilidade,
    FoiFallback = tipoDeFallback != TipoDeFallbackDeIa.NaoEhFallback,
    TipoDeFallback = tipoDeFallback,
    RodadaId = rodadaId,
    MotivoDaNaoResposta = sucesso && podeResponder == false ? (motivoDaNaoResposta ?? MotivoDaNaoRespostaDeIa.NaoClassificado) : null,
    TipoServicoId = tipoServicoId,
    TokensEntrada = 50,
    TokensSaida = 30,
    TokensPensamento = 0
};

SituacaoFixa SituacaoAtendendoNormalmente() => new(true, true, "SemChave", null, "Disponivel");
SituacaoFixa SituacaoDesligada() => new(false, false, "SemChave", null, "SemChave");
SituacaoFixa SituacaoContingenciaGemini() => new(true, true, "Contingencia", DateTime.UtcNow.AddMinutes(5), "Disponivel");
SituacaoFixa SituacaoTetoDoHaiku() => new(true, true, "SemChave", null, "TetoDiarioAtingido");

var tenantId = Guid.NewGuid();

Console.WriteLine("Tenant .... " + tenantId);
Console.WriteLine();

// ============================================================================
// CENARIO 1 — Fechamento: Tentativas = Entregues + NaoSoube + Falhas, e a
// soma da serie temporal bate com o card de resultado das tentativas.
// ============================================================================
Console.WriteLine("CENARIO 1 — fechamento: Tentativas = Entregues + NaoSoube + Falhas, serie = card");
{
    using var db = NovoBanco();
    var agora = DateTime.UtcNow;

    db.ChamadasDeIa.AddRange(
        NovaLinha(tenantId, agora.AddHours(-1), sucesso: true, podeResponder: true, rodadaId: Guid.NewGuid()),
        NovaLinha(tenantId, agora.AddHours(-2), sucesso: true, podeResponder: true, rodadaId: Guid.NewGuid()),
        NovaLinha(tenantId, agora.AddHours(-3), sucesso: true, podeResponder: false, rodadaId: Guid.NewGuid()),
        NovaLinha(tenantId, agora.AddHours(-4), sucesso: false, rodadaId: Guid.NewGuid()));
    await db.SaveChangesAsync();

    var servico = new AnaliseDaIa(db, new ContextoFixo(tenantId), SituacaoAtendendoNormalmente());
    var resposta = await servico.ObterAsync(agora.AddDays(-1), agora.AddHours(1));

    Checar(
        "ResultadoDasTentativas fecha: 2 entregues + 1 nao soube + 1 falha",
        resposta.ResultadoDasTentativas is { Entregues: 2, NaoSoubeResponder: 1, Falhas: 1 });

    var somaSerieEntregues = resposta.VolumeAoLongoDoTempo.Sum(p => p.Entregues);
    var somaSerieNaoSoube = resposta.VolumeAoLongoDoTempo.Sum(p => p.NaoSoubeResponder);
    var somaSerieFalhas = resposta.VolumeAoLongoDoTempo.Sum(p => p.Falhas);

    Checar(
        "Soma da serie temporal bate exatamente com o card (Entregues)",
        somaSerieEntregues == resposta.ResultadoDasTentativas.Entregues);
    Checar(
        "Soma da serie temporal bate exatamente com o card (NaoSoube)",
        somaSerieNaoSoube == resposta.ResultadoDasTentativas.NaoSoubeResponder);
    Checar(
        "Soma da serie temporal bate exatamente com o card (Falhas)",
        somaSerieFalhas == resposta.ResultadoDasTentativas.Falhas);
}
Console.WriteLine();

// ============================================================================
// CENARIO 2 — Isolamento por tenant: dado do tenant B nunca aparece na
// analise do tenant A.
// ============================================================================
Console.WriteLine("CENARIO 2 — isolamento por tenant");
{
    using var db = NovoBanco();
    var tenantA = Guid.NewGuid();
    var tenantB = Guid.NewGuid();
    var agora = DateTime.UtcNow;

    db.ChamadasDeIa.AddRange(
        NovaLinha(tenantA, agora.AddHours(-1), rodadaId: Guid.NewGuid()),
        NovaLinha(tenantB, agora.AddHours(-1), rodadaId: Guid.NewGuid()),
        NovaLinha(tenantB, agora.AddHours(-1), rodadaId: Guid.NewGuid()),
        NovaLinha(tenantB, agora.AddHours(-1), rodadaId: Guid.NewGuid()));
    await db.SaveChangesAsync();

    var servicoA = new AnaliseDaIa(db, new ContextoFixo(tenantA), SituacaoAtendendoNormalmente());
    var respostaA = await servicoA.ObterAsync(agora.AddDays(-1), agora.AddHours(1));

    Checar(
        "Tenant A ve so a propria linha (1), nao as 3 do tenant B",
        respostaA.ResultadoDasTentativas.Entregues == 1);
}
Console.WriteLine();

// ============================================================================
// CENARIO 3 — Linha antiga (RodadaId nulo): entra nos totais de tentativa,
// NUNCA e' contada como uma rodada — senao infla a taxa de resolucao.
// ============================================================================
Console.WriteLine("CENARIO 3 — RodadaId nulo nao infla a contagem de rodadas");
{
    using var db = NovoBanco();
    var agora = DateTime.UtcNow;

    // 3 linhas SEM RodadaId (anteriores a migracao 042) + 1 rodada de verdade.
    db.ChamadasDeIa.AddRange(
        NovaLinha(tenantId, agora.AddHours(-1), rodadaId: null),
        NovaLinha(tenantId, agora.AddHours(-2), rodadaId: null),
        NovaLinha(tenantId, agora.AddHours(-3), rodadaId: null),
        NovaLinha(tenantId, agora.AddHours(-4), rodadaId: Guid.NewGuid()));
    await db.SaveChangesAsync();

    var servico = new AnaliseDaIa(db, new ContextoFixo(tenantId), SituacaoAtendendoNormalmente());
    // Periodo comeca EXATAMENTE em RodadasMedidasDesde (= IniciadaEm da unica
    // linha com RodadaId, agora-4h) — comecar ANTES tornaria a medicao nao
    // confiavel de proposito (PerguntasAtendidas vira null; ver CENARIO 8).
    var resposta = await servico.ObterAsync(agora.AddHours(-4), agora.AddHours(1));

    Checar(
        "Resultado das tentativas conta as 4 linhas (entregues)",
        resposta.ResultadoDasTentativas.Entregues == 4);
    Checar(
        "PerguntasAtendidas (rodadas) conta so a UNICA rodada com RodadaId — nao as 3 nulas como uma so",
        resposta.ValorEntregue.PerguntasAtendidas == 1,
        $"PerguntasAtendidas={resposta.ValorEntregue.PerguntasAtendidas}");
}
Console.WriteLine();

// ============================================================================
// CENARIO 4 — Balde "Nao identificado" nunca desaparece quando existe.
// ============================================================================
Console.WriteLine("CENARIO 4 — assunto 'Nao identificado' aparece quando TipoServicoId e nulo");
{
    using var db = NovoBanco();
    var agora = DateTime.UtcNow;
    var servicoId = Guid.NewGuid();

    db.TiposServico.Add(new CrmCartorio.Api.Modules.Servicos.Domain.Entities.TipoServico
    {
        TenantId = tenantId,
        Nome = "Reconhecimento de firma",
        SetorId = Guid.NewGuid()
    });

    var idServicoReal = db.TiposServico.Local.First().Id;

    db.ChamadasDeIa.AddRange(
        NovaLinha(tenantId, agora.AddHours(-1), rodadaId: Guid.NewGuid(), tipoServicoId: idServicoReal),
        NovaLinha(tenantId, agora.AddHours(-2), rodadaId: Guid.NewGuid(), tipoServicoId: null),
        NovaLinha(tenantId, agora.AddHours(-3), rodadaId: Guid.NewGuid(), tipoServicoId: null));
    await db.SaveChangesAsync();

    var servico = new AnaliseDaIa(db, new ContextoFixo(tenantId), SituacaoAtendendoNormalmente());
    var resposta = await servico.ObterAsync(agora.AddDays(-1), agora.AddHours(1));

    var naoIdentificado = resposta.AssuntosMaisPerguntados.FirstOrDefault(a => a.TipoServicoId == null);
    Checar("Balde 'Nao identificado' presente com 2 ocorrencias", naoIdentificado is { Total: 2, Nome: "Não identificado" });
    Checar(
        "Servico real presente com o nome certo",
        resposta.AssuntosMaisPerguntados.Any(a => a.TipoServicoId == idServicoReal && a.Nome == "Reconhecimento de firma"));
}
Console.WriteLine();

// ============================================================================
// CENARIO 5 — Situacao agora: as quatro combinacoes, sem nunca chamar
// "contingencia" o que e' so decisao de configuracao (Gemini desligado).
// ============================================================================
Console.WriteLine("CENARIO 5 — Situacao Agora: vocabulario sem provedor, sem alarme falso");
{
    using var db = NovoBanco();

    var desligada = await new AnaliseDaIa(db, new ContextoFixo(tenantId), SituacaoDesligada()).ObterAsync(null, null);
    Checar("IA desabilitada -> Status=Desligada", desligada.SituacaoAgora.Status == "Desligada");

    var normal = await new AnaliseDaIa(db, new ContextoFixo(tenantId), SituacaoAtendendoNormalmente()).ObterAsync(null, null);
    Checar(
        "Gemini desligado por CONFIGURACAO (SemChave) + Haiku disponivel -> Atendendo (nunca 'contingencia' por isso)",
        normal.SituacaoAgora.Status == "Atendendo");

    var contingenciaGemini = await new AnaliseDaIa(db, new ContextoFixo(tenantId), SituacaoContingenciaGemini()).ObterAsync(null, null);
    Checar(
        "Disjuntor do Gemini ABERTO de verdade -> Contingencia, com detalhe",
        contingenciaGemini.SituacaoAgora.Status == "Contingencia" && !string.IsNullOrEmpty(contingenciaGemini.SituacaoAgora.Detalhe));

    var tetoHaiku = await new AnaliseDaIa(db, new ContextoFixo(tenantId), SituacaoTetoDoHaiku()).ObterAsync(null, null);
    Checar("Teto diario do Haiku atingido -> Contingencia", tetoHaiku.SituacaoAgora.Status == "Contingencia");
}
Console.WriteLine();

// ============================================================================
// CENARIO 6 — Insights: nada aparece sem amostra minima; a lacuna aparece
// quando a amostra bate o piso (>=5).
// ============================================================================
Console.WriteLine("CENARIO 6 — insights respeitam amostra minima");
{
    using var db = NovoBanco();
    var agora = DateTime.UtcNow;

    // So 3 "nao soube" no mesmo servico — abaixo do piso de 5.
    var servicoId = Guid.NewGuid();
    for (var i = 0; i < 3; i++)
    {
        db.ChamadasDeIa.Add(NovaLinha(
            tenantId, agora.AddHours(-i), sucesso: true, podeResponder: false,
            rodadaId: Guid.NewGuid(), tipoServicoId: servicoId, motivoDaNaoResposta: MotivoDaNaoRespostaDeIa.PrecoNaoCadastrado));
    }
    await db.SaveChangesAsync();

    var servico = new AnaliseDaIa(db, new ContextoFixo(tenantId), SituacaoAtendendoNormalmente());
    var respostaAbaixoDoPiso = await servico.ObterAsync(agora.AddDays(-1), agora.AddHours(1));

    Checar(
        "3 ocorrencias (abaixo do piso de 5): NENHUM insight de lacuna emitido",
        !respostaAbaixoDoPiso.Insights.Any(i => i.Texto.Contains("não soube responder")));

    // Sobe para 5 — bate o piso.
    db.ChamadasDeIa.AddRange(
        NovaLinha(tenantId, agora.AddHours(-4), sucesso: true, podeResponder: false, rodadaId: Guid.NewGuid(), tipoServicoId: servicoId, motivoDaNaoResposta: MotivoDaNaoRespostaDeIa.PrecoNaoCadastrado),
        NovaLinha(tenantId, agora.AddHours(-5), sucesso: true, podeResponder: false, rodadaId: Guid.NewGuid(), tipoServicoId: servicoId, motivoDaNaoResposta: MotivoDaNaoRespostaDeIa.PrecoNaoCadastrado));
    await db.SaveChangesAsync();

    var respostaNoPiso = await servico.ObterAsync(agora.AddDays(-1), agora.AddHours(1));
    Checar(
        "5 ocorrencias (no piso): insight de lacuna APARECE",
        respostaNoPiso.Insights.Any(i => i.Texto.Contains("não soube responder")));
}
Console.WriteLine();

// ============================================================================
// CENARIO 7 — Comparacao com periodo anterior: delta correto, e "sem
// amostra anterior" nunca vira percentual inventado.
// ============================================================================
Console.WriteLine("CENARIO 7 — delta contra periodo anterior");
{
    using var db = NovoBanco();
    var agora = DateTime.UtcNow;

    // Periodo atual: [agora-1d, agora]. Anterior: [agora-2d, agora-1d).
    db.ChamadasDeIa.AddRange(
        NovaLinha(tenantId, agora.AddHours(-2), rodadaId: Guid.NewGuid()),
        NovaLinha(tenantId, agora.AddHours(-3), rodadaId: Guid.NewGuid()),
        // 1 rodada no periodo ANTERIOR.
        NovaLinha(tenantId, agora.AddDays(-1).AddHours(-2), rodadaId: Guid.NewGuid()));
    await db.SaveChangesAsync();

    var servico = new AnaliseDaIa(db, new ContextoFixo(tenantId), SituacaoAtendendoNormalmente());
    var resposta = await servico.ObterAsync(agora.AddDays(-1), agora);

    Checar("2 rodadas no periodo atual", resposta.ValorEntregue.PerguntasAtendidas == 2);
    Checar(
        "Delta de perguntas atendidas: de 1 para 2 = +100%",
        resposta.ValorEntregue.PerguntasAtendidasDeltaPercentual == 100m,
        $"delta={resposta.ValorEntregue.PerguntasAtendidasDeltaPercentual}");
}
Console.WriteLine();

// ============================================================================
// CENARIO 8 — Sem nenhum dado no periodo: a tela nao quebra, devolve zeros e
// listas vazias, nunca excecao.
// ============================================================================
Console.WriteLine("CENARIO 8 — periodo vazio nao quebra, devolve zeros honestos");
{
    using var db = NovoBanco();
    var agora = DateTime.UtcNow;

    var servico = new AnaliseDaIa(db, new ContextoFixo(tenantId), SituacaoAtendendoNormalmente());
    var resposta = await servico.ObterAsync(agora.AddDays(-1), agora);

    Checar("ResultadoDasTentativas todo zero", resposta.ResultadoDasTentativas is { Entregues: 0, NaoSoubeResponder: 0, Falhas: 0 });
    Checar("PerguntasAtendidas e' 0, nao nulo (ha medicao, so nao ha dado)", resposta.ValorEntregue.PerguntasAtendidas == 0);
    Checar("Nenhum assunto", resposta.AssuntosMaisPerguntados.Count == 0);
    Checar("Nenhuma lacuna", resposta.LacunasDeCatalogo.Count == 0);
    Checar("Nenhum insight (amostra insuficiente em tudo)", resposta.Insights.Count == 0);
}
Console.WriteLine();

Console.WriteLine(falhas == 0 ? "TODOS OS CENARIOS PASSARAM." : $"{falhas} CENARIO(S) COM FALHA.");
return falhas == 0 ? 0 : 1;

file sealed class ContextoFixo(Guid tenantId) : IContextoDoUsuario
{
    public bool Autenticado => true;
    public Guid TenantId => tenantId;
    public Guid AtendenteId => Guid.NewGuid();
    public string Nome => "Verificador";
    public PerfilDeAtendente Perfil => PerfilDeAtendente.Administrador;
    public bool Pode(PermissaoDoSistema permissao) => true;
}

/// <summary>
/// Fake de <see cref="ISituacaoDaIa"/> — devolve um estado fixo, configuravel
/// por cenario. Os agregados de periodo (Hoje/MotivosDeFallback/Economia) nao
/// importam para AnaliseDaIa (ela so le Habilitada/EstaConfigurada/Gemini/
/// Anthropic), entao ficam com valores neutros.
/// </summary>
file sealed class SituacaoFixa(
    bool habilitada,
    bool estaConfigurada,
    string statusGemini,
    DateTime? voltaATentarEm,
    string statusAnthropic) : ISituacaoDaIa
{
    public Task<SituacaoDaIaResponse> ObterAsync(DateTime? de, DateTime? ate, CancellationToken ct = default) =>
        Task.FromResult(new SituacaoDaIaResponse(
            Habilitada: habilitada,
            EstaConfigurada: estaConfigurada,
            Gemini: new EstadoDoProvedorResponse("Gemini 3.7 Flash", "gemini-3.7-flash", statusGemini != "SemChave", statusGemini, voltaATentarEm, null),
            Anthropic: new EstadoDoProvedorResponse("Claude Haiku 4.5", "claude-haiku-4-5", statusAnthropic != "SemChave", statusAnthropic, null, null),
            LimiteDiario: 300,
            UsadoHojeGeral: 0,
            LimiteDiarioHaiku: 300,
            UsadoHojeHaiku: 0,
            Franquia: new FranquiaDoGeminiResponse(false, null, 0, null, 0, null, 0, 85, null),
            Hoje: new HojeDaIaResponse(0, 0, 0, 0m, 0m, 0),
            MotivosDeFallback: new MotivosDeFallbackResponse(0, 0, 0, 0, 0, 0, 0),
            Economia: new EconomiaResponse(0m, 0m, 0m, 0m),
            PeriodoDe: de ?? DateTime.UtcNow.Date,
            PeriodoAte: ate ?? DateTime.UtcNow));

    public Task<ResumoDaIaResponse> ObterResumoAsync(CancellationToken ct = default) =>
        Task.FromResult(new ResumoDaIaResponse(habilitada, estaConfigurada, "claude-haiku-4-5", 300, 0));
}
