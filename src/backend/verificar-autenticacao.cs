#:project CrmCartorio.Api/CrmCartorio.Api.csproj
#:property PublishAot=false

using CrmCartorio.Api.Infrastructure.Identity;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Application;
using CrmCartorio.Api.Modules.Usuarios.Domain;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using OtpNet;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

// VERIFICADOR DE AUTENTICACAO
//   SEC-01 — revogacao de sessao (cenarios 1..12)
//   SEC-02 — protecao contra forca bruta no login (cenarios 13..25)
//   SEC-03 — MFA/TOTP para contas privilegiadas (cenarios 26 em diante)
//
// Rode com: dotnet run verificar-autenticacao.cs ["<connection string>"]
//
// Chama os SERVICOS REAIS (AutenticacaoService, AtendenteService,
// ValidadorDeSessao, GeradorDeToken) contra SQL Server de verdade — nao EF
// InMemory, porque tanto o incremento atomico de TokenVersion quanto TODO o
// backoff de SEC-02 sao ExecuteUpdateAsync com CASE e WHERE condicional, que o
// provedor InMemory nao traduz e que nao provariam nada sobre concorrencia real
// entre conexoes — justamente o que os cenarios 19 e 20 existem para provar.
//
// O que este verificador NAO cobre: um handshake HTTP/WebSocket do SignalR
// real — isto exigiria subir um servidor Kestrel de verdade e um cliente
// SignalR de verdade, fora do padrao dos verificadores deste projeto (e fora
// do escopo deste PR). O cenario 10 NAO deve ser lido como prova de handshake
// — ele prova so o componente de validacao que tanto a policy padrao
// (controllers) quanto o Hub chamam; a fiacao em si (SessaoAtivaHandler e
// AtendimentoHub.OnConnectedAsync de fato invocando este metodo) fica
// verificada por leitura de codigo, nao por este teste. O handshake SignalR
// end-to-end sera validado no ambiente TESTE. Ver comentario no cenario 10,
// abaixo.
//
// ATENCAO: este verificador ESCREVE no banco. Rode SOMENTE contra base de
// desenvolvimento. Ao final remove tudo que criou.

var conexao = args.Length > 0
    ? args[0]
    : "Server=.\\SQLEXPRESS;Database=CRM_Cartorio_Dev;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

var construtor = new DbContextOptionsBuilder<CrmDbContext>();
ConfiguracaoDoBanco.Configurar(construtor, conexao);
var opcoes = construtor.Options;

// SEC-03 (bloqueador 13) — mesma connection string, mas com o interceptor de
// falha injetada plugado. So existe aqui, neste verificador — nunca na
// configuracao real de producao.
DbContextOptions<CrmDbContext> OpcoesComFalhaDeEvento(TipoDeEventoDeSeguranca tipoParaFalhar)
{
    var builder = new DbContextOptionsBuilder<CrmDbContext>();
    ConfiguracaoDoBanco.Configurar(builder, conexao);
    builder.AddInterceptors(new InterceptorDeFalhaDeEvento(tipoParaFalhar));
    return builder.Options;
}

await using (var seguranca = new CrmDbContext(opcoes))
{
    if (!string.Equals(seguranca.Database.GetDbConnection().Database, "CRM_Cartorio_Dev", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Recusado: o verificador so escreve em CRM_Cartorio_Dev.");
    }

    if (!await seguranca.Database.CanConnectAsync())
    {
        Console.WriteLine("Nao foi possivel conectar. Verifique a connection string.");
        return 2;
    }
}

var falhas = 0;
void Checar(string nome, bool ok)
{
    Console.WriteLine($"{(ok ? "OK   " : "FALHA")} {nome}");
    if (!ok) falhas++;
}

var hash = new HashDeSenha();
var tokens = new GeradorDeToken(Options.Create(new OpcoesDeJwt
{
    Emissor = "CrmCartorio",
    Audiencia = "CrmCartorio",
    Chave = "chave-somente-deste-verificador-nao-e-segredo-real-0123456789",
    ExpiracaoEmMinutos = 480
}));

// Declarado antes das funcoes locais de proposito: uma funcao local pode
// capturar uma variavel declarada mais abaixo no mesmo bloco, mas o
// compilador ainda exige que o PONTO DE DECLARACAO textual venha antes de
// qualquer uso, mesmo dentro da funcao local (CS0841).
var tenantId = Guid.Empty;
var ids = new List<Guid>();

// SEC-03: chave de teste, isolada — nada aqui e segredo real.
var opcoesDeMfa = Options.Create(new OpcoesDeMfa
{
    VersaoCorrente = "1",
    Chaves = new Dictionary<string, string> { ["1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }
});
var cifra = new CifraDeSegredoMfa(opcoesDeMfa);

// SEC-03 "confiar neste dispositivo": o cookie e o unico estado que vive no
// NAVEGADOR. Quando o cenario nao passa um, cada chamada ganha um pote vazio e
// descartavel — e o equivalente a "navegador novo, sem cookie nenhum", que e
// exatamente o pressuposto de todos os cenarios anteriores a esta feature.
IAutenticacaoService NovoAutenticacao(CrmDbContext db, Guid atorId, ICookieDeDispositivoConfiavel? navegador = null) =>
    new AutenticacaoService(db, hash, tokens, cifra, new ContextoFixo(tenantId, atorId, "Teste", PerfilDeAtendente.Atendente), navegador ?? new NavegadorFalso(), NullLogger<AutenticacaoService>.Instance);

// Mesma coisa, mas para um tenant ISOLADO — AlterarSenhaAsync filtra por
// `ctx.AtendenteId && ctx.TenantId`, entao o AutenticacaoService acima
// (fechado no `tenantId` COMPARTILHADO) nunca enxerga conta de tenant
// isolado.
IAutenticacaoService NovoAutenticacaoParaTenant(CrmDbContext db, Guid atorId, Guid tenantAlvo) =>
    new AutenticacaoService(db, hash, tokens, cifra, new ContextoFixo(tenantAlvo, atorId, "Teste", PerfilDeAtendente.Atendente), new NavegadorFalso(), NullLogger<AutenticacaoService>.Instance);

IAtendenteService NovoAtendenteService(CrmDbContext db, Guid atorId) =>
    new AtendenteService(db, new ContextoFixo(tenantId, atorId, "Admin", PerfilDeAtendente.Administrador), hash);

// Mesma coisa, mas para um tenant ISOLADO — AtualizarAsync filtra por
// `TenantId == ctx.TenantId`, entao o AtendenteService acima (fechado no
// `tenantId` COMPARTILHADO) nunca enxerga conta de tenant isolado.
IAtendenteService NovoAtendenteServiceParaTenant(CrmDbContext db, Guid atorId, Guid tenantAlvo) =>
    new AtendenteService(db, new ContextoFixo(tenantAlvo, atorId, "Admin", PerfilDeAtendente.Administrador), hash);

IMfaService NovoMfaService(CrmDbContext db, ICookieDeDispositivoConfiavel? navegador = null) =>
    new MfaService(db, hash, cifra, tokens, navegador ?? new NavegadorFalso(), NullLogger<MfaService>.Instance);

/// <summary>
/// SEC-03 (revisao independente do PR #46, bloqueador 9) —
/// FerramentaDeBootstrapMfa.TentarRegistrarTicketAsync e `private` de
/// proposito: nenhum metodo publico da ferramenta de bootstrap pode gerar,
/// registrar ou persistir artefato utilizavel como ticket. Para provar que o
/// nucleo atomico/testavel funciona sob concorrencia sem reabrir essa
/// superficie, este verificador chama o metodo privado via REFLEXAO — nunca
/// atraves de uma API publica de producao. So este processo de teste tem
/// esse acesso (System.Reflection contra um membro `private static` do
/// tipo); nenhum outro codigo do assembly, nem em producao, pode fazer o
/// mesmo sem tambem recorrer a reflexao.
/// </summary>
async Task<ElegibilidadeDeBootstrap> TentarRegistrarTicketViaReflexaoAsync(
    CrmDbContext db, string email, string hashDoTicket, string motivo, string atorExterno)
{
    var metodo = typeof(FerramentaDeBootstrapMfa).GetMethod(
        "TentarRegistrarTicketAsync", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("TentarRegistrarTicketAsync nao encontrado via reflexao — a assinatura mudou?");

    var tarefa = (Task<ElegibilidadeDeBootstrap>)metodo.Invoke(
        null, [db, email, hashDoTicket, motivo, atorExterno, CancellationToken.None])!;

    return await tarefa;
}

IValidadorDeSessao NovoValidador(CrmDbContext db) => new ValidadorDeSessao(db);

/// <summary>
/// SEC-03 — extrai o VALOR do atributo Path de um cabecalho Set-Cookie, para
/// os testes compararem o caminho por igualdade e nao por Contains.
/// </summary>
/// <remarks>
/// Contains e armadilha aqui: "path=/api/autenticacao" e substring de
/// "path=/api/autenticacao/login", entao um teste escrito com Contains
/// continuaria verde se alguem alargasse o caminho do cookie de volta para o
/// controller inteiro — que e justamente a regressao a evitar. Devolve string
/// vazia quando nao ha Path, para o teste falhar em vez de lancar.
/// </remarks>
static string CaminhoDoSetCookie(string? setCookie) =>
    (setCookie ?? string.Empty)
        .Split(';', StringSplitOptions.TrimEntries)
        .Where(atributo => atributo.StartsWith("path=", StringComparison.OrdinalIgnoreCase))
        .Select(atributo => atributo["path=".Length..])
        .FirstOrDefault() ?? string.Empty;

int LerVersaoDaClaim(string jwt) =>
    int.Parse(new JsonWebTokenHandler().ReadJsonWebToken(jwt).Claims.First(c => c.Type == ClaimsDoAtendente.TokenVersion).Value);

async Task<Atendente> Ler(Guid id)
{
    await using var db = new CrmDbContext(opcoes);
    return await db.Atendentes.AsNoTracking().SingleAsync(a => a.Id == id);
}

// ------------------------------------------------------- apoio de SEC-02

const string SenhaBoa = "SenhaCorreta123";
const string SenhaRuim = "SenhaErrada000";

/// <summary>Conta nova, destravada, so para o cenario que a pediu.</summary>
async Task<(Guid Id, string Email)> NovaConta(string rotulo, bool ativo = true)
{
    await using var db = new CrmDbContext(opcoes);
    var conta = new Atendente
    {
        TenantId = tenantId,
        Nome = $"[TESTE SEC-02] {rotulo}",
        Email = $"teste-sec-02-{Guid.NewGuid():N}@teste.local",
        SenhaHash = hash.Gerar(SenhaBoa),
        Perfil = PerfilDeAtendente.Atendente,
        Ativo = ativo
    };
    db.Atendentes.Add(conta);
    await db.SaveChangesAsync();
    ids.Add(conta.Id);
    return (conta.Id, conta.Email);
}

/// <summary>
/// Tenta o login pelo servico real numa conta SEM MFA (SEC-02). Devolve o
/// token, ou null se recusado. Para contas COM MFA, ver EtapaUm/EtapaDois abaixo.
/// </summary>
async Task<string?> Tentar(string email, string senha)
{
    await using var db = new CrmDbContext(opcoes);
    try
    {
        var resposta = await NovoAutenticacao(db, Guid.NewGuid())
            .AutenticarAsync(new LoginRequest(email, senha));

        return resposta.Sessao?.Token;
    }
    catch (UsuariosServiceException ex) when (ex.Codigo == "credenciais_invalidas")
    {
        return null;
    }
}

async Task<int> Falhar(string email, int vezes)
{
    var recusadas = 0;
    for (var i = 0; i < vezes; i++)
    {
        if (await Tentar(email, SenhaRuim) is null) recusadas++;
    }

    return recusadas;
}

/// <summary>
/// Empurra o bloqueio para o passado, em vez de dormir ate ele expirar de
/// verdade — o cenario sob teste e "o que acontece DEPOIS que expirou", e
/// esperar 1 minuto de relogio nao provaria nada a mais.
/// </summary>
async Task ExpirarBloqueio(Guid id)
{
    await using var db = new CrmDbContext(opcoes);
    await db.Atendentes.Where(a => a.Id == id)
        .ExecuteUpdateAsync(s => s.SetProperty(
            a => a.BloqueadoAte, DateTime.UtcNow.AddMinutes(-1)));
}

/// <summary>
/// Confere a duracao do bloqueio contra a janela em que ele PODE ter sido
/// carimbado (entre o instante anterior e o posterior a chamada), e nao contra
/// um relogio fixo: o servico usa o proprio DateTime.UtcNow dele, que cai em
/// algum ponto desse intervalo.
/// </summary>
bool BloqueioDurou(DateTime? bloqueadoAte, DateTime antes, DateTime depois, int minutos) =>
    bloqueadoAte is { } ate
    && ate >= antes.AddMinutes(minutos).AddSeconds(-5)
    && ate <= depois.AddMinutes(minutos).AddSeconds(5);

// ------------------------------------------------------- apoio de SEC-03

/// <summary>Conta nova, privilegiada por padrao (Administrador) — o alvo natural da politica de MFA.</summary>
async Task<(Guid Id, string Email)> NovaContaPrivilegiada(
    string rotulo, PerfilDeAtendente perfil = PerfilDeAtendente.Administrador,
    CargoNaServentia cargo = CargoNaServentia.NaoInformado, bool ativo = true)
{
    await using var db = new CrmDbContext(opcoes);
    var conta = new Atendente
    {
        TenantId = tenantId,
        Nome = $"[TESTE SEC-03] {rotulo}",
        Email = $"teste-sec-03-{Guid.NewGuid():N}@teste.local",
        SenhaHash = hash.Gerar(SenhaBoa),
        Perfil = perfil,
        Cargo = cargo,
        Ativo = ativo
    };
    db.Atendentes.Add(conta);
    await db.SaveChangesAsync();
    ids.Add(conta.Id);
    return (conta.Id, conta.Email);
}

/// <summary>TOTP valido para o instante ATUAL (deslocamento em passos de 30s, 0 = agora).</summary>
string CodigoTotp(string segredoBase32, int deslocamentoDePassos = 0)
{
    var totp = new Totp(Base32Encoding.ToBytes(segredoBase32));
    var instante = DateTime.UtcNow.AddSeconds(deslocamentoDePassos * 30);
    return totp.ComputeTotp(instante);
}

/// <summary>
/// Emite um ticket CRU (INSERT direto em DesafiosDeMfa, Tipo=Ticket) — o
/// equivalente de teste ao que a ferramenta de bootstrap ou
/// IMfaService.EmitirTicketAsync produzem. Existe so para montar o CENARIO
/// (uma conta privilegiada com um ticket valido em maos); o CONSUMO do
/// ticket, esse sim, sempre passa pelo servico real
/// (IniciarMatriculaPrivilegiadaAsync) — nunca e o ponto que os testes pulam.
/// </summary>
async Task<string> EmitirTicketCruAsync(Guid atendenteId, int tokenVersionDoAlvo, TimeSpan? ttl = null)
{
    var tokenEmClaro = SegredosAleatoriosMfa.NovoToken();

    await using var db = new CrmDbContext(opcoes);
    db.DesafiosDeMfa.Add(new DesafioDeMfa
    {
        TenantId = tenantId,
        AtendenteId = atendenteId,
        HashDoDesafio = SegredosAleatoriosMfa.Sha256Hex(tokenEmClaro),
        Tipo = TipoDeDesafioMfa.Ticket,
        TokenVersionNoDesafio = tokenVersionDoAlvo,
        ExpiraEm = DateTime.UtcNow.Add(ttl ?? TimeSpan.FromHours(24))
    });
    await db.SaveChangesAsync();

    return tokenEmClaro;
}

/// <summary>
/// Cria a conta, ativa e CONFIRMA o MFA pelo servico real. Devolve tudo que os
/// testes precisam.
/// </summary>
/// <remarks>
/// CORRECAO (revisao independente do PR #46, bloqueador 1): conta PRIVILEGIADA
/// (Administrador ou Titular) nunca mais matricula pelo fluxo autenticado
/// direto — o proprio MfaService recusa (MatriculaPrivilegiadaExigeTicket).
/// Este helper roteia sozinho: privilegiada vai pelo fluxo REAL de ticket
/// (EmitirTicketCruAsync monta o cenario; IniciarMatriculaPrivilegiadaAsync,
/// IniciarViaDesafioAsync e ConfirmarViaDesafioAsync — os mesmos metodos que o
/// endpoint publico chama — fazem a matricula de verdade); nao privilegiada
/// continua pelo fluxo autenticado direto, que continua valido para ela.
/// Nenhum dos ~26 cenarios que chamam este helper precisou mudar por causa
/// disto — e exatamente o ponto: a correcao do bloqueio mora aqui, uma vez so.
/// </remarks>
async Task<(Guid Id, string Email, string SegredoBase32, IReadOnlyList<string> Codigos)> NovaContaComMfaAtivo(
    string rotulo, PerfilDeAtendente perfil = PerfilDeAtendente.Administrador,
    CargoNaServentia cargo = CargoNaServentia.NaoInformado)
{
    var (id, email) = await NovaContaPrivilegiada(rotulo, perfil, cargo);
    var privilegiada = perfil == PerfilDeAtendente.Administrador || cargo == CargoNaServentia.Titular;

    if (privilegiada)
    {
        var tokenVersionAtual = (await Ler(id)).TokenVersion;
        var ticket = await EmitirTicketCruAsync(id, tokenVersionAtual);

        await using var db = new CrmDbContext(opcoes);
        var mfa = NovoMfaService(db);

        var sessaoDeMatricula = await mfa.IniciarMatriculaPrivilegiadaAsync(
            new MatricularPrivilegiadoRequest(email, SenhaBoa, ticket), null, default);
        var iniciado = await mfa.IniciarViaDesafioAsync(
            new IniciarViaDesafioRequest(sessaoDeMatricula.Desafio), default);
        var confirmado = await mfa.ConfirmarViaDesafioAsync(
            new ConfirmarViaDesafioRequest(sessaoDeMatricula.Desafio, CodigoTotp(iniciado.SegredoBase32, -1)),
            null, default);

        return (id, email, iniciado.SegredoBase32, confirmado.CodigosDeRecuperacao);
    }

    await using var dbDireto = new CrmDbContext(opcoes);
    var identidade = new IdentidadeParaMfa(tenantId, id);
    var mfaDireto = NovoMfaService(dbDireto);

    var iniciar = await mfaDireto.IniciarAsync(identidade, new IniciarMfaRequest(SenhaBoa, null), null, default);

    // Confirma com o passo -1 (nao o "atual"), de proposito: assim o passo
    // "atual" (deslocamento 0, o padrao usado por CodigoTotp em todo o resto
    // deste arquivo) fica LIVRE para o primeiro login de teste depois da
    // ativacao. Sem isto, qualquer teste rapido demais (confirmar e logar em
    // menos de 30s, o caso normal aqui) colidiria com o proprio anti-replay
    // da confirmacao — nao e falha do produto, e um artefato de teste rapido.
    var confirmar = await mfaDireto.ConfirmarAsync(identidade, new ConfirmarMfaRequest(CodigoTotp(iniciar.SegredoBase32, -1)), null, default);

    return (id, email, iniciar.SegredoBase32, confirmar.CodigosDeRecuperacao);
}

/// <summary>Etapa 1 pelo servico real.</summary>
async Task<LoginEtapa1Response> EtapaUm(string email, string senha, ICookieDeDispositivoConfiavel? navegador = null)
{
    await using var db = new CrmDbContext(opcoes);
    return await NovoAutenticacao(db, Guid.NewGuid(), navegador).AutenticarAsync(new LoginRequest(email, senha));
}

/// <summary>Etapa 2 pelo servico real. Devolve o token, ou null se recusado (qualquer motivo).</summary>
async Task<string?> EtapaDois(
    string desafio, string codigo, ICookieDeDispositivoConfiavel? navegador = null, bool confiarNesteDispositivo = false)
{
    await using var db = new CrmDbContext(opcoes);
    try
    {
        var resposta = await NovoAutenticacao(db, Guid.NewGuid(), navegador)
            .ConcluirLoginMfaAsync(new ConcluirLoginMfaRequest(desafio, codigo, confiarNesteDispositivo));

        return resposta.Token;
    }
    catch (UsuariosServiceException)
    {
        return null;
    }
}

/// <summary>Login completo com MFA — atalho para cenarios que so precisam do resultado final.</summary>
async Task<string?> LoginComMfa(string email, string senha, string segredoBase32)
{
    var etapa1 = await EtapaUm(email, senha);
    if (!etapa1.RequerMfa || etapa1.Desafio is null) return etapa1.Sessao?.Token;
    return await EtapaDois(etapa1.Desafio.Desafio, CodigoTotp(segredoBase32));
}

async Task<int> FalharMfa(string desafio, int vezes)
{
    var recusadas = 0;
    for (var i = 0; i < vezes; i++)
    {
        if (await EtapaDois(desafio, "000000") is null) recusadas++;
    }
    return recusadas;
}

async Task ExpirarBloqueioDeMfa(Guid id)
{
    await using var db = new CrmDbContext(opcoes);
    await db.Atendentes.Where(a => a.Id == id)
        .ExecuteUpdateAsync(s => s.SetProperty(a => a.MfaBloqueadoAte, DateTime.UtcNow.AddMinutes(-1)));
}

/// <summary>Login completo com MFA usando um codigo EXATO (para testar replay do mesmo passo).</summary>
async Task<string?> LoginComMfaComCodigoFixo(string email, string senha, string codigo)
{
    var etapa1 = await EtapaUm(email, senha);
    if (!etapa1.RequerMfa || etapa1.Desafio is null) return etapa1.Sessao?.Token;
    return await EtapaDois(etapa1.Desafio.Desafio, codigo);
}

async Task<DesafioDeMfa?> LerDesafioPorTexto(string desafioEmClaro)
{
    await using var db = new CrmDbContext(opcoes);
    var hashDoDesafio = SegredosAleatoriosMfa.Sha256Hex(desafioEmClaro);
    return await db.DesafiosDeMfa.AsNoTracking().SingleOrDefaultAsync(d => d.HashDoDesafio == hashDoDesafio);
}

var tenantIdsDeTeste = new List<Guid>();

try
{
    // ------------------------------------------------------------- preparo

    Guid atendenteId;
    var nomeAtual = "[TESTE SEC-01] Atendente";
    var emailAtual = $"teste-sec-01-{Guid.NewGuid():N}@teste.local";
    var perfilAtual = PerfilDeAtendente.Atendente;
    const CargoNaServentia cargoAtual = CargoNaServentia.NaoInformado;
    Guid? setorAtual = null;
    var ativoAtual = true;
    var senhaAtual = "SenhaInicial123";

    await using (var db = new CrmDbContext(opcoes))
    {
        tenantId = await db.Tenants.Select(t => t.Id).FirstAsync();

        var atendente = new Atendente
        {
            TenantId = tenantId,
            Nome = nomeAtual,
            Email = emailAtual,
            SenhaHash = hash.Gerar(senhaAtual),
            Perfil = perfilAtual,
            Cargo = cargoAtual,
            Ativo = ativoAtual
        };
        db.Atendentes.Add(atendente);
        await db.SaveChangesAsync();
        atendenteId = atendente.Id;
        ids.Add(atendenteId);
    }

    AtualizarAtendenteRequest RequisicaoDeAtualizacao() =>
        new(nomeAtual, emailAtual, perfilAtual, setorAtual, ativoAtual, cargoAtual);

    // ------------------------------------------------------------- cenario 1

    var estado1 = await Ler(atendenteId);
    var (token1, _) = tokens.Gerar(estado1);
    await using (var db = new CrmDbContext(opcoes))
    {
        Checar("1. JWT valido + versao correta -> autorizado",
            await NovoValidador(db).CarregarSessaoAsync(atendenteId, tenantId, LerVersaoDaClaim(token1)) is not null);
    }

    // ------------------------------------------------------------- cenario 2
    // troca da PROPRIA senha

    var estadoAntesDaTroca = await Ler(atendenteId);
    var (tokenAntesDaTroca, _) = tokens.Gerar(estadoAntesDaTroca);
    var versaoAntesDaTroca = LerVersaoDaClaim(tokenAntesDaTroca);

    await using (var db = new CrmDbContext(opcoes))
    {
        await NovoAutenticacao(db, atendenteId)
            .AlterarSenhaAsync(new AlterarSenhaRequest(senhaAtual, "SenhaNova456xyz"));
    }
    senhaAtual = "SenhaNova456xyz";

    await using (var db = new CrmDbContext(opcoes))
    {
        Checar("2. JWT antigo apos troca da propria senha -> nao autorizado",
            await NovoValidador(db).CarregarSessaoAsync(atendenteId, tenantId, versaoAntesDaTroca) is null);
    }

    // ------------------------------------------------------------- cenario 3
    // redefinicao de senha PELO ADMINISTRADOR

    var estadoAntesDoAdmin = await Ler(atendenteId);
    var (tokenAntesDoAdmin, _) = tokens.Gerar(estadoAntesDoAdmin);
    var versaoAntesDoAdmin = LerVersaoDaClaim(tokenAntesDoAdmin);

    await using (var db = new CrmDbContext(opcoes))
    {
        await NovoAtendenteService(db, Guid.NewGuid())
            .RedefinirSenhaAsync(atendenteId, new RedefinirSenhaRequest("SenhaAdmin789xyz"));
    }
    senhaAtual = "SenhaAdmin789xyz";

    await using (var db = new CrmDbContext(opcoes))
    {
        Checar("3. JWT antigo apos redefinicao pelo administrador -> nao autorizado",
            await NovoValidador(db).CarregarSessaoAsync(atendenteId, tenantId, versaoAntesDoAdmin) is null);
    }

    // ------------------------------------------------------------- cenario 4
    // mudanca de Perfil (Atendente -> Supervisor), ainda ativo

    var estadoAntesDoPerfil = await Ler(atendenteId);
    var (tokenAntesDoPerfil, _) = tokens.Gerar(estadoAntesDoPerfil);
    var versaoAntesDoPerfil = LerVersaoDaClaim(tokenAntesDoPerfil);

    perfilAtual = PerfilDeAtendente.Supervisor;
    await using (var db = new CrmDbContext(opcoes))
    {
        await NovoAtendenteService(db, Guid.NewGuid())
            .AtualizarAsync(atendenteId, RequisicaoDeAtualizacao());
    }

    await using (var db = new CrmDbContext(opcoes))
    {
        Checar("4. JWT antigo apos alteracao de Perfil -> nao autorizado",
            await NovoValidador(db).CarregarSessaoAsync(atendenteId, tenantId, versaoAntesDoPerfil) is null);
    }

    // ------------------------------------------------------------- cenario 5
    // desativacao (Ativo true -> false)

    var estadoAntesDaDesativacao = await Ler(atendenteId);
    var (tokenAntesDaDesativacao, _) = tokens.Gerar(estadoAntesDaDesativacao);
    var versaoAntesDaDesativacao = LerVersaoDaClaim(tokenAntesDaDesativacao);

    ativoAtual = false;
    await using (var db = new CrmDbContext(opcoes))
    {
        await NovoAtendenteService(db, Guid.NewGuid())
            .AtualizarAsync(atendenteId, RequisicaoDeAtualizacao());
    }

    await using (var db = new CrmDbContext(opcoes))
    {
        Checar("5. JWT antigo apos desativacao -> nao autorizado",
            await NovoValidador(db).CarregarSessaoAsync(atendenteId, tenantId, versaoAntesDaDesativacao) is null);
    }

    // ------------------------------------------------------------- cenario 6

    await using (var db = new CrmDbContext(opcoes))
    {
        Checar("6. Atendente inexistente -> nao autorizado",
            await NovoValidador(db).CarregarSessaoAsync(Guid.NewGuid(), tenantId, 1) is null);
    }

    // ------------------------------------------------------------- cenario 7

    var estadoAtualParaTenant = await Ler(atendenteId);
    await using (var db = new CrmDbContext(opcoes))
    {
        Checar("7. Tenant da claim incompativel -> nao autorizado",
            await NovoValidador(db).CarregarSessaoAsync(atendenteId, Guid.NewGuid(), estadoAtualParaTenant.TokenVersion) is null);
    }

    // ------------------------------------------------------------- cenario 8
    // reativacao: token velho (de antes da desativacao) continua invalido;
    // login novo funciona, com a versao atual

    ativoAtual = true;
    await using (var db = new CrmDbContext(opcoes))
    {
        await NovoAtendenteService(db, Guid.NewGuid())
            .AtualizarAsync(atendenteId, RequisicaoDeAtualizacao());
    }

    await using (var db = new CrmDbContext(opcoes))
    {
        Checar("8a. Reativado: token de antes da desativacao continua invalido",
            await NovoValidador(db).CarregarSessaoAsync(atendenteId, tenantId, versaoAntesDaDesativacao) is null);
    }

    var versaoAposReativar = (await Ler(atendenteId)).TokenVersion;
    Checar("8b. Reativar nao reduz nem zera TokenVersion",
        versaoAposReativar >= versaoAntesDaDesativacao);

    LoginResponse loginNovo;
    await using (var db = new CrmDbContext(opcoes))
    {
        var etapa1DaReativacao = await NovoAutenticacao(db, atendenteId)
            .AutenticarAsync(new LoginRequest(emailAtual, senhaAtual));
        loginNovo = etapa1DaReativacao.Sessao!;
    }

    await using (var db = new CrmDbContext(opcoes))
    {
        Checar("8c. Login novo apos reativacao funciona e emite versao atual",
            await NovoValidador(db).CarregarSessaoAsync(atendenteId, tenantId, LerVersaoDaClaim(loginNovo.Token)) is not null);
    }

    // ------------------------------------------------------------- cenario 9
    // duas (N) alteracoes criticas concorrentes nao podem perder incremento
    // nem fazer TokenVersion voltar — SQL Server de verdade, ExecuteUpdateAsync

    const int Concorrentes = 10;
    Guid atendenteConcorrenciaId;
    await using (var db = new CrmDbContext(opcoes))
    {
        var concorrencia = new Atendente
        {
            TenantId = tenantId,
            Nome = "[TESTE SEC-01] Concorrencia",
            Email = $"teste-sec-01-conc-{Guid.NewGuid():N}@teste.local",
            SenhaHash = hash.Gerar("SenhaBase123"),
            Perfil = PerfilDeAtendente.Atendente,
            Ativo = true
        };
        db.Atendentes.Add(concorrencia);
        await db.SaveChangesAsync();
        atendenteConcorrenciaId = concorrencia.Id;
        ids.Add(atendenteConcorrenciaId);
    }

    var versaoInicialConcorrencia = (await Ler(atendenteConcorrenciaId)).TokenVersion;

    await Task.WhenAll(Enumerable.Range(0, Concorrentes).Select(async i =>
    {
        await using var db = new CrmDbContext(opcoes);
        await NovoAtendenteService(db, Guid.NewGuid())
            .RedefinirSenhaAsync(atendenteConcorrenciaId, new RedefinirSenhaRequest($"SenhaConcorrente{i}Abc"));
    }));

    var versaoFinalConcorrencia = (await Ler(atendenteConcorrenciaId)).TokenVersion;
    Checar($"9. {Concorrentes} redefinicoes de senha concorrentes incrementam TokenVersion exatamente {Concorrentes}x (sem perda nem retrocesso)",
        versaoFinalConcorrencia == versaoInicialConcorrencia + Concorrentes);

    // ------------------------------------------------------------- cenario 10
    // NAO e um teste de handshake SignalR real (nao sobe Kestrel nem cliente
    // SignalR). SessaoAtivaHandler (policy padrao, controllers) e
    // AtendimentoHub.OnConnectedAsync chamam, por leitura de codigo, este
    // MESMO ValidadorDeSessao.CarregarSessaoAsync — o que este cenario prova e
    // so que o validador compartilhado recusa corretamente um TokenVersion
    // revogado; nao prova a fiacao REST/SignalR em si, nem o comportamento do
    // handshake HTTP/WebSocket. O handshake SignalR end-to-end (negotiate +
    // upgrade recusados com token revogado, conexao aberta sobrevivendo a
    // revogacao — SEC-01B) sera validado no ambiente TESTE.

    await using (var db = new CrmDbContext(opcoes))
    {
        Checar("10. Validador compartilhado por REST/SignalR recusa TokenVersion revogado",
            await NovoValidador(db).CarregarSessaoAsync(atendenteId, tenantId, versaoAntesDaDesativacao) is null);
    }

    // ------------------------------------------------------------- cenario 11

    await using (var db = new CrmDbContext(opcoes))
    {
        Checar("11. Token valido continua funcionando normalmente",
            await NovoValidador(db).CarregarSessaoAsync(atendenteId, tenantId, LerVersaoDaClaim(loginNovo.Token)) is not null);
    }

    // ------------------------------------------------------------- cenario 12
    // AllowAnonymous nao muda: a garantia e estrutural (o framework nunca
    // avalia [Authorize]/policy padrao para rota marcada [AllowAnonymous]).
    // Chamar o servico real por baixo confirma que nada quebrou funcionalmente.

    await using (var db = new CrmDbContext(opcoes))
    {
        var serventia = await NovoAutenticacao(db, Guid.NewGuid()).ObterServentiaPublicaAsync();
        Checar("12. Endpoint AllowAnonymous (serventia publica) continua funcionando",
            !string.IsNullOrWhiteSpace(serventia.Nome));
    }

    // =====================================================================
    // SEC-02 — protecao contra forca bruta no login
    // =====================================================================

    // ------------------------------------------------------------ cenario 13
    // 4 falhas nao bloqueiam: a 5a tentativa, com a senha certa, entra.

    {
        var (id, email) = await NovaConta("4 falhas e acerta");

        await Falhar(email, 4);

        var estado = await Ler(id);
        Checar("13a. 4 falhas seguidas nao bloqueiam a conta",
            estado.BloqueadoAte is null && estado.FalhasDeLoginConsecutivas == 4);

        Checar("13b. 5a tentativa com senha certa entra normalmente",
            await Tentar(email, SenhaBoa) is not null);

        var apos = await Ler(id);
        Checar("13c. login bem-sucedido zera contador, nivel e bloqueio",
            apos is { FalhasDeLoginConsecutivas: 0, NivelDeBloqueio: 0, BloqueadoAte: null });
    }

    // ------------------------------------------------------------ cenario 14
    // 5 falhas bloqueiam — e a senha CERTA nao fura o bloqueio.

    {
        var (id, email) = await NovaConta("5 falhas bloqueiam");

        var antes = DateTime.UtcNow;
        await Falhar(email, 5);
        var depois = DateTime.UtcNow;

        var estado = await Ler(id);
        Checar("14a. 5a falha abre bloqueio de 1 minuto (primeiro nivel)",
            BloqueioDurou(estado.BloqueadoAte, antes, depois, 1));
        Checar("14b. o ciclo fecha: contador volta a zero e o nivel sobe para 1",
            estado is { FalhasDeLoginConsecutivas: 0, NivelDeBloqueio: 1 });

        Checar("14c. senha CORRETA durante bloqueio NAO entra (nao fura bloqueio)",
            await Tentar(email, SenhaBoa) is null);
    }

    // ------------------------------------------------------------ cenario 15
    // Tentativa durante bloqueio nao estende o bloqueio — nem a errada, nem a
    // certa. Sem isto, um atacante manteria a conta travada para sempre.

    {
        var (id, email) = await NovaConta("bloqueio nao estende");
        await Falhar(email, 5);

        var bloqueioOriginal = (await Ler(id)).BloqueadoAte;

        await Tentar(email, SenhaRuim);
        var aposErrada = await Ler(id);

        await Tentar(email, SenhaBoa);
        var aposCerta = await Ler(id);

        Checar("15a. tentativa ERRADA durante bloqueio nao altera BloqueadoAte",
            aposErrada.BloqueadoAte == bloqueioOriginal);
        Checar("15b. tentativa CERTA durante bloqueio nao altera BloqueadoAte",
            aposCerta.BloqueadoAte == bloqueioOriginal);
        Checar("15c. tentativa durante bloqueio nao mexe em contador nem nivel",
            aposCerta is { FalhasDeLoginConsecutivas: 0, NivelDeBloqueio: 1 });
    }

    // ------------------------------------------------------------ cenario 16
    // Depois de expirar, a senha certa entra e limpa tudo.

    {
        var (id, email) = await NovaConta("expira e entra");
        await Falhar(email, 5);
        await ExpirarBloqueio(id);

        Checar("16a. apos a expiracao, senha correta entra",
            await Tentar(email, SenhaBoa) is not null);

        var apos = await Ler(id);
        Checar("16b. o login apos expiracao zera contador, nivel e bloqueio",
            apos is { FalhasDeLoginConsecutivas: 0, NivelDeBloqueio: 0, BloqueadoAte: null });
    }

    // ------------------------------------------------------------ cenario 17
    // Progressao: SEM login bem-sucedido entre os ciclos, o segundo bloqueio
    // dura 2 minutos, nao 1.

    {
        var (id, email) = await NovaConta("progressao escala");

        await Falhar(email, 5);
        await ExpirarBloqueio(id);

        var antes = DateTime.UtcNow;
        await Falhar(email, 5);
        var depois = DateTime.UtcNow;

        var estado = await Ler(id);
        Checar("17a. segundo ciclo SEM login no meio dura 2 minutos",
            BloqueioDurou(estado.BloqueadoAte, antes, depois, 2));
        Checar("17b. o nivel avancou para 2",
            estado.NivelDeBloqueio == 2);
    }

    // ------------------------------------------------------------ cenario 18
    // ... e um login bem-sucedido no meio REINICIA a progressao: o proximo
    // bloqueio volta a durar 1 minuto. E o contraste exato do cenario 17.

    {
        var (id, email) = await NovaConta("progressao reinicia");

        await Falhar(email, 5);
        await ExpirarBloqueio(id);
        await Tentar(email, SenhaBoa);

        var antes = DateTime.UtcNow;
        await Falhar(email, 5);
        var depois = DateTime.UtcNow;

        var estado = await Ler(id);
        Checar("18a. apos login bem-sucedido, o proximo bloqueio volta a 1 minuto",
            BloqueioDurou(estado.BloqueadoAte, antes, depois, 1));
        Checar("18b. o nivel voltou a 1 (progressao reiniciada)",
            estado.NivelDeBloqueio == 1);
    }

    // ------------------------------------------------------------ cenario 19
    // N falhas SIMULTANEAS na mesma conta nao perdem incremento. O UPDATE usa o
    // valor da propria coluna (nao um lido antes em memoria), entao o SQL Server
    // serializa as escritas e o total tem de bater exatamente.

    {
        var (id, email) = await NovaConta("falhas simultaneas");

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Tentar(email, SenhaRuim)));

        var estado = await Ler(id);
        Checar("19. 4 falhas simultaneas contam exatamente 4 (nenhuma perdida)",
            estado.FalhasDeLoginConsecutivas == 4 && estado.BloqueadoAte is null);
    }

    // ------------------------------------------------------------ cenario 20
    // A CORRIDA CENTRAL de SEC-02: com 4 falhas ja acumuladas, uma tentativa
    // CERTA e a 5a tentativa ERRADA chegam ao mesmo tempo, em conexoes
    // diferentes. So dois estados finais sao aceitaveis — e o que nunca pode
    // acontecer e emitir JWT com a conta bloqueada.

    {
        var (id, email) = await NovaConta("corrida 5a falha x acerto");
        await Falhar(email, 4);

        var certa = Tentar(email, SenhaBoa);
        var errada = Tentar(email, SenhaRuim);
        await Task.WhenAll(certa, errada);

        var token = await certa;
        var estado = await Ler(id);
        var agora = DateTime.UtcNow;

        // CASO A — o acerto venceu: entrou, zerou tudo, e a errada que chegou
        // depois virou a primeira falha de um ciclo novo.
        var casoA = token is not null
            && estado.BloqueadoAte is null
            && estado.NivelDeBloqueio == 0
            && estado.FalhasDeLoginConsecutivas == 1;

        // CASO B — a 5a falha venceu: bloqueou, e o acerto foi recusado sem
        // apagar o bloqueio recem-criado.
        var casoB = token is null
            && estado.BloqueadoAte > agora
            && estado.NivelDeBloqueio == 1
            && estado.FalhasDeLoginConsecutivas == 0;

        Checar($"20. corrida 5a falha x acerto termina em exatamente um dos dois estados validos " +
               $"(A={casoA}, B={casoB}; token={(token is null ? "nenhum" : "emitido")}, " +
               $"falhas={estado.FalhasDeLoginConsecutivas}, nivel={estado.NivelDeBloqueio}, " +
               $"bloqueado={(estado.BloqueadoAte > agora ? "sim" : "nao")})",
            casoA ^ casoB);
    }

    // ------------------------------------------------------------ cenario 21
    // Conta INATIVA nao acumula lockout. Sem isto, uma conta desligada podia ser
    // martelada por semanas e voltaria da reativacao ja bloqueada, no topo da
    // progressao, sem ninguem entender por que.

    {
        var (id, email) = await NovaConta("inativa nao acumula", ativo: false);

        await Falhar(email, 6);

        var estado = await Ler(id);
        Checar("21a. conta inativa nao acumula falhas nem bloqueio",
            estado is { FalhasDeLoginConsecutivas: 0, NivelDeBloqueio: 0, BloqueadoAte: null });

        Checar("21b. conta inativa continua recusando o login (com a senha certa, inclusive)",
            await Tentar(email, SenhaBoa) is null);
    }

    // ------------------------------------------------------------ cenario 22
    // Redefinicao de senha pelo administrador desbloqueia — a saida do suporte
    // para quem foi travado por engano ou por ataque contra o proprio e-mail.

    {
        var (id, email) = await NovaConta("admin desbloqueia");
        await Falhar(email, 5);

        Checar("22a. conta esta de fato bloqueada antes da redefinicao",
            (await Ler(id)).BloqueadoAte is not null);

        await using (var db = new CrmDbContext(opcoes))
        {
            await NovoAtendenteService(db, Guid.NewGuid())
                .RedefinirSenhaAsync(id, new RedefinirSenhaRequest("SenhaDoAdmin987"));
        }

        var apos = await Ler(id);
        Checar("22b. redefinicao pelo admin zera contador, nivel e bloqueio",
            apos is { FalhasDeLoginConsecutivas: 0, NivelDeBloqueio: 0, BloqueadoAte: null });

        Checar("22c. e o atendente entra na hora com a senha nova",
            await Tentar(email, "SenhaDoAdmin987") is not null);
    }

    // ------------------------------------------------------------ cenario 23
    // O PBKDF2 roda ANTES de qualquer decisao publica, nos quatro casos —
    // inclusive bloqueado e inexistente. Contado por um duble que registra a
    // invocacao, NAO medido em milissegundos: um teste de tempo seria fragil e
    // nao pertence a um verificador de rotina. Ver o risco residual de timing
    // registrado no README do modulo Usuarios.

    {
        var (_, emailBloqueado) = await NovaConta("hash sempre roda");
        await Falhar(emailBloqueado, 5);

        var (_, emailAtivo) = await NovaConta("hash sempre roda - ativo");
        var (_, emailInativo) = await NovaConta("hash sempre roda - inativo", ativo: false);

        async Task<int> ContarHashes(string email, string senha)
        {
            var espiao = new HashEspiao(hash);
            await using var db = new CrmDbContext(opcoes);
            var servico = new AutenticacaoService(
                db, espiao, tokens, cifra,
                new ContextoFixo(tenantId, Guid.NewGuid(), "Teste", PerfilDeAtendente.Atendente),
                new NavegadorFalso(),
                NullLogger<AutenticacaoService>.Instance);

            try { await servico.AutenticarAsync(new LoginRequest(email, senha)); }
            catch (UsuariosServiceException) { }

            return espiao.Verificacoes;
        }

        Checar("23a. e-mail inexistente ainda paga o custo do PBKDF2",
            await ContarHashes($"nao-existe-{Guid.NewGuid():N}@teste.local", SenhaRuim) == 1);
        Checar("23b. senha errada em conta ativa paga o custo do PBKDF2",
            await ContarHashes(emailAtivo, SenhaRuim) == 1);
        Checar("23c. conta inativa paga o custo do PBKDF2",
            await ContarHashes(emailInativo, SenhaBoa) == 1);
        Checar("23d. conta BLOQUEADA paga o custo do PBKDF2 (nao ha atalho de saida)",
            await ContarHashes(emailBloqueado, SenhaBoa) == 1);
    }

    // ------------------------------------------------------------ cenario 24
    // A resposta publica nao regrediu: os quatro casos continuam devolvendo
    // exatamente o mesmo codigo e a mesma mensagem. Qualquer diferenca aqui
    // devolveria ao atacante a lista de e-mails validos.

    {
        var (_, emailAtivo) = await NovaConta("resposta uniforme");
        var (_, emailInativo) = await NovaConta("resposta uniforme - inativo", ativo: false);
        var (_, emailBloqueado) = await NovaConta("resposta uniforme - bloqueado");
        await Falhar(emailBloqueado, 5);

        async Task<(string Codigo, string Mensagem, int Status)> Recusa(string email, string senha)
        {
            await using var db = new CrmDbContext(opcoes);
            try
            {
                await NovoAutenticacao(db, Guid.NewGuid())
                    .AutenticarAsync(new LoginRequest(email, senha));

                return ("nenhuma", "login aceito", 200);
            }
            catch (UsuariosServiceException ex)
            {
                return (ex.Codigo, ex.Message, ex.StatusHttp);
            }
        }

        var respostas = new[]
        {
            await Recusa($"nao-existe-{Guid.NewGuid():N}@teste.local", SenhaRuim),
            await Recusa(emailAtivo, SenhaRuim),
            await Recusa(emailInativo, SenhaBoa),
            await Recusa(emailBloqueado, SenhaBoa)
        };

        Checar("24. inexistente, senha errada, inativo e bloqueado devolvem resposta identica (401 credenciais_invalidas)",
            respostas.Distinct().Count() == 1
            && respostas[0] is { Codigo: "credenciais_invalidas", Status: 401 });
    }

    // ------------------------------------------------------------ cenario 25
    // Resolvedor de IP do limitador por IP. Puro, sem banco — mas mora aqui
    // porque e parte de SEC-02 e nao teria onde mais viver.

    {
        static HttpContext Requisicao(string? ipDaConexao, string? cabecalho)
        {
            var http = new DefaultHttpContext();
            http.Connection.RemoteIpAddress = ipDaConexao is null ? null : IPAddress.Parse(ipDaConexao);

            if (cabecalho is not null)
            {
                http.Request.Headers[ResolvedorDeIpDoCliente.CabecalhoDaCloudflare] = cabecalho;
            }

            return http;
        }

        Checar("25a. peer loopback + CF-Connecting-IP valido -> usa o IP do cabecalho",
            ResolvedorDeIpDoCliente.Resolver(Requisicao("127.0.0.1", "203.0.113.7")) == "203.0.113.7");

        Checar("25b. peer loopback + cabecalho invalido -> ignora e usa o IP da conexao",
            ResolvedorDeIpDoCliente.Resolver(Requisicao("127.0.0.1", "nao-e-um-ip")) == "127.0.0.1");

        Checar("25c. peer NAO loopback + cabecalho forjado -> ignora o cabecalho por completo",
            ResolvedorDeIpDoCliente.Resolver(Requisicao("198.51.100.4", "203.0.113.7")) == "198.51.100.4");

        Checar("25d. cabecalho vazio em peer loopback -> cai para o IP da conexao",
            ResolvedorDeIpDoCliente.Resolver(Requisicao("127.0.0.1", "")) == "127.0.0.1");

        // O IPv6 abreviado e o expandido sao o MESMO endereco: sem canonicalizar,
        // o mesmo cliente ganharia um balde de tentativas para cada grafia.
        Checar("25e. IPv6 do cabecalho e canonicalizado (mesma chave para grafias diferentes)",
            ResolvedorDeIpDoCliente.Resolver(Requisicao("::1", "2001:0db8:0000:0000:0000:0000:0000:0001"))
            == ResolvedorDeIpDoCliente.Resolver(Requisicao("::1", "2001:db8::1")));

        Checar("25f. sem IP de conexao e sem cabecalho -> chave conhecida, nunca nula",
            ResolvedorDeIpDoCliente.Resolver(Requisicao(null, null)) == ResolvedorDeIpDoCliente.Desconhecido);
    }

    // =====================================================================
    // SEC-03 — MFA / TOTP para contas privilegiadas
    // =====================================================================

    // ------------------------------------------------------------ cenario 26
    // TOTP: janela +-1 passo entra, +-2 nao entra. Teste PURO contra
    // ValidadorDeTotp (sem login, sem banco) — a janela e uma propriedade do
    // validador em si, independente do anti-replay (que os cenarios 27, 30 e
    // 40 testam, esses sim via login de verdade). Misturar os dois exigiria
    // controlar timing real entre chamadas para nao colidir consigo mesmo
    // (o proprio anti-replay), o que seria um teste fragil por natureza.

    {
        var segredo = ValidadorDeTotp.GerarSegredoBase32();

        Checar("26a. codigo do passo atual e aceito",
            ValidadorDeTotp.Validar(segredo, CodigoTotp(segredo, 0), out _));

        Checar("26b. codigo do passo -1 (30s atras) e aceito",
            ValidadorDeTotp.Validar(segredo, CodigoTotp(segredo, -1), out _));

        Checar("26c. codigo do passo +1 (30s a frente) e aceito",
            ValidadorDeTotp.Validar(segredo, CodigoTotp(segredo, 1), out _));

        Checar("26d. codigo do passo +2 (60s a frente) NAO e aceito",
            !ValidadorDeTotp.Validar(segredo, CodigoTotp(segredo, 2), out _));

        Checar("26e. codigo do passo -2 (60s atras) NAO e aceito",
            !ValidadorDeTotp.Validar(segredo, CodigoTotp(segredo, -2), out _));
    }

    // ------------------------------------------------------------ cenario 27
    // Replay: o MESMO codigo (mesmo passo) nao entra duas vezes.

    {
        var (id, email, segredo, _) = await NovaContaComMfaAtivo("replay");
        var codigo = CodigoTotp(segredo);

        Checar("27a. primeiro uso do codigo entra",
            await LoginComMfaComCodigoFixo(email, SenhaBoa, codigo) is not null);

        Checar("27b. o MESMO codigo (mesmo passo) nao entra de novo — anti-replay",
            await LoginComMfaComCodigoFixo(email, SenhaBoa, codigo) is null);
    }

    // ------------------------------------------------------------ cenario 28
    // Regressao: conta SEM MFA continua logando em uma unica etapa, exatamente
    // como antes de SEC-03.

    {
        var (_, email) = await NovaConta("sem mfa continua igual");
        var etapa1 = await EtapaUm(email, SenhaBoa);

        Checar("28. conta sem MFA: etapa 1 ja devolve sessao completa, sem desafio",
            !etapa1.RequerMfa && etapa1.Sessao is not null && etapa1.Desafio is null);
    }

    // ------------------------------------------------------------ cenario 29
    // Conta COM MFA: etapa 1 NUNCA devolve sessao — so o desafio.

    {
        var (_, email, _, _) = await NovaContaComMfaAtivo("etapa 1 so desafio");
        var etapa1 = await EtapaUm(email, SenhaBoa);

        Checar("29. conta com MFA: etapa 1 devolve desafio, nunca sessao",
            etapa1.RequerMfa && etapa1.Sessao is null && etapa1.Desafio is not null
            && etapa1.Desafio.Tipo == "SegundoFator");
    }

    // ------------------------------------------------------------ cenario 30
    // Reserva de tentativa ANTES de validar: o 1o codigo errado NAO consome o
    // desafio (ConsumidoEm continua nulo), so incrementa TentativasDeCodigo.

    {
        var (id, email, segredo, _) = await NovaContaComMfaAtivo("1o erro nao consome");
        var etapa1 = await EtapaUm(email, SenhaBoa);
        var desafioTexto = etapa1.Desafio!.Desafio;

        Checar("30a. codigo errado e recusado",
            await EtapaDois(desafioTexto, "000000") is null);

        var desafioNoBanco = await LerDesafioPorTexto(desafioTexto);
        Checar("30b. desafio continua ABERTO apos 1 erro (nao foi consumido)",
            desafioNoBanco is { ConsumidoEm: null, TentativasDeCodigo: 1 });

        Checar("30c. o MESMO desafio ainda aceita o codigo certo depois de 1 erro",
            await EtapaDois(desafioTexto, CodigoTotp(segredo)) is not null);
    }

    // ------------------------------------------------------------ cenario 31
    // 5 erros esgotam o desafio — a 6a tentativa falha na RESERVA, mesmo com o
    // codigo certo.

    {
        var (id, email, segredo, _) = await NovaContaComMfaAtivo("desafio esgota em 5");
        var etapa1 = await EtapaUm(email, SenhaBoa);
        var desafioTexto = etapa1.Desafio!.Desafio;

        var recusadas = await FalharMfa(desafioTexto, 5);
        Checar("31a. 5 tentativas com codigo errado sao todas recusadas", recusadas == 5);

        Checar("31b. a 6a tentativa falha mesmo com o codigo CERTO — desafio esgotado",
            await EtapaDois(desafioTexto, CodigoTotp(segredo)) is null);
    }

    // ------------------------------------------------------------ cenario 32
    // Bloqueio de MFA e por CONTA, nao por desafio: um desafio NOVO nao zera o
    // contador de falhas que o desafio anterior ja acumulou.

    {
        var (id, email, segredo, _) = await NovaContaComMfaAtivo("desafio novo nao zera");

        var etapa1a = await EtapaUm(email, SenhaBoa);
        await FalharMfa(etapa1a.Desafio!.Desafio, 4);

        var estadoApos4 = await Ler(id);
        Checar("32a. 4 falhas no primeiro desafio contam na conta",
            estadoApos4.MfaFalhasConsecutivas == 4 && estadoApos4.MfaBloqueadoAte is null);

        // Pede um desafio NOVO (simula abrir outra tentativa de login) e falha
        // mais uma vez — e a 5a falha da CONTA, nao do desafio novo (que teria
        // so 1 falha se o contador fosse por desafio).
        var etapa1b = await EtapaUm(email, SenhaBoa);
        var antes = DateTime.UtcNow;
        await FalharMfa(etapa1b.Desafio!.Desafio, 1);
        var depois = DateTime.UtcNow;

        var estadoApos5 = await Ler(id);
        Checar("32b. a 5a falha, num desafio DIFERENTE, ainda bloqueia a conta",
            BloqueioDurou(estadoApos5.MfaBloqueadoAte, antes, depois, 1));
    }

    // ------------------------------------------------------------ cenario 33
    // Trocar o IP de origem nao contorna o bloqueio de MFA — o contador e da
    // CONTA, sem IP na chave.

    {
        var (id, email, segredo, _) = await NovaContaComMfaAtivo("ips diferentes nao contornam");
        var etapa1 = await EtapaUm(email, SenhaBoa);

        // FalharMfa nao varia IP (o servico nem recebe IP na etapa 2 hoje —
        // o bloqueio por conta e agnostico de IP por construcao). Prova disso:
        // 5 falhas ainda bloqueiam mesmo vindo, logicamente, de "origens"
        // diferentes (cada Tentar() usa uma instancia de servico nova, o mais
        // proximo de "outra requisicao" que este verificador consegue simular
        // sem subir um Kestrel real).
        await FalharMfa(etapa1.Desafio!.Desafio, 5);

        var estado = await Ler(id);
        Checar("33. bloqueio de MFA por conta nao depende de IP — 5 falhas bloqueiam do mesmo jeito",
            estado.MfaBloqueadoAte > DateTime.UtcNow);

        Checar("33b. enquanto bloqueado, nem o codigo CERTO entra",
            await LoginComMfaComCodigoFixo(email, SenhaBoa, CodigoTotp(segredo, 1)) is null);

        await ExpirarBloqueioDeMfa(id);

        Checar("33c. apos o bloqueio de MFA expirar, o codigo certo volta a entrar",
            await LoginComMfaComCodigoFixo(email, SenhaBoa, CodigoTotp(segredo, 1)) is not null);
    }

    // ------------------------------------------------------------ cenario 34
    // Login com MFA bem-sucedido zera falhas, nivel e bloqueio da CONTA.

    {
        var (id, email, segredo, _) = await NovaContaComMfaAtivo("sucesso zera");
        var etapa1a = await EtapaUm(email, SenhaBoa);
        await FalharMfa(etapa1a.Desafio!.Desafio, 4);

        Checar("34. login com MFA bem-sucedido apos 4 falhas entra e zera o contador",
            await LoginComMfa(email, SenhaBoa, segredo) is not null);

        var estado = await Ler(id);
        Checar("34b. estado pos-sucesso: falhas=0, nivel=0, sem bloqueio",
            estado is { MfaFalhasConsecutivas: 0, MfaNivelDeBloqueio: 0, MfaBloqueadoAte: null });
    }

    // ------------------------------------------------------------ cenario 35
    // UltimoAcessoEm: senha correta sozinha NAO conclui login quando ha MFA;
    // so a etapa 2 conclui.

    {
        var (id, email, segredo, _) = await NovaContaComMfaAtivo("ultimoacesso separado");
        var antesDoAcesso = (await Ler(id)).UltimoAcessoEm;

        var etapa1 = await EtapaUm(email, SenhaBoa);
        var aposEtapa1 = (await Ler(id)).UltimoAcessoEm;

        Checar("35a. UltimoAcessoEm NAO muda so com a senha correta (MFA pendente)",
            aposEtapa1 == antesDoAcesso);

        var token = await EtapaDois(etapa1.Desafio!.Desafio, CodigoTotp(segredo));
        var aposEtapa2 = (await Ler(id)).UltimoAcessoEm;

        Checar("35b. token emitido na etapa 2", token is not null);
        Checar("35c. UltimoAcessoEm MUDA depois da etapa 2 concluir",
            aposEtapa2 is not null && aposEtapa2 != antesDoAcesso);
    }

    // ------------------------------------------------------------ cenario 36
    // TokenVersion mudou entre a etapa 1 e a etapa 2 (troca de senha) ->
    // desafio antigo invalido, mesmo com o codigo certo.

    {
        var (id, email, segredo, _) = await NovaContaComMfaAtivo("senha trocada invalida desafio");
        var etapa1 = await EtapaUm(email, SenhaBoa);

        await using (var db = new CrmDbContext(opcoes))
        {
            await NovoAutenticacao(db, id).AlterarSenhaAsync(new AlterarSenhaRequest(SenhaBoa, "SenhaNovaXyz123"));
        }

        Checar("36. troca de senha entre as etapas invalida o desafio pendente",
            await EtapaDois(etapa1.Desafio!.Desafio, CodigoTotp(segredo)) is null);
    }

    // ------------------------------------------------------------ cenario 37
    // Desativacao entre as etapas invalida o desafio.

    {
        var (id, email, segredo, _) = await NovaContaComMfaAtivo("desativacao invalida desafio", perfil: PerfilDeAtendente.Atendente);
        var etapa1 = await EtapaUm(email, SenhaBoa);

        await using (var db = new CrmDbContext(opcoes))
        {
            await NovoAtendenteService(db, Guid.NewGuid()).AtualizarAsync(id,
                new AtualizarAtendenteRequest("Nome", email, PerfilDeAtendente.Atendente, null, false));
        }

        Checar("37. desativar a conta entre as etapas invalida o desafio pendente",
            await EtapaDois(etapa1.Desafio!.Desafio, CodigoTotp(segredo)) is null);
    }

    // ------------------------------------------------------------ cenario 38
    // Reset administrativo de MFA entre as etapas invalida o desafio (o reset
    // sobe TokenVersion).

    {
        var (id, email, segredo, _) = await NovaContaComMfaAtivo("reset invalida desafio");
        var (adminId, _, adminSegredo, _) = await NovaContaComMfaAtivo("admin que reseta (38)");
        var etapa1 = await EtapaUm(email, SenhaBoa);

        await using (var db = new CrmDbContext(opcoes))
        {
            await NovoMfaService(db).ResetarAsync(
                new IdentidadeParaMfa(tenantId, adminId), id,
                new ResetarMfaRequest(SenhaBoa, CodigoTotp(adminSegredo), "teste automatizado"), null, default);
        }

        Checar("38. reset administrativo entre as etapas invalida o desafio pendente",
            await EtapaDois(etapa1.Desafio!.Desafio, CodigoTotp(segredo)) is null);
    }

    // ------------------------------------------------------------ cenario 39
    // A CORRIDA central de SEC-03: 4 falhas de MFA ja acumuladas, e a 5a falha
    // corre contra um acerto simultaneo — cada um usando um DESAFIO
    // INDEPENDENTE (dois logins concorrentes de verdade, nao a mesma
    // requisicao disputada). Isso abre um terceiro desfecho legitimo alem dos
    // dois de SEC-02: o acerto pode vencer por completo ANTES do registro da
    // falha (falhas=0), ou o registro da falha — que ja estava fadado a
    // acontecer, independente da ordem — pode commitar LOGO DEPOIS do reset
    // do acerto, virando a 1a falha de um ciclo novo (falhas=1). Os dois sao
    // seguros; o que nunca pode acontecer e token emitido COM bloqueio ativo,
    // ou token negado SEM bloqueio coerente.

    {
        var (id, email, segredo, _) = await NovaContaComMfaAtivo("corrida 5a falha mfa x acerto");
        var etapa1a = await EtapaUm(email, SenhaBoa);
        await FalharMfa(etapa1a.Desafio!.Desafio, 4);

        var etapa1b = await EtapaUm(email, SenhaBoa);
        var codigoCerto = CodigoTotp(segredo);

        var certa = EtapaDois(etapa1b.Desafio!.Desafio, codigoCerto);
        var errada = EtapaDois(etapa1a.Desafio!.Desafio, "000000");
        await Task.WhenAll(certa, errada);

        var token = await certa;
        var estado = await Ler(id);
        var agora = DateTime.UtcNow;

        var casoA = token is not null
            && estado.MfaBloqueadoAte is null
            && estado.MfaNivelDeBloqueio == 0
            && estado.MfaFalhasConsecutivas is 0 or 1;

        var casoB = token is null
            && estado.MfaBloqueadoAte > agora
            && estado.MfaNivelDeBloqueio == 1
            && estado.MfaFalhasConsecutivas == 0;

        Checar($"39. corrida 5a falha MFA x acerto termina em um estado consistente " +
               $"(A={casoA}, B={casoB}; token={(token is null ? "nenhum" : "emitido")}, " +
               $"falhas={estado.MfaFalhasConsecutivas}, nivel={estado.MfaNivelDeBloqueio})",
            casoA || casoB);
    }

    // ------------------------------------------------------------ cenario 40
    // Concorrencia real (SQL Server): o MESMO codigo TOTP, no MESMO desafio,
    // apresentado duas vezes ao mesmo tempo — exatamente um vence (a outra cai
    // no anti-replay OU no consumo do desafio, tanto faz qual).

    {
        var (id, email, segredo, _) = await NovaContaComMfaAtivo("mesmo totp concorrente");
        var etapa1 = await EtapaUm(email, SenhaBoa);
        var codigo = CodigoTotp(segredo);

        var resultados = await Task.WhenAll(
            EtapaDois(etapa1.Desafio!.Desafio, codigo),
            EtapaDois(etapa1.Desafio!.Desafio, codigo));

        Checar("40. mesmo TOTP simultaneo no mesmo desafio: exatamente 1 token emitido",
            resultados.Count(t => t is not null) == 1);
    }

    // ------------------------------------------------------------ cenario 41
    // Concorrencia real: o MESMO codigo de recuperacao usado por duas
    // requisicoes simultaneas — exatamente uma vence.

    {
        var (id, email, segredo, codigos) = await NovaContaComMfaAtivo("mesmo recovery concorrente");
        var etapa1a = await EtapaUm(email, SenhaBoa);
        var etapa1b = await EtapaUm(email, SenhaBoa);
        var codigoDeRecuperacao = codigos[0];

        var resultados = await Task.WhenAll(
            EtapaDois(etapa1a.Desafio!.Desafio, codigoDeRecuperacao),
            EtapaDois(etapa1b.Desafio!.Desafio, codigoDeRecuperacao));

        Checar("41. mesmo codigo de recuperacao simultaneo (2 desafios): exatamente 1 token emitido",
            resultados.Count(t => t is not null) == 1);
    }

    // ------------------------------------------------------------ cenario 42
    // Concorrencia real: o MESMO desafio consumido por dois pedidos
    // simultaneos com o codigo certo — exatamente um JWT sai.

    {
        var (id, email, segredo, _) = await NovaContaComMfaAtivo("mesmo desafio concorrente");
        var etapa1 = await EtapaUm(email, SenhaBoa);
        var codigo = CodigoTotp(segredo);

        var resultados = await Task.WhenAll(
            EtapaDois(etapa1.Desafio!.Desafio, codigo),
            EtapaDois(etapa1.Desafio!.Desafio, codigo),
            EtapaDois(etapa1.Desafio!.Desafio, codigo));

        Checar("42. mesmo desafio disputado por 3 requisicoes simultaneas: exatamente 1 JWT",
            resultados.Count(t => t is not null) == 1);
    }

    // ------------------------------------------------------------ cenario 43
    // Codigo de recuperacao funciona no login e fica marcado como usado —
    // reuso e recusado.

    {
        var (id, email, segredo, codigos) = await NovaContaComMfaAtivo("recovery uso unico");
        var etapa1a = await EtapaUm(email, SenhaBoa);

        Checar("43a. codigo de recuperacao entra no login",
            await EtapaDois(etapa1a.Desafio!.Desafio, codigos[1]) is not null);

        var etapa1b = await EtapaUm(email, SenhaBoa);
        Checar("43b. o MESMO codigo de recuperacao nao entra de novo",
            await EtapaDois(etapa1b.Desafio!.Desafio, codigos[1]) is null);
    }

    // ------------------------------------------------------------ cenario 44
    // JWT roubado sozinho nao troca autenticador, nao desativa e nao regenera
    // codigos — todas exigem senha + fator atual (step-up).

    {
        var (id, email, segredo, codigos) = await NovaContaComMfaAtivo("jwt sozinho nao muda mfa");
        var identidade = new IdentidadeParaMfa(tenantId, id);

        await using var db = new CrmDbContext(opcoes);
        var mfa = NovoMfaService(db);

        async Task<bool> Recusado(Func<Task> acao)
        {
            try { await acao(); return false; }
            catch (UsuariosServiceException) { return true; }
        }

        Checar("44a. iniciar troca de autenticador SEM CodigoAtual e recusado",
            await Recusado(() => mfa.IniciarAsync(identidade, new IniciarMfaRequest(SenhaBoa, null), null, default)));

        Checar("44b. desativar SEM senha/codigo certos e recusado",
            await Recusado(() => mfa.DesativarAsync(identidade, new DesativarMfaRequest("senha-errada", "000000"), null, default)));

        Checar("44c. regenerar codigos SEM senha/codigo certos e recusado",
            await Recusado(() => mfa.RegenerarCodigosAsync(identidade, new RegenerarCodigosRequest("senha-errada", "000000"), null, default)));

        var situacao = await mfa.ObterSituacaoAsync(identidade, default);
        Checar("44d. MFA continua ativo depois das tres tentativas recusadas", situacao.Ativo);
    }

    // ------------------------------------------------------------ cenario 45
    // Politica obrigatoria: o proprio usuario NAO consegue desativar o MFA
    // quando a politica exige para o perfil/cargo dele.

    {
        var (id, email, segredo, codigos) = await NovaContaComMfaAtivo("nao desativa sob obrigatoriedade");

        await using (var db = new CrmDbContext(opcoes))
        {
            await db.Tenants.Where(t => t.Id == tenantId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.MfaObrigatorioParaAdministradores, true));
        }

        try
        {
            await using var db = new CrmDbContext(opcoes);
            var identidade = new IdentidadeParaMfa(tenantId, id);
            UsuariosServiceException? falha = null;

            try
            {
                await NovoMfaService(db).DesativarAsync(
                    identidade, new DesativarMfaRequest(SenhaBoa, CodigoTotp(segredo)), null, default);
            }
            catch (UsuariosServiceException ex) { falha = ex; }

            Checar("45. desativar MFA sob politica obrigatoria e recusado com o codigo certo",
                falha?.Codigo == "mfa_obrigatorio_nao_pode_ser_desativado");
        }
        finally
        {
            await using var db = new CrmDbContext(opcoes);
            await db.Tenants.Where(t => t.Id == tenantId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.MfaObrigatorioParaAdministradores, false));
        }
    }

    // ------------------------------------------------------------ cenario 46
    // Reset administrativo: remove MFA do alvo, sobe TokenVersion, apaga
    // codigos de recuperacao — a conta exige NOVA matricula.

    {
        var (id, email, segredo, codigos) = await NovaContaComMfaAtivo("reset exige nova matricula");
        var (adminId, _, adminSegredo, _) = await NovaContaComMfaAtivo("admin que reseta (46)");
        var versaoAntes = (await Ler(id)).TokenVersion;

        await using (var db = new CrmDbContext(opcoes))
        {
            await NovoMfaService(db).ResetarAsync(
                new IdentidadeParaMfa(tenantId, adminId), id,
                new ResetarMfaRequest(SenhaBoa, CodigoTotp(adminSegredo), "perdeu o celular"), null, default);
        }

        var estado = await Ler(id);
        Checar("46a. reset desliga MfaAtivo e limpa o segredo",
            estado is { MfaAtivo: false, MfaSegredoCifrado: null });
        Checar("46b. reset sobe TokenVersion", estado.TokenVersion > versaoAntes);

        await using (var db2 = new CrmDbContext(opcoes))
        {
            Checar("46c. codigos de recuperacao anteriores foram apagados",
                !await db2.CodigosDeRecuperacaoMfa.AnyAsync(c => c.AtendenteId == id));
        }

        // A conta volta a logar em uma etapa (MFA nao esta mais ativo) — "exige
        // nova matricula" na pratica: sem isto, precisaria escanear QR de novo.
        Checar("46d. login pos-reset e de UMA etapa (sem MFA ate nova matricula)",
            (await EtapaUm(email, SenhaBoa)).Sessao is not null);
    }

    // ------------------------------------------------------------ cenario 47
    // Titular protegido: Administrador comum nao reseta MFA de Titular;
    // Administrador que TAMBEM e Titular consegue.

    {
        var (idTitular, _, segredoTitular, _) = await NovaContaComMfaAtivo(
            "titular protegido", cargo: CargoNaServentia.Titular);
        var (idAdminComum, _, segredoAdminComum, _) = await NovaContaComMfaAtivo("admin comum (47)");
        var (idAdminTitular, _, segredoAdminTitular, _) = await NovaContaComMfaAtivo(
            "admin+titular (47)", cargo: CargoNaServentia.Titular);

        await using (var db = new CrmDbContext(opcoes))
        {
            UsuariosServiceException? falha = null;
            try
            {
                await NovoMfaService(db).ResetarAsync(
                    new IdentidadeParaMfa(tenantId, idAdminComum), idTitular,
                    new ResetarMfaRequest(SenhaBoa, CodigoTotp(segredoAdminComum), "teste"), null, default);
            }
            catch (UsuariosServiceException ex) { falha = ex; }

            Checar("47a. Administrador comum NAO reseta MFA de Titular",
                falha?.Codigo == "apenas_titular_reseta_titular");
        }

        await using (var db = new CrmDbContext(opcoes))
        {
            await NovoMfaService(db).ResetarAsync(
                new IdentidadeParaMfa(tenantId, idAdminTitular), idTitular,
                new ResetarMfaRequest(SenhaBoa, CodigoTotp(segredoAdminTitular), "teste"), null, default);
        }

        Checar("47b. Administrador que TAMBEM e Titular consegue resetar",
            !(await Ler(idTitular)).MfaAtivo);
    }

    // ------------------------------------------------------------ cenario 48
    // Matricula privilegiada SEM ticket (ou com ticket invalido) e recusada.

    {
        var (id, email) = await NovaContaPrivilegiada("matricula sem ticket");

        await using var db = new CrmDbContext(opcoes);
        UsuariosServiceException? falha = null;
        try
        {
            await NovoMfaService(db).IniciarMatriculaPrivilegiadaAsync(
                new MatricularPrivilegiadoRequest(email, SenhaBoa, "ticket-que-nao-existe"), null, default);
        }
        catch (UsuariosServiceException ex) { falha = ex; }

        Checar("48. matricula privilegiada sem ticket valido e recusada",
            falha?.Codigo == "ticket_invalido");
    }

    // ------------------------------------------------------------ cenario 49
    // Ticket: uso unico, vinculado ao AtendenteId certo, respeita expiracao.

    {
        var (idAlvo, emailAlvo) = await NovaContaPrivilegiada("ticket uso unico");
        var (idOutro, emailOutro) = await NovaContaPrivilegiada("ticket vinculo errado");

        Guid tenantDoTicket; int tokenVersionDoAlvo;
        await using (var db = new CrmDbContext(opcoes))
        {
            var alvo = await db.Atendentes.AsNoTracking().SingleAsync(a => a.Id == idAlvo);
            tenantDoTicket = alvo.TenantId;
            tokenVersionDoAlvo = alvo.TokenVersion;
        }

        var ticketEmClaro = SegredosAleatoriosMfa.NovoToken();
        await using (var db = new CrmDbContext(opcoes))
        {
            db.DesafiosDeMfa.Add(new DesafioDeMfa
            {
                TenantId = tenantDoTicket,
                AtendenteId = idAlvo,
                HashDoDesafio = SegredosAleatoriosMfa.Sha256Hex(ticketEmClaro),
                Tipo = TipoDeDesafioMfa.Ticket,
                TokenVersionNoDesafio = tokenVersionDoAlvo,
                ExpiraEm = DateTime.UtcNow.AddHours(24)
            });
            await db.SaveChangesAsync();
        }

        await using (var db = new CrmDbContext(opcoes))
        {
            UsuariosServiceException? falha = null;
            try
            {
                await NovoMfaService(db).IniciarMatriculaPrivilegiadaAsync(
                    new MatricularPrivilegiadoRequest(emailOutro, SenhaBoa, ticketEmClaro), null, default);
            }
            catch (UsuariosServiceException ex) { falha = ex; }

            Checar("49a. ticket emitido para OUTRA conta nao serve",
                falha?.Codigo == "ticket_invalido");
        }

        await using (var db = new CrmDbContext(opcoes))
        {
            var desafio = await NovoMfaService(db).IniciarMatriculaPrivilegiadaAsync(
                new MatricularPrivilegiadoRequest(emailAlvo, SenhaBoa, ticketEmClaro), null, default);

            Checar("49b. ticket vinculado ao AtendenteId certo funciona", desafio.Desafio.Length > 0);
        }

        await using (var db = new CrmDbContext(opcoes))
        {
            UsuariosServiceException? falha = null;
            try
            {
                await NovoMfaService(db).IniciarMatriculaPrivilegiadaAsync(
                    new MatricularPrivilegiadoRequest(emailAlvo, SenhaBoa, ticketEmClaro), null, default);
            }
            catch (UsuariosServiceException ex) { falha = ex; }

            Checar("49c. o MESMO ticket usado de novo e recusado (uso unico)",
                falha?.Codigo == "ticket_invalido");
        }
    }

    // ------------------------------------------------------------ cenario 50
    // Fluxo completo de matricula privilegiada por ticket: sessao (ticket) ->
    // iniciar autenticador -> confirmar -> sessao completa emitida.

    {
        var (id, email) = await NovaContaPrivilegiada("matricula completa via ticket");

        int tokenVersion;
        await using (var db = new CrmDbContext(opcoes))
        {
            tokenVersion = (await db.Atendentes.AsNoTracking().SingleAsync(a => a.Id == id)).TokenVersion;
        }

        var ticketEmClaro = SegredosAleatoriosMfa.NovoToken();
        await using (var db = new CrmDbContext(opcoes))
        {
            db.DesafiosDeMfa.Add(new DesafioDeMfa
            {
                TenantId = tenantId,
                AtendenteId = id,
                HashDoDesafio = SegredosAleatoriosMfa.Sha256Hex(ticketEmClaro),
                Tipo = TipoDeDesafioMfa.Ticket,
                TokenVersionNoDesafio = tokenVersion,
                ExpiraEm = DateTime.UtcNow.AddHours(24)
            });
            await db.SaveChangesAsync();
        }

        DesafioMfaResponse desafioDeMatricula;
        await using (var db = new CrmDbContext(opcoes))
        {
            desafioDeMatricula = await NovoMfaService(db).IniciarMatriculaPrivilegiadaAsync(
                new MatricularPrivilegiadoRequest(email, SenhaBoa, ticketEmClaro), null, default);
        }

        IniciarMfaResponse iniciado;
        await using (var db = new CrmDbContext(opcoes))
        {
            iniciado = await NovoMfaService(db).IniciarViaDesafioAsync(
                new IniciarViaDesafioRequest(desafioDeMatricula.Desafio), default);
        }

        ConfirmarMfaResponse confirmado;
        await using (var db = new CrmDbContext(opcoes))
        {
            confirmado = await NovoMfaService(db).ConfirmarViaDesafioAsync(
                new ConfirmarViaDesafioRequest(desafioDeMatricula.Desafio, CodigoTotp(iniciado.SegredoBase32)), null, default);
        }

        Checar("50a. matricula via ticket devolve sessao completa (JWT)",
            !string.IsNullOrEmpty(confirmado.Sessao.Token));
        Checar("50b. matricula via ticket devolve 10 codigos de recuperacao",
            confirmado.CodigosDeRecuperacao.Count == 10);

        var estado = await Ler(id);
        Checar("50c. MFA fica ativo apos a matricula via ticket", estado.MfaAtivo);

        await using (var db = new CrmDbContext(opcoes))
        {
            UsuariosServiceException? falha = null;
            try
            {
                await NovoMfaService(db).IniciarViaDesafioAsync(
                    new IniciarViaDesafioRequest(desafioDeMatricula.Desafio), default);
            }
            catch (UsuariosServiceException ex) { falha = ex; }

            Checar("50d. o desafio de matricula ja consumido nao serve mais",
                falha?.Codigo == "desafio_mfa_invalido");
        }
    }

    // ------------------------------------------------------------ cenario 51
    // Flip da politica ABORTA se houver Administrador/Titular sem MFA no
    // tenant — usa o tenant COMPARTILHADO de proposito: ele ja tem
    // Administradores reais sem MFA, e e exatamente isso que precisa travar o
    // flip. Nao muta nada alem do step-up do proprio ator.

    {
        var (idAtor, _, segredoAtor, _) = await NovaContaComMfaAtivo("ator do flip abortado");

        await using var db = new CrmDbContext(opcoes);
        UsuariosServiceException? falha = null;
        try
        {
            await NovoMfaService(db).AtivarObrigatoriedadeAsync(
                new IdentidadeParaMfa(tenantId, idAtor),
                new AtivarObrigatoriedadeRequest(SenhaBoa, CodigoTotp(segredoAtor), "teste automatizado"), null, default);
        }
        catch (UsuariosServiceException ex) { falha = ex; }

        Checar("51. flip aborta quando ha Administrador/Titular sem MFA (tenant real ja tem)",
            falha?.Codigo == "politica_mfa_contas_pendentes"
            && falha.Detalhes.TryGetValue("contasPendentes", out var pendentes)
            && pendentes is IReadOnlyList<string> lista && lista.Count > 0);

        await using var confirmacao = new CrmDbContext(opcoes);
        Checar("51b. a politica CONTINUA desligada apos o aborto",
            !await confirmacao.Tenants.AsNoTracking()
                .Where(t => t.Id == tenantId).Select(t => t.MfaObrigatorioParaAdministradores).SingleAsync());
    }

    // ------------------------------------------------------------ cenario 52
    // Flip bem-sucedido precisa de tenant ISOLADO — o compartilhado nunca vai
    // ter TODOS os Administradores/Titulares matriculados so para este teste.

    {
        Guid tenantIsolado;
        await using (var db = new CrmDbContext(opcoes))
        {
            var tenant = new Tenant
            {
                RazaoSocial = "[TESTE SEC-03] Tenant isolado — flip",
                Cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString()
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            tenantIsolado = tenant.Id;
        }
        tenantIdsDeTeste.Add(tenantIsolado);

        // CORRECAO (bloqueador 1): igual a NovaContaComMfaAtivo, matricula
        // PRIVILEGIADA so pelo fluxo REAL de ticket — nunca IniciarAsync direto.
        // Ticket montado aqui (nao via EmitirTicketCruAsync) porque este
        // cenario usa um tenant ISOLADO, diferente do `tenantId` que aquele
        // helper fecha por closure.
        async Task<(Guid Id, string Email, string Segredo)> ContaMatriculadaNoIsolado(string rotulo)
        {
            await using var db = new CrmDbContext(opcoes);
            var conta = new Atendente
            {
                TenantId = tenantIsolado,
                Nome = $"[TESTE SEC-03] {rotulo}",
                Email = $"teste-sec-03-isolado-{Guid.NewGuid():N}@teste.local",
                SenhaHash = hash.Gerar(SenhaBoa),
                Perfil = PerfilDeAtendente.Administrador,
                Ativo = true
            };
            db.Atendentes.Add(conta);

            var tokenEmClaro = SegredosAleatoriosMfa.NovoToken();
            db.DesafiosDeMfa.Add(new DesafioDeMfa
            {
                TenantId = tenantIsolado,
                AtendenteId = conta.Id,
                HashDoDesafio = SegredosAleatoriosMfa.Sha256Hex(tokenEmClaro),
                Tipo = TipoDeDesafioMfa.Ticket,
                TokenVersionNoDesafio = conta.TokenVersion,
                ExpiraEm = DateTime.UtcNow.AddHours(24)
            });
            await db.SaveChangesAsync();
            ids.Add(conta.Id);

            var mfa = NovoMfaService(db);
            var sessaoDeMatricula = await mfa.IniciarMatriculaPrivilegiadaAsync(
                new MatricularPrivilegiadoRequest(conta.Email, SenhaBoa, tokenEmClaro), null, default);
            var iniciado = await mfa.IniciarViaDesafioAsync(
                new IniciarViaDesafioRequest(sessaoDeMatricula.Desafio), default);
            // Mesmo motivo de NovaContaComMfaAtivo: confirma no passo -1 para
            // deixar o passo "atual" (deslocamento 0) livre para o proximo uso.
            await mfa.ConfirmarViaDesafioAsync(
                new ConfirmarViaDesafioRequest(sessaoDeMatricula.Desafio, CodigoTotp(iniciado.SegredoBase32, -1)),
                null, default);

            return (conta.Id, conta.Email, iniciado.SegredoBase32);
        }

        var ator = await ContaMatriculadaNoIsolado("ator do flip");
        var outroAdmin = await ContaMatriculadaNoIsolado("outro admin afetado");

        var tokenVersionAtorAntes = (await Ler(ator.Id)).TokenVersion;
        var tokenVersionOutroAntes = (await Ler(outroAdmin.Id)).TokenVersion;

        LoginResponse sessaoDoFlip;
        await using (var db = new CrmDbContext(opcoes))
        {
            sessaoDoFlip = await NovoMfaService(db).AtivarObrigatoriedadeAsync(
                new IdentidadeParaMfa(tenantIsolado, ator.Id),
                new AtivarObrigatoriedadeRequest(SenhaBoa, CodigoTotp(ator.Segredo), "todos matriculados — teste"), null, default);
        }

        await using (var db = new CrmDbContext(opcoes))
        {
            Checar("52a. flip liga a politica quando todos estao matriculados",
                await db.Tenants.AsNoTracking().Where(t => t.Id == tenantIsolado)
                    .Select(t => t.MfaObrigatorioParaAdministradores).SingleAsync());
        }

        Checar("52b. flip devolve sessao NOVA para o proprio ator",
            !string.IsNullOrEmpty(sessaoDoFlip.Token));

        var tokenVersionAtorDepois = (await Ler(ator.Id)).TokenVersion;
        var tokenVersionOutroDepois = (await Ler(outroAdmin.Id)).TokenVersion;

        Checar("52c. TokenVersion do proprio ator subiu",
            tokenVersionAtorDepois > tokenVersionAtorAntes);
        Checar("52d. TokenVersion de OUTRO administrador afetado tambem subiu (revogacao em massa)",
            tokenVersionOutroDepois > tokenVersionOutroAntes);

        await using (var db = new CrmDbContext(opcoes))
        {
            Checar("52e. sessao ANTIGA do outro administrador (TokenVersion velha) nao vale mais",
                await NovoValidador(db).CarregarSessaoAsync(outroAdmin.Id, tenantIsolado, tokenVersionOutroAntes) is null);
        }

        // ------------------------------------------------------------ cenario 53
        // Administrador criado DEPOIS do flip nao ganha sessao sem MFA — cai
        // exatamente no mesmo caminho de MfaObrigatorioNaoMatriculado.

        Guid idNovoAdmin; string emailNovoAdmin;
        await using (var db = new CrmDbContext(opcoes))
        {
            var novo = new Atendente
            {
                TenantId = tenantIsolado,
                Nome = "[TESTE SEC-03] admin criado pos-flip",
                Email = $"teste-sec-03-posflip-{Guid.NewGuid():N}@teste.local",
                SenhaHash = hash.Gerar(SenhaBoa),
                Perfil = PerfilDeAtendente.Administrador,
                Ativo = true
            };
            db.Atendentes.Add(novo);
            await db.SaveChangesAsync();
            ids.Add(novo.Id);
            idNovoAdmin = novo.Id;
            emailNovoAdmin = novo.Email;
        }

        await using (var db = new CrmDbContext(opcoes))
        {
            UsuariosServiceException? falha = null;
            try
            {
                await NovoAutenticacao(db, Guid.NewGuid()).AutenticarAsync(new LoginRequest(emailNovoAdmin, SenhaBoa));
            }
            catch (UsuariosServiceException ex) { falha = ex; }

            Checar("53. Administrador criado DEPOIS do flip nao entra sem matricula de MFA",
                falha?.Codigo == "mfa_obrigatorio_nao_matriculado");
        }
    }

    // ------------------------------------------------------------ cenario 54
    // Cifra do segredo TOTP: ida/volta, AAD por conta, chave errada falha
    // fechado, nonce aleatorio produz ciphertexts diferentes.

    {
        var tenantA = Guid.NewGuid();
        var contaA = Guid.NewGuid();
        var contaB = Guid.NewGuid();
        const string segredo = "JBSWY3DPEHPK3PXP";

        var cifrado1 = cifra.Cifrar(segredo, tenantA, contaA);
        var cifrado2 = cifra.Cifrar(segredo, tenantA, contaA);

        Checar("54a. decifrar com o MESMO tenant/conta devolve o segredo original",
            cifra.Decifrar(cifrado1, tenantA, contaA) == segredo);

        Checar("54b. dois cifrados do MESMO segredo sao diferentes (nonce aleatorio)",
            !cifrado1.SequenceEqual(cifrado2));

        var decifrouComContaErrada = false;
        try { cifra.Decifrar(cifrado1, tenantA, contaB); decifrouComContaErrada = true; }
        catch (CryptographicException) { /* esperado */ }

        Checar("54c. decifrar com AAD de OUTRA conta falha (nunca decifra por engano)",
            !decifrouComContaErrada);

        var cifraComChaveErrada = new CifraDeSegredoMfa(Options.Create(new OpcoesDeMfa
        {
            VersaoCorrente = "1",
            Chaves = new Dictionary<string, string> { ["1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }
        }));

        var decifrouComChaveErrada = false;
        try { cifraComChaveErrada.Decifrar(cifrado1, tenantA, contaA); decifrouComChaveErrada = true; }
        catch (CryptographicException) { /* esperado — fail closed */ }

        Checar("54d. decifrar com chave ERRADA falha fechado (CryptographicException, sem vazar dado)",
            !decifrouComChaveErrada);
    }

    // =====================================================================
    // Revisao independente do PR #46 — bloqueadores 1, 2, 3 e testes que faltavam
    // =====================================================================

    // ------------------------------------------------------------ cenario 55
    // MUDANCA DE REGRA DE PRODUTO (pos-homologacao do SEC-03 em TESTE):
    // qualquer perfil — INCLUSIVE Titular e Administrador — pode ativar o MFA
    // da propria conta voluntariamente pelo fluxo autenticado comum (JWT +
    // senha). O bloqueador 1 original (privilegiado sem MFA sempre recusado
    // aqui) foi revogado deliberadamente; o que sobrevive dele e testado no
    // cenario 81 (rematricula obrigatoria pos-emergencia).

    {
        var (idAdmin, emailAdmin) = await NovaContaPrivilegiada("cenario55 - admin sem mfa");
        var (idTitular, emailTitular) = await NovaContaPrivilegiada(
            "cenario55 - titular sem mfa", perfil: PerfilDeAtendente.Atendente, cargo: CargoNaServentia.Titular);
        var (idComum, emailComum) = await NovaContaPrivilegiada(
            "cenario55 - nao privilegiado", perfil: PerfilDeAtendente.Atendente);

        async Task<(string? Codigo, IniciarMfaResponse? Resposta)> TentarIniciarComJwtESenha(Guid id, string senha)
        {
            await using var db = new CrmDbContext(opcoes);
            try
            {
                var resposta = await NovoMfaService(db).IniciarAsync(
                    new IdentidadeParaMfa(tenantId, id), new IniciarMfaRequest(senha, null), null, default);
                return (null, resposta);
            }
            catch (UsuariosServiceException ex)
            {
                return (ex.Codigo, null);
            }
        }

        // 1/2/3 da lista pedida: comum, Administrador e Titular sem MFA
        // conseguem iniciar a ativacao voluntaria — nenhum dos tres e
        // recusado por privilegio.
        var (codigoAdmin, iniciadoAdmin) = await TentarIniciarComJwtESenha(idAdmin, SenhaBoa);
        Checar("55a. Administrador sem MFA + JWT + senha correta em /mfa/iniciar -> SUCESSO (ativacao voluntaria permitida)",
            codigoAdmin is null && iniciadoAdmin is not null);

        var (codigoTitular, iniciadoTitular) = await TentarIniciarComJwtESenha(idTitular, SenhaBoa);
        Checar("55b. Titular (via Cargo) sem MFA + JWT + senha correta em /mfa/iniciar -> SUCESSO (ativacao voluntaria permitida)",
            codigoTitular is null && iniciadoTitular is not null);

        var (codigoComum, iniciadoComum) = await TentarIniciarComJwtESenha(idComum, SenhaBoa);
        Checar("55c. conta NAO privilegiada sem MFA: ativacao voluntaria continua funcionando",
            codigoComum is null && iniciadoComum is not null);

        // 4 da lista pedida: senha errada continua bloqueando os tres,
        // independente de privilegio — a mudanca de regra so afeta a trava
        // de privilegio, nunca a exigencia de senha correta.
        Checar("55d. senha ERRADA bloqueia o Administrador do mesmo jeito (nao vira atalho)",
            (await TentarIniciarComJwtESenha(idAdmin, SenhaRuim)).Codigo == "senha_atual_incorreta");
        Checar("55e. senha ERRADA bloqueia o Titular do mesmo jeito",
            (await TentarIniciarComJwtESenha(idTitular, SenhaRuim)).Codigo == "senha_atual_incorreta");
        Checar("55f. senha ERRADA bloqueia a conta comum do mesmo jeito",
            (await TentarIniciarComJwtESenha(idComum, SenhaRuim)).Codigo == "senha_atual_incorreta");

        // Nenhuma das tres contas foi afetada pelas tentativas com senha errada.
        Checar("55g. as tres contas continuam com MfaAtivo=false apos as tentativas com senha errada",
            !(await Ler(idAdmin)).MfaAtivo && !(await Ler(idTitular)).MfaAtivo && !(await Ler(idComum)).MfaAtivo);

        // A ativacao voluntaria nao para em "iniciar" — o Administrador e o
        // Titular tem que conseguir CONCLUIR de verdade: confirmar com o TOTP
        // correto, MfaAtivo vira true, 10 codigos de recuperacao emitidos, e
        // TODAS as protecoes de sempre (anti-replay, TokenVersion) continuam
        // valendo — nada disto foi simplificado.
        await using (var db = new CrmDbContext(opcoes))
        {
            var confirmadoAdmin = await NovoMfaService(db).ConfirmarAsync(
                new IdentidadeParaMfa(tenantId, idAdmin),
                new ConfirmarMfaRequest(CodigoTotp(iniciadoAdmin!.SegredoBase32)), null, default);

            Checar("55h. Administrador CONCLUI a ativacao voluntaria (10 codigos de recuperacao emitidos)",
                confirmadoAdmin.CodigosDeRecuperacao.Count == 10);
        }
        Checar("55i. Administrador esta com MfaAtivo=true apos a confirmacao voluntaria",
            (await Ler(idAdmin)).MfaAtivo);

        await using (var db = new CrmDbContext(opcoes))
        {
            var confirmadoTitular = await NovoMfaService(db).ConfirmarAsync(
                new IdentidadeParaMfa(tenantId, idTitular),
                new ConfirmarMfaRequest(CodigoTotp(iniciadoTitular!.SegredoBase32)), null, default);

            Checar("55j. Titular CONCLUI a ativacao voluntaria (10 codigos de recuperacao emitidos)",
                confirmadoTitular.CodigosDeRecuperacao.Count == 10);
        }
        Checar("55k. Titular esta com MfaAtivo=true apos a confirmacao voluntaria",
            (await Ler(idTitular)).MfaAtivo);

        // Depois de ativo, uma nova "primeira confirmacao" (sem segredo
        // pendente, porque o anterior ja foi consumido) nao encontra nada
        // para confirmar — o anti-replay de troca de autenticador (exige
        // CodigoAtual/step-up) e coberto pelos cenarios 27 e 44, que este
        // cenario nao duplica.
        await using (var db = new CrmDbContext(opcoes))
        {
            UsuariosServiceException? falhaSemSegredoPendente = null;
            try
            {
                await NovoMfaService(db).ConfirmarAsync(
                    new IdentidadeParaMfa(tenantId, idAdmin),
                    new ConfirmarMfaRequest(CodigoTotp(iniciadoAdmin!.SegredoBase32)), null, default);
            }
            catch (UsuariosServiceException ex) { falhaSemSegredoPendente = ex; }

            Checar("55l. depois de ativo, confirmar de novo sem segredo pendente falha (nao ha reativacao gratuita)",
                falhaSemSegredoPendente?.Codigo == "codigo_mfa_invalido");
        }
    }

    // ------------------------------------------------------------ cenario 56
    // BLOQUEADOR 2 — o consumo do ticket reconfirma TUDO atomicamente contra o
    // estado ATUAL do atendente, nunca um valor lido antes em memoria.

    async Task<string?> TentarMatricularComTicket(string email, string ticket, string? senha = null)
    {
        await using var db = new CrmDbContext(opcoes);
        try
        {
            await NovoMfaService(db).IniciarMatriculaPrivilegiadaAsync(
                new MatricularPrivilegiadoRequest(email, senha ?? SenhaBoa, ticket), null, default);
            return null;
        }
        catch (UsuariosServiceException ex)
        {
            return ex.Codigo;
        }
    }

    {
        // 56a — ticket de OUTRO tenant nao funciona.
        var (idAlvo, emailAlvo) = await NovaContaPrivilegiada("bloqueador2 - outro tenant");

        Guid tenantEstranho;
        await using (var db = new CrmDbContext(opcoes))
        {
            var tenant = new Tenant
            {
                RazaoSocial = "[TESTE SEC-03] Tenant estranho ao ticket",
                Cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString()
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            tenantEstranho = tenant.Id;
        }
        tenantIdsDeTeste.Add(tenantEstranho);

        var tokenVersionAlvo = (await Ler(idAlvo)).TokenVersion;
        var tokenDeOutroTenant = SegredosAleatoriosMfa.NovoToken();
        await using (var db = new CrmDbContext(opcoes))
        {
            db.DesafiosDeMfa.Add(new DesafioDeMfa
            {
                TenantId = tenantEstranho, // TENANT ERRADO de proposito
                AtendenteId = idAlvo,
                HashDoDesafio = SegredosAleatoriosMfa.Sha256Hex(tokenDeOutroTenant),
                Tipo = TipoDeDesafioMfa.Ticket,
                TokenVersionNoDesafio = tokenVersionAlvo,
                ExpiraEm = DateTime.UtcNow.AddHours(24)
            });
            await db.SaveChangesAsync();
        }

        Checar("56a. ticket gravado com TenantId de OUTRO tenant nao funciona",
            await TentarMatricularComTicket(emailAlvo, tokenDeOutroTenant) == "ticket_invalido");

        // 56b — ticket expirado nao funciona.
        var (idExpirado, emailExpirado) = await NovaContaPrivilegiada("bloqueador2 - expirado");
        var tokenVersionExpirado = (await Ler(idExpirado)).TokenVersion;
        var ticketExpirado = await EmitirTicketCruAsync(idExpirado, tokenVersionExpirado, ttl: TimeSpan.FromSeconds(-1));

        Checar("56b. ticket ja expirado nao funciona",
            await TentarMatricularComTicket(emailExpirado, ticketExpirado) == "ticket_invalido");

        // 56c — senha alterada depois da emissao do ticket -> ticket antigo recusado.
        var (idSenha, emailSenha) = await NovaContaPrivilegiada("bloqueador2 - senha trocada");
        var tokenVersionSenha = (await Ler(idSenha)).TokenVersion;
        var ticketComSenhaAntiga = await EmitirTicketCruAsync(idSenha, tokenVersionSenha);

        await using (var db = new CrmDbContext(opcoes))
        {
            await NovoAutenticacao(db, idSenha).AlterarSenhaAsync(new AlterarSenhaRequest(SenhaBoa, "SenhaTrocada999xy"));
        }

        // Usa a senha NOVA (senha certa, ticket velho) — isola o motivo: o
        // ticket falha por TokenVersion desatualizado, nao por senha errada.
        Checar("56c. ticket emitido ANTES da troca de senha e recusado depois",
            await TentarMatricularComTicket(emailSenha, ticketComSenhaAntiga, senha: "SenhaTrocada999xy") == "ticket_invalido");

        // 56d — TokenVersion sobe por outro motivo (Perfil mudou), mantendo a
        // conta privilegiada (via Cargo=Titular) -> ticket antigo recusado.
        var (idPerfil, emailPerfil) = await NovaContaPrivilegiada(
            "bloqueador2 - perfil mudou", perfil: PerfilDeAtendente.Atendente, cargo: CargoNaServentia.Titular);
        var tokenVersionPerfil = (await Ler(idPerfil)).TokenVersion;
        var ticketComPerfilAntigo = await EmitirTicketCruAsync(idPerfil, tokenVersionPerfil);

        await using (var db = new CrmDbContext(opcoes))
        {
            await NovoAtendenteService(db, Guid.NewGuid()).AtualizarAsync(idPerfil,
                new AtualizarAtendenteRequest("Nome", emailPerfil, PerfilDeAtendente.Supervisor, null, true, CargoNaServentia.Titular));
        }

        var estadoPerfil = await Ler(idPerfil);
        Checar("56d-preparo. mudar SO o Perfil sobe TokenVersion (a conta continua privilegiada via Cargo)",
            estadoPerfil.TokenVersion > tokenVersionPerfil
            && (estadoPerfil.Perfil == PerfilDeAtendente.Supervisor)
            && estadoPerfil.Cargo == CargoNaServentia.Titular);

        Checar("56d. TokenVersion subiu por OUTRO motivo (Perfil) -> ticket antigo recusado",
            await TentarMatricularComTicket(emailPerfil, ticketComPerfilAntigo) == "ticket_invalido");

        // 56e — conta desativada depois da emissao -> ticket recusado.
        var (idInativo, emailInativo) = await NovaContaPrivilegiada("bloqueador2 - desativada depois");
        var tokenVersionInativo = (await Ler(idInativo)).TokenVersion;
        var ticketDeContaQueSeraDesativada = await EmitirTicketCruAsync(idInativo, tokenVersionInativo);

        await using (var db = new CrmDbContext(opcoes))
        {
            await NovoAtendenteService(db, Guid.NewGuid()).AtualizarAsync(idInativo,
                new AtualizarAtendenteRequest("Nome", emailInativo, PerfilDeAtendente.Administrador, null, false));
        }

        // O metodo le o atendente de novo no INICIO (nao usa cache) — a
        // pre-checagem de Ativo ja rejeita com credenciais_invalidas (mesmo
        // codigo generico de conta inativa em qualquer outro fluxo, por
        // desenho anti-enumeracao). A re-checagem atomica de Ativo dentro da
        // transacao e defesa em profundidade para a corrida de verdade
        // (desativar ENTRE a pre-checagem e o commit) — nao alcancavel por um
        // teste sequencial como este.
        Checar("56e. conta foi DESATIVADA depois da emissao -> ticket recusado",
            await TentarMatricularComTicket(emailInativo, ticketDeContaQueSeraDesativada) == "credenciais_invalidas");

        // 56f — conta deixa de ser privilegiada por mudanca de CARGO SOZINHA.
        // CORRECAO (revisao independente do PR #46, bloqueador 4): ate aqui,
        // Titular -> Substituto NAO subia TokenVersion (AtendenteService.
        // AtualizarAsync so olhava Perfil/Ativo) — agora sobe, porque a
        // fronteira de privilegio do SEC-03 (Perfil==Administrador ||
        // Cargo==Titular) mudou de lado. Ver cenario 61 para a bateria
        // completa dessa correcao. Aqui o ponto continua sendo o mesmo de
        // antes: mesmo se a checagem de TokenVersion nao pegasse (hipotese
        // que a correcao do bloqueador 4 tornou irreal, mas a re-checagem de
        // "ainda privilegiado" no consumo do ticket existe independente
        // disso, como defesa em profundidade), a conta ja nao privilegiada
        // seria recusada de qualquer forma.
        var (idCargo, emailCargo) = await NovaContaPrivilegiada(
            "bloqueador2 - deixou de ser privilegiado", perfil: PerfilDeAtendente.Atendente, cargo: CargoNaServentia.Titular);
        var tokenVersionCargoAntes = (await Ler(idCargo)).TokenVersion;
        var ticketDeQuemDeixouDeSerTitular = await EmitirTicketCruAsync(idCargo, tokenVersionCargoAntes);

        await using (var db = new CrmDbContext(opcoes))
        {
            await NovoAtendenteService(db, Guid.NewGuid()).AtualizarAsync(idCargo,
                new AtualizarAtendenteRequest("Nome", emailCargo, PerfilDeAtendente.Atendente, null, true, CargoNaServentia.Substituto));
        }

        var estadoCargo = await Ler(idCargo);
        Checar("56f-preparo. mudar Titular -> Substituto CRUZA a fronteira de privilegio e sobe TokenVersion",
            estadoCargo.TokenVersion > tokenVersionCargoAntes && estadoCargo.Cargo == CargoNaServentia.Substituto);

        // A pre-checagem de "privilegiado" le o Cargo ATUAL (Substituto) e
        // rejeita com conta_nao_privilegiada antes de chegar na transacao —
        // dispara antes mesmo de a re-checagem de TokenVersion ser avaliada.
        Checar("56f. conta deixou de ser privilegiada (por Cargo) -> ticket recusado",
            await TentarMatricularComTicket(emailCargo, ticketDeQuemDeixouDeSerTitular) == "conta_nao_privilegiada");

        // 56g — MFA ja foi ativado por outro caminho entre a emissao e o
        // consumo (aqui, via um SEGUNDO ticket) -> o primeiro ticket, com
        // TokenVersion antiga, e recusado.
        var (idDuploTicket, emailDuploTicket) = await NovaContaPrivilegiada("bloqueador2 - mfa ja ativo");
        var tokenVersionDuplo = (await Ler(idDuploTicket)).TokenVersion;
        var primeiroTicket = await EmitirTicketCruAsync(idDuploTicket, tokenVersionDuplo);
        var segundoTicket = await EmitirTicketCruAsync(idDuploTicket, tokenVersionDuplo);

        await using (var db = new CrmDbContext(opcoes))
        {
            var mfa = NovoMfaService(db);
            var sessao = await mfa.IniciarMatriculaPrivilegiadaAsync(
                new MatricularPrivilegiadoRequest(emailDuploTicket, SenhaBoa, segundoTicket), null, default);
            var iniciado = await mfa.IniciarViaDesafioAsync(new IniciarViaDesafioRequest(sessao.Desafio), default);
            await mfa.ConfirmarViaDesafioAsync(
                new ConfirmarViaDesafioRequest(sessao.Desafio, CodigoTotp(iniciado.SegredoBase32, -1)), null, default);
        }

        // Mesma logica: a pre-checagem de MfaAtivo (lida fresca) rejeita com
        // mfa_ja_ativo antes da transacao — a re-checagem atomica de MfaAtivo
        // e defesa em profundidade para a corrida verdadeira (dois tickets
        // consumidos ao mesmo tempo), que o cenario 57 cobre.
        Checar("56g. MFA ja ativo (ativado com outro ticket) -> o PRIMEIRO ticket e recusado",
            await TentarMatricularComTicket(emailDuploTicket, primeiroTicket) == "mfa_ja_ativo");
    }

    // ------------------------------------------------------------ cenario 57
    // BLOQUEADOR 3 — consumo do ticket e criacao do desafio de matricula sao
    // atomicos: sob N requisicoes simultaneas com o MESMO ticket, exatamente
    // uma recebe desafio.

    {
        var (idAlvo, emailAlvo) = await NovaContaPrivilegiada("bloqueador3 - ticket concorrente");
        var tokenVersionAlvo = (await Ler(idAlvo)).TokenVersion;
        var ticket = await EmitirTicketCruAsync(idAlvo, tokenVersionAlvo);

        async Task<bool> TentarConcorrente()
        {
            await using var db = new CrmDbContext(opcoes);
            try
            {
                await NovoMfaService(db).IniciarMatriculaPrivilegiadaAsync(
                    new MatricularPrivilegiadoRequest(emailAlvo, SenhaBoa, ticket), null, default);
                return true;
            }
            catch (UsuariosServiceException)
            {
                return false;
            }
        }

        var resultados = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => TentarConcorrente()));

        Checar("57. mesmo ticket disputado por 5 requisicoes simultaneas: exatamente 1 recebe desafio",
            resultados.Count(r => r) == 1);

        await using (var db = new CrmDbContext(opcoes))
        {
            var quantosDesafiosDeMatricula = await db.DesafiosDeMfa
                .CountAsync(d => d.AtendenteId == idAlvo && d.Tipo == TipoDeDesafioMfa.MatriculaPrivilegiada);

            Checar("57b. nunca existem dois desafios de matricula originados do mesmo ticket",
                quantosDesafiosDeMatricula == 1);
        }
    }

    // ------------------------------------------------------------ cenario 58
    // Reset administrativo SOB POLITICA OBRIGATORIA — fluxo completo: a conta
    // fica sem login ate NOVA matricula via ticket.

    {
        var (idAlvo, emailAlvo, segredoAlvo, _) = await NovaContaComMfaAtivo("reset sob obrigatoriedade");
        var (idAtor, _, segredoAtor, _) = await NovaContaComMfaAtivo("ator do reset sob obrigatoriedade");

        // Liga a politica pelo fluxo REAL — precisa que TODOS os
        // Administrador/Titular ativos do tenant compartilhado tenham MFA, o
        // que nao e o caso (contas reais de demonstracao). Entao este cenario
        // roda num tenant ISOLADO, como o 52.
        Guid tenantIsolado;
        await using (var db = new CrmDbContext(opcoes))
        {
            var tenant = new Tenant
            {
                RazaoSocial = "[TESTE SEC-03] Tenant isolado — reset sob obrigatoriedade",
                Cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString()
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            tenantIsolado = tenant.Id;
        }
        tenantIdsDeTeste.Add(tenantIsolado);

        async Task<(Guid Id, string Email, string Segredo)> ContaMatriculadaNoIsolado58(string rotulo)
        {
            await using var db = new CrmDbContext(opcoes);
            var conta = new Atendente
            {
                TenantId = tenantIsolado,
                Nome = $"[TESTE SEC-03] {rotulo}",
                Email = $"teste-sec-03-58-{Guid.NewGuid():N}@teste.local",
                SenhaHash = hash.Gerar(SenhaBoa),
                Perfil = PerfilDeAtendente.Administrador,
                Ativo = true
            };
            db.Atendentes.Add(conta);

            var tokenEmClaro = SegredosAleatoriosMfa.NovoToken();
            db.DesafiosDeMfa.Add(new DesafioDeMfa
            {
                TenantId = tenantIsolado,
                AtendenteId = conta.Id,
                HashDoDesafio = SegredosAleatoriosMfa.Sha256Hex(tokenEmClaro),
                Tipo = TipoDeDesafioMfa.Ticket,
                TokenVersionNoDesafio = conta.TokenVersion,
                ExpiraEm = DateTime.UtcNow.AddHours(24)
            });
            await db.SaveChangesAsync();
            ids.Add(conta.Id);

            var mfa = NovoMfaService(db);
            var sessao = await mfa.IniciarMatriculaPrivilegiadaAsync(
                new MatricularPrivilegiadoRequest(conta.Email, SenhaBoa, tokenEmClaro), null, default);
            var iniciado = await mfa.IniciarViaDesafioAsync(new IniciarViaDesafioRequest(sessao.Desafio), default);
            await mfa.ConfirmarViaDesafioAsync(
                new ConfirmarViaDesafioRequest(sessao.Desafio, CodigoTotp(iniciado.SegredoBase32, -1)), null, default);

            return (conta.Id, conta.Email, iniciado.SegredoBase32);
        }

        var alvo58 = await ContaMatriculadaNoIsolado58("alvo do reset");
        var ator58 = await ContaMatriculadaNoIsolado58("ator do reset e do flip");

        await using (var db = new CrmDbContext(opcoes))
        {
            // Passo -1 ja foi consumido pela confirmacao da matricula do ator
            // (ContaMatriculadaNoIsolado58) — o anti-replay exige passo
            // estritamente maior a cada uso; passo "atual" (deslocamento 0)
            // ainda esta livre.
            await NovoMfaService(db).AtivarObrigatoriedadeAsync(
                new IdentidadeParaMfa(tenantIsolado, ator58.Id),
                new AtivarObrigatoriedadeRequest(SenhaBoa, CodigoTotp(ator58.Segredo), "teste do cenario 58"),
                null, default);
        }

        Checar("58a. politica obrigatoria ligada no tenant isolado",
            await (new Func<Task<bool>>(async () =>
            {
                await using var db = new CrmDbContext(opcoes);
                return await db.Tenants.AsNoTracking().Where(t => t.Id == tenantIsolado)
                    .Select(t => t.MfaObrigatorioParaAdministradores).SingleAsync();
            }))());

        // Reset administrativo do ALVO, executado pelo ATOR (que teve o
        // TokenVersion subido pelo flip — precisa reler o codigo com o
        // segredo, que continua o mesmo; so a sessao antiga do ator morreu).
        // Passo 0 ja foi consumido pelo flip acima — usa +1 (proximo passo
        // livre para este ator).
        await using (var db = new CrmDbContext(opcoes))
        {
            await NovoMfaService(db).ResetarAsync(
                new IdentidadeParaMfa(tenantIsolado, ator58.Id), alvo58.Id,
                new ResetarMfaRequest(SenhaBoa, CodigoTotp(ator58.Segredo, 1), "cenario 58 — reset sob obrigatoriedade"),
                null, default);
        }

        var estadoAlvo58 = await Ler(alvo58.Id);
        Checar("58b. MfaAtivo=false apos o reset", !estadoAlvo58.MfaAtivo);
        Checar("58c. segredo apagado", estadoAlvo58.MfaSegredoCifrado is null);

        await using (var db = new CrmDbContext(opcoes))
        {
            Checar("58d. recovery codes apagados",
                !await db.CodigosDeRecuperacaoMfa.AnyAsync(c => c.AtendenteId == alvo58.Id));

            Checar("58e. challenges/tickets anteriores do alvo estao todos consumidos/invalidados",
                !await db.DesafiosDeMfa.AnyAsync(d => d.AtendenteId == alvo58.Id && d.ConsumidoEm == null));
        }

        // Login com senha correta e RECUSADO — a politica exige MFA e a conta
        // nao esta mais matriculada.
        await using (var db = new CrmDbContext(opcoes))
        {
            UsuariosServiceException? falha = null;
            try
            {
                await NovoAutenticacao(db, Guid.NewGuid()).AutenticarAsync(new LoginRequest(alvo58.Email, SenhaBoa));
            }
            catch (UsuariosServiceException ex) { falha = ex; }

            Checar("58f. login com senha correta e RECUSADO (mfa_obrigatorio_nao_matriculado)",
                falha?.Codigo == "mfa_obrigatorio_nao_matriculado");
        }

        // So depois de NOVO ticket + matricula completa a conta volta a entrar.
        // Ticket montado aqui (nao via EmitirTicketCruAsync, que grava no
        // tenant COMPARTILHADO) porque o alvo pertence ao tenant ISOLADO.
        var tokenVersionAlvoPosReset = (await Ler(alvo58.Id)).TokenVersion;
        var novoTicket = SegredosAleatoriosMfa.NovoToken();
        await using (var db = new CrmDbContext(opcoes))
        {
            db.DesafiosDeMfa.Add(new DesafioDeMfa
            {
                TenantId = tenantIsolado,
                AtendenteId = alvo58.Id,
                HashDoDesafio = SegredosAleatoriosMfa.Sha256Hex(novoTicket),
                Tipo = TipoDeDesafioMfa.Ticket,
                TokenVersionNoDesafio = tokenVersionAlvoPosReset,
                ExpiraEm = DateTime.UtcNow.AddHours(24)
            });
            await db.SaveChangesAsync();
        }

        await using (var db = new CrmDbContext(opcoes))
        {
            var mfa = NovoMfaService(db);
            var sessao = await mfa.IniciarMatriculaPrivilegiadaAsync(
                new MatricularPrivilegiadoRequest(alvo58.Email, SenhaBoa, novoTicket), null, default);
            var iniciado = await mfa.IniciarViaDesafioAsync(new IniciarViaDesafioRequest(sessao.Desafio), default);
            await mfa.ConfirmarViaDesafioAsync(
                new ConfirmarViaDesafioRequest(sessao.Desafio, CodigoTotp(iniciado.SegredoBase32, -1)), null, default);
        }

        Checar("58g. apos NOVO ticket + matricula completa, a conta volta a poder entrar (com MFA)",
            (await Ler(alvo58.Id)).MfaAtivo);
    }

    // ------------------------------------------------------------ cenario 59
    // Politica ligada ENTRE as etapas do login: TokenVersion sobe para o
    // afetado, e o desafio pendente (aberto ANTES do flip) deixa de valer.

    {
        var (idAlvo, emailAlvo, segredoAlvo, _) = await NovaContaComMfaAtivo("politica liga entre etapas");
        var (idAtor, _, segredoAtor, _) = await NovaContaComMfaAtivo("ator do flip (cenario 59)");

        Guid tenantIsolado;
        await using (var db = new CrmDbContext(opcoes))
        {
            var tenant = new Tenant
            {
                RazaoSocial = "[TESTE SEC-03] Tenant isolado — politica entre etapas",
                Cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString()
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            tenantIsolado = tenant.Id;
        }
        tenantIdsDeTeste.Add(tenantIsolado);

        async Task<(Guid Id, string Email, string Segredo)> ContaMatriculadaNoIsolado59(string rotulo)
        {
            await using var db = new CrmDbContext(opcoes);
            var conta = new Atendente
            {
                TenantId = tenantIsolado,
                Nome = $"[TESTE SEC-03] {rotulo}",
                Email = $"teste-sec-03-59-{Guid.NewGuid():N}@teste.local",
                SenhaHash = hash.Gerar(SenhaBoa),
                Perfil = PerfilDeAtendente.Administrador,
                Ativo = true
            };
            db.Atendentes.Add(conta);

            var tokenEmClaro = SegredosAleatoriosMfa.NovoToken();
            db.DesafiosDeMfa.Add(new DesafioDeMfa
            {
                TenantId = tenantIsolado,
                AtendenteId = conta.Id,
                HashDoDesafio = SegredosAleatoriosMfa.Sha256Hex(tokenEmClaro),
                Tipo = TipoDeDesafioMfa.Ticket,
                TokenVersionNoDesafio = conta.TokenVersion,
                ExpiraEm = DateTime.UtcNow.AddHours(24)
            });
            await db.SaveChangesAsync();
            ids.Add(conta.Id);

            var mfa = NovoMfaService(db);
            var sessao = await mfa.IniciarMatriculaPrivilegiadaAsync(
                new MatricularPrivilegiadoRequest(conta.Email, SenhaBoa, tokenEmClaro), null, default);
            var iniciado = await mfa.IniciarViaDesafioAsync(new IniciarViaDesafioRequest(sessao.Desafio), default);
            await mfa.ConfirmarViaDesafioAsync(
                new ConfirmarViaDesafioRequest(sessao.Desafio, CodigoTotp(iniciado.SegredoBase32, -1)), null, default);

            return (conta.Id, conta.Email, iniciado.SegredoBase32);
        }

        var alvo59 = await ContaMatriculadaNoIsolado59("alvo — desafio antes do flip");
        var ator59 = await ContaMatriculadaNoIsolado59("ator do flip (isolado, cenario 59)");

        // 1. Abre a etapa 1 do login do ALVO — desafio nasce com o
        // TokenVersion de AGORA (antes do flip).
        LoginEtapa1Response etapa1Do59;
        await using (var db = new CrmDbContext(opcoes))
        {
            etapa1Do59 = await NovoAutenticacao(db, Guid.NewGuid())
                .AutenticarAsync(new LoginRequest(alvo59.Email, SenhaBoa));
        }
        Checar("59a-preparo. etapa 1 do alvo abriu desafio normalmente (MFA ja ativo antes do flip)",
            etapa1Do59.RequerMfa && etapa1Do59.Desafio is not null);

        // 2. ANTES da etapa 2, liga a politica pelo fluxo real — sobe
        // TokenVersion de TODOS os Administrador/Titular do tenant isolado,
        // alvo incluido.
        await using (var db = new CrmDbContext(opcoes))
        {
            // Passo -1 ja foi consumido pela confirmacao da matricula do ator
            // (ContaMatriculadaNoIsolado59) — usa o passo "atual" (livre).
            await NovoMfaService(db).AtivarObrigatoriedadeAsync(
                new IdentidadeParaMfa(tenantIsolado, ator59.Id),
                new AtivarObrigatoriedadeRequest(SenhaBoa, CodigoTotp(ator59.Segredo), "teste do cenario 59"),
                null, default);
        }

        // 3/4. O codigo TOTP CORRETO no desafio ANTIGO nao emite JWT — a
        // revogacao de TokenVersion do flip invalidou o desafio.
        await using (var db = new CrmDbContext(opcoes))
        {
            // Passo -1 ja foi consumido pela confirmacao da matricula do alvo
            // (ContaMatriculadaNoIsolado59) — usa o passo "atual" (nunca usado
            // por este alvo), pra provar que e a REVOGACAO de TokenVersion que
            // barra, nao um replay de passo.
            UsuariosServiceException? falha = null;
            try
            {
                await NovoAutenticacao(db, Guid.NewGuid()).ConcluirLoginMfaAsync(
                    new ConcluirLoginMfaRequest(etapa1Do59.Desafio!.Desafio, CodigoTotp(alvo59.Segredo)));
            }
            catch (UsuariosServiceException ex) { falha = ex; }

            Checar("59b. politica ligada ENTRE as etapas invalida o desafio — codigo certo nao emite JWT",
                falha?.Codigo == "desafio_mfa_invalido");
        }
    }

    // ------------------------------------------------------------ cenario 60
    // Bootstrap — a mesma logica de elegibilidade que o comando interativo usa,
    // testada direto (sem simular stdin). Ver FerramentaDeBootstrapMfa.

    {
        var (idPrimeiro, emailPrimeiro) = await NovaContaPrivilegiada("bootstrap - primeiro titular");

        Guid tenantIsoladoBootstrap;
        await using (var db = new CrmDbContext(opcoes))
        {
            var tenant = new Tenant
            {
                RazaoSocial = "[TESTE SEC-03] Tenant isolado — bootstrap",
                Cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString()
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            tenantIsoladoBootstrap = tenant.Id;
        }
        tenantIdsDeTeste.Add(tenantIsoladoBootstrap);

        // Precisa de tenant ISOLADO: o auto-fechamento olha o tenant INTEIRO, e
        // o compartilhado ja pode ter (ou nao, dependendo da ordem de outros
        // cenarios) alguem matriculado — o teste precisa controlar isto.
        Guid idPrimeiroIsolado;
        string emailPrimeiroIsolado;
        await using (var db = new CrmDbContext(opcoes))
        {
            var conta = new Atendente
            {
                TenantId = tenantIsoladoBootstrap,
                Nome = "[TESTE SEC-03] bootstrap - primeiro",
                Email = $"teste-sec-03-bootstrap-{Guid.NewGuid():N}@teste.local",
                SenhaHash = hash.Gerar(SenhaBoa),
                Perfil = PerfilDeAtendente.Administrador,
                Ativo = true
            };
            db.Atendentes.Add(conta);
            await db.SaveChangesAsync();
            ids.Add(conta.Id);
            idPrimeiroIsolado = conta.Id;
            emailPrimeiroIsolado = conta.Email;
        }

        await using (var db = new CrmDbContext(opcoes))
        {
            Checar("60a. SEM privilegiado matriculado no tenant -> elegivel para o primeiro ticket",
                await FerramentaDeBootstrapMfa.VerificarElegibilidadeAsync(db, emailPrimeiroIsolado)
                    == ElegibilidadeDeBootstrap.Elegivel);
        }

        // CORRECAO (bloqueador 7): EmitirTicketAsync (o que gera e devolve
        // texto claro) virou `private` de proposito — nenhum codigo fora de
        // ExecutarAsync pode mais chamar isto, nem um teste. O teste gera o
        // PROPRIO plaintext (nunca recebido de volta da producao) e entrega
        // so o hash ao nucleo atomico privado TentarRegistrarTicketAsync, via
        // reflexao (bloqueador 9 — o metodo nao e mais publico) — exatamente
        // como um DesafioDeMfa.HashDoDesafio real.
        var ticketDoBootstrap = SegredosAleatoriosMfa.NovoToken();
        await using (var db = new CrmDbContext(opcoes))
        {
            var resultado = await TentarRegistrarTicketViaReflexaoAsync(
                db, emailPrimeiroIsolado, SegredosAleatoriosMfa.Sha256Hex(ticketDoBootstrap),
                "teste automatizado do cenario 60", "teste@verificador");

            Checar("60a-preparo. TentarRegistrarTicketAsync grava o ticket quando elegivel",
                resultado == ElegibilidadeDeBootstrap.Elegivel);
        }

        // Matricula de verdade, pelo MESMO fluxo publico — prova que o ticket
        // do bootstrap funciona identico a um ticket normal.
        IniciarMfaResponse iniciadoBootstrap;
        DesafioMfaResponse sessaoBootstrap;
        await using (var db = new CrmDbContext(opcoes))
        {
            var mfa = NovoMfaService(db);
            sessaoBootstrap = await mfa.IniciarMatriculaPrivilegiadaAsync(
                new MatricularPrivilegiadoRequest(emailPrimeiroIsolado, SenhaBoa, ticketDoBootstrap), null, default);
            iniciadoBootstrap = await mfa.IniciarViaDesafioAsync(new IniciarViaDesafioRequest(sessaoBootstrap.Desafio), default);
            await mfa.ConfirmarViaDesafioAsync(
                new ConfirmarViaDesafioRequest(sessaoBootstrap.Desafio, CodigoTotp(iniciadoBootstrap.SegredoBase32, -1)), null, default);
        }

        Checar("60b. ticket do bootstrap matricula de verdade pelo fluxo publico",
            (await Ler(idPrimeiroIsolado)).MfaAtivo);

        // Agora que ha um privilegiado matriculado no tenant, o bootstrap
        // FECHA SOZINHO — mesmo para uma conta DIFERENTE, ainda sem MFA.
        Guid idSegundoIsolado;
        string emailSegundoIsolado;
        await using (var db = new CrmDbContext(opcoes))
        {
            var conta = new Atendente
            {
                TenantId = tenantIsoladoBootstrap,
                Nome = "[TESTE SEC-03] bootstrap - segundo (deveria ser recusado)",
                Email = $"teste-sec-03-bootstrap-{Guid.NewGuid():N}@teste.local",
                SenhaHash = hash.Gerar(SenhaBoa),
                Perfil = PerfilDeAtendente.Administrador,
                Ativo = true
            };
            db.Atendentes.Add(conta);
            await db.SaveChangesAsync();
            ids.Add(conta.Id);
            idSegundoIsolado = conta.Id;
            emailSegundoIsolado = conta.Email;
        }

        await using (var db = new CrmDbContext(opcoes))
        {
            Checar("60c. DEPOIS de existir Administrador/Titular com MFA ativo -> bootstrap RECUSA (auto-fechamento)",
                await FerramentaDeBootstrapMfa.VerificarElegibilidadeAsync(db, emailSegundoIsolado)
                    == ElegibilidadeDeBootstrap.JaExistePrivilegiadoMatriculado);
        }

        await using (var db = new CrmDbContext(opcoes))
        {
            Checar("60d. e-mail inexistente -> ContaInexistente",
                await FerramentaDeBootstrapMfa.VerificarElegibilidadeAsync(db, $"nao-existe-{Guid.NewGuid():N}@teste.local")
                    == ElegibilidadeDeBootstrap.ContaInexistente);
        }

        var (idInativoBootstrap, emailInativoBootstrap) = await NovaContaPrivilegiada("bootstrap - inativo", ativo: false);
        await using (var db = new CrmDbContext(opcoes))
        {
            Checar("60e. conta inativa -> ContaInativa",
                await FerramentaDeBootstrapMfa.VerificarElegibilidadeAsync(db, emailInativoBootstrap)
                    == ElegibilidadeDeBootstrap.ContaInativa);
        }

        var (idComumBootstrap, emailComumBootstrap) = await NovaContaPrivilegiada(
            "bootstrap - nao privilegiado", perfil: PerfilDeAtendente.Atendente);
        await using (var db = new CrmDbContext(opcoes))
        {
            Checar("60f. conta NAO privilegiada -> NaoPrivilegiada",
                await FerramentaDeBootstrapMfa.VerificarElegibilidadeAsync(db, emailComumBootstrap)
                    == ElegibilidadeDeBootstrap.NaoPrivilegiada);
        }

        await using (var db = new CrmDbContext(opcoes))
        {
            Checar("60g. conta que JA tem MFA ativo -> MfaJaAtivo",
                await FerramentaDeBootstrapMfa.VerificarElegibilidadeAsync(db, emailPrimeiroIsolado)
                    == ElegibilidadeDeBootstrap.MfaJaAtivo);
        }
    }

    // =====================================================================
    // Segunda revisao independente do PR #46 — bloqueadores 4, 5 e 6, mais
    // evidencia HTTP real do bloqueador 1
    // =====================================================================

    // ------------------------------------------------------------ cenario 61
    // BLOQUEADOR 4 — TokenVersion sobe quando a FRONTEIRA de privilegio do
    // SEC-03 (Perfil==Administrador || Cargo==Titular) muda de lado, mesmo
    // quando so o Cargo muda. Ver AtendenteService.AtualizarAsync.

    {
        Guid tenantIsolado61;
        await using (var db = new CrmDbContext(opcoes))
        {
            var tenant = new Tenant
            {
                RazaoSocial = "[TESTE SEC-03] Tenant isolado — fronteira de Cargo (bloqueador 4)",
                Cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString()
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            tenantIsolado61 = tenant.Id;
        }
        tenantIdsDeTeste.Add(tenantIsolado61);

        async Task<(Guid Id, string Email)> ContaNoIsolado61(string rotulo, CargoNaServentia cargo)
        {
            await using var db = new CrmDbContext(opcoes);
            var conta = new Atendente
            {
                TenantId = tenantIsolado61,
                Nome = $"[TESTE SEC-03] {rotulo}",
                Email = $"teste-sec-03-61-{Guid.NewGuid():N}@teste.local",
                SenhaHash = hash.Gerar(SenhaBoa),
                Perfil = PerfilDeAtendente.Atendente,
                Cargo = cargo,
                Ativo = true
            };
            db.Atendentes.Add(conta);
            await db.SaveChangesAsync();
            ids.Add(conta.Id);
            return (conta.Id, conta.Email);
        }

        // 61a — Substituto -> Titular sobe TokenVersion (fronteira false->true).
        var (idA, emailA) = await ContaNoIsolado61("61a", CargoNaServentia.Substituto);
        var v0A = (await Ler(idA)).TokenVersion;
        await using (var db = new CrmDbContext(opcoes))
        {
            await NovoAtendenteServiceParaTenant(db, Guid.NewGuid(), tenantIsolado61).AtualizarAsync(idA,
                new AtualizarAtendenteRequest("Nome", emailA, PerfilDeAtendente.Atendente, null, true, CargoNaServentia.Titular));
        }
        Checar("61a. Atendente/Substituto -> Atendente/Titular sobe TokenVersion",
            (await Ler(idA)).TokenVersion > v0A);

        // 61b — Titular -> Substituto sobe TokenVersion (fronteira true->false).
        var (idB, emailB) = await ContaNoIsolado61("61b", CargoNaServentia.Titular);
        var v0B = (await Ler(idB)).TokenVersion;
        await using (var db = new CrmDbContext(opcoes))
        {
            await NovoAtendenteServiceParaTenant(db, Guid.NewGuid(), tenantIsolado61).AtualizarAsync(idB,
                new AtualizarAtendenteRequest("Nome", emailB, PerfilDeAtendente.Atendente, null, true, CargoNaServentia.Substituto));
        }
        Checar("61b. Titular -> Substituto sobe TokenVersion",
            (await Ler(idB)).TokenVersion > v0B);

        // 61c — Titular -> Substituto -> Titular NAO ressuscita ticket antigo:
        // duas travessias da fronteira sobem TokenVersion duas vezes — o
        // ticket gravado com a TokenVersion original nunca mais bate.
        var (idC, emailC) = await ContaNoIsolado61("61c", CargoNaServentia.Titular);
        var v0C = (await Ler(idC)).TokenVersion;
        var tokenEmClaro61c = SegredosAleatoriosMfa.NovoToken();
        await using (var db = new CrmDbContext(opcoes))
        {
            db.DesafiosDeMfa.Add(new DesafioDeMfa
            {
                TenantId = tenantIsolado61,
                AtendenteId = idC,
                HashDoDesafio = SegredosAleatoriosMfa.Sha256Hex(tokenEmClaro61c),
                Tipo = TipoDeDesafioMfa.Ticket,
                TokenVersionNoDesafio = v0C,
                ExpiraEm = DateTime.UtcNow.AddHours(24)
            });
            await db.SaveChangesAsync();
        }

        await using (var db = new CrmDbContext(opcoes))
        {
            await NovoAtendenteServiceParaTenant(db, Guid.NewGuid(), tenantIsolado61).AtualizarAsync(idC,
                new AtualizarAtendenteRequest("Nome", emailC, PerfilDeAtendente.Atendente, null, true, CargoNaServentia.Substituto));
        }
        await using (var db = new CrmDbContext(opcoes))
        {
            await NovoAtendenteServiceParaTenant(db, Guid.NewGuid(), tenantIsolado61).AtualizarAsync(idC,
                new AtualizarAtendenteRequest("Nome", emailC, PerfilDeAtendente.Atendente, null, true, CargoNaServentia.Titular));
        }

        UsuariosServiceException? falha61c = null;
        await using (var db = new CrmDbContext(opcoes))
        {
            try
            {
                await NovoMfaService(db).IniciarMatriculaPrivilegiadaAsync(
                    new MatricularPrivilegiadoRequest(emailC, SenhaBoa, tokenEmClaro61c), null, default);
            }
            catch (UsuariosServiceException ex) { falha61c = ex; }
        }
        Checar("61c. Titular -> Substituto -> Titular NAO ressuscita o ticket antigo",
            falha61c?.Codigo == "ticket_invalido");

        // 61d — JWT emitido antes da promocao por Cargo deixa de ser valido.
        var (idD, emailD) = await ContaNoIsolado61("61d", CargoNaServentia.Substituto);
        var estadoAntesD = await Ler(idD);
        var (tokenD, _) = tokens.Gerar(estadoAntesD);
        var versaoAntesD = LerVersaoDaClaim(tokenD);

        await using (var db = new CrmDbContext(opcoes))
        {
            await NovoAtendenteServiceParaTenant(db, Guid.NewGuid(), tenantIsolado61).AtualizarAsync(idD,
                new AtualizarAtendenteRequest("Nome", emailD, PerfilDeAtendente.Atendente, null, true, CargoNaServentia.Titular));
        }

        await using (var db = new CrmDbContext(opcoes))
        {
            Checar("61d. JWT emitido antes da promocao por Cargo deixa de ser valido",
                await NovoValidador(db).CarregarSessaoAsync(idD, tenantIsolado61, versaoAntesD) is null);
        }

        // 61e — com politica obrigatoria ligada, promover por Cargo para
        // Titular nao deixa a sessao antiga valer nem loga sem matricula.
        // Precisa de tenant PROPRIO: ligar a politica exige TODOS os
        // Administrador/Titular ja matriculados.
        Guid tenantIsolado61e;
        await using (var db = new CrmDbContext(opcoes))
        {
            var tenant = new Tenant
            {
                RazaoSocial = "[TESTE SEC-03] Tenant isolado — promocao por Cargo sob politica (61e)",
                Cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString()
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            tenantIsolado61e = tenant.Id;
        }
        tenantIdsDeTeste.Add(tenantIsolado61e);

        async Task<(Guid Id, string Email, string Segredo)> AtorMatriculadoNoIsolado61e(string rotulo)
        {
            await using var db = new CrmDbContext(opcoes);
            var conta = new Atendente
            {
                TenantId = tenantIsolado61e,
                Nome = $"[TESTE SEC-03] {rotulo}",
                Email = $"teste-sec-03-61e-{Guid.NewGuid():N}@teste.local",
                SenhaHash = hash.Gerar(SenhaBoa),
                Perfil = PerfilDeAtendente.Administrador,
                Ativo = true
            };
            db.Atendentes.Add(conta);

            var tokenEmClaro = SegredosAleatoriosMfa.NovoToken();
            db.DesafiosDeMfa.Add(new DesafioDeMfa
            {
                TenantId = tenantIsolado61e,
                AtendenteId = conta.Id,
                HashDoDesafio = SegredosAleatoriosMfa.Sha256Hex(tokenEmClaro),
                Tipo = TipoDeDesafioMfa.Ticket,
                TokenVersionNoDesafio = conta.TokenVersion,
                ExpiraEm = DateTime.UtcNow.AddHours(24)
            });
            await db.SaveChangesAsync();
            ids.Add(conta.Id);

            var mfa = NovoMfaService(db);
            var sessao = await mfa.IniciarMatriculaPrivilegiadaAsync(
                new MatricularPrivilegiadoRequest(conta.Email, SenhaBoa, tokenEmClaro), null, default);
            var iniciado = await mfa.IniciarViaDesafioAsync(new IniciarViaDesafioRequest(sessao.Desafio), default);
            await mfa.ConfirmarViaDesafioAsync(
                new ConfirmarViaDesafioRequest(sessao.Desafio, CodigoTotp(iniciado.SegredoBase32, -1)), null, default);

            return (conta.Id, conta.Email, iniciado.SegredoBase32);
        }

        var ator61e = await AtorMatriculadoNoIsolado61e("ator do flip (61e)");

        await using (var db = new CrmDbContext(opcoes))
        {
            await NovoMfaService(db).AtivarObrigatoriedadeAsync(
                new IdentidadeParaMfa(tenantIsolado61e, ator61e.Id),
                new AtivarObrigatoriedadeRequest(SenhaBoa, CodigoTotp(ator61e.Segredo), "teste do cenario 61e"),
                null, default);
        }

        Guid idE; string emailE;
        await using (var db = new CrmDbContext(opcoes))
        {
            var conta = new Atendente
            {
                TenantId = tenantIsolado61e,
                Nome = "[TESTE SEC-03] 61e - promovido por Cargo",
                Email = $"teste-sec-03-61e-alvo-{Guid.NewGuid():N}@teste.local",
                SenhaHash = hash.Gerar(SenhaBoa),
                Perfil = PerfilDeAtendente.Atendente,
                Cargo = CargoNaServentia.Substituto,
                Ativo = true
            };
            db.Atendentes.Add(conta);
            await db.SaveChangesAsync();
            ids.Add(conta.Id);
            idE = conta.Id;
            emailE = conta.Email;
        }

        // Login normal enquanto ainda NAO privilegiado — a politica so exige
        // MFA de quem e privilegiado, entao entra numa etapa so, com JWT.
        LoginEtapa1Response etapa1AntesDaPromocao;
        await using (var db = new CrmDbContext(opcoes))
        {
            etapa1AntesDaPromocao = await NovoAutenticacao(db, Guid.NewGuid())
                .AutenticarAsync(new LoginRequest(emailE, SenhaBoa));
        }
        Checar("61e-preparo. login normal antes da promocao (nao privilegiado) devolve sessao completa",
            !etapa1AntesDaPromocao.RequerMfa && etapa1AntesDaPromocao.Sessao is not null);

        var versaoAntesDaPromocaoE = LerVersaoDaClaim(etapa1AntesDaPromocao.Sessao!.Token);

        await using (var db = new CrmDbContext(opcoes))
        {
            await NovoAtendenteServiceParaTenant(db, Guid.NewGuid(), tenantIsolado61e).AtualizarAsync(idE,
                new AtualizarAtendenteRequest("Nome", emailE, PerfilDeAtendente.Atendente, null, true, CargoNaServentia.Titular));
        }

        await using (var db = new CrmDbContext(opcoes))
        {
            Checar("61e. sessao antiga (de antes da promocao por Cargo) nao vale mais",
                await NovoValidador(db).CarregarSessaoAsync(idE, tenantIsolado61e, versaoAntesDaPromocaoE) is null);
        }

        await using (var db = new CrmDbContext(opcoes))
        {
            UsuariosServiceException? falha = null;
            try
            {
                await NovoAutenticacao(db, Guid.NewGuid()).AutenticarAsync(new LoginRequest(emailE, SenhaBoa));
            }
            catch (UsuariosServiceException ex) { falha = ex; }

            Checar("61e. novo login apos promocao por Cargo, sob politica obrigatoria, exige matricula de MFA",
                falha?.Codigo == "mfa_obrigatorio_nao_matriculado");
        }

        // 61f — desafio de MFA (2o fator) aberto ANTES da promocao por Cargo
        // fica invalido: codigo certo no desafio velho nao emite JWT.
        var (idF, emailF, segredoF, _) = await NovaContaComMfaAtivo(
            "61f - promovido depois do desafio aberto", perfil: PerfilDeAtendente.Atendente, cargo: CargoNaServentia.Substituto);

        LoginEtapa1Response etapa1F;
        await using (var db = new CrmDbContext(opcoes))
        {
            etapa1F = await NovoAutenticacao(db, Guid.NewGuid()).AutenticarAsync(new LoginRequest(emailF, SenhaBoa));
        }
        Checar("61f-preparo. desafio de 2o fator aberto (conta ja tinha MFA, Cargo=Substituto)",
            etapa1F.RequerMfa && etapa1F.Desafio is not null);

        await using (var db = new CrmDbContext(opcoes))
        {
            await NovoAtendenteService(db, Guid.NewGuid()).AtualizarAsync(idF,
                new AtualizarAtendenteRequest("Nome", emailF, PerfilDeAtendente.Atendente, null, true, CargoNaServentia.Titular));
        }

        Checar("61f. desafio de MFA aberto ANTES da mudanca de fronteira de Cargo fica invalido",
            await EtapaDois(etapa1F.Desafio!.Desafio, CodigoTotp(segredoF)) is null);

        // 61g — regressao: Cargo muda mas NENHUM dos dois lados e Titular ->
        // a fronteira nao muda de lado -> TokenVersion NAO sobe.
        var (idG, emailG) = await ContaNoIsolado61("61g", CargoNaServentia.Substituto);
        var v0G = (await Ler(idG)).TokenVersion;
        await using (var db = new CrmDbContext(opcoes))
        {
            await NovoAtendenteServiceParaTenant(db, Guid.NewGuid(), tenantIsolado61).AtualizarAsync(idG,
                new AtualizarAtendenteRequest("Nome", emailG, PerfilDeAtendente.Atendente, null, true, CargoNaServentia.EscreventeAutorizado));
        }
        Checar("61g. mudanca de Cargo que NAO cruza a fronteira de privilegio nao sobe TokenVersion",
            (await Ler(idG)).TokenVersion == v0G);
    }

    // ------------------------------------------------------------ cenario 62
    // MUDANCA DE REGRA: este bloqueador NAO e mais sobre privilegio — desde o
    // cenario 55, privilegiado pode concluir a ativacao voluntaria
    // normalmente. O que sobra aqui e uma protecao mais generica e mais
    // forte, que ja existia por baixo do disjunto de privilegio removido:
    // qualquer promocao que cruze a fronteira de privilegio (Perfil/Cargo) ja
    // sobe TokenVersion (AtendenteService.AtualizarAsync, cenario 61), e o
    // porteiro final de ConfirmarNucleoAsync exige `a.TokenVersion ==
    // a.MfaPendenteTokenVersion` — uma promocao ENTRE iniciar e confirmar
    // quebra essa igualdade e falha fechado (codigo_mfa_invalido), mesmo com
    // o TOTP certo, independente de para onde a conta foi promovida.

    {
        // 62a — comum inicia -> promovido a Administrador (via Perfil) ->
        // confirmar sem ticket falha (TokenVersion mudou no meio do caminho).
        var (idA, emailA) = await NovaContaPrivilegiada("bloqueador5 - promovido por Perfil", perfil: PerfilDeAtendente.Atendente);

        IniciarMfaResponse iniciadoA;
        await using (var db = new CrmDbContext(opcoes))
        {
            iniciadoA = await NovoMfaService(db).IniciarAsync(
                new IdentidadeParaMfa(tenantId, idA), new IniciarMfaRequest(SenhaBoa, null), null, default);
        }

        await using (var db = new CrmDbContext(opcoes))
        {
            await NovoAtendenteService(db, Guid.NewGuid()).AtualizarAsync(idA,
                new AtualizarAtendenteRequest("Nome", emailA, PerfilDeAtendente.Administrador, null, true));
        }

        await using (var db = new CrmDbContext(opcoes))
        {
            UsuariosServiceException? falha = null;
            try
            {
                await NovoMfaService(db).ConfirmarAsync(
                    new IdentidadeParaMfa(tenantId, idA), new ConfirmarMfaRequest(CodigoTotp(iniciadoA.SegredoBase32)), null, default);
            }
            catch (UsuariosServiceException ex) { falha = ex; }

            Checar("62a. comum inicia MFA -> promovido a Administrador (Perfil) -> confirmar sem ticket falha por TokenVersion (nao por privilegio)",
                falha?.Codigo == "codigo_mfa_invalido");
        }

        Checar("62a-pos. MfaAtivo continua false apos a confirmacao recusada",
            !(await Ler(idA)).MfaAtivo);

        // 62b — mesma coisa, mas a promocao e por CARGO (Titular).
        var (idB, emailB) = await NovaContaPrivilegiada("bloqueador5 - promovido por Cargo", perfil: PerfilDeAtendente.Atendente);

        IniciarMfaResponse iniciadoB;
        await using (var db = new CrmDbContext(opcoes))
        {
            iniciadoB = await NovoMfaService(db).IniciarAsync(
                new IdentidadeParaMfa(tenantId, idB), new IniciarMfaRequest(SenhaBoa, null), null, default);
        }

        await using (var db = new CrmDbContext(opcoes))
        {
            await NovoAtendenteService(db, Guid.NewGuid()).AtualizarAsync(idB,
                new AtualizarAtendenteRequest("Nome", emailB, PerfilDeAtendente.Atendente, null, true, CargoNaServentia.Titular));
        }

        await using (var db = new CrmDbContext(opcoes))
        {
            UsuariosServiceException? falha = null;
            try
            {
                await NovoMfaService(db).ConfirmarAsync(
                    new IdentidadeParaMfa(tenantId, idB), new ConfirmarMfaRequest(CodigoTotp(iniciadoB.SegredoBase32)), null, default);
            }
            catch (UsuariosServiceException ex) { falha = ex; }

            Checar("62b. comum inicia MFA -> promovido a Titular (Cargo) -> confirmar sem ticket falha por TokenVersion (nao por privilegio)",
                falha?.Codigo == "codigo_mfa_invalido");
        }

        // 62c — corrida REAL: ConfirmarAsync concorrente com a promocao a
        // Administrador. As duas operacoes fazem UPDATE na MESMA linha de
        // Atendentes — o SQL Server serializa por lock de linha, entao uma
        // sempre efetivamente acontece antes da outra (nunca as duas ao mesmo
        // tempo). Em varias tentativas independentes, o resultado reportado
        // pela chamada de confirmar sempre bate com o MfaAtivo gravado.
        var trials62c = 5;
        var inconsistencias62c = 0;

        for (var i = 0; i < trials62c; i++)
        {
            var (idC, emailC) = await NovaContaPrivilegiada($"bloqueador5 - corrida {i}", perfil: PerfilDeAtendente.Atendente);

            IniciarMfaResponse iniciadoC;
            await using (var db = new CrmDbContext(opcoes))
            {
                iniciadoC = await NovoMfaService(db).IniciarAsync(
                    new IdentidadeParaMfa(tenantId, idC), new IniciarMfaRequest(SenhaBoa, null), null, default);
            }

            var codigoC = CodigoTotp(iniciadoC.SegredoBase32);

            async Task<bool> TentarConfirmar()
            {
                await using var db = new CrmDbContext(opcoes);
                try
                {
                    await NovoMfaService(db).ConfirmarAsync(
                        new IdentidadeParaMfa(tenantId, idC), new ConfirmarMfaRequest(codigoC), null, default);
                    return true;
                }
                catch (UsuariosServiceException)
                {
                    return false;
                }
            }

            async Task PromoverAsync()
            {
                await using var db = new CrmDbContext(opcoes);
                await NovoAtendenteService(db, Guid.NewGuid()).AtualizarAsync(idC,
                    new AtualizarAtendenteRequest("Nome", emailC, PerfilDeAtendente.Administrador, null, true));
            }

            var tarefaConfirmar = TentarConfirmar();
            var tarefaPromover = PromoverAsync();
            await Task.WhenAll(tarefaConfirmar, tarefaPromover);

            var confirmouOk = await tarefaConfirmar;
            var estadoFinal = await Ler(idC);

            if (confirmouOk != estadoFinal.MfaAtivo)
            {
                inconsistencias62c++;
            }
        }

        Checar($"62c. corrida confirmacao x promocao (Perfil): resultado da confirmacao sempre bate com MfaAtivo final ({trials62c} tentativas)",
            inconsistencias62c == 0);

        // 62d — regressao: Administrador que JA tem MFA ativo continua
        // trocando de autenticador com step-up (a.MfaAtivo == true no WHERE
        // atomico da confirmacao — o ramo que este bloqueador precisa manter
        // intacto).
        var (idD, emailD, segredoD, _) = await NovaContaComMfaAtivo("bloqueador5 - troca de autenticador");

        IniciarMfaResponse iniciadoTrocaD;
        await using (var db = new CrmDbContext(opcoes))
        {
            iniciadoTrocaD = await NovoMfaService(db).IniciarAsync(
                new IdentidadeParaMfa(tenantId, idD), new IniciarMfaRequest(SenhaBoa, CodigoTotp(segredoD)), null, default);
        }

        ConfirmarMfaResponse confirmadoTrocaD;
        await using (var db = new CrmDbContext(opcoes))
        {
            confirmadoTrocaD = await NovoMfaService(db).ConfirmarAsync(
                new IdentidadeParaMfa(tenantId, idD), new ConfirmarMfaRequest(CodigoTotp(iniciadoTrocaD.SegredoBase32, -1)), null, default);
        }

        Checar("62d. Administrador com MFA ja ativo continua trocando de autenticador (step-up)",
            !string.IsNullOrEmpty(confirmadoTrocaD.Sessao.Token) && (await Ler(idD)).MfaAtivo);
    }

    // ------------------------------------------------------------ cenario 63
    // BLOQUEADOR 6 — FerramentaDeBootstrapMfa.EmitirTicketAsync reconfere a
    // elegibilidade no INSTANTE da escrita, dentro de uma transacao
    // SERIALIZABLE — nunca confia na verificacao feita antes da interacao
    // humana.

    {
        Guid tenantIsolado63a;
        await using (var db = new CrmDbContext(opcoes))
        {
            var tenant = new Tenant
            {
                RazaoSocial = "[TESTE SEC-03] Tenant isolado — TOCTOU do bootstrap (63a)",
                Cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString()
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            tenantIsolado63a = tenant.Id;
        }
        tenantIdsDeTeste.Add(tenantIsolado63a);

        // Alvo do bootstrap: privilegiado, sem MFA, elegivel no comeco.
        Guid idAlvo63a; string emailAlvo63a;
        await using (var db = new CrmDbContext(opcoes))
        {
            var conta = new Atendente
            {
                TenantId = tenantIsolado63a,
                Nome = "[TESTE SEC-03] 63a - alvo do bootstrap",
                Email = $"teste-sec-03-63a-alvo-{Guid.NewGuid():N}@teste.local",
                SenhaHash = hash.Gerar(SenhaBoa),
                Perfil = PerfilDeAtendente.Administrador,
                Ativo = true
            };
            db.Atendentes.Add(conta);
            await db.SaveChangesAsync();
            ids.Add(conta.Id);
            idAlvo63a = conta.Id;
            emailAlvo63a = conta.Email;
        }

        await using (var db = new CrmDbContext(opcoes))
        {
            Checar("63a-preparo. verificacao inicial: alvo Elegivel para bootstrap",
                await FerramentaDeBootstrapMfa.VerificarElegibilidadeAsync(db, emailAlvo63a)
                    == ElegibilidadeDeBootstrap.Elegivel);
        }

        // ANTES da emissao (simulando o intervalo de interacao humana entre a
        // verificacao inicial e a confirmacao no CLI), OUTRO privilegiado do
        // MESMO tenant conclui a propria matricula de MFA — o bootstrap
        // deveria ter se auto-fechado.
        await using (var db = new CrmDbContext(opcoes))
        {
            var conta = new Atendente
            {
                TenantId = tenantIsolado63a,
                Nome = "[TESTE SEC-03] 63a - outro privilegiado (fecha o bootstrap)",
                Email = $"teste-sec-03-63a-outro-{Guid.NewGuid():N}@teste.local",
                SenhaHash = hash.Gerar(SenhaBoa),
                Perfil = PerfilDeAtendente.Administrador,
                Ativo = true
            };
            db.Atendentes.Add(conta);

            var tokenOutro = SegredosAleatoriosMfa.NovoToken();
            db.DesafiosDeMfa.Add(new DesafioDeMfa
            {
                TenantId = tenantIsolado63a,
                AtendenteId = conta.Id,
                HashDoDesafio = SegredosAleatoriosMfa.Sha256Hex(tokenOutro),
                Tipo = TipoDeDesafioMfa.Ticket,
                TokenVersionNoDesafio = conta.TokenVersion,
                ExpiraEm = DateTime.UtcNow.AddHours(24)
            });
            await db.SaveChangesAsync();
            ids.Add(conta.Id);

            var mfa = NovoMfaService(db);
            var sessao = await mfa.IniciarMatriculaPrivilegiadaAsync(
                new MatricularPrivilegiadoRequest(conta.Email, SenhaBoa, tokenOutro), null, default);
            var iniciado = await mfa.IniciarViaDesafioAsync(new IniciarViaDesafioRequest(sessao.Desafio), default);
            await mfa.ConfirmarViaDesafioAsync(
                new ConfirmarViaDesafioRequest(sessao.Desafio, CodigoTotp(iniciado.SegredoBase32, -1)), null, default);
        }

        // Emissao: deve ser recusada — a elegibilidade mudou. Usa o nucleo
        // atomico privado (TentarRegistrarTicketAsync), via reflexao, com um
        // hash gerado aqui mesmo no teste — nenhum metodo publico gera nem
        // registra ticket (bloqueador 9).
        await using (var db = new CrmDbContext(opcoes))
        {
            var resultado = await TentarRegistrarTicketViaReflexaoAsync(
                db, emailAlvo63a, SegredosAleatoriosMfa.Sha256Hex(SegredosAleatoriosMfa.NovoToken()),
                "teste do cenario 63a", "teste@verificador");

            Checar("63a. emissao recusada: elegibilidade mudou entre a verificacao e a emissao",
                resultado == ElegibilidadeDeBootstrap.JaExistePrivilegiadoMatriculado);
        }

        await using (var db = new CrmDbContext(opcoes))
        {
            Checar("63a-pos. nenhum ticket de bootstrap foi gravado para o alvo",
                !await db.DesafiosDeMfa.AnyAsync(d => d.AtendenteId == idAlvo63a && d.Tipo == TipoDeDesafioMfa.Ticket));
        }
    }

    // ----------------------------------------------------------- cenario 63b
    // Concorrencia: depois que a condicao de auto-fechamento se torna
    // verdadeira, NENHUMA tentativa concorrente de emitir ticket de bootstrap
    // produz ticket — nunca uma leitura obsoleta escapa pela concorrencia.

    {
        Guid tenantIsolado63b;
        await using (var db = new CrmDbContext(opcoes))
        {
            var tenant = new Tenant
            {
                RazaoSocial = "[TESTE SEC-03] Tenant isolado — bootstrap concorrente (63b)",
                Cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString()
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            tenantIsolado63b = tenant.Id;
        }
        tenantIdsDeTeste.Add(tenantIsolado63b);

        Guid idAlvo63b; string emailAlvo63b;
        await using (var db = new CrmDbContext(opcoes))
        {
            var conta = new Atendente
            {
                TenantId = tenantIsolado63b,
                Nome = "[TESTE SEC-03] 63b - alvo",
                Email = $"teste-sec-03-63b-alvo-{Guid.NewGuid():N}@teste.local",
                SenhaHash = hash.Gerar(SenhaBoa),
                Perfil = PerfilDeAtendente.Administrador,
                Ativo = true
            };
            db.Atendentes.Add(conta);
            await db.SaveChangesAsync();
            ids.Add(conta.Id);
            idAlvo63b = conta.Id;
            emailAlvo63b = conta.Email;
        }

        // Auto-fechamento JA verdadeiro ANTES de disparar as tentativas
        // concorrentes: outro privilegiado do mesmo tenant ja matriculado.
        await using (var db = new CrmDbContext(opcoes))
        {
            var conta = new Atendente
            {
                TenantId = tenantIsolado63b,
                Nome = "[TESTE SEC-03] 63b - ja matriculado",
                Email = $"teste-sec-03-63b-matriculado-{Guid.NewGuid():N}@teste.local",
                SenhaHash = hash.Gerar(SenhaBoa),
                Perfil = PerfilDeAtendente.Administrador,
                Ativo = true
            };
            db.Atendentes.Add(conta);

            var tokenJaMatriculado = SegredosAleatoriosMfa.NovoToken();
            db.DesafiosDeMfa.Add(new DesafioDeMfa
            {
                TenantId = tenantIsolado63b,
                AtendenteId = conta.Id,
                HashDoDesafio = SegredosAleatoriosMfa.Sha256Hex(tokenJaMatriculado),
                Tipo = TipoDeDesafioMfa.Ticket,
                TokenVersionNoDesafio = conta.TokenVersion,
                ExpiraEm = DateTime.UtcNow.AddHours(24)
            });
            await db.SaveChangesAsync();
            ids.Add(conta.Id);

            var mfa = NovoMfaService(db);
            var sessao = await mfa.IniciarMatriculaPrivilegiadaAsync(
                new MatricularPrivilegiadoRequest(conta.Email, SenhaBoa, tokenJaMatriculado), null, default);
            var iniciado = await mfa.IniciarViaDesafioAsync(new IniciarViaDesafioRequest(sessao.Desafio), default);
            await mfa.ConfirmarViaDesafioAsync(
                new ConfirmarViaDesafioRequest(sessao.Desafio, CodigoTotp(iniciado.SegredoBase32, -1)), null, default);
        }

        async Task<bool> TentarEmitirTicketBootstrap()
        {
            await using var db = new CrmDbContext(opcoes);
            var resultado = await TentarRegistrarTicketViaReflexaoAsync(
                db, emailAlvo63b, SegredosAleatoriosMfa.Sha256Hex(SegredosAleatoriosMfa.NovoToken()),
                "teste concorrente 63b", "teste@verificador");
            return resultado == ElegibilidadeDeBootstrap.Elegivel;
        }

        var resultados63b = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => TentarEmitirTicketBootstrap()));

        Checar("63b. 5 tentativas concorrentes DEPOIS do auto-fechamento: nenhuma produz ticket",
            resultados63b.All(r => !r));

        await using (var db = new CrmDbContext(opcoes))
        {
            Checar("63b-pos. nenhum ticket de bootstrap foi gravado para o alvo",
                !await db.DesafiosDeMfa.AnyAsync(d => d.AtendenteId == idAlvo63b && d.Tipo == TipoDeDesafioMfa.Ticket));
        }
    }

    // ----------------------------------------------------------- cenario 63c
    // TERCEIRA revisao independente — o 63b acima comeca com o auto-fechamento
    // JA verdadeiro (prova a recusa, nao a corrida). Aqui a emissao do
    // bootstrap e a conclusao da matricula que FECHA a elegibilidade disparam
    // CONCORRENTEMENTE, disputando a mesma janela — prova que o mecanismo
    // (SERIALIZABLE em TentarRegistrarTicketAsync) nao tem janela de corrida
    // propria, nao so que a checagem existe quando ja resolvida de antemao.
    // Nunca depende de receber texto claro de volta de uma API programatica —
    // so o resultado (ElegibilidadeDeBootstrap) e o estado gravado no banco.

    {
        var trials63c = 6;
        var inconsistencias63c = 0;
        var algumaRecusaPorFechamento63c = false;

        for (var i = 0; i < trials63c; i++)
        {
            Guid tenantTrial;
            await using (var db = new CrmDbContext(opcoes))
            {
                var tenant = new Tenant
                {
                    RazaoSocial = $"[TESTE SEC-03] Tenant isolado — corrida real do bootstrap 63c-{i}",
                    Cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString()
                };
                db.Tenants.Add(tenant);
                await db.SaveChangesAsync();
                tenantTrial = tenant.Id;
            }
            tenantIdsDeTeste.Add(tenantTrial);

            Guid idAlvo; string emailAlvo;
            await using (var db = new CrmDbContext(opcoes))
            {
                var conta = new Atendente
                {
                    TenantId = tenantTrial,
                    Nome = $"[TESTE SEC-03] 63c-{i} - alvo do bootstrap",
                    Email = $"teste-sec-03-63c-{i}-alvo-{Guid.NewGuid():N}@teste.local",
                    SenhaHash = hash.Gerar(SenhaBoa),
                    Perfil = PerfilDeAtendente.Administrador,
                    Ativo = true
                };
                db.Atendentes.Add(conta);
                await db.SaveChangesAsync();
                ids.Add(conta.Id);
                idAlvo = conta.Id;
                emailAlvo = conta.Email;
            }

            // Outro privilegiado com ticket JA emitido e pronto para
            // consumir — concluir a matricula dele (3 chamadas reais em
            // sequencia) e o evento que fecha a elegibilidade do tenant,
            // disparado AO MESMO TEMPO que a tentativa de emissao.
            Guid idFechamento; string emailFechamento; string ticketDeFechamento;
            await using (var db = new CrmDbContext(opcoes))
            {
                var conta = new Atendente
                {
                    TenantId = tenantTrial,
                    Nome = $"[TESTE SEC-03] 63c-{i} - fecha a elegibilidade",
                    Email = $"teste-sec-03-63c-{i}-fecha-{Guid.NewGuid():N}@teste.local",
                    SenhaHash = hash.Gerar(SenhaBoa),
                    Perfil = PerfilDeAtendente.Administrador,
                    Ativo = true
                };
                db.Atendentes.Add(conta);

                var tokenFechamento = SegredosAleatoriosMfa.NovoToken();
                db.DesafiosDeMfa.Add(new DesafioDeMfa
                {
                    TenantId = tenantTrial,
                    AtendenteId = conta.Id,
                    HashDoDesafio = SegredosAleatoriosMfa.Sha256Hex(tokenFechamento),
                    Tipo = TipoDeDesafioMfa.Ticket,
                    TokenVersionNoDesafio = conta.TokenVersion,
                    ExpiraEm = DateTime.UtcNow.AddHours(24)
                });
                await db.SaveChangesAsync();
                ids.Add(conta.Id);
                idFechamento = conta.Id;
                emailFechamento = conta.Email;
                ticketDeFechamento = tokenFechamento;
            }

            async Task<ElegibilidadeDeBootstrap> TentarEmitirAsync()
            {
                await using var db = new CrmDbContext(opcoes);
                return await TentarRegistrarTicketViaReflexaoAsync(
                    db, emailAlvo, SegredosAleatoriosMfa.Sha256Hex(SegredosAleatoriosMfa.NovoToken()),
                    $"corrida real 63c-{i}", "teste@verificador");
            }

            async Task FecharElegibilidadeAsync()
            {
                await using var db = new CrmDbContext(opcoes);
                var mfa = NovoMfaService(db);
                var sessao = await mfa.IniciarMatriculaPrivilegiadaAsync(
                    new MatricularPrivilegiadoRequest(emailFechamento, SenhaBoa, ticketDeFechamento), null, default);
                var iniciado = await mfa.IniciarViaDesafioAsync(new IniciarViaDesafioRequest(sessao.Desafio), default);
                await mfa.ConfirmarViaDesafioAsync(
                    new ConfirmarViaDesafioRequest(sessao.Desafio, CodigoTotp(iniciado.SegredoBase32, -1)), null, default);
            }

            var tarefaEmitir = TentarEmitirAsync();
            var tarefaFechar = FecharElegibilidadeAsync();
            await Task.WhenAll(tarefaEmitir, tarefaFechar);

            var resultadoEmissao = await tarefaEmitir;

            await using (var db = new CrmDbContext(opcoes))
            {
                var ticketExiste = await db.DesafiosDeMfa
                    .AnyAsync(d => d.AtendenteId == idAlvo && d.Tipo == TipoDeDesafioMfa.Ticket);
                var fechamentoAtivo = await db.Atendentes.AsNoTracking()
                    .Where(a => a.Id == idFechamento).Select(a => a.MfaAtivo).SingleAsync();

                var autoConsistente = (resultadoEmissao == ElegibilidadeDeBootstrap.Elegivel) == ticketExiste;
                var resultadoValido = resultadoEmissao == ElegibilidadeDeBootstrap.Elegivel
                    || resultadoEmissao == ElegibilidadeDeBootstrap.JaExistePrivilegiadoMatriculado;

                if (!autoConsistente || !fechamentoAtivo || !resultadoValido)
                {
                    inconsistencias63c++;
                }

                if (resultadoEmissao == ElegibilidadeDeBootstrap.JaExistePrivilegiadoMatriculado)
                {
                    algumaRecusaPorFechamento63c = true;
                }
            }
        }

        Checar($"63c. corrida REAL emissao x fechamento concorrente: resultado sempre serializado de forma consistente ({trials63c} tentativas)",
            inconsistencias63c == 0);

        Console.WriteLine(algumaRecusaPorFechamento63c
            ? "  (63c: em pelo menos uma tentativa o fechamento concorrente venceu a corrida e a emissao foi recusada.)"
            : "  (63c: em todas as tentativas a emissao venceu a corrida — resultado consistente; a recusa deterministica pelo fechamento fica provada em 63a/63b.)");
    }

    // ------------------------------------------------------------ cenario 64
    // EVIDENCIA (revisao independente do PR #46) — teste HTTP REAL contra o
    // artefato de `dotnet publish -c Release`, nao chamada direta de
    // servico e nao o assembly de build Debug.
    //
    // CORRECAO (terceira revisao independente): a versao anterior rodava
    // contra CrmCartorio.Api/bin/Debug/net10.0/CrmCartorio.Api.dll e chamava
    // isso de "binario publicado" — afirmacao mais forte que a evidencia
    // (Debug != publish). Agora publica de verdade (mesmo comando de
    // docs/DEPLOY.md: `dotnet publish -c Release`) para uma pasta descartavel
    // antes de subir o processo, e SO ENTAO roda o Kestrel real em loopback
    // e bate nele com HttpClient — prova a trava do bloqueador 1 na borda
    // HTTP real (auth JWT, roteamento, middleware, ProblemDetails) contra o
    // artefato que de fato seria implantado, que o cenario 55 (chamada
    // direta ao MfaService) nao alcanca.

    {
        var pastaDePublicacao = Path.Combine(Path.GetTempPath(), "crm-cartorio-verificacao-http-real");
        if (Directory.Exists(pastaDePublicacao))
        {
            Directory.Delete(pastaDePublicacao, recursive: true);
        }

        Console.WriteLine("64-preparo-publish. Publicando (dotnet publish -c Release) para o teste HTTP real...");

        using (var publicacao = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"publish \"{Path.GetFullPath(Path.Combine("CrmCartorio.Api", "CrmCartorio.Api.csproj"))}\" " +
                        $"-c Release -o \"{pastaDePublicacao}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!)
        {
            var stdoutPublish = await publicacao.StandardOutput.ReadToEndAsync();
            var stderrPublish = await publicacao.StandardError.ReadToEndAsync();
            await publicacao.WaitForExitAsync();

            if (publicacao.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"'dotnet publish' falhou (exit {publicacao.ExitCode}) — teste HTTP real nao pode continuar.\n{stderrPublish}\n{stdoutPublish}");
            }
        }

        var caminhoDoAssembly = Path.Combine(pastaDePublicacao, "CrmCartorio.Api.dll");
        if (!File.Exists(caminhoDoAssembly))
        {
            throw new InvalidOperationException(
                $"'{caminhoDoAssembly}' nao encontrado apos 'dotnet publish' — algo mudou na saida do publish.");
        }

        int porta;
        using (var sonda = new TcpListener(IPAddress.Loopback, 0))
        {
            sonda.Start();
            porta = ((IPEndPoint)sonda.LocalEndpoint).Port;
        }

        var baseUrl = $"http://127.0.0.1:{porta}";

        var processo = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{caminhoDoAssembly}\"",
                WorkingDirectory = Path.GetDirectoryName(caminhoDoAssembly),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        processo.StartInfo.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Development";
        processo.StartInfo.EnvironmentVariables["ASPNETCORE_URLS"] = baseUrl;
        processo.StartInfo.EnvironmentVariables["CanalWhatsApp__Habilitado"] = "false";
        processo.StartInfo.EnvironmentVariables["RespostaInteligente__Habilitada"] = "false";

        var saidaErro = new System.Text.StringBuilder();
        processo.OutputDataReceived += (_, e) => { };
        processo.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null && saidaErro.Length < 20000) saidaErro.AppendLine(e.Data);
        };

        processo.Start();
        processo.BeginOutputReadLine();
        processo.BeginErrorReadLine();

        try
        {
            var jsonOpcoes = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            };
            // Mesmo conversor que Program.cs registra para a API real — sem
            // isto, enum (ex.: Perfil) chega como string e a desserializacao
            // do lado do teste falha.
            jsonOpcoes.Converters.Add(new JsonStringEnumConverter());

            using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(10) };

            var pronto = false;
            for (var tentativa = 0; tentativa < 60 && !pronto; tentativa++)
            {
                try
                {
                    using var resposta = await http.GetAsync("/health/pronto");
                    pronto = resposta.IsSuccessStatusCode;
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) { }

                if (!pronto) await Task.Delay(500);
            }

            Checar("64-preparo. API publicada (dotnet publish -c Release) subiu e /health/pronto respondeu OK", pronto);

            if (!pronto)
            {
                var erro = saidaErro.ToString();
                Console.WriteLine($"  Diagnostico (stderr, ate 2000 chars): {erro[..Math.Min(2000, erro.Length)]}");
            }
            else
            {
                // Fixtures descartaveis: privilegiado sem MFA e comum sem
                // MFA, no tenant compartilhado — o login HTTP real busca por
                // e-mail, sem filtrar por tenant (Fase 1/2 sao single-tenant;
                // ver o mesmo padrao ja usado nos cenarios 58/59 com contas
                // de tenant isolado).
                var (idPriv, emailPriv) = await NovaContaPrivilegiada("64 - privilegiado via HTTP real");
                var (idComum, emailComum) = await NovaContaPrivilegiada("64 - comum via HTTP real", perfil: PerfilDeAtendente.Atendente);

                async Task<string?> LoginHttpAsync(string email)
                {
                    using var resposta = await http.PostAsJsonAsync("/api/autenticacao/login",
                        new { email, senha = SenhaBoa }, jsonOpcoes);
                    if (!resposta.IsSuccessStatusCode) return null;
                    var corpo = await resposta.Content.ReadFromJsonAsync<LoginEtapa1Response>(jsonOpcoes);
                    return corpo?.Sessao?.Token;
                }

                var jwtPriv = await LoginHttpAsync(emailPriv);
                var jwtComum = await LoginHttpAsync(emailComum);

                Checar("64a. login HTTP real (privilegiado sem MFA, senha correta) devolve JWT", !string.IsNullOrEmpty(jwtPriv));
                Checar("64b. login HTTP real (comum sem MFA, senha correta) devolve JWT", !string.IsNullOrEmpty(jwtComum));

                async Task<(HttpStatusCode Status, string Corpo)> IniciarMfaHttpAsync(string jwt)
                {
                    using var requisicao = new HttpRequestMessage(HttpMethod.Post, "/api/mfa/iniciar")
                    {
                        Content = JsonContent.Create(new { senhaAtual = SenhaBoa, codigoAtual = (string?)null }, options: jsonOpcoes)
                    };
                    requisicao.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwt);

                    using var resposta = await http.SendAsync(requisicao);
                    var corpo = await resposta.Content.ReadAsStringAsync();
                    return (resposta.StatusCode, corpo);
                }

                // MUDANCA DE REGRA: privilegiado sem MFA agora ativa
                // voluntariamente pelo fluxo HTTP comum, igual a conta comum —
                // prova na borda real (nao so na chamada direta ao servico do
                // cenario 55), com o artefato publicado de verdade.
                var (statusPriv, corpoPriv) = await IniciarMfaHttpAsync(jwtPriv ?? string.Empty);
                Checar("64c. POST /api/mfa/iniciar real (privilegiado sem MFA, JWT+senha corretos) -> 200 OK (ativacao voluntaria permitida)",
                    statusPriv == HttpStatusCode.OK);
                Checar("64d. corpo real do privilegiado traz um segredoBase32",
                    corpoPriv.Contains("segredoBase32"));

                var (statusComum, corpoComum) = await IniciarMfaHttpAsync(jwtComum ?? string.Empty);
                Checar("64e. POST /api/mfa/iniciar real (conta comum sem MFA) -> 200, continua funcionando",
                    statusComum == HttpStatusCode.OK);
                Checar("64f. corpo real da conta comum traz um segredoBase32",
                    corpoComum.Contains("segredoBase32"));

                // 64g — o privilegiado consegue CONCLUIR a ativacao pela
                // borda HTTP real: /confirmar com o TOTP correto tem que
                // suceder, exatamente como aconteceria para a conta comum.
                var segredoPriv = JsonDocument.Parse(corpoPriv).RootElement.GetProperty("segredoBase32").GetString()!;
                using var requisicaoConfirmar = new HttpRequestMessage(HttpMethod.Post, "/api/mfa/confirmar")
                {
                    Content = JsonContent.Create(new { codigo = CodigoTotp(segredoPriv) }, options: jsonOpcoes)
                };
                requisicaoConfirmar.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwtPriv ?? string.Empty);
                using var respostaConfirmar = await http.SendAsync(requisicaoConfirmar);
                var corpoConfirmar = await respostaConfirmar.Content.ReadAsStringAsync();

                Checar("64g. POST /api/mfa/confirmar real (privilegiado, TOTP correto) -> sucesso, MFA ativado pela borda HTTP",
                    respostaConfirmar.IsSuccessStatusCode && corpoConfirmar.Contains("codigosDeRecuperacao"));

                Checar("64g2. o privilegiado esta com MfaAtivo=true no banco apos a confirmacao HTTP real",
                    (await Ler(idPriv)).MfaAtivo);

                // ---------------------------------------------------------
                // SEC-03 "confiar neste dispositivo": os atributos do cookie
                // sao a defesa contra XSS (HttpOnly) e CSRF (SameSite), e nao
                // aparecem em nenhum teste de chamada direta ao servico — so
                // no cabecalho HTTP de verdade. Por isso a leitura e do
                // Set-Cookie CRU, e nao de um CookieContainer (que ja teria
                // interpretado e descartado os atributos).
                // ---------------------------------------------------------
                {
                    var (_, emailCookie) = await NovaContaPrivilegiada(
                        "64 - cookie de dispositivo", perfil: PerfilDeAtendente.Atendente);

                    // A matricula tem que passar pela BORDA HTTP, nao pelo
                    // MfaService local: o segredo TOTP e guardado cifrado, e a
                    // chave deste verificador (aleatoria, so dele) nao e a que
                    // a API publicada carrega do appsettings. Segredo gravado
                    // aqui sairia ilegivel la — e o codigo, sempre invalido.
                    var jwtCookie = await LoginHttpAsync(emailCookie);
                    var (_, corpoIniciarCookie) = await IniciarMfaHttpAsync(jwtCookie ?? string.Empty);
                    var segredoCookie = JsonDocument.Parse(corpoIniciarCookie)
                        .RootElement.GetProperty("segredoBase32").GetString()!;

                    using (var requisicaoConfirmarCookie = new HttpRequestMessage(HttpMethod.Post, "/api/mfa/confirmar")
                    {
                        Content = JsonContent.Create(new { codigo = CodigoTotp(segredoCookie, -1) }, options: jsonOpcoes)
                    })
                    {
                        requisicaoConfirmarCookie.Headers.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwtCookie ?? string.Empty);
                        using var respostaConfirmarCookie = await http.SendAsync(requisicaoConfirmarCookie);
                        Checar("64h-preparo. matricula de MFA pela borda HTTP real conclui",
                            respostaConfirmarCookie.IsSuccessStatusCode);
                    }

                    // Cliente SEM jar de cookies: o HttpClient padrao guardaria
                    // e reenviaria o cookie sozinho, e o teste negativo ("sem o
                    // cookie ainda pede o codigo") passaria a testar o
                    // HttpClient, nao a API. Aqui cada cookie enviado e
                    // colocado a mao, de proposito.
                    using var semJar = new HttpClientHandler { UseCookies = false };
                    using var httpDispositivo = new HttpClient(semJar)
                    {
                        BaseAddress = new Uri(baseUrl),
                        Timeout = TimeSpan.FromSeconds(10)
                    };

                    using var respostaEtapa1 = await httpDispositivo.PostAsJsonAsync("/api/autenticacao/login",
                        new { email = emailCookie, senha = SenhaBoa }, jsonOpcoes);
                    var etapa1Http = await respostaEtapa1.Content.ReadFromJsonAsync<LoginEtapa1Response>(jsonOpcoes);

                    Checar("64h. login HTTP real de conta com MFA devolve desafio, nunca sessao",
                        etapa1Http is { RequerMfa: true, Sessao: null, Desafio: not null });

                    using var respostaEtapa2 = await httpDispositivo.PostAsJsonAsync("/api/autenticacao/login/mfa",
                        new
                        {
                            desafio = etapa1Http!.Desafio!.Desafio,
                            codigo = CodigoTotp(segredoCookie),
                            confiarNesteDispositivo = true
                        }, jsonOpcoes);

                    var setCookie = respostaEtapa2.Headers.TryGetValues("Set-Cookie", out var valores)
                        ? valores.FirstOrDefault(v => v.StartsWith("crm_dispositivo_confiavel=", StringComparison.Ordinal))
                        : null;

                    Checar("64i. concluir o segundo fator com confiarNesteDispositivo emite o Set-Cookie",
                        respostaEtapa2.IsSuccessStatusCode && setCookie is not null);

                    Checar("64j. o cookie e HttpOnly — JavaScript da pagina nunca alcanca o token",
                        setCookie?.Contains("httponly", StringComparison.OrdinalIgnoreCase) == true);

                    Checar("64k. o cookie e SameSite=Strict — nao viaja em requisicao de outro site (CSRF)",
                        setCookie?.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase) == true);

                    // Comparacao EXATA, nao Contains: "path=/api/autenticacao"
                    // e substring de "path=/api/autenticacao/login", entao um
                    // Contains passaria feliz se alguem alargasse o caminho de
                    // volta — exatamente a regressao que este check existe para
                    // pegar.
                    Checar("64l. o cookie fica preso ao caminho EXATO /api/autenticacao/login — nao acompanha as rotas irmas",
                        CaminhoDoSetCookie(setCookie) == "/api/autenticacao/login");

                    // Esta API de teste sobe com ASPNETCORE_ENVIRONMENT=Development
                    // (ver o preparo do cenario), em HTTP puro no loopback. A
                    // ausencia de `Secure` aqui e decisao explicita de AMBIENTE,
                    // nao do esquema da requisicao: em Development o painel roda
                    // em localhost sobre HTTP, e um `Secure` fixo faria o
                    // navegador descartar o cookie em silencio. Teste e Production
                    // marcam `Secure` independentemente de Request.IsHttps — e o
                    // que 64s prova, com a MESMA API publicada, tambem em HTTP puro.
                    Checar("64m. em Development sobre HTTP puro o cookie NAO usa Secure — decisao explicita de ambiente, nao do esquema da requisicao",
                        setCookie?.Contains("secure", StringComparison.OrdinalIgnoreCase) == false);

                    Checar("64n. o cookie carrega uma data de expiracao (nao e cookie de sessao)",
                        setCookie?.Contains("expires=", StringComparison.OrdinalIgnoreCase) == true);

                    // Replay do cookie na borda HTTP: o mesmo valor devolvido
                    // pelo servidor dispensa o segundo fator no proximo login.
                    var valorDoCookie = setCookie!.Split(';')[0];
                    using var requisicaoComCookie = new HttpRequestMessage(HttpMethod.Post, "/api/autenticacao/login")
                    {
                        Content = JsonContent.Create(new { email = emailCookie, senha = SenhaBoa }, options: jsonOpcoes)
                    };
                    requisicaoComCookie.Headers.Add("Cookie", valorDoCookie);

                    using var respostaComCookie = await httpDispositivo.SendAsync(requisicaoComCookie);
                    var etapa1ComCookie = await respostaComCookie.Content.ReadFromJsonAsync<LoginEtapa1Response>(jsonOpcoes);

                    Checar("64o. reapresentar o cookie na borda HTTP dispensa o segundo fator e ja devolve a sessao",
                        etapa1ComCookie is { RequerMfa: false, Sessao: not null, Desafio: null });

                    using var requisicaoSemCookie = await httpDispositivo.PostAsJsonAsync("/api/autenticacao/login",
                        new { email = emailCookie, senha = SenhaBoa }, jsonOpcoes);
                    var etapa1SemCookie = await requisicaoSemCookie.Content.ReadFromJsonAsync<LoginEtapa1Response>(jsonOpcoes);

                    Checar("64p. sem o cookie, a mesma conta continua exigindo o segundo fator",
                        etapa1SemCookie is { RequerMfa: true, Sessao: null });
                }

                // ---------------------------------------------------------
                // SEC-03 — a MESMA API publicada, subida de novo com
                // ASPNETCORE_ENVIRONMENT=Teste, ainda em HTTP puro no
                // loopback. E exatamente a forma da infraestrutura real: TLS
                // termina na Cloudflare e a perna interna do IIS chega em
                // HTTP. Aqui `Request.IsHttps` e false — e o cookie tem que
                // sair COM `Secure` mesmo assim.
                // ---------------------------------------------------------
                {
                    int portaTeste;
                    using (var sondaTeste = new TcpListener(IPAddress.Loopback, 0))
                    {
                        sondaTeste.Start();
                        portaTeste = ((IPEndPoint)sondaTeste.LocalEndpoint).Port;
                    }

                    var baseUrlTeste = $"http://127.0.0.1:{portaTeste}";

                    var processoTeste = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "dotnet",
                            Arguments = $"\"{caminhoDoAssembly}\"",
                            WorkingDirectory = Path.GetDirectoryName(caminhoDoAssembly),
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        }
                    };

                    // Ambiente Teste nao carrega appsettings.Development.json,
                    // entao o que a API exige para subir vai por variavel de
                    // ambiente. Chaves DESCARTAVEIS, geradas agora, so deste
                    // processo de teste — nunca segredo real, nunca versionado.
                    processoTeste.StartInfo.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Teste";
                    processoTeste.StartInfo.EnvironmentVariables["ASPNETCORE_URLS"] = baseUrlTeste;
                    processoTeste.StartInfo.EnvironmentVariables["ConnectionStrings__SqlServer"] = conexao;
                    processoTeste.StartInfo.EnvironmentVariables["Jwt__Chave"] =
                        Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
                    processoTeste.StartInfo.EnvironmentVariables["Mfa__VersaoCorrente"] = "1";
                    processoTeste.StartInfo.EnvironmentVariables["Mfa__Chaves__1"] =
                        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                    processoTeste.StartInfo.EnvironmentVariables["CanalWhatsApp__Habilitado"] = "false";
                    processoTeste.StartInfo.EnvironmentVariables["RespostaInteligente__Habilitada"] = "false";
                    processoTeste.StartInfo.EnvironmentVariables["Automacao__VarrerPrazos"] = "false";

                    var erroDoTeste = new System.Text.StringBuilder();
                    processoTeste.OutputDataReceived += (_, e) => { };
                    processoTeste.ErrorDataReceived += (_, e) =>
                    {
                        if (e.Data is not null && erroDoTeste.Length < 20000) erroDoTeste.AppendLine(e.Data);
                    };

                    processoTeste.Start();
                    processoTeste.BeginOutputReadLine();
                    processoTeste.BeginErrorReadLine();

                    try
                    {
                        using var semJarTeste = new HttpClientHandler { UseCookies = false };
                        using var httpTeste = new HttpClient(semJarTeste)
                        {
                            BaseAddress = new Uri(baseUrlTeste),
                            Timeout = TimeSpan.FromSeconds(10)
                        };

                        var prontoTeste = false;
                        for (var tentativa = 0; tentativa < 60 && !prontoTeste; tentativa++)
                        {
                            try
                            {
                                using var resposta = await httpTeste.GetAsync("/health/pronto");
                                prontoTeste = resposta.IsSuccessStatusCode;
                            }
                            catch (HttpRequestException) { }
                            catch (TaskCanceledException) { }

                            if (!prontoTeste) await Task.Delay(500);
                        }

                        Checar("64q-preparo. a MESMA API publicada sobe com ASPNETCORE_ENVIRONMENT=Teste em HTTP puro",
                            prontoTeste);

                        if (!prontoTeste)
                        {
                            var erro = erroDoTeste.ToString();
                            Console.WriteLine($"  Diagnostico: {erro[..Math.Min(2000, erro.Length)]}");
                        }
                        else
                        {
                            var (_, emailTeste) = await NovaContaPrivilegiada(
                                "64 - cookie em ambiente Teste", perfil: PerfilDeAtendente.Atendente);

                            // Matricula pela borda desta instancia: a cifra do
                            // segredo TOTP usa a chave DELA, nao a do processo
                            // Development nem a deste verificador.
                            using var loginTeste = await httpTeste.PostAsJsonAsync("/api/autenticacao/login",
                                new { email = emailTeste, senha = SenhaBoa }, jsonOpcoes);
                            var jwtTeste = (await loginTeste.Content.ReadFromJsonAsync<LoginEtapa1Response>(jsonOpcoes))
                                ?.Sessao?.Token;

                            string segredoTeste;
                            using (var iniciarTeste = new HttpRequestMessage(HttpMethod.Post, "/api/mfa/iniciar")
                            {
                                Content = JsonContent.Create(
                                    new { senhaAtual = SenhaBoa, codigoAtual = (string?)null }, options: jsonOpcoes)
                            })
                            {
                                iniciarTeste.Headers.Authorization =
                                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwtTeste ?? string.Empty);
                                using var respostaIniciarTeste = await httpTeste.SendAsync(iniciarTeste);
                                segredoTeste = JsonDocument.Parse(await respostaIniciarTeste.Content.ReadAsStringAsync())
                                    .RootElement.GetProperty("segredoBase32").GetString()!;
                            }

                            using (var confirmarTeste = new HttpRequestMessage(HttpMethod.Post, "/api/mfa/confirmar")
                            {
                                Content = JsonContent.Create(new { codigo = CodigoTotp(segredoTeste, -1) }, options: jsonOpcoes)
                            })
                            {
                                confirmarTeste.Headers.Authorization =
                                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwtTeste ?? string.Empty);
                                using var respostaConfirmarTeste = await httpTeste.SendAsync(confirmarTeste);
                                Checar("64q. matricula de MFA pela API em ambiente Teste conclui",
                                    respostaConfirmarTeste.IsSuccessStatusCode);
                            }

                            using var etapa1Teste = await httpTeste.PostAsJsonAsync("/api/autenticacao/login",
                                new { email = emailTeste, senha = SenhaBoa }, jsonOpcoes);
                            var desafioTeste = await etapa1Teste.Content.ReadFromJsonAsync<LoginEtapa1Response>(jsonOpcoes);

                            using var etapa2Teste = await httpTeste.PostAsJsonAsync("/api/autenticacao/login/mfa",
                                new
                                {
                                    desafio = desafioTeste!.Desafio!.Desafio,
                                    codigo = CodigoTotp(segredoTeste),
                                    confiarNesteDispositivo = true
                                }, jsonOpcoes);

                            var setCookieTeste = etapa2Teste.Headers.TryGetValues("Set-Cookie", out var valoresTeste)
                                ? valoresTeste.FirstOrDefault(v => v.StartsWith("crm_dispositivo_confiavel=", StringComparison.Ordinal))
                                : null;

                            Checar("64r. ambiente Teste emite o Set-Cookie do dispositivo confiavel",
                                etapa2Teste.IsSuccessStatusCode && setCookieTeste is not null);

                            // O ponto do bloqueador: HTTP puro na perna
                            // interna, `Request.IsHttps == false`, e mesmo
                            // assim o cookie sai marcado como Secure.
                            Checar("64s. em Teste, sobre HTTP puro, o cookie SAI COM Secure — nao depende de Request.IsHttps",
                                setCookieTeste?.Contains("secure", StringComparison.OrdinalIgnoreCase) == true);

                            Checar("64t. em Teste, HttpOnly e SameSite=Strict continuam presentes",
                                setCookieTeste?.Contains("httponly", StringComparison.OrdinalIgnoreCase) == true
                                && setCookieTeste.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));

                            Checar("64u. em Teste, o Path continua exatamente /api/autenticacao/login",
                                CaminhoDoSetCookie(setCookieTeste) == "/api/autenticacao/login");
                        }
                    }
                    finally
                    {
                        if (!processoTeste.HasExited)
                        {
                            try { processoTeste.Kill(entireProcessTree: true); } catch { }
                            try { processoTeste.WaitForExit(5000); } catch { }
                        }
                        processoTeste.Dispose();
                    }
                }
            }
        }
        finally
        {
            if (!processo.HasExited)
            {
                try { processo.Kill(entireProcessTree: true); } catch { }
                try { processo.WaitForExit(5000); } catch { }
            }
            processo.Dispose();

            // Artefato descartavel do publish — nunca faz parte do repositorio.
            try
            {
                if (Directory.Exists(pastaDePublicacao))
                {
                    Directory.Delete(pastaDePublicacao, recursive: true);
                }
            }
            catch { }
        }
    }

    // ------------------------------------------------------------ cenario 65
    // BLOQUEADOR 7 (e sua correcao no bloqueador 9) — verificacao
    // ESTRUTURAL/REGRESSIVA: nao basta "nenhum metodo publico devolve texto
    // claro" (isso ainda deixava TentarRegistrarTicketAsync publico, e
    // qualquer chamador podia gerar o proprio texto claro, calcular o hash e
    // registra-lo por essa API — o proprio cenario 63c antigo fazia
    // exatamente isso). A prova agora e mais forte: o UNICO metodo publico
    // mutavel da ferramenta de bootstrap e o entrypoint interativo protegido
    // (ExecutarAsync); o resto do superficie publica e so leitura
    // (VerificarElegibilidadeAsync, que nem recebe os parametros necessarios
    // para produzir um ticket). Prova por reflexao, nao por leitura de
    // codigo — se algum dia alguem tornar TentarRegistrarTicketAsync (ou
    // qualquer metodo novo) publico de novo, este cenario quebra sozinho,
    // sem depender de revisao humana notar.

    {
        var tipoDaFerramenta = typeof(FerramentaDeBootstrapMfa);
        var metodosPublicos = tipoDaFerramenta
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Checar("65a. a superficie publica de FerramentaDeBootstrapMfa e EXATAMENTE {ExecutarAsync, VerificarElegibilidadeAsync} — nada mais",
            metodosPublicos.SequenceEqual(new[] { "ExecutarAsync", "VerificarElegibilidadeAsync" }, StringComparer.Ordinal));

        var verificarElegibilidade = tipoDaFerramenta.GetMethod("VerificarElegibilidadeAsync", BindingFlags.Public | BindingFlags.Static);
        Checar("65b. VerificarElegibilidadeAsync (o unico metodo publico alem do entrypoint) so recebe email — nao tem como produzir/registrar ticket",
            verificarElegibilidade is not null
            && verificarElegibilidade.GetParameters().Select(p => p.Name).Where(n => n != "ct").SequenceEqual(["db", "email"]));

        Checar("65c. EmitirTicketAsync (gera texto claro) nao e publico",
            tipoDaFerramenta.GetMethod("EmitirTicketAsync", BindingFlags.Public | BindingFlags.Static) is null);
        Checar("65c-existe. EmitirTicketAsync continua existindo, so que privado (alcancavel exclusivamente por ExecutarAsync)",
            tipoDaFerramenta.GetMethod("EmitirTicketAsync", BindingFlags.NonPublic | BindingFlags.Static) is not null);

        Checar("65d. TentarRegistrarTicketAsync (o nucleo atomico que grava o ticket) NAO e publico",
            tipoDaFerramenta.GetMethod("TentarRegistrarTicketAsync", BindingFlags.Public | BindingFlags.Static) is null);

        var nucleoAtomico = tipoDaFerramenta.GetMethod("TentarRegistrarTicketAsync", BindingFlags.NonPublic | BindingFlags.Static);
        Checar("65d-existe. TentarRegistrarTicketAsync continua existindo, privado, so alcancavel via reflexao (nunca por codigo de producao)",
            nucleoAtomico is not null && nucleoAtomico.ReturnType == typeof(Task<ElegibilidadeDeBootstrap>));

        Checar("65e. TentarRegistrarTicketAsync nao aceita TTL do chamador — o TTL do ticket e decidido so pelo codigo de producao",
            nucleoAtomico!.GetParameters().All(p => p.ParameterType != typeof(TimeSpan)));

        Checar("65f. TentarRegistrarTicketAsync recebe o ticket so como HASH (string hashDoTicket), nunca gera nem devolve o texto claro",
            nucleoAtomico.GetParameters().Any(p => p.Name == "hashDoTicket" && p.ParameterType == typeof(string)));
    }

    // ------------------------------------------------------------ cenario 66
    // BLOQUEADOR 8 — o segredo MFA pendente (ativacao voluntaria OU troca de
    // autenticador) fica vinculado a TokenVersion do instante em que foi
    // gerado (MfaPendenteTokenVersion). Qualquer evento que suba a
    // TokenVersion entre iniciar e confirmar invalida a cerimonia pendente,
    // mesmo com o codigo TOTP correto.

    {
        // 66a — comum inicia MFA (primeira ativacao voluntaria) -> senha
        // muda -> confirmacao antiga recusada.
        var (idA, emailA) = await NovaContaPrivilegiada("bloqueador8 - comum, senha muda", perfil: PerfilDeAtendente.Atendente);

        // JWT capturado ANTES da revogacao (66h usa isto depois) — mesmo
        // idioma dos cenarios 1-8 (tokens.Gerar + LerVersaoDaClaim).
        var (tokenAntesDaRevogacaoA, _) = tokens.Gerar(await Ler(idA));
        var versaoAntesDaRevogacaoA = LerVersaoDaClaim(tokenAntesDaRevogacaoA);

        IniciarMfaResponse iniciadoA;
        await using (var db = new CrmDbContext(opcoes))
        {
            iniciadoA = await NovoMfaService(db).IniciarAsync(
                new IdentidadeParaMfa(tenantId, idA), new IniciarMfaRequest(SenhaBoa, null), null, default);
        }
        var codigoA = CodigoTotp(iniciadoA.SegredoBase32);

        await using (var db = new CrmDbContext(opcoes))
        {
            await NovoAutenticacao(db, idA).AlterarSenhaAsync(new AlterarSenhaRequest(SenhaBoa, "SenhaNova66a#1"));
        }

        await using (var db = new CrmDbContext(opcoes))
        {
            UsuariosServiceException? falha = null;
            try
            {
                await NovoMfaService(db).ConfirmarAsync(
                    new IdentidadeParaMfa(tenantId, idA), new ConfirmarMfaRequest(codigoA), null, default);
            }
            catch (UsuariosServiceException ex) { falha = ex; }

            Checar("66a. comum inicia MFA -> senha muda -> confirmacao antiga (codigo TOTP correto) e recusada",
                falha?.Codigo == "codigo_mfa_invalido");
        }
        Checar("66a-pos. MfaAtivo continua false — a cerimonia antiga nao completou",
            !(await Ler(idA)).MfaAtivo);

        // 66b — conta com MFA ativo inicia TROCA de autenticador -> senha
        // muda -> confirmacao antiga recusada (o segredo ANTIGO continua
        // sendo o que autentica).
        var (idB, emailB, segredoB, _) = await NovaContaComMfaAtivo("bloqueador8 - troca, senha muda");

        IniciarMfaResponse iniciadoB;
        await using (var db = new CrmDbContext(opcoes))
        {
            iniciadoB = await NovoMfaService(db).IniciarAsync(
                new IdentidadeParaMfa(tenantId, idB), new IniciarMfaRequest(SenhaBoa, CodigoTotp(segredoB)), null, default);
        }
        var codigoB = CodigoTotp(iniciadoB.SegredoBase32, -1);

        await using (var db = new CrmDbContext(opcoes))
        {
            await NovoAutenticacao(db, idB).AlterarSenhaAsync(new AlterarSenhaRequest(SenhaBoa, "SenhaNova66b#1"));
        }

        await using (var db = new CrmDbContext(opcoes))
        {
            UsuariosServiceException? falha = null;
            try
            {
                await NovoMfaService(db).ConfirmarAsync(
                    new IdentidadeParaMfa(tenantId, idB), new ConfirmarMfaRequest(codigoB), null, default);
            }
            catch (UsuariosServiceException ex) { falha = ex; }

            Checar("66b. troca de autenticador -> senha muda -> confirmacao antiga recusada",
                falha?.Codigo == "codigo_mfa_invalido");
        }
        Checar("66b-pos. MfaAtivo continua true com o segredo ANTIGO (a troca nunca completou)",
            (await Ler(idB)).MfaAtivo);

        // 66c — troca de autenticador -> TokenVersion sobe por mudanca de
        // Perfil (Administrador -> Supervisor: fora da fronteira de
        // privilegio, so para isolar bloqueador 8 de bloqueador 5) ->
        // confirmacao antiga recusada.
        var (idC, emailC, segredoC, _) = await NovaContaComMfaAtivo("bloqueador8 - troca, Perfil muda");

        IniciarMfaResponse iniciadoC;
        await using (var db = new CrmDbContext(opcoes))
        {
            iniciadoC = await NovoMfaService(db).IniciarAsync(
                new IdentidadeParaMfa(tenantId, idC), new IniciarMfaRequest(SenhaBoa, CodigoTotp(segredoC)), null, default);
        }
        var codigoC = CodigoTotp(iniciadoC.SegredoBase32, -1);

        await using (var db = new CrmDbContext(opcoes))
        {
            await NovoAtendenteService(db, Guid.NewGuid()).AtualizarAsync(idC,
                new AtualizarAtendenteRequest("Nome", emailC, PerfilDeAtendente.Supervisor, null, true));
        }

        await using (var db = new CrmDbContext(opcoes))
        {
            UsuariosServiceException? falha = null;
            try
            {
                await NovoMfaService(db).ConfirmarAsync(
                    new IdentidadeParaMfa(tenantId, idC), new ConfirmarMfaRequest(codigoC), null, default);
            }
            catch (UsuariosServiceException ex) { falha = ex; }

            Checar("66c. troca de autenticador -> TokenVersion sobe por Perfil -> confirmacao antiga recusada",
                falha?.Codigo == "codigo_mfa_invalido");
        }

        // 66d — troca de autenticador -> TokenVersion sobe pela FRONTEIRA de
        // Cargo (Titular -> Substituto: MfaAtivo=true ja libera o WHERE do
        // bloqueador 5, entao isto testa bloqueador 8 isoladamente) ->
        // confirmacao antiga recusada.
        var (idD, emailD, segredoD, _) = await NovaContaComMfaAtivo(
            "bloqueador8 - troca, fronteira de Cargo", perfil: PerfilDeAtendente.Atendente, cargo: CargoNaServentia.Titular);

        IniciarMfaResponse iniciadoD;
        await using (var db = new CrmDbContext(opcoes))
        {
            iniciadoD = await NovoMfaService(db).IniciarAsync(
                new IdentidadeParaMfa(tenantId, idD), new IniciarMfaRequest(SenhaBoa, CodigoTotp(segredoD)), null, default);
        }
        var codigoD = CodigoTotp(iniciadoD.SegredoBase32, -1);

        await using (var db = new CrmDbContext(opcoes))
        {
            await NovoAtendenteService(db, Guid.NewGuid()).AtualizarAsync(idD,
                new AtualizarAtendenteRequest("Nome", emailD, PerfilDeAtendente.Atendente, null, true, CargoNaServentia.Substituto));
        }

        await using (var db = new CrmDbContext(opcoes))
        {
            UsuariosServiceException? falha = null;
            try
            {
                await NovoMfaService(db).ConfirmarAsync(
                    new IdentidadeParaMfa(tenantId, idD), new ConfirmarMfaRequest(codigoD), null, default);
            }
            catch (UsuariosServiceException ex) { falha = ex; }

            Checar("66d. troca de autenticador -> TokenVersion sobe pela fronteira de Cargo -> confirmacao antiga recusada",
                falha?.Codigo == "codigo_mfa_invalido");
        }

        // 66e — troca de autenticador -> politica obrigatoria liga ENTRE
        // iniciar e confirmar (sobe TokenVersion do proprio ator) ->
        // cerimonia antiga recusada. Precisa de tenant isolado (o flip exige
        // TODOS os privilegiados ja matriculados).
        Guid tenantIsolado66e;
        await using (var db = new CrmDbContext(opcoes))
        {
            var tenant = new Tenant
            {
                RazaoSocial = "[TESTE SEC-03] Tenant isolado — bloqueador 8, politica entre iniciar/confirmar (66e)",
                Cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString()
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            tenantIsolado66e = tenant.Id;
        }
        tenantIdsDeTeste.Add(tenantIsolado66e);

        Guid idE; string emailE; string segredoE;
        await using (var db = new CrmDbContext(opcoes))
        {
            var conta = new Atendente
            {
                TenantId = tenantIsolado66e,
                Nome = "[TESTE SEC-03] 66e - ator do flip e da troca",
                Email = $"teste-sec-03-66e-{Guid.NewGuid():N}@teste.local",
                SenhaHash = hash.Gerar(SenhaBoa),
                Perfil = PerfilDeAtendente.Administrador,
                Ativo = true
            };
            db.Atendentes.Add(conta);

            var tokenEmClaro = SegredosAleatoriosMfa.NovoToken();
            db.DesafiosDeMfa.Add(new DesafioDeMfa
            {
                TenantId = tenantIsolado66e,
                AtendenteId = conta.Id,
                HashDoDesafio = SegredosAleatoriosMfa.Sha256Hex(tokenEmClaro),
                Tipo = TipoDeDesafioMfa.Ticket,
                TokenVersionNoDesafio = conta.TokenVersion,
                ExpiraEm = DateTime.UtcNow.AddHours(24)
            });
            await db.SaveChangesAsync();
            ids.Add(conta.Id);
            idE = conta.Id;
            emailE = conta.Email;

            var mfa = NovoMfaService(db);
            var sessao = await mfa.IniciarMatriculaPrivilegiadaAsync(
                new MatricularPrivilegiadoRequest(conta.Email, SenhaBoa, tokenEmClaro), null, default);
            var iniciado = await mfa.IniciarViaDesafioAsync(new IniciarViaDesafioRequest(sessao.Desafio), default);
            await mfa.ConfirmarViaDesafioAsync(
                new ConfirmarViaDesafioRequest(sessao.Desafio, CodigoTotp(iniciado.SegredoBase32, -1)), null, default);
            segredoE = iniciado.SegredoBase32;
        }

        // Passo -1 ja consumido na matricula acima; passo 0 esta livre para
        // o step-up do IniciarAsync (troca de autenticador).
        IniciarMfaResponse iniciadoE;
        await using (var db = new CrmDbContext(opcoes))
        {
            iniciadoE = await NovoMfaService(db).IniciarAsync(
                new IdentidadeParaMfa(tenantIsolado66e, idE), new IniciarMfaRequest(SenhaBoa, CodigoTotp(segredoE)), null, default);
        }
        var codigoE = CodigoTotp(iniciadoE.SegredoBase32, -1);

        // Passo 1 (o proximo livre) para o step-up do proprio flip.
        await using (var db = new CrmDbContext(opcoes))
        {
            await NovoMfaService(db).AtivarObrigatoriedadeAsync(
                new IdentidadeParaMfa(tenantIsolado66e, idE),
                new AtivarObrigatoriedadeRequest(SenhaBoa, CodigoTotp(segredoE, 1), "teste do cenario 66e"),
                null, default);
        }

        await using (var db = new CrmDbContext(opcoes))
        {
            UsuariosServiceException? falha = null;
            try
            {
                await NovoMfaService(db).ConfirmarAsync(
                    new IdentidadeParaMfa(tenantIsolado66e, idE), new ConfirmarMfaRequest(codigoE), null, default);
            }
            catch (UsuariosServiceException ex) { falha = ex; }

            Checar("66e. politica obrigatoria liga ENTRE iniciar e confirmar a troca -> cerimonia antiga recusada",
                falha?.Codigo == "codigo_mfa_invalido");
        }

        // 66f — regressao: SEM nenhuma alteracao de TokenVersion entre
        // iniciar e confirmar, a confirmacao legitima continua funcionando —
        // tanto na primeira ativacao quanto na troca de autenticador.
        var (idF, emailF) = await NovaContaPrivilegiada("bloqueador8 - regressao, sem mudanca", perfil: PerfilDeAtendente.Atendente);

        IniciarMfaResponse iniciadoF;
        await using (var db = new CrmDbContext(opcoes))
        {
            iniciadoF = await NovoMfaService(db).IniciarAsync(
                new IdentidadeParaMfa(tenantId, idF), new IniciarMfaRequest(SenhaBoa, null), null, default);
        }

        ConfirmarMfaResponse confirmadoF;
        await using (var db = new CrmDbContext(opcoes))
        {
            confirmadoF = await NovoMfaService(db).ConfirmarAsync(
                new IdentidadeParaMfa(tenantId, idF), new ConfirmarMfaRequest(CodigoTotp(iniciadoF.SegredoBase32)), null, default);
        }

        Checar("66f. sem alteracao de TokenVersion, confirmacao legitima continua funcionando",
            !string.IsNullOrEmpty(confirmadoF.Sessao.Token) && (await Ler(idF)).MfaAtivo);

        // 66g — corrida REAL: ConfirmarAsync concorrente com um evento que
        // sobe TokenVersion (troca de senha). Mesma tecnica de 62c: como as
        // duas operacoes escrevem na MESMA linha, o SQL Server serializa por
        // lock de linha — o resultado reportado pela confirmacao sempre bate
        // com o MfaAtivo final gravado, em qualquer ordem de vitoria.
        var trials66g = 5;
        var inconsistencias66g = 0;

        for (var i = 0; i < trials66g; i++)
        {
            var (idG, emailG) = await NovaContaPrivilegiada($"bloqueador8 - corrida {i}", perfil: PerfilDeAtendente.Atendente);

            IniciarMfaResponse iniciadoG;
            await using (var db = new CrmDbContext(opcoes))
            {
                iniciadoG = await NovoMfaService(db).IniciarAsync(
                    new IdentidadeParaMfa(tenantId, idG), new IniciarMfaRequest(SenhaBoa, null), null, default);
            }
            var codigoG = CodigoTotp(iniciadoG.SegredoBase32);

            async Task<bool> TentarConfirmarAsync()
            {
                await using var db = new CrmDbContext(opcoes);
                try
                {
                    await NovoMfaService(db).ConfirmarAsync(
                        new IdentidadeParaMfa(tenantId, idG), new ConfirmarMfaRequest(codigoG), null, default);
                    return true;
                }
                catch (UsuariosServiceException)
                {
                    return false;
                }
            }

            async Task TrocarSenhaAsync()
            {
                await using var db = new CrmDbContext(opcoes);
                await NovoAutenticacao(db, idG).AlterarSenhaAsync(new AlterarSenhaRequest(SenhaBoa, $"SenhaCorrida66g#{i}"));
            }

            var tarefaConfirmar = TentarConfirmarAsync();
            var tarefaTrocar = TrocarSenhaAsync();
            await Task.WhenAll(tarefaConfirmar, tarefaTrocar);

            var confirmouOk = await tarefaConfirmar;
            var estadoFinal = await Ler(idG);

            if (confirmouOk != estadoFinal.MfaAtivo)
            {
                inconsistencias66g++;
            }
        }

        Checar($"66g. corrida real confirmacao x troca de senha: resultado sempre consistente com MfaAtivo final ({trials66g} tentativas)",
            inconsistencias66g == 0);

        // 66h — nenhum JWT emitido ANTES da revogacao consegue completar a
        // cerimonia depois: tanto a sessao (CarregarSessaoAsync) quanto o
        // porteiro de MFA (ConfirmarAsync, ja provado em 66a) recusam,
        // independentemente — duas camadas, nao uma so. Reusa o JWT
        // capturado ANTES da troca de senha em 66a.
        await using (var db = new CrmDbContext(opcoes))
        {
            Checar("66h. JWT emitido ANTES da revogacao (66a) tambem nao vale mais (camada de sessao, independente do gate de MFA)",
                await NovoValidador(db).CarregarSessaoAsync(idA, tenantId, versaoAntesDaRevogacaoA) is null);
        }
    }

    // ------------------------------------------------------------ cenario 67
    // BLOQUEADOR 10 (quarta revisao independente) — a versao anterior do
    // bloqueador 8 gravava `MfaPendenteTokenVersion = a.TokenVersion`
    // AUTO-REFERENCIADA ao valor que estivesse na linha quando o UPDATE
    // rodasse — "adotando" silenciosamente uma TokenVersion NOVA se algo a
    // tivesse subido ENTRE a validacao da senha/step-up e a escrita do
    // segredo pendente. Corrigido: GerarSegredoPendenteNaTransacaoAsync agora
    // exige `a.TokenVersion == tokenVersionEsperada` (a versao capturada no
    // INICIO do metodo, antes de qualquer escrita) e grava exatamente esse
    // valor — nunca um lido depois. Os cenarios abaixo prova isto sob
    // corrida REAL (nao so entre iniciar/confirmar como o cenario 66, mas
    // DENTRO da propria chamada de iniciar) — a unica forma honesta de
    // alcancar a janela exata que o bloqueador 10 fecha, ja que
    // IniciarAsync/IniciarViaDesafioAsync nao tem um ponto de pausa
    // observavel de fora sem instrumentar o codigo de producao.

    {
        // 67a — primeira ativacao voluntaria x troca de senha, corrida real.
        var trials67a = 6;
        var inconsistencias67a = 0;
        var algumaRecusaPorRevogacao67a = false;

        for (var i = 0; i < trials67a; i++)
        {
            var (idX, emailX) = await NovaContaPrivilegiada($"bloqueador10 - 67a corrida {i}", perfil: PerfilDeAtendente.Atendente);

            async Task<bool> TentarIniciarAsync()
            {
                await using var db = new CrmDbContext(opcoes);
                try
                {
                    await NovoMfaService(db).IniciarAsync(
                        new IdentidadeParaMfa(tenantId, idX), new IniciarMfaRequest(SenhaBoa, null), null, default);
                    return true;
                }
                catch (UsuariosServiceException)
                {
                    return false;
                }
            }

            async Task TrocarSenhaAsync()
            {
                await using var db = new CrmDbContext(opcoes);
                await NovoAutenticacao(db, idX).AlterarSenhaAsync(new AlterarSenhaRequest(SenhaBoa, $"SenhaCorrida67a#{i}"));
            }

            var tarefaIniciar = TentarIniciarAsync();
            var tarefaTrocar = TrocarSenhaAsync();
            await Task.WhenAll(tarefaIniciar, tarefaTrocar);

            var iniciouOk = await tarefaIniciar;
            var estadoFinal = await Ler(idX);

            var autoConsistente = iniciouOk
                ? estadoFinal.MfaSegredoPendenteCifrado is not null && estadoFinal.MfaPendenteTokenVersion is not null
                : estadoFinal.MfaSegredoPendenteCifrado is null;

            if (!autoConsistente) inconsistencias67a++;
            if (!iniciouOk) algumaRecusaPorRevogacao67a = true;
        }

        Checar($"67a. corrida REAL iniciar (primeira ativacao) x troca de senha: resultado sempre consistente com o banco ({trials67a} tentativas)",
            inconsistencias67a == 0);
        Console.WriteLine(algumaRecusaPorRevogacao67a
            ? "  (67a: em pelo menos uma tentativa a troca de senha venceu a corrida e Iniciar foi recusado.)"
            : "  (67a: em todas as tentativas Iniciar venceu a corrida — resultado consistente.)");

        // 67b — troca de autenticador (MFA ja ativo) x troca de senha, corrida real.
        var trials67b = 6;
        var inconsistencias67b = 0;
        var algumaRecusaPorRevogacao67b = false;

        for (var i = 0; i < trials67b; i++)
        {
            var (idY, emailY, segredoY, _) = await NovaContaComMfaAtivo($"bloqueador10 - 67b corrida {i}");
            var codigoDeStepUp = CodigoTotp(segredoY);

            async Task<bool> TentarIniciarTrocaAsync()
            {
                await using var db = new CrmDbContext(opcoes);
                try
                {
                    await NovoMfaService(db).IniciarAsync(
                        new IdentidadeParaMfa(tenantId, idY), new IniciarMfaRequest(SenhaBoa, codigoDeStepUp), null, default);
                    return true;
                }
                catch (UsuariosServiceException)
                {
                    return false;
                }
            }

            async Task TrocarSenhaAsync()
            {
                await using var db = new CrmDbContext(opcoes);
                await NovoAutenticacao(db, idY).AlterarSenhaAsync(new AlterarSenhaRequest(SenhaBoa, $"SenhaCorrida67b#{i}"));
            }

            var tarefaIniciar = TentarIniciarTrocaAsync();
            var tarefaTrocar = TrocarSenhaAsync();
            await Task.WhenAll(tarefaIniciar, tarefaTrocar);

            var iniciouOk = await tarefaIniciar;
            var estadoFinal = await Ler(idY);

            var autoConsistente = iniciouOk
                ? estadoFinal.MfaSegredoPendenteCifrado is not null && estadoFinal.MfaPendenteTokenVersion is not null
                : estadoFinal.MfaSegredoPendenteCifrado is null;

            if (!autoConsistente) inconsistencias67b++;
            if (!iniciouOk) algumaRecusaPorRevogacao67b = true;
        }

        Checar($"67b. corrida REAL iniciar (troca de autenticador) x troca de senha: resultado sempre consistente com o banco ({trials67b} tentativas)",
            inconsistencias67b == 0);
        Console.WriteLine(algumaRecusaPorRevogacao67b
            ? "  (67b: em pelo menos uma tentativa a troca de senha venceu a corrida e Iniciar foi recusado.)"
            : "  (67b: em todas as tentativas Iniciar venceu a corrida — resultado consistente.)");

        // 67c — matricula privilegiada via desafio (IniciarViaDesafioAsync) x
        // troca de senha, corrida real. TokenVersionNoDesafio (que
        // ResolverDesafioDeMatriculaAsync devolve e antes era descartada por
        // IniciarViaDesafioAsync) e quem protege este caminho agora.
        var trials67c = 6;
        var inconsistencias67c = 0;
        var algumaRecusaPorRevogacao67c = false;

        for (var i = 0; i < trials67c; i++)
        {
            var (idZ, emailZ) = await NovaContaPrivilegiada($"bloqueador10 - 67c corrida {i}");
            var tokenVersionZ = (await Ler(idZ)).TokenVersion;
            var ticketZ = await EmitirTicketCruAsync(idZ, tokenVersionZ);

            DesafioMfaResponse sessaoZ;
            await using (var db = new CrmDbContext(opcoes))
            {
                sessaoZ = await NovoMfaService(db).IniciarMatriculaPrivilegiadaAsync(
                    new MatricularPrivilegiadoRequest(emailZ, SenhaBoa, ticketZ), null, default);
            }

            async Task<bool> TentarIniciarViaDesafioAsync()
            {
                await using var db = new CrmDbContext(opcoes);
                try
                {
                    await NovoMfaService(db).IniciarViaDesafioAsync(new IniciarViaDesafioRequest(sessaoZ.Desafio), default);
                    return true;
                }
                catch (UsuariosServiceException)
                {
                    return false;
                }
            }

            async Task TrocarSenhaAsync()
            {
                await using var db = new CrmDbContext(opcoes);
                await NovoAutenticacao(db, idZ).AlterarSenhaAsync(new AlterarSenhaRequest(SenhaBoa, $"SenhaCorrida67c#{i}"));
            }

            var tarefaIniciar = TentarIniciarViaDesafioAsync();
            var tarefaTrocar = TrocarSenhaAsync();
            await Task.WhenAll(tarefaIniciar, tarefaTrocar);

            var iniciouOk = await tarefaIniciar;
            var estadoFinal = await Ler(idZ);

            var autoConsistente = iniciouOk
                ? estadoFinal.MfaSegredoPendenteCifrado is not null && estadoFinal.MfaPendenteTokenVersion is not null
                : estadoFinal.MfaSegredoPendenteCifrado is null;

            if (!autoConsistente) inconsistencias67c++;
            if (!iniciouOk) algumaRecusaPorRevogacao67c = true;
        }

        Checar($"67c. corrida REAL iniciar-via-desafio (matricula privilegiada) x troca de senha: resultado sempre consistente com o banco ({trials67c} tentativas)",
            inconsistencias67c == 0);
        Console.WriteLine(algumaRecusaPorRevogacao67c
            ? "  (67c: em pelo menos uma tentativa a troca de senha venceu a corrida e IniciarViaDesafio foi recusado.)"
            : "  (67c: em todas as tentativas IniciarViaDesafio venceu a corrida — resultado consistente.)");

        // 67d — regressao: sem NENHUMA mudanca de TokenVersion (ja coberto
        // extensivamente por dezenas de cenarios anteriores) — caso explicito
        // e isolado, provando que MfaPendenteTokenVersion bate exatamente com
        // a TokenVersion no momento da geracao.
        var (idW, emailW) = await NovaContaPrivilegiada("bloqueador10 - regressao sem corrida", perfil: PerfilDeAtendente.Atendente);
        IniciarMfaResponse respostaW;
        await using (var db = new CrmDbContext(opcoes))
        {
            respostaW = await NovoMfaService(db).IniciarAsync(
                new IdentidadeParaMfa(tenantId, idW), new IniciarMfaRequest(SenhaBoa, null), null, default);
        }
        var estadoW = await Ler(idW);
        Checar("67d. sem mudanca de TokenVersion, Iniciar continua gravando o segredo pendente normalmente",
            !string.IsNullOrEmpty(respostaW.SegredoBase32)
            && estadoW.MfaSegredoPendenteCifrado is not null
            && estadoW.MfaPendenteTokenVersion == estadoW.TokenVersion);
    }

    // ------------------------------------------------------------ cenario 68
    // BLOQUEADOR 11 — step-up e a acao sensivel que ele autoriza agora vivem
    // na MESMA transacao (ExecutarCasDeStepUpAsync nunca abre/commita/reverte
    // sozinho), e o CAS do step-up exige `TokenVersion == tokenVersionEsperada`
    // — uma autorizacao validada sob uma TokenVersion antiga nunca commita
    // sob uma nova. Os cenarios abaixo provam isto sob corrida REAL para os
    // casos explicitamente exigidos pela revisao.

    {
        // 68a — Desativar x AtivarObrigatoriedade, corrida real, DOIS atores
        // distintos no mesmo tenant isolado. Estado PROIBIDO:
        // MfaObrigatorioParaAdministradores=true E algum privilegiado com
        // MfaAtivo=false por desativacao voluntaria — nunca pode acontecer,
        // em nenhuma das duas ordens de vitoria.
        Guid tenantIsolado68a;
        await using (var db = new CrmDbContext(opcoes))
        {
            var tenant = new Tenant
            {
                RazaoSocial = "[TESTE SEC-03] Tenant isolado — desativar x politica (68a)",
                Cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString()
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            tenantIsolado68a = tenant.Id;
        }
        tenantIdsDeTeste.Add(tenantIsolado68a);

        async Task<(Guid Id, string Email, string Segredo)> ContaMatriculada68a(string rotulo)
        {
            await using var db = new CrmDbContext(opcoes);
            var conta = new Atendente
            {
                TenantId = tenantIsolado68a,
                Nome = $"[TESTE SEC-03] {rotulo}",
                Email = $"teste-sec-03-68a-{Guid.NewGuid():N}@teste.local",
                SenhaHash = hash.Gerar(SenhaBoa),
                Perfil = PerfilDeAtendente.Administrador,
                Ativo = true
            };
            db.Atendentes.Add(conta);

            var tokenEmClaro = SegredosAleatoriosMfa.NovoToken();
            db.DesafiosDeMfa.Add(new DesafioDeMfa
            {
                TenantId = tenantIsolado68a,
                AtendenteId = conta.Id,
                HashDoDesafio = SegredosAleatoriosMfa.Sha256Hex(tokenEmClaro),
                Tipo = TipoDeDesafioMfa.Ticket,
                TokenVersionNoDesafio = conta.TokenVersion,
                ExpiraEm = DateTime.UtcNow.AddHours(24)
            });
            await db.SaveChangesAsync();
            ids.Add(conta.Id);

            var mfa = NovoMfaService(db);
            var sessao = await mfa.IniciarMatriculaPrivilegiadaAsync(
                new MatricularPrivilegiadoRequest(conta.Email, SenhaBoa, tokenEmClaro), null, default);
            var iniciado = await mfa.IniciarViaDesafioAsync(new IniciarViaDesafioRequest(sessao.Desafio), default);
            await mfa.ConfirmarViaDesafioAsync(
                new ConfirmarViaDesafioRequest(sessao.Desafio, CodigoTotp(iniciado.SegredoBase32, -1)), null, default);

            return (conta.Id, conta.Email, iniciado.SegredoBase32);
        }

        var desativa68a = await ContaMatriculada68a("68a - vai desativar o proprio MFA");
        var ativa68a = await ContaMatriculada68a("68a - vai ligar a politica");

        async Task<bool> TentarDesativarAsync()
        {
            await using var db = new CrmDbContext(opcoes);
            try
            {
                await NovoMfaService(db).DesativarAsync(
                    new IdentidadeParaMfa(tenantIsolado68a, desativa68a.Id),
                    new DesativarMfaRequest(SenhaBoa, CodigoTotp(desativa68a.Segredo)), null, default);
                return true;
            }
            catch (UsuariosServiceException)
            {
                return false;
            }
        }

        async Task<bool> TentarAtivarPoliticaAsync()
        {
            await using var db = new CrmDbContext(opcoes);
            try
            {
                await NovoMfaService(db).AtivarObrigatoriedadeAsync(
                    new IdentidadeParaMfa(tenantIsolado68a, ativa68a.Id),
                    new AtivarObrigatoriedadeRequest(SenhaBoa, CodigoTotp(ativa68a.Segredo), "corrida 68a"), null, default);
                return true;
            }
            catch (UsuariosServiceException)
            {
                return false;
            }
        }

        var tarefaDesativar68a = TentarDesativarAsync();
        var tarefaAtivar68a = TentarAtivarPoliticaAsync();
        await Task.WhenAll(tarefaDesativar68a, tarefaAtivar68a);

        var desativouOk68a = await tarefaDesativar68a;
        var ativouOk68a = await tarefaAtivar68a;

        await using (var db = new CrmDbContext(opcoes))
        {
            var estadoDesativa = await Ler(desativa68a.Id);
            var politicaLigada = await db.Tenants.AsNoTracking()
                .Where(t => t.Id == tenantIsolado68a).Select(t => t.MfaObrigatorioParaAdministradores).SingleAsync();

            var estadoProibido = politicaLigada && !estadoDesativa.MfaAtivo;

            Checar("68a. corrida REAL desativar x ativar-politica: NUNCA politica ligada com privilegiado desativado por acao voluntaria",
                !estadoProibido);
            Checar("68a-b. exatamente uma das duas operacoes concorrentes venceu (nunca as duas ao mesmo tempo)",
                desativouOk68a != ativouOk68a);
        }

        // 68b — Demotion do ator DURANTE EmitirTicketAsync (para alvo
        // Titular), corrida real: se o ator perde o Cargo Titular antes do
        // commit final, o ticket NAO pode sair.
        Guid tenantIsolado68b;
        await using (var db = new CrmDbContext(opcoes))
        {
            var tenant = new Tenant
            {
                RazaoSocial = "[TESTE SEC-03] Tenant isolado — demotion do ator (68b)",
                Cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString()
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            tenantIsolado68b = tenant.Id;
        }
        tenantIdsDeTeste.Add(tenantIsolado68b);

        Guid idAtor68b; string emailAtor68b; string segredoAtor68b;
        await using (var db = new CrmDbContext(opcoes))
        {
            var conta = new Atendente
            {
                TenantId = tenantIsolado68b,
                Nome = "[TESTE SEC-03] 68b - ator Titular",
                Email = $"teste-sec-03-68b-ator-{Guid.NewGuid():N}@teste.local",
                SenhaHash = hash.Gerar(SenhaBoa),
                Perfil = PerfilDeAtendente.Administrador,
                Cargo = CargoNaServentia.Titular,
                Ativo = true
            };
            db.Atendentes.Add(conta);

            var tokenEmClaro = SegredosAleatoriosMfa.NovoToken();
            db.DesafiosDeMfa.Add(new DesafioDeMfa
            {
                TenantId = tenantIsolado68b,
                AtendenteId = conta.Id,
                HashDoDesafio = SegredosAleatoriosMfa.Sha256Hex(tokenEmClaro),
                Tipo = TipoDeDesafioMfa.Ticket,
                TokenVersionNoDesafio = conta.TokenVersion,
                ExpiraEm = DateTime.UtcNow.AddHours(24)
            });
            await db.SaveChangesAsync();
            ids.Add(conta.Id);
            idAtor68b = conta.Id;
            emailAtor68b = conta.Email;

            var mfa = NovoMfaService(db);
            var sessao = await mfa.IniciarMatriculaPrivilegiadaAsync(
                new MatricularPrivilegiadoRequest(conta.Email, SenhaBoa, tokenEmClaro), null, default);
            var iniciado = await mfa.IniciarViaDesafioAsync(new IniciarViaDesafioRequest(sessao.Desafio), default);
            await mfa.ConfirmarViaDesafioAsync(
                new ConfirmarViaDesafioRequest(sessao.Desafio, CodigoTotp(iniciado.SegredoBase32, -1)), null, default);
            segredoAtor68b = iniciado.SegredoBase32;
        }

        Guid idAlvo68b; string emailAlvo68b;
        await using (var db = new CrmDbContext(opcoes))
        {
            var conta = new Atendente
            {
                TenantId = tenantIsolado68b,
                Nome = "[TESTE SEC-03] 68b - alvo Titular sem MFA",
                Email = $"teste-sec-03-68b-alvo-{Guid.NewGuid():N}@teste.local",
                SenhaHash = hash.Gerar(SenhaBoa),
                Perfil = PerfilDeAtendente.Administrador,
                Cargo = CargoNaServentia.Titular,
                Ativo = true
            };
            db.Atendentes.Add(conta);
            await db.SaveChangesAsync();
            ids.Add(conta.Id);
            idAlvo68b = conta.Id;
            emailAlvo68b = conta.Email;
        }

        var codigoDoTicket68b = CodigoTotp(segredoAtor68b);

        async Task<bool> TentarEmitirTicketAsync()
        {
            await using var db = new CrmDbContext(opcoes);
            try
            {
                await NovoMfaService(db).EmitirTicketAsync(
                    new IdentidadeParaMfa(tenantIsolado68b, idAtor68b), idAlvo68b,
                    new EmitirTicketRequest(SenhaBoa, codigoDoTicket68b), null, default);
                return true;
            }
            catch (UsuariosServiceException)
            {
                return false;
            }
        }

        async Task DemoverAtorAsync()
        {
            await using var db = new CrmDbContext(opcoes);
            await NovoAtendenteServiceParaTenant(db, Guid.NewGuid(), tenantIsolado68b).AtualizarAsync(idAtor68b,
                new AtualizarAtendenteRequest("Nome", emailAtor68b, PerfilDeAtendente.Administrador, null, true, CargoNaServentia.Substituto));
        }

        var tarefaTicket68b = TentarEmitirTicketAsync();
        var tarefaDemover68b = DemoverAtorAsync();
        await Task.WhenAll(tarefaTicket68b, tarefaDemover68b);

        var emitiuOk68b = await tarefaTicket68b;

        await using (var db = new CrmDbContext(opcoes))
        {
            var ticketExiste68b = await db.DesafiosDeMfa
                .AnyAsync(d => d.AtendenteId == idAlvo68b && d.Tipo == TipoDeDesafioMfa.Ticket);

            Checar("68b. corrida REAL demotion do ator x EmitirTicketAsync: resultado sempre consistente (ticket existe SSE a chamada relatou sucesso)",
                emitiuOk68b == ticketExiste68b);
        }

        // 68c — senha do ator muda DURANTE ResetarAsync, corrida real: a
        // operacao antiga (autorizada sob a senha/TokenVersion velha) nunca
        // pode commitar depois da troca.
        Guid tenantIsolado68c;
        await using (var db = new CrmDbContext(opcoes))
        {
            var tenant = new Tenant
            {
                RazaoSocial = "[TESTE SEC-03] Tenant isolado — senha do ator muda (68c)",
                Cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString()
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            tenantIsolado68c = tenant.Id;
        }
        tenantIdsDeTeste.Add(tenantIsolado68c);

        Guid idAtor68c; string emailAtor68c; string segredoAtor68c;
        await using (var db = new CrmDbContext(opcoes))
        {
            var conta = new Atendente
            {
                TenantId = tenantIsolado68c,
                Nome = "[TESTE SEC-03] 68c - ator do reset",
                Email = $"teste-sec-03-68c-ator-{Guid.NewGuid():N}@teste.local",
                SenhaHash = hash.Gerar(SenhaBoa),
                Perfil = PerfilDeAtendente.Administrador,
                Ativo = true
            };
            db.Atendentes.Add(conta);

            var tokenEmClaro = SegredosAleatoriosMfa.NovoToken();
            db.DesafiosDeMfa.Add(new DesafioDeMfa
            {
                TenantId = tenantIsolado68c,
                AtendenteId = conta.Id,
                HashDoDesafio = SegredosAleatoriosMfa.Sha256Hex(tokenEmClaro),
                Tipo = TipoDeDesafioMfa.Ticket,
                TokenVersionNoDesafio = conta.TokenVersion,
                ExpiraEm = DateTime.UtcNow.AddHours(24)
            });
            await db.SaveChangesAsync();
            ids.Add(conta.Id);
            idAtor68c = conta.Id;
            emailAtor68c = conta.Email;

            var mfa = NovoMfaService(db);
            var sessao = await mfa.IniciarMatriculaPrivilegiadaAsync(
                new MatricularPrivilegiadoRequest(conta.Email, SenhaBoa, tokenEmClaro), null, default);
            var iniciado = await mfa.IniciarViaDesafioAsync(new IniciarViaDesafioRequest(sessao.Desafio), default);
            await mfa.ConfirmarViaDesafioAsync(
                new ConfirmarViaDesafioRequest(sessao.Desafio, CodigoTotp(iniciado.SegredoBase32, -1)), null, default);
            segredoAtor68c = iniciado.SegredoBase32;
        }

        var (idAlvo68c, emailAlvo68c, segredoAlvo68c, _) = await NovaContaComMfaAtivo("68c - alvo do reset", perfil: PerfilDeAtendente.Atendente);
        // Move o alvo para o tenant isolado — mais simples criar direto la.
        await using (var db = new CrmDbContext(opcoes))
        {
            await db.Atendentes.Where(a => a.Id == idAlvo68c).ExecuteUpdateAsync(s => s.SetProperty(a => a.TenantId, tenantIsolado68c));
        }

        var codigoDoReset68c = CodigoTotp(segredoAtor68c);

        async Task<bool> TentarResetarAsync()
        {
            await using var db = new CrmDbContext(opcoes);
            try
            {
                await NovoMfaService(db).ResetarAsync(
                    new IdentidadeParaMfa(tenantIsolado68c, idAtor68c), idAlvo68c,
                    new ResetarMfaRequest(SenhaBoa, codigoDoReset68c, "corrida 68c"), null, default);
                return true;
            }
            catch (UsuariosServiceException)
            {
                return false;
            }
        }

        async Task TrocarSenhaDoAtorAsync()
        {
            await using var db = new CrmDbContext(opcoes);
            await NovoAutenticacaoParaTenant(db, idAtor68c, tenantIsolado68c)
                .AlterarSenhaAsync(new AlterarSenhaRequest(SenhaBoa, "SenhaCorrida68c#1"));
        }

        var tarefaReset68c = TentarResetarAsync();
        var tarefaTrocarSenha68c = TrocarSenhaDoAtorAsync();
        await Task.WhenAll(tarefaReset68c, tarefaTrocarSenha68c);

        var resetouOk68c = await tarefaReset68c;

        await using (var db = new CrmDbContext(opcoes))
        {
            var estadoAlvo68c = await Ler(idAlvo68c);
            var resetouDeFato = !estadoAlvo68c.MfaAtivo;

            Checar("68c. corrida REAL troca de senha do ator x ResetarAsync: resultado sempre consistente (alvo resetado SSE a chamada relatou sucesso)",
                resetouOk68c == resetouDeFato);
        }

        // 68d — EventosDeSeguranca na MESMA unidade de commit: um step-up
        // criptograficamente correto que falha no CAS final (TokenVersion
        // mudou) nunca deixa um EventoDeSeguranca orfao para tras.
        var (idD68d, emailD68d, segredoD68d, _) = await NovaContaComMfaAtivo("68d - eventos atomicos");
        var codigoDesativarD68d = CodigoTotp(segredoD68d);

        async Task<bool> TentarDesativarD68dAsync()
        {
            await using var db = new CrmDbContext(opcoes);
            try
            {
                await NovoMfaService(db).DesativarAsync(
                    new IdentidadeParaMfa(tenantId, idD68d),
                    new DesativarMfaRequest(SenhaBoa, codigoDesativarD68d), null, default);
                return true;
            }
            catch (UsuariosServiceException)
            {
                return false;
            }
        }

        async Task TrocarSenhaD68dAsync()
        {
            await using var db = new CrmDbContext(opcoes);
            await NovoAutenticacao(db, idD68d).AlterarSenhaAsync(new AlterarSenhaRequest(SenhaBoa, "SenhaCorrida68d#1"));
        }

        var tarefaDesativarD68d = TentarDesativarD68dAsync();
        var tarefaTrocarD68d = TrocarSenhaD68dAsync();
        await Task.WhenAll(tarefaDesativarD68d, tarefaTrocarD68d);

        var desativouOkD68d = await tarefaDesativarD68d;

        await using (var db = new CrmDbContext(opcoes))
        {
            var quantosEventosMfaDesativado = await db.EventosDeSeguranca
                .CountAsync(e => e.AtendenteId == idD68d && e.Tipo == TipoDeEventoDeSeguranca.MfaDesativado);

            Checar("68d. EventoDeSeguranca(MfaDesativado) existe SSE a desativacao de fato commitou (nunca orfao de um step-up que falhou no CAS final)",
                desativouOkD68d ? quantosEventosMfaDesativado == 1 : quantosEventosMfaDesativado == 0);
        }
    }

    // ------------------------------------------------------------ cenario 69
    // BLOQUEADOR 13 — fault injection: prova a direcao QUE FALTAVA (68d so
    // provava "acao falhou -> nenhum evento orfao"). Aqui: "evento falhou ->
    // a acao TAMBEM nao persiste". InterceptorDeFalhaDeEvento (definido no
    // fim deste arquivo) lanca ao detectar o EventoDeSeguranca alvo sendo
    // salvo — nunca um schema/trigger novo, so um SaveChangesInterceptor que
    // existe SOMENTE na configuracao de DbContext deste verificador.

    {
        // 69a — falha ao persistir MfaAtivado (primeira ativacao voluntaria).
        var (idA69, emailA69) = await NovaContaPrivilegiada("bloqueador13 - falha MfaAtivado", perfil: PerfilDeAtendente.Atendente);

        IniciarMfaResponse iniciado69a;
        await using (var db = new CrmDbContext(opcoes))
        {
            iniciado69a = await NovoMfaService(db).IniciarAsync(
                new IdentidadeParaMfa(tenantId, idA69), new IniciarMfaRequest(SenhaBoa, null), null, default);
        }
        var tokenVersionAntes69a = (await Ler(idA69)).TokenVersion;
        var codigo69a = CodigoTotp(iniciado69a.SegredoBase32);

        Exception? falha69a = null;
        await using (var dbComFalha = new CrmDbContext(OpcoesComFalhaDeEvento(TipoDeEventoDeSeguranca.MfaAtivado)))
        {
            try
            {
                await NovoMfaService(dbComFalha).ConfirmarAsync(
                    new IdentidadeParaMfa(tenantId, idA69), new ConfirmarMfaRequest(codigo69a), null, default);
            }
            catch (Exception ex)
            {
                falha69a = ex;
            }
        }

        Checar("69a. falha injetada ao persistir MfaAtivado: a chamada de fato lanca excecao",
            falha69a is not null);

        var estado69a = await Ler(idA69);
        Checar("69a. MfaAtivo NAO muda quando o evento falha",
            !estado69a.MfaAtivo);
        Checar("69a. TokenVersion NAO muda quando o evento falha",
            estado69a.TokenVersion == tokenVersionAntes69a);
        Checar("69a. o segredo pendente ORIGINAL continua intacto (rollback desfez a limpeza)",
            estado69a.MfaSegredoPendenteCifrado is not null);

        await using (var db = new CrmDbContext(opcoes))
        {
            var temRecoveryCodesNovos = await db.CodigosDeRecuperacaoMfa.AnyAsync(c => c.AtendenteId == idA69);
            Checar("69a. nenhum recovery code novo ficou gravado",
                !temRecoveryCodesNovos);
        }

        // Sem a falha (db normal), a MESMA cerimonia — nunca marcada como
        // concluida pelo tentativa anterior, que reverteu por completo —
        // ainda funciona normalmente.
        await using (var db = new CrmDbContext(opcoes))
        {
            var confirmadoDeVerdade = await NovoMfaService(db).ConfirmarAsync(
                new IdentidadeParaMfa(tenantId, idA69), new ConfirmarMfaRequest(codigo69a), null, default);

            Checar("69a-pos. sem a falha, a MESMA cerimonia (nunca antes concluida de verdade) ainda funciona",
                !string.IsNullOrEmpty(confirmadoDeVerdade.Sessao.Token));
        }

        // 69b — falha ao persistir CodigoDeRecuperacaoUsado durante o login MFA.
        var (idB69, emailB69, segredoB69, codigosB69) = await NovaContaComMfaAtivo(
            "bloqueador13 - falha CodigoDeRecuperacaoUsado", perfil: PerfilDeAtendente.Atendente);
        var codigoDeRecuperacao69b = codigosB69[0];

        LoginEtapa1Response etapa1_69b;
        await using (var db = new CrmDbContext(opcoes))
        {
            etapa1_69b = await NovoAutenticacao(db, Guid.NewGuid()).AutenticarAsync(new LoginRequest(emailB69, SenhaBoa));
        }
        Checar("69b-preparo. etapa 1 abriu desafio (conta com MFA ativo)",
            etapa1_69b.RequerMfa && etapa1_69b.Desafio is not null);

        var estadoAntes69b = await Ler(idB69);

        Exception? falha69b = null;
        await using (var dbComFalha = new CrmDbContext(OpcoesComFalhaDeEvento(TipoDeEventoDeSeguranca.CodigoDeRecuperacaoUsado)))
        {
            try
            {
                await NovoAutenticacao(dbComFalha, Guid.NewGuid()).ConcluirLoginMfaAsync(
                    new ConcluirLoginMfaRequest(etapa1_69b.Desafio!.Desafio, codigoDeRecuperacao69b));
            }
            catch (Exception ex)
            {
                falha69b = ex;
            }
        }

        Checar("69b. falha injetada ao persistir CodigoDeRecuperacaoUsado: a chamada de fato lanca excecao",
            falha69b is not null);

        await using (var db = new CrmDbContext(opcoes))
        {
            var quantosUsados = await db.CodigosDeRecuperacaoMfa.CountAsync(c => c.AtendenteId == idB69 && c.UsadoEm != null);
            Checar("69b. nenhum recovery code fica consumido quando o evento falha",
                quantosUsados == 0);
        }

        await using (var db = new CrmDbContext(opcoes))
        {
            var desafioAindaAberto = await db.DesafiosDeMfa.AnyAsync(d =>
                d.HashDoDesafio == SegredosAleatoriosMfa.Sha256Hex(etapa1_69b.Desafio!.Desafio) && d.ConsumidoEm == null);
            Checar("69b. o challenge NAO fica consumido quando o evento falha",
                desafioAindaAberto);
        }

        var estadoDepois69b = await Ler(idB69);
        Checar("69b. UltimoAcessoEm NAO muda quando o evento falha",
            estadoDepois69b.UltimoAcessoEm == estadoAntes69b.UltimoAcessoEm);

        // Sem a falha, o MESMO desafio + o MESMO codigo de recuperacao (nunca
        // consumido de verdade pela tentativa anterior) ainda completa o login.
        await using (var db = new CrmDbContext(opcoes))
        {
            var loginDeVerdade = await NovoAutenticacao(db, Guid.NewGuid()).ConcluirLoginMfaAsync(
                new ConcluirLoginMfaRequest(etapa1_69b.Desafio!.Desafio, codigoDeRecuperacao69b));

            Checar("69b-pos. sem a falha, o MESMO desafio+codigo (nunca antes consumido de verdade) ainda completa o login",
                !string.IsNullOrEmpty(loginDeVerdade.Token));
        }
    }

    // ------------------------------------------------------------ cenario 70
    // BLOQUEADOR 14 — UltimoAcessoEm agora e gravado no MESMO UPDATE que de
    // fato confirma o MFA (ConfirmarNucleoAsync), nunca so na entidade em
    // memoria usada pra montar a resposta.

    {
        // 70a — matricula privilegiada completa por ticket.
        var (idPriv70, emailPriv70) = await NovaContaPrivilegiada("bloqueador14 - matricula privilegiada");
        var estadoAntes70a = await Ler(idPriv70);
        Checar("70a-preparo. UltimoAcessoEm comeca null antes da matricula",
            estadoAntes70a.UltimoAcessoEm is null);

        var ticket70a = await EmitirTicketCruAsync(idPriv70, estadoAntes70a.TokenVersion);

        ConfirmarMfaResponse confirmado70a;
        await using (var db = new CrmDbContext(opcoes))
        {
            var mfa = NovoMfaService(db);
            var sessao70a = await mfa.IniciarMatriculaPrivilegiadaAsync(
                new MatricularPrivilegiadoRequest(emailPriv70, SenhaBoa, ticket70a), null, default);
            var iniciado70a = await mfa.IniciarViaDesafioAsync(new IniciarViaDesafioRequest(sessao70a.Desafio), default);
            confirmado70a = await mfa.ConfirmarViaDesafioAsync(
                new ConfirmarViaDesafioRequest(sessao70a.Desafio, CodigoTotp(iniciado70a.SegredoBase32, -1)), null, default);
        }

        Checar("70a. confirmacao correta devolve JWT",
            !string.IsNullOrEmpty(confirmado70a.Sessao.Token));

        var estadoDepois70a = await Ler(idPriv70);
        Checar("70a. banco passa a ter UltimoAcessoEm gravado",
            estadoDepois70a.UltimoAcessoEm is not null);
        Checar("70a. resposta e banco sao coerentes (mesmo instante)",
            Math.Abs((estadoDepois70a.UltimoAcessoEm!.Value - DateTime.UtcNow).TotalSeconds) < 30);

        // 70b — codigo incorreto.
        var (idB70, emailB70) = await NovaContaPrivilegiada("bloqueador14 - codigo incorreto", perfil: PerfilDeAtendente.Atendente);
        await using (var db = new CrmDbContext(opcoes))
        {
            await NovoMfaService(db).IniciarAsync(
                new IdentidadeParaMfa(tenantId, idB70), new IniciarMfaRequest(SenhaBoa, null), null, default);
        }
        var estadoAntesB70 = await Ler(idB70);

        await using (var db = new CrmDbContext(opcoes))
        {
            UsuariosServiceException? falha = null;
            try
            {
                await NovoMfaService(db).ConfirmarAsync(
                    new IdentidadeParaMfa(tenantId, idB70), new ConfirmarMfaRequest("000000"), null, default);
            }
            catch (UsuariosServiceException ex) { falha = ex; }

            Checar("70b-preparo. codigo incorreto e de fato recusado",
                falha?.Codigo == "codigo_mfa_invalido");
        }

        var estadoDepoisB70 = await Ler(idB70);
        Checar("70b. UltimoAcessoEm NAO muda com codigo incorreto",
            estadoDepoisB70.UltimoAcessoEm == estadoAntesB70.UltimoAcessoEm);

        // 70c — challenge/TokenVersion invalido (senha mudou entre iniciar e confirmar).
        var (idC70, emailC70) = await NovaContaPrivilegiada("bloqueador14 - tokenversion invalido", perfil: PerfilDeAtendente.Atendente);
        IniciarMfaResponse iniciadoC70;
        await using (var db = new CrmDbContext(opcoes))
        {
            iniciadoC70 = await NovoMfaService(db).IniciarAsync(
                new IdentidadeParaMfa(tenantId, idC70), new IniciarMfaRequest(SenhaBoa, null), null, default);
        }
        await using (var db = new CrmDbContext(opcoes))
        {
            await NovoAutenticacao(db, idC70).AlterarSenhaAsync(new AlterarSenhaRequest(SenhaBoa, "SenhaNova70c#1"));
        }
        var estadoAntesC70 = await Ler(idC70);

        await using (var db = new CrmDbContext(opcoes))
        {
            UsuariosServiceException? falha = null;
            try
            {
                await NovoMfaService(db).ConfirmarAsync(
                    new IdentidadeParaMfa(tenantId, idC70), new ConfirmarMfaRequest(CodigoTotp(iniciadoC70.SegredoBase32)), null, default);
            }
            catch (UsuariosServiceException ex) { falha = ex; }

            Checar("70c-preparo. TokenVersion invalida (revogada) e de fato recusada",
                falha?.Codigo == "codigo_mfa_invalido");
        }

        var estadoDepoisC70 = await Ler(idC70);
        Checar("70c. UltimoAcessoEm NAO muda quando a TokenVersion esta invalida",
            estadoDepoisC70.UltimoAcessoEm == estadoAntesC70.UltimoAcessoEm);

        // 70d — confirmacao concorrente: so quem de fato conclui grava UltimoAcessoEm.
        var (idD70, emailD70) = await NovaContaPrivilegiada("bloqueador14 - concorrencia", perfil: PerfilDeAtendente.Atendente);
        IniciarMfaResponse iniciadoD70;
        await using (var db = new CrmDbContext(opcoes))
        {
            iniciadoD70 = await NovoMfaService(db).IniciarAsync(
                new IdentidadeParaMfa(tenantId, idD70), new IniciarMfaRequest(SenhaBoa, null), null, default);
        }
        var codigoD70 = CodigoTotp(iniciadoD70.SegredoBase32);

        async Task<bool> TentarConfirmarD70()
        {
            await using var db = new CrmDbContext(opcoes);
            try
            {
                await NovoMfaService(db).ConfirmarAsync(
                    new IdentidadeParaMfa(tenantId, idD70), new ConfirmarMfaRequest(codigoD70), null, default);
                return true;
            }
            catch (UsuariosServiceException)
            {
                return false;
            }
        }

        var resultadosD70 = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => TentarConfirmarD70()));

        Checar("70d. exatamente 1 das 3 confirmacoes concorrentes (mesmo codigo) conclui",
            resultadosD70.Count(r => r) == 1);

        var estadoD70 = await Ler(idD70);
        Checar("70d. UltimoAcessoEm foi gravado pela UNICA que concluiu",
            estadoD70.UltimoAcessoEm is not null && estadoD70.MfaAtivo);
    }

    // ------------------------------------------------------------ cenario 71
    // BLOQUEADOR 15 — o desafio de MatriculaPrivilegiada agora reserva
    // TentativasDeCodigo ANTES de validar, mesmo teto (5) e mesma disciplina
    // do segundo fator no login. ResolverDesafioDeMatriculaAsync tambem
    // recusa um desafio ja esgotado, mesmo para reiniciar a cerimonia.

    {
        async Task<(Guid Id, string Email, DesafioMfaResponse Sessao, IniciarMfaResponse Iniciado)> PrepararMatriculaPendente71(string rotulo)
        {
            var (id, email) = await NovaContaPrivilegiada(rotulo);
            var tokenVersion = (await Ler(id)).TokenVersion;
            var ticket = await EmitirTicketCruAsync(id, tokenVersion);

            await using var db = new CrmDbContext(opcoes);
            var mfa = NovoMfaService(db);
            var sessao = await mfa.IniciarMatriculaPrivilegiadaAsync(
                new MatricularPrivilegiadoRequest(email, SenhaBoa, ticket), null, default);
            var iniciado = await mfa.IniciarViaDesafioAsync(new IniciarViaDesafioRequest(sessao.Desafio), default);

            return (id, email, sessao, iniciado);
        }

        async Task<UsuariosServiceException?> TentarConfirmarViaDesafio71(string desafio, string codigo)
        {
            await using var db = new CrmDbContext(opcoes);
            try
            {
                await NovoMfaService(db).ConfirmarViaDesafioAsync(new ConfirmarViaDesafioRequest(desafio, codigo), null, default);
                return null;
            }
            catch (UsuariosServiceException ex)
            {
                return ex;
            }
        }

        // 71-1 — 1 erro: recusado, TentativasDeCodigo==1, ConsumidoEm null, MFA desligado.
        var t71_1 = await PrepararMatriculaPendente71("bloqueador15 - 1 erro");
        var falha71_1 = await TentarConfirmarViaDesafio71(t71_1.Sessao.Desafio, "000000");
        Checar("71-1. codigo errado e recusado", falha71_1?.Codigo == "codigo_mfa_invalido");

        await using (var db = new CrmDbContext(opcoes))
        {
            var desafioDb = await db.DesafiosDeMfa.AsNoTracking()
                .SingleAsync(d => d.HashDoDesafio == SegredosAleatoriosMfa.Sha256Hex(t71_1.Sessao.Desafio));
            Checar("71-1. TentativasDeCodigo == 1", desafioDb.TentativasDeCodigo == 1);
            Checar("71-1. ConsumidoEm continua null", desafioDb.ConsumidoEm is null);
        }
        Checar("71-1. MFA continua desligado", !(await Ler(t71_1.Id)).MfaAtivo);

        // 71-2 — apos 1 erro, o codigo CORRETO ainda funciona (mesma conta do 71-1).
        ConfirmarMfaResponse confirmado71_2;
        await using (var db = new CrmDbContext(opcoes))
        {
            confirmado71_2 = await NovoMfaService(db).ConfirmarViaDesafioAsync(
                new ConfirmarViaDesafioRequest(t71_1.Sessao.Desafio, CodigoTotp(t71_1.Iniciado.SegredoBase32, -1)), null, default);
        }
        Checar("71-2. apos 1 erro, o codigo CORRETO ainda funciona",
            !string.IsNullOrEmpty(confirmado71_2.Sessao.Token));

        // 71-3 — 5 codigos errados: todos recusados, challenge esgotado.
        var t71_3 = await PrepararMatriculaPendente71("bloqueador15 - 5 erros");
        var todosRecusados71_3 = true;
        for (var i = 0; i < 5; i++)
        {
            var falha = await TentarConfirmarViaDesafio71(t71_3.Sessao.Desafio, "000000");
            if (falha?.Codigo != "codigo_mfa_invalido") todosRecusados71_3 = false;
        }
        Checar("71-3. 5 tentativas com codigo errado sao todas recusadas", todosRecusados71_3);

        await using (var db = new CrmDbContext(opcoes))
        {
            var desafioDb = await db.DesafiosDeMfa.AsNoTracking()
                .SingleAsync(d => d.HashDoDesafio == SegredosAleatoriosMfa.Sha256Hex(t71_3.Sessao.Desafio));
            Checar("71-3. challenge fica esgotado (TentativasDeCodigo == 5)", desafioDb.TentativasDeCodigo == 5);
        }

        // 71-4 — 6a tentativa com codigo CORRETO: ainda recusada, sem JWT, MFA desligado.
        var falha71_4 = await TentarConfirmarViaDesafio71(t71_3.Sessao.Desafio, CodigoTotp(t71_3.Iniciado.SegredoBase32, -1));
        Checar("71-4. 6a tentativa com codigo CORRETO tambem e recusada (challenge esgotado)",
            falha71_4?.Codigo == "desafio_mfa_invalido");
        Checar("71-4. MFA continua desligado", !(await Ler(t71_3.Id)).MfaAtivo);

        // 71-5 — 4 erros + 5a correta: exatamente a 5a conclui, challenge consumido.
        var t71_5 = await PrepararMatriculaPendente71("bloqueador15 - 4 erros + acerto");
        for (var i = 0; i < 4; i++)
        {
            await TentarConfirmarViaDesafio71(t71_5.Sessao.Desafio, "000000");
        }

        ConfirmarMfaResponse confirmado71_5;
        await using (var db = new CrmDbContext(opcoes))
        {
            confirmado71_5 = await NovoMfaService(db).ConfirmarViaDesafioAsync(
                new ConfirmarViaDesafioRequest(t71_5.Sessao.Desafio, CodigoTotp(t71_5.Iniciado.SegredoBase32, -1)), null, default);
        }
        Checar("71-5. a 5a tentativa (correta, apos 4 erros) conclui",
            !string.IsNullOrEmpty(confirmado71_5.Sessao.Token));

        await using (var db = new CrmDbContext(opcoes))
        {
            var desafioDb = await db.DesafiosDeMfa.AsNoTracking()
                .SingleAsync(d => d.HashDoDesafio == SegredosAleatoriosMfa.Sha256Hex(t71_5.Sessao.Desafio));
            Checar("71-5. o challenge foi consumido", desafioDb.ConsumidoEm is not null);
        }

        // 71-6 — concorrencia: N confirmacoes simultaneas (mesmo desafio/codigo)
        // nunca produzem mais de 1 ativacao/JWT, nem ultrapassam o teto.
        var t71_6 = await PrepararMatriculaPendente71("bloqueador15 - concorrencia");
        var codigo71_6 = CodigoTotp(t71_6.Iniciado.SegredoBase32, -1);

        async Task<bool> TentarConfirmarConcorrente71_6()
        {
            await using var db = new CrmDbContext(opcoes);
            try
            {
                await NovoMfaService(db).ConfirmarViaDesafioAsync(
                    new ConfirmarViaDesafioRequest(t71_6.Sessao.Desafio, codigo71_6), null, default);
                return true;
            }
            catch (UsuariosServiceException)
            {
                return false;
            }
        }

        var resultados71_6 = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => TentarConfirmarConcorrente71_6()));
        Checar("71-6. 5 confirmacoes concorrentes (mesmo desafio/codigo) — exatamente 1 conclui",
            resultados71_6.Count(r => r) == 1);

        await using (var db = new CrmDbContext(opcoes))
        {
            var desafioDb = await db.DesafiosDeMfa.AsNoTracking()
                .SingleAsync(d => d.HashDoDesafio == SegredosAleatoriosMfa.Sha256Hex(t71_6.Sessao.Desafio));
            Checar("71-6. TentativasDeCodigo nunca ultrapassa o teto (5)",
                desafioDb.TentativasDeCodigo <= 5);
        }

        // 71-7 — challenge esgotado nao pode reiniciar a cerimonia via IniciarViaDesafioAsync.
        await using (var db = new CrmDbContext(opcoes))
        {
            UsuariosServiceException? falha = null;
            try
            {
                await NovoMfaService(db).IniciarViaDesafioAsync(new IniciarViaDesafioRequest(t71_3.Sessao.Desafio), default);
            }
            catch (UsuariosServiceException ex) { falha = ex; }

            Checar("71-7. challenge esgotado nao pode ser usado para reiniciar a cerimonia (IniciarViaDesafioAsync)",
                falha?.Codigo == "desafio_mfa_invalido");
        }
    }

    // ------------------------------------------------------------ cenario 72
    // BLOQUEADOR 16 — bootstrap E break-glass agora recusam tambem por
    // STDOUT redirecionado, nao so stdin. Prova por reflexao (decisao PURA,
    // privada, extraida de proposito) — nao reabre nenhuma superficie
    // publica mutavel.
    //
    // Limite honesto deste ambiente: tanto stdin quanto stdout do PROPRIO
    // processo verificador ja chegam redirecionados por quem o executa
    // (confirmado neste ambiente: os dois sao `true`) — nao ha como isolar
    // so o lado da saida via subprocesso, porque o filho herdaria a MESMA
    // condicao do pai. A prova comportamental fica limitada a "recusa
    // quando ambos estao redirecionados" (que ja acontecia mesmo ANTES desta
    // correcao, ja que so a entrada ja bastava) — por isso o cenario TAMBEM
    // confere, por leitura do proprio codigo-fonte, que a checagem de SAIDA
    // especificamente existe (regressao direta contra alguem remover so essa
    // linha) e que a de entrada nao foi trocada por ela.

    {
        foreach (var (tipo, caminhoDoArquivo) in new (Type Tipo, string Caminho)[]
        {
            (typeof(FerramentaDeBootstrapMfa), Path.Combine("CrmCartorio.Api", "Infrastructure", "Security", "FerramentaDeBootstrapMfa.cs")),
            (typeof(FerramentaDeResetDeEmergencia), Path.Combine("CrmCartorio.Api", "Infrastructure", "Security", "FerramentaDeResetDeEmergencia.cs"))
        })
        {
            var rotulo = tipo.Name;

            var metodo = tipo.GetMethod("AmbienteNaoInterativo", BindingFlags.NonPublic | BindingFlags.Static);
            Checar($"72-{rotulo}a. AmbienteNaoInterativo existe, privado, estatico, devolve bool",
                metodo is not null && metodo.ReturnType == typeof(bool) && !metodo.IsPublic);

            var resultado = (bool)metodo!.Invoke(null, null)!;
            Checar($"72-{rotulo}b. neste ambiente (stdin e stdout ja redirecionados por quem executa o teste), a guarda recusa",
                resultado);

            var codigoFonte = await File.ReadAllTextAsync(caminhoDoArquivo);
            Checar($"72-{rotulo}c. o codigo-fonte confere Console.IsOutputRedirected (regressao contra remover so este lado)",
                codigoFonte.Contains("Console.IsOutputRedirected"));
            Checar($"72-{rotulo}d. o codigo-fonte continua conferindo Console.IsInputRedirected tambem (nao trocou um pelo outro)",
                codigoFonte.Contains("Console.IsInputRedirected"));

            var metodosPublicos = tipo.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Checar($"72-{rotulo}e. superficie publica continua exatamente {{ExecutarAsync, VerificarElegibilidadeAsync}}",
                metodosPublicos.SequenceEqual(["ExecutarAsync", "VerificarElegibilidadeAsync"], StringComparer.Ordinal));
        }
    }

    // ------------------------------------------------------------ cenario 73
    // GAP 17 — break-glass (FerramentaDeResetDeEmergencia): certificacao em
    // CRM_Cartorio_Dev, incluindo o cenario de DEADLOCK operacional que o
    // break-glass existe para resolver (outro Administrador com MFA ativo,
    // mas nao-Titular, ja existente no tenant — bootstrap ja fechado, reset
    // normal bloqueado pela protecao do Titular).

    {
        async Task<ElegibilidadeDeEmergencia> TentarResetarDeEmergenciaViaReflexaoAsync(
            CrmDbContext db, string email, string hashDoNovoTicket, string motivo, string atorExterno)
        {
            var metodo = typeof(FerramentaDeResetDeEmergencia).GetMethod(
                "TentarResetarDeEmergenciaAsync", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("TentarResetarDeEmergenciaAsync nao encontrado via reflexao.");

            var tarefa = (Task<ElegibilidadeDeEmergencia>)metodo.Invoke(
                null, [db, email, hashDoNovoTicket, motivo, atorExterno, CancellationToken.None])!;

            return await tarefa;
        }

        Guid tenantIsolado73;
        await using (var db = new CrmDbContext(opcoes))
        {
            var tenant = new Tenant
            {
                RazaoSocial = "[TESTE SEC-03] Tenant isolado — break-glass (73)",
                Cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString()
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            tenantIsolado73 = tenant.Id;
        }
        tenantIdsDeTeste.Add(tenantIsolado73);

        async Task<(Guid Id, string Email, string Segredo)> ContaMatriculada73(string rotulo, CargoNaServentia cargo)
        {
            await using var db = new CrmDbContext(opcoes);
            var conta = new Atendente
            {
                TenantId = tenantIsolado73,
                Nome = $"[TESTE SEC-03] {rotulo}",
                Email = $"teste-sec-03-73-{Guid.NewGuid():N}@teste.local",
                SenhaHash = hash.Gerar(SenhaBoa),
                Perfil = PerfilDeAtendente.Administrador,
                Cargo = cargo,
                Ativo = true
            };
            db.Atendentes.Add(conta);

            var tokenEmClaro = SegredosAleatoriosMfa.NovoToken();
            db.DesafiosDeMfa.Add(new DesafioDeMfa
            {
                TenantId = tenantIsolado73,
                AtendenteId = conta.Id,
                HashDoDesafio = SegredosAleatoriosMfa.Sha256Hex(tokenEmClaro),
                Tipo = TipoDeDesafioMfa.Ticket,
                TokenVersionNoDesafio = conta.TokenVersion,
                ExpiraEm = DateTime.UtcNow.AddHours(24)
            });
            await db.SaveChangesAsync();
            ids.Add(conta.Id);

            var mfa = NovoMfaService(db);
            var sessao = await mfa.IniciarMatriculaPrivilegiadaAsync(
                new MatricularPrivilegiadoRequest(conta.Email, SenhaBoa, tokenEmClaro), null, default);
            var iniciado = await mfa.IniciarViaDesafioAsync(new IniciarViaDesafioRequest(sessao.Desafio), default);
            await mfa.ConfirmarViaDesafioAsync(
                new ConfirmarViaDesafioRequest(sessao.Desafio, CodigoTotp(iniciado.SegredoBase32, -1)), null, default);

            return (conta.Id, conta.Email, iniciado.SegredoBase32);
        }

        // 1 — o unico Titular "perde todos os fatores": simulado pelo simples
        // fato de que o teste nunca mais usa o segredo/recovery codes dele a
        // partir daqui — exatamente como alguem que perdeu o celular e o
        // papel dos codigos.
        var titularPreso73 = await ContaMatriculada73("73 - Titular preso", CargoNaServentia.Titular);

        // Cenario do deadlock (teste 9): outro Administrador com MFA ativo,
        // mas NAO Titular, ja existe no tenant.
        var outroAdmin73 = await ContaMatriculada73("73 - outro Admin nao Titular", CargoNaServentia.Substituto);

        // Confirma o DEADLOCK antes do break-glass. Na realidade, quem perdeu
        // o autenticador e os codigos de recuperacao NAO tem como apagar o
        // proprio MfaAtivo do banco — o registro continua dizendo que ele
        // esta matriculado. Por isso o bootstrap normal recusa por
        // MfaJaAtivo (o alvo em si ja tem MFA ativo), nao por
        // JaExistePrivilegiadoMatriculado — mas o efeito e o mesmo: bootstrap
        // fechado, sem caminho para emitir um novo ticket para o Titular.
        await using (var db = new CrmDbContext(opcoes))
        {
            Checar("73-deadlock-a. bootstrap normal ja fechado para o Titular (MfaAtivo=true no banco, apesar dele ter perdido o autenticador)",
                await FerramentaDeBootstrapMfa.VerificarElegibilidadeAsync(db, titularPreso73.Email)
                    == ElegibilidadeDeBootstrap.MfaJaAtivo);
        }

        await using (var db = new CrmDbContext(opcoes))
        {
            UsuariosServiceException? falha = null;
            try
            {
                await NovoMfaService(db).ResetarAsync(
                    new IdentidadeParaMfa(tenantIsolado73, outroAdmin73.Id), titularPreso73.Id,
                    new ResetarMfaRequest(SenhaBoa, CodigoTotp(outroAdmin73.Segredo), "tentativa normal, precisa falhar"), null, default);
            }
            catch (UsuariosServiceException ex) { falha = ex; }

            Checar("73-deadlock-b. Administrador NAO-Titular nao pode resetar o Titular pelo caminho normal (deadlock confirmado)",
                falha?.Codigo == "apenas_titular_reseta_titular");
        }

        // Liga a politica obrigatoria AGORA — enquanto os dois ainda tem MFA
        // ativo de verdade (AtivarObrigatoriedadeAsync recusa se existir
        // qualquer privilegiado sem MFA, e o break-glass, la na frente, vai
        // desligar o do Titular). E o cenario real: a serventia ja exige MFA
        // de privilegiados antes de alguem perder o proprio autenticador.
        await using (var db = new CrmDbContext(opcoes))
        {
            await NovoMfaService(db).AtivarObrigatoriedadeAsync(
                new IdentidadeParaMfa(tenantIsolado73, outroAdmin73.Id),
                new AtivarObrigatoriedadeRequest(SenhaBoa, CodigoTotp(outroAdmin73.Segredo, 1), "politica obrigatoria — cenario 73"),
                null, default);
        }

        // "Artefato antigo" real: um desafio de SEGUNDO FATOR aberto (login
        // etapa 1) usando o segredo antigo do Titular, ANTES do break-glass —
        // para o teste 4 provar de verdade que fica invalido.
        LoginEtapa1Response etapa1AntesDoBreakGlass73;
        await using (var db = new CrmDbContext(opcoes))
        {
            etapa1AntesDoBreakGlass73 = await NovoAutenticacao(db, Guid.NewGuid())
                .AutenticarAsync(new LoginRequest(titularPreso73.Email, SenhaBoa));
        }
        Checar("73-preparo. desafio de 2o fator aberto ANTES do break-glass (com o segredo antigo)",
            etapa1AntesDoBreakGlass73.RequerMfa && etapa1AntesDoBreakGlass73.Desafio is not null);

        // Sessao/JWT antigo (capturado ANTES do break-glass) — teste 3.
        var (tokenAntigoTitular73, _) = tokens.Gerar(await Ler(titularPreso73.Id));
        var versaoAntigaTitular73 = LerVersaoDaClaim(tokenAntigoTitular73);
        var tokenVersionAntesDoBreakGlass73 = (await Ler(titularPreso73.Id)).TokenVersion;

        // 2 — break-glass remove os fatores de forma auditada.
        var tokenNovoDeRematricula73 = SegredosAleatoriosMfa.NovoToken();
        ElegibilidadeDeEmergencia resultadoBreakGlass73;
        await using (var db = new CrmDbContext(opcoes))
        {
            resultadoBreakGlass73 = await TentarResetarDeEmergenciaViaReflexaoAsync(
                db, titularPreso73.Email, SegredosAleatoriosMfa.Sha256Hex(tokenNovoDeRematricula73),
                "teste automatizado do cenario 73 — break-glass", "teste@verificador");
        }

        Checar("73-2. break-glass reconhece elegibilidade e reseta",
            resultadoBreakGlass73 == ElegibilidadeDeEmergencia.Elegivel);

        var estadoPosBreakGlass73 = await Ler(titularPreso73.Id);
        Checar("73-2b. MFA removido (MfaAtivo=false, segredo apagado)",
            !estadoPosBreakGlass73.MfaAtivo && estadoPosBreakGlass73.MfaSegredoCifrado is null);

        await using (var db = new CrmDbContext(opcoes))
        {
            Checar("73-2c. recovery codes apagados",
                !await db.CodigosDeRecuperacaoMfa.AnyAsync(c => c.AtendenteId == titularPreso73.Id));
        }

        Checar("73-2d. TokenVersion subiu",
            estadoPosBreakGlass73.TokenVersion > tokenVersionAntesDoBreakGlass73);

        // 3 — sessao/token ANTIGO fica invalido.
        await using (var db = new CrmDbContext(opcoes))
        {
            Checar("73-3. JWT/sessao emitido ANTES do break-glass fica invalido",
                await NovoValidador(db).CarregarSessaoAsync(titularPreso73.Id, tenantIsolado73, versaoAntigaTitular73) is null);
        }

        // 4 — artefatos antigos (o desafio de 2o fator aberto antes) nao
        // funcionam mais: nem com o codigo do segredo ANTIGO (MfaAtivo=false
        // agora), nem de nenhum outro jeito — o desafio foi consumido.
        await using (var db = new CrmDbContext(opcoes))
        {
            var desafioAntigoAindaAberto = await db.DesafiosDeMfa.AnyAsync(d =>
                d.HashDoDesafio == SegredosAleatoriosMfa.Sha256Hex(etapa1AntesDoBreakGlass73.Desafio!.Desafio)
                && d.ConsumidoEm == null);
            Checar("73-4. o desafio de 2o fator aberto ANTES do break-glass foi invalidado (consumido)",
                !desafioAntigoAindaAberto);
        }

        // 5/6 — nova matricula segura E POSSIVEL, e SO DEPOIS dela a conta
        // volta a logar. A politica obrigatoria ja foi ligada antes do
        // break-glass (usando o OUTRO Administrador, que nunca foi tocado) —
        // agora prova que, ate a rematricula, a conta fica mesmo sem login.
        await using (var db = new CrmDbContext(opcoes))
        {
            UsuariosServiceException? falha = null;
            try
            {
                await NovoAutenticacao(db, Guid.NewGuid()).AutenticarAsync(new LoginRequest(titularPreso73.Email, SenhaBoa));
            }
            catch (UsuariosServiceException ex) { falha = ex; }

            Checar("73-6a. ANTES da rematricula, login com senha correta e RECUSADO (politica obrigatoria)",
                falha?.Codigo == "mfa_obrigatorio_nao_matriculado");
        }

        IniciarMfaResponse iniciadoRematricula73;
        await using (var db = new CrmDbContext(opcoes))
        {
            var mfa = NovoMfaService(db);
            var sessaoRematricula = await mfa.IniciarMatriculaPrivilegiadaAsync(
                new MatricularPrivilegiadoRequest(titularPreso73.Email, SenhaBoa, tokenNovoDeRematricula73), null, default);
            iniciadoRematricula73 = await mfa.IniciarViaDesafioAsync(new IniciarViaDesafioRequest(sessaoRematricula.Desafio), default);
            await mfa.ConfirmarViaDesafioAsync(
                new ConfirmarViaDesafioRequest(sessaoRematricula.Desafio, CodigoTotp(iniciadoRematricula73.SegredoBase32, -1)), null, default);
        }

        Checar("73-5. nova matricula segura E POSSIVEL, usando o ticket que o proprio break-glass emitiu",
            (await Ler(titularPreso73.Id)).MfaAtivo);

        await using (var db = new CrmDbContext(opcoes))
        {
            var loginDepoisDaRematricula = await NovoAutenticacao(db, Guid.NewGuid())
                .AutenticarAsync(new LoginRequest(titularPreso73.Email, SenhaBoa));

            Checar("73-6b. SO DEPOIS da rematricula a conta volta a logar (agora exigindo MFA de novo, como esperado)",
                loginDepoisDaRematricula.RequerMfa);
        }

        // 7 — ResetDeEmergencia existe EXATAMENTE uma vez.
        await using (var db = new CrmDbContext(opcoes))
        {
            var eventos = await db.EventosDeSeguranca
                .Where(e => e.AtendenteId == titularPreso73.Id && e.Tipo == TipoDeEventoDeSeguranca.ResetDeEmergencia)
                .ToListAsync();

            Checar("73-7. ResetDeEmergencia existe EXATAMENTE uma vez", eventos.Count == 1);
            Checar("73-7b. AtorExterno preenchido com a identidade do operador (nao no Motivo)",
                eventos.Count == 1 && eventos[0].AtorExterno == "teste@verificador");
            Checar("73-7c. AtorAtendenteId nulo (executor foi operador SSH, nao Administrador do app)",
                eventos.Count == 1 && eventos[0].AtorAtendenteId is null);

            // 8 — nenhuma credencial aparece nos campos auditaveis.
            var camposAuditaveis = eventos.Count == 1 ? $"{eventos[0].Motivo} {eventos[0].AtorExterno}" : string.Empty;
            Checar("73-8. nenhuma credencial (senha/TOTP/ticket/segredo) aparece nos campos auditaveis do evento",
                !camposAuditaveis.Contains(SenhaBoa)
                && !camposAuditaveis.Contains(tokenNovoDeRematricula73)
                && !camposAuditaveis.Contains(iniciadoRematricula73.SegredoBase32));
        }

        // 9 — o deadlock teve saida segura: o break-glass funcionou SEM
        // nunca tocar o outro Administrador nem burlar a protecao do Titular
        // dele.
        Checar("73-9. o outro Administrador (nao-Titular) continua com MFA intacto — nunca foi tocado pelo break-glass",
            (await Ler(outroAdmin73.Id)).MfaAtivo);

        await using (var db = new CrmDbContext(opcoes))
        {
            Checar("73-pos. a politica obrigatoria do tenant continua ligada (break-glass nunca a desliga silenciosamente)",
                await db.Tenants.AsNoTracking().Where(t => t.Id == tenantIsolado73)
                    .Select(t => t.MfaObrigatorioParaAdministradores).SingleAsync());
        }
    }

    // ------------------------------------------------------------ cenario 74
    // BLOQUEADOR B18 (sexta revisao independente) — break-glass exclusivamente
    // como saida de deadlock: exige MFA ATUALMENTE ativo no alvo, e recusa
    // quando existe outro ator apto a executar o reset NORMAL (outro
    // Administrador com MFA — Titular tambem, se o alvo for Titular).

    {
        // Cada sub-teste ganha o PROPRIO tenant isolado — de proposito: a
        // regra nova (existeAtorApto) e "existe OUTRO privilegiado apto NO
        // MESMO TENANT". Compartilhar um unico tenant entre 74a-74d faria os
        // administradores criados para PROVAR uma recusa (74b/74c) vazarem
        // para o controle positivo (74d) e o fazerem falhar por um motivo
        // that nao e o que o teste pretende provar.
        async Task<Guid> NovoTenante74(string rotulo)
        {
            await using var db = new CrmDbContext(opcoes);
            var tenant = new Tenant
            {
                RazaoSocial = $"[TESTE SEC-03] Tenant isolado — break-glass elegibilidade ({rotulo})",
                Cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString()
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            tenantIdsDeTeste.Add(tenant.Id);
            return tenant.Id;
        }

        async Task<(Guid Id, string Email, string Segredo)> ContaMatriculada74(Guid tenantId, string rotulo, CargoNaServentia cargo)
        {
            await using var db = new CrmDbContext(opcoes);
            var conta = new Atendente
            {
                TenantId = tenantId,
                Nome = $"[TESTE SEC-03] {rotulo}",
                Email = $"teste-sec-03-74-{Guid.NewGuid():N}@teste.local",
                SenhaHash = hash.Gerar(SenhaBoa),
                Perfil = PerfilDeAtendente.Administrador,
                Cargo = cargo,
                Ativo = true
            };
            db.Atendentes.Add(conta);

            var tokenEmClaro = SegredosAleatoriosMfa.NovoToken();
            db.DesafiosDeMfa.Add(new DesafioDeMfa
            {
                TenantId = tenantId,
                AtendenteId = conta.Id,
                HashDoDesafio = SegredosAleatoriosMfa.Sha256Hex(tokenEmClaro),
                Tipo = TipoDeDesafioMfa.Ticket,
                TokenVersionNoDesafio = conta.TokenVersion,
                ExpiraEm = DateTime.UtcNow.AddHours(24)
            });
            await db.SaveChangesAsync();
            ids.Add(conta.Id);

            var mfa = NovoMfaService(db);
            var sessao = await mfa.IniciarMatriculaPrivilegiadaAsync(
                new MatricularPrivilegiadoRequest(conta.Email, SenhaBoa, tokenEmClaro), null, default);
            var iniciado = await mfa.IniciarViaDesafioAsync(new IniciarViaDesafioRequest(sessao.Desafio), default);
            await mfa.ConfirmarViaDesafioAsync(
                new ConfirmarViaDesafioRequest(sessao.Desafio, CodigoTotp(iniciado.SegredoBase32, -1)), null, default);

            return (conta.Id, conta.Email, iniciado.SegredoBase32);
        }

        /// <summary>Privilegiada, ATIVA, mas NUNCA passou pelo ticket — MfaAtivo continua false.</summary>
        async Task<(Guid Id, string Email)> ContaSemMfa74(Guid tenantId, string rotulo, CargoNaServentia cargo)
        {
            await using var db = new CrmDbContext(opcoes);
            var conta = new Atendente
            {
                TenantId = tenantId,
                Nome = $"[TESTE SEC-03] {rotulo}",
                Email = $"teste-sec-03-74-{Guid.NewGuid():N}@teste.local",
                SenhaHash = hash.Gerar(SenhaBoa),
                Perfil = PerfilDeAtendente.Administrador,
                Cargo = cargo,
                Ativo = true
            };
            db.Atendentes.Add(conta);
            await db.SaveChangesAsync();
            ids.Add(conta.Id);
            return (conta.Id, conta.Email);
        }

        // 74a — MfaAtivo=false no alvo: nao e um caso de break-glass (quem
        // nunca se matriculou tem bootstrap/ticket normal a disposicao).
        var tenant74a = await NovoTenante74("74a");
        var semMfa74a = await ContaSemMfa74(tenant74a, "74a - privilegiada sem MFA", CargoNaServentia.Titular);
        await using (var db = new CrmDbContext(opcoes))
        {
            Checar("74a. alvo sem MFA ativo -> ContaSemMfaAtivo (nao e caso de break-glass)",
                await FerramentaDeResetDeEmergencia.VerificarElegibilidadeAsync(db, semMfa74a.Email)
                    == ElegibilidadeDeEmergencia.ContaSemMfaAtivo);
        }

        // 74b — alvo TITULAR com outro Administrador+Titular ja matriculado
        // com MFA: o caminho NORMAL resolve sozinho, break-glass recusa.
        var tenant74b = await NovoTenante74("74b");
        var titular74b = await ContaMatriculada74(tenant74b, "74b - Titular com saida normal", CargoNaServentia.Titular);
        var outroTitularApto74b = await ContaMatriculada74(tenant74b, "74b - outro Titular apto", CargoNaServentia.Titular);
        await using (var db = new CrmDbContext(opcoes))
        {
            Checar("74b. alvo Titular com outro Administrador+Titular apto -> ExisteAtorAptoParaResetNormal",
                await FerramentaDeResetDeEmergencia.VerificarElegibilidadeAsync(db, titular74b.Email)
                    == ElegibilidadeDeEmergencia.ExisteAtorAptoParaResetNormal);
        }

        // 74c — alvo NAO-Titular com qualquer outro Administrador com MFA
        // (nao precisa ser Titular): caminho normal resolve, break-glass recusa.
        var tenant74c = await NovoTenante74("74c");
        var admin74c = await ContaMatriculada74(tenant74c, "74c - Administrador com saida normal", CargoNaServentia.Substituto);
        var outroAdminApto74c = await ContaMatriculada74(tenant74c, "74c - outro Administrador apto", CargoNaServentia.Substituto);
        await using (var db = new CrmDbContext(opcoes))
        {
            Checar("74c. alvo nao-Titular com outro Administrador apto (nao precisa ser Titular) -> ExisteAtorAptoParaResetNormal",
                await FerramentaDeResetDeEmergencia.VerificarElegibilidadeAsync(db, admin74c.Email)
                    == ElegibilidadeDeEmergencia.ExisteAtorAptoParaResetNormal);
        }

        // 74d — controle positivo: alvo nao-Titular com outro Administrador
        // presente, mas SEM MFA ativo (nao apto) -> continua Elegivel.
        var tenant74d = await NovoTenante74("74d");
        var admin74d = await ContaMatriculada74(tenant74d, "74d - Administrador preso", CargoNaServentia.Substituto);
        var outroSemMfa74d = await ContaSemMfa74(tenant74d, "74d - outro Administrador sem MFA (nao apto)", CargoNaServentia.Substituto);
        await using (var db = new CrmDbContext(opcoes))
        {
            _ = outroSemMfa74d;
            Checar("74d. outro Administrador presente mas SEM MFA ativo nao bloqueia -> continua Elegivel",
                await FerramentaDeResetDeEmergencia.VerificarElegibilidadeAsync(db, admin74d.Email)
                    == ElegibilidadeDeEmergencia.Elegivel);
        }
    }

    // ------------------------------------------------------------ cenario 75
    // BLOQUEADOR B19 (sexta revisao independente) — apos o break-glass a
    // conta fica sem login ATE a rematricula, INDEPENDENTE da politica do
    // tenant. O cenario 73 ligava a politica antes do reset, o que mascarava
    // este bloqueador (a recusa acontecia por causa da politica, nao do
    // sinalizador novo). Este cenario roda com a politica DESLIGADA o tempo
    // todo, provando que e o sinalizador por conta — nao a politica — quem
    // bloqueia o login so-senha.

    {
        async Task<ElegibilidadeDeEmergencia> TentarResetarDeEmergenciaViaReflexaoAsync75(
            CrmDbContext db, string email, string hashDoNovoTicket, string motivo, string atorExterno)
        {
            var metodo = typeof(FerramentaDeResetDeEmergencia).GetMethod(
                "TentarResetarDeEmergenciaAsync", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("TentarResetarDeEmergenciaAsync nao encontrado via reflexao.");

            var tarefa = (Task<ElegibilidadeDeEmergencia>)metodo.Invoke(
                null, [db, email, hashDoNovoTicket, motivo, atorExterno, CancellationToken.None])!;

            return await tarefa;
        }

        Guid tenantIsolado75;
        await using (var db = new CrmDbContext(opcoes))
        {
            var tenant = new Tenant
            {
                RazaoSocial = "[TESTE SEC-03] Tenant isolado — break-glass politica OFF (75)",
                Cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString()
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            tenantIsolado75 = tenant.Id;
        }
        tenantIdsDeTeste.Add(tenantIsolado75);

        // Titular SOLITARIO no tenant — nenhum outro privilegiado, entao a
        // elegibilidade do break-glass nao esbarra em ExisteAtorAptoParaResetNormal.
        Guid contaId75; string email75; string segredo75;
        await using (var db = new CrmDbContext(opcoes))
        {
            var conta = new Atendente
            {
                TenantId = tenantIsolado75,
                Nome = "[TESTE SEC-03] 75 - Titular solitario",
                Email = $"teste-sec-03-75-{Guid.NewGuid():N}@teste.local",
                SenhaHash = hash.Gerar(SenhaBoa),
                Perfil = PerfilDeAtendente.Administrador,
                Cargo = CargoNaServentia.Titular,
                Ativo = true
            };
            db.Atendentes.Add(conta);

            var tokenEmClaro = SegredosAleatoriosMfa.NovoToken();
            db.DesafiosDeMfa.Add(new DesafioDeMfa
            {
                TenantId = tenantIsolado75,
                AtendenteId = conta.Id,
                HashDoDesafio = SegredosAleatoriosMfa.Sha256Hex(tokenEmClaro),
                Tipo = TipoDeDesafioMfa.Ticket,
                TokenVersionNoDesafio = conta.TokenVersion,
                ExpiraEm = DateTime.UtcNow.AddHours(24)
            });
            await db.SaveChangesAsync();
            ids.Add(conta.Id);
            contaId75 = conta.Id;
            email75 = conta.Email;

            var mfa = NovoMfaService(db);
            var sessao = await mfa.IniciarMatriculaPrivilegiadaAsync(
                new MatricularPrivilegiadoRequest(conta.Email, SenhaBoa, tokenEmClaro), null, default);
            var iniciado = await mfa.IniciarViaDesafioAsync(new IniciarViaDesafioRequest(sessao.Desafio), default);
            await mfa.ConfirmarViaDesafioAsync(
                new ConfirmarViaDesafioRequest(sessao.Desafio, CodigoTotp(iniciado.SegredoBase32, -1)), null, default);
            segredo75 = iniciado.SegredoBase32;
        }

        await using (var db = new CrmDbContext(opcoes))
        {
            Checar("75-preparo. politica obrigatoria do tenant continua DESLIGADA (controle do cenario)",
                !await db.Tenants.AsNoTracking().Where(t => t.Id == tenantIsolado75)
                    .Select(t => t.MfaObrigatorioParaAdministradores).SingleAsync());
        }

        var tokenNovoDeRematricula75 = SegredosAleatoriosMfa.NovoToken();
        ElegibilidadeDeEmergencia resultadoBreakGlass75;
        await using (var db = new CrmDbContext(opcoes))
        {
            resultadoBreakGlass75 = await TentarResetarDeEmergenciaViaReflexaoAsync75(
                db, email75, SegredosAleatoriosMfa.Sha256Hex(tokenNovoDeRematricula75),
                "teste automatizado do cenario 75 — break-glass com politica OFF", "teste@verificador");
        }

        Checar("75-1. break-glass reconhece elegibilidade mesmo sem nenhum outro privilegiado no tenant",
            resultadoBreakGlass75 == ElegibilidadeDeEmergencia.Elegivel);

        var estadoPosBreakGlass75 = await Ler(contaId75);
        Checar("75-2. MFA removido pelo break-glass (MfaAtivo=false)", !estadoPosBreakGlass75.MfaAtivo);
        Checar("75-3. RematriculaObrigatoriaAposEmergencia = true logo apos o break-glass",
            estadoPosBreakGlass75.RematriculaObrigatoriaAposEmergencia);

        // O NUCLEO do bloqueador B19: politica do tenant DESLIGADA, mas o
        // login so-senha AINDA ASSIM tem que ser recusado — e o sinalizador
        // por conta, nao a politica, quem decide aqui.
        await using (var db = new CrmDbContext(opcoes))
        {
            UsuariosServiceException? falha = null;
            try
            {
                await NovoAutenticacao(db, Guid.NewGuid()).AutenticarAsync(new LoginRequest(email75, SenhaBoa));
            }
            catch (UsuariosServiceException ex) { falha = ex; }

            Checar("75-4. com a politica DESLIGADA, login so-senha e RECUSADO apos o break-glass (mfa_obrigatorio_nao_matriculado)",
                falha?.Codigo == "mfa_obrigatorio_nao_matriculado");
        }

        // Rematricula com o ticket que o proprio break-glass emitiu.
        IniciarMfaResponse iniciadoRematricula75;
        await using (var db = new CrmDbContext(opcoes))
        {
            var mfa = NovoMfaService(db);
            var sessaoRematricula = await mfa.IniciarMatriculaPrivilegiadaAsync(
                new MatricularPrivilegiadoRequest(email75, SenhaBoa, tokenNovoDeRematricula75), null, default);
            iniciadoRematricula75 = await mfa.IniciarViaDesafioAsync(new IniciarViaDesafioRequest(sessaoRematricula.Desafio), default);
            await mfa.ConfirmarViaDesafioAsync(
                new ConfirmarViaDesafioRequest(sessaoRematricula.Desafio, CodigoTotp(iniciadoRematricula75.SegredoBase32, -1)), null, default);
        }

        var estadoPosRematricula75 = await Ler(contaId75);
        Checar("75-5. rematricula com o ticket do break-glass E POSSIVEL (MfaAtivo volta a true)",
            estadoPosRematricula75.MfaAtivo);
        Checar("75-6. RematriculaObrigatoriaAposEmergencia volta a false so na confirmacao da matricula nova",
            !estadoPosRematricula75.RematriculaObrigatoriaAposEmergencia);

        // SO DEPOIS da rematricula o login volta a funcionar — e volta a
        // exigir MFA de novo (nunca vira login so-senha so porque a politica
        // esta desligada: MfaAtivo=true sempre pede o segundo fator).
        await using (var db = new CrmDbContext(opcoes))
        {
            var loginDepoisDaRematricula75 = await NovoAutenticacao(db, Guid.NewGuid())
                .AutenticarAsync(new LoginRequest(email75, SenhaBoa));

            Checar("75-7. SO DEPOIS da rematricula o login volta a funcionar, exigindo MFA de novo",
                loginDepoisDaRematricula75.RequerMfa);
        }

        _ = segredo75;
    }

    // ------------------------------------------------------------ cenario 76
    // BLOQUEADORES B22/B23 (oitava revisao independente) — o controle de
    // duas pessoas deixou de ser um segredo TOTP COMPARTILHADO (cujo seed
    // vivia no proprio servidor, entao quem tinha SSH gerava os codigos da
    // "segunda pessoa" — B23; e cujo codigo valia mais de uma vez dentro da
    // janela de +-1 passo — B22) e passou a ser ASSINATURA ASSIMETRICA
    // ECDSA P-256/SHA-256 sobre um desafio com nonce fresco de 256 bits.
    //
    // O servidor guarda SO a chave publica. Os testes abaixo geram o par EM
    // MEMORIA (nunca uma chave fixa no repositorio) e exercitam as decisoes
    // PURAS por reflexao — mesmo padrao de AmbienteNaoInterativo (cenario 72),
    // sem reabrir superficie publica nenhuma.

    {
        var assemblyDaApi = typeof(FerramentaDeResetDeEmergencia).Assembly;
        var tipoAutorizacao = assemblyDaApi
            .GetType("CrmCartorio.Api.Infrastructure.Security.AutorizacaoDeSegundaPessoa")
            ?? throw new InvalidOperationException("AutorizacaoDeSegundaPessoa nao encontrada.");
        var tipoOperacao = assemblyDaApi
            .GetType("CrmCartorio.Api.Infrastructure.Security.OperacaoDeEmergencia")
            ?? throw new InvalidOperationException("OperacaoDeEmergencia nao encontrada.");

        var metodoMontar = tipoAutorizacao.GetMethod("MontarDesafioCanonico", BindingFlags.NonPublic | BindingFlags.Static);
        var metodoConfere = tipoAutorizacao.GetMethod("AssinaturaConfere", BindingFlags.NonPublic | BindingFlags.Static);
        var metodoChaveValida = tipoAutorizacao.GetMethod("ChavePublicaConfiguradaValida", BindingFlags.NonPublic | BindingFlags.Static);
        var metodoNonce = tipoAutorizacao.GetMethod("GerarNonce", BindingFlags.NonPublic | BindingFlags.Static);
        var metodoAtor = tipoAutorizacao.GetMethod("FormatarAtorDeDuasPessoas", BindingFlags.NonPublic | BindingFlags.Static);

        Checar("76a. AutorizacaoDeSegundaPessoa expoe as decisoes puras (montar/verificar/nonce), todas nao-publicas",
            metodoMontar is not null && metodoConfere is not null && metodoChaveValida is not null
            && metodoNonce is not null && metodoAtor is not null
            && !metodoConfere.IsPublic && !metodoMontar.IsPublic && !metodoNonce.IsPublic
            && metodoConfere.ReturnType == typeof(bool));

        var operacaoReset76 = Enum.Parse(tipoOperacao, "ResetDeEmergencia");
        var operacaoReemissao76 = Enum.Parse(tipoOperacao, "ReemitirTicketDeEmergencia");

        string Nonce76() => (string)metodoNonce!.Invoke(null, null)!;

        string Desafio76(object operacao, string alvo, string operador, string custodiante, string motivo, string nonce) =>
            (string)metodoMontar!.Invoke(null, [operacao, alvo, operador, custodiante, motivo, nonce])!;

        bool Confere76(string? chavePublicaPem, string desafio, string? assinaturaBase64) =>
            (bool)metodoConfere!.Invoke(null, [chavePublicaPem, desafio, assinaturaBase64])!;

        // Par de chaves GERADO EM MEMORIA so para o teste — a chave privada
        // existe apenas dentro deste processo e nunca e escrita em lugar
        // nenhum, exatamente como a do custodiante real nunca entra no
        // servidor.
        using var chaveDoCustodiante76 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pemPublico76 = chaveDoCustodiante76.ExportSubjectPublicKeyInfoPem();

        using var chaveDeOutroCustodiante76 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pemPublicoDeOutro76 = chaveDeOutroCustodiante76.ExportSubjectPublicKeyInfoPem();

        string Assinar76(ECDsa chave, string desafio) =>
            Convert.ToBase64String(chave.SignData(
                Encoding.UTF8.GetBytes(desafio), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));

        const string alvo76 = "titular.preso@cartorio.example";
        const string operador76 = "thiago@SERVIDOR";
        const string custodiante76 = "substituto-chave-2026-09";
        const string motivo76 = "perdeu autenticador e codigos de recuperacao";

        var nonce76 = Nonce76();
        var desafioBase76 = Desafio76(operacaoReset76, alvo76, operador76, custodiante76, motivo76, nonce76);
        var assinaturaBase76 = Assinar76(chaveDoCustodiante76, desafioBase76);

        // (1) caminho feliz
        Checar("76b. chave publica valida + assinatura correta do custodiante -> ACEITA",
            Confere76(pemPublico76, desafioBase76, assinaturaBase76));

        // (2) chave publica ausente
        Checar("76c. chave publica ausente/vazia -> RECUSA (falha fechada, sem fallback)",
            !Confere76(null, desafioBase76, assinaturaBase76)
            && !Confere76("", desafioBase76, assinaturaBase76)
            && !(bool)metodoChaveValida!.Invoke(null, [(string?)null])!);

        // (3) chave publica malformada
        Checar("76d. chave publica malformada (PEM invalido) -> RECUSA, nunca lanca",
            !Confere76("-----BEGIN PUBLIC KEY-----\nnaoEhBase64Valido!!!\n-----END PUBLIC KEY-----", desafioBase76, assinaturaBase76)
            && !(bool)metodoChaveValida!.Invoke(null, ["isto nao e pem"])!);

        // (3b) algoritmo/curva inesperados
        using var chaveP384no76 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Checar("76e. chave publica de curva inesperada (P-384) -> RECUSA (so P-256 e aceita)",
            !(bool)metodoChaveValida!.Invoke(null, [chaveP384no76.ExportSubjectPublicKeyInfoPem()])!);

        using var chaveRsa76 = RSA.Create(2048);
        Checar("76f. chave publica de algoritmo inesperado (RSA) -> RECUSA",
            !(bool)metodoChaveValida!.Invoke(null, [chaveRsa76.ExportSubjectPublicKeyInfoPem()])!);

        // BLOQUEADOR B24 (nona revisao independente) — ECDsa.ImportFromPem
        // aceita "PUBLIC KEY" (SPKI), mas TAMBEM "PRIVATE KEY" (PKCS8) e
        // "EC PRIVATE KEY" (SEC1). Um PEM com a chave PRIVADA do MESMO par
        // usado no teste (chaveDoCustodiante76) colado em
        // Emergencia:ChavePublicaDoSegundoCustodiante nao pode ser aceito —
        // isso reabriria B23 (material privado acessivel a partir do
        // servidor). A correcao usa PemEncoding + ImportSubjectPublicKeyInfo,
        // exigindo label EXATO "PUBLIC KEY" e um UNICO bloco no texto.
        var pemPrivadoEc76 = chaveDoCustodiante76.ExportECPrivateKeyPem();
        var pemPrivadoPkcs876 = chaveDoCustodiante76.ExportPkcs8PrivateKeyPem();

        Checar("76f-1. ExportECPrivateKeyPem (label \"EC PRIVATE KEY\") da MESMA chave -> RECUSA",
            !(bool)metodoChaveValida!.Invoke(null, [pemPrivadoEc76])!);

        Checar("76f-2. ExportPkcs8PrivateKeyPem (label \"PRIVATE KEY\") da MESMA chave -> RECUSA",
            !(bool)metodoChaveValida!.Invoke(null, [pemPrivadoPkcs876])!);

        Checar("76f-3. private key valida + ASSINATURA CORRETA -> mesmo assim RECUSA (private key nunca serve de 'chave publica')",
            !Confere76(pemPrivadoEc76, desafioBase76, assinaturaBase76)
            && !Confere76(pemPrivadoPkcs876, desafioBase76, assinaturaBase76));

        Checar("76f-4. PEM com PUBLIC KEY + EC PRIVATE KEY colados (nesta ordem) -> RECUSA",
            !(bool)metodoChaveValida!.Invoke(null, [pemPublico76 + "\n" + pemPrivadoEc76])!
            && !Confere76(pemPublico76 + "\n" + pemPrivadoEc76, desafioBase76, assinaturaBase76));

        Checar("76f-5. PEM com EC PRIVATE KEY + PUBLIC KEY colados (ordem invertida) -> RECUSA tambem",
            !(bool)metodoChaveValida!.Invoke(null, [pemPrivadoEc76 + "\n" + pemPublico76])!);

        Checar("76f-6. PEM com PUBLIC KEY + PRIVATE KEY (PKCS8) colados -> RECUSA",
            !(bool)metodoChaveValida!.Invoke(null, [pemPublico76 + "\n" + pemPrivadoPkcs876])!);

        Checar("76f-7. PEM com a MESMA chave publica duplicada (dois blocos validos) -> RECUSA (sem ambiguidade aceitavel)",
            !(bool)metodoChaveValida!.Invoke(null, [pemPublico76 + "\n" + pemPublico76])!);

        Checar("76f-8. label com capitalizacao diferente (\"Public Key\", nao \"PUBLIC KEY\") -> RECUSA (comparacao ordinal exata)",
            !(bool)metodoChaveValida!.Invoke(null, [pemPublico76.Replace("PUBLIC KEY", "Public Key")])!);

        Checar("76f-9. caminho feliz PERMANECE aceitando PEM SPKI normal (ExportSubjectPublicKeyInfoPem) apos a correcao",
            (bool)metodoChaveValida!.Invoke(null, [pemPublico76])!
            && Confere76(pemPublico76, desafioBase76, assinaturaBase76));

        // (4) assinatura malformada
        Checar("76g. assinatura malformada (nao-Base64) -> RECUSA, nunca lanca",
            !Confere76(pemPublico76, desafioBase76, "isto###nao#e#base64")
            && !Confere76(pemPublico76, desafioBase76, "")
            && !Confere76(pemPublico76, desafioBase76, null));

        // (5) assinatura sintaticamente Base64, mas nao e uma assinatura DER
        Checar("76h. assinatura Base64 valida mas DER invalido -> RECUSA",
            !Confere76(pemPublico76, desafioBase76, Convert.ToBase64String([1, 2, 3, 4, 5, 6, 7, 8])));

        // (6) chave privada diferente
        Checar("76i. assinatura de OUTRA chave privada -> RECUSA",
            !Confere76(pemPublico76, desafioBase76, Assinar76(chaveDeOutroCustodiante76, desafioBase76)));

        Checar("76j. assinatura correta conferida contra a chave publica ERRADA -> RECUSA",
            !Confere76(pemPublicoDeOutro76, desafioBase76, assinaturaBase76));

        // (7) desafio A nao valida desafio B
        var desafioOutroNonce76 = Desafio76(operacaoReset76, alvo76, operador76, custodiante76, motivo76, Nonce76());
        Checar("76k. assinatura do desafio A NAO valida o desafio B (nonce diferente)",
            !Confere76(pemPublico76, desafioOutroNonce76, assinaturaBase76));

        // (8) ASSINATURA CAPTURADA — o teste que substitui diretamente B22:
        // uma execucao inteira acontece, a assinatura dela e capturada, e a
        // execucao SEGUINTE (mesma operacao, mesmo alvo, mesmo motivo, mesmo
        // operador) precisa recusa-la, porque o nonce e novo.
        var nonceExecucao1_76 = Nonce76();
        var desafioExecucao1_76 = Desafio76(operacaoReset76, alvo76, operador76, custodiante76, motivo76, nonceExecucao1_76);
        var assinaturaExecucao1_76 = Assinar76(chaveDoCustodiante76, desafioExecucao1_76);
        var execucao1Autorizou76 = Confere76(pemPublico76, desafioExecucao1_76, assinaturaExecucao1_76);

        var nonceExecucao2_76 = Nonce76();
        var desafioExecucao2_76 = Desafio76(operacaoReset76, alvo76, operador76, custodiante76, motivo76, nonceExecucao2_76);
        var execucao2AceitouAssinaturaVelha76 = Confere76(pemPublico76, desafioExecucao2_76, assinaturaExecucao1_76);

        Checar("76l. ASSINATURA CAPTURADA: autoriza a execucao 1 e NAO autoriza a execucao 2 (replay fechado — B22)",
            execucao1Autorizou76 && !execucao2AceitouAssinaturaVelha76 && nonceExecucao1_76 != nonceExecucao2_76);

        // (9)/(10) cross-operation
        var desafioReemissao76 = Desafio76(operacaoReemissao76, alvo76, operador76, custodiante76, motivo76, nonce76);
        var assinaturaReemissao76 = Assinar76(chaveDoCustodiante76, desafioReemissao76);

        Checar("76m. assinatura de RESET_DE_EMERGENCIA NAO autoriza REEMITIR_TICKET_DE_EMERGENCIA",
            !Confere76(pemPublico76, desafioReemissao76, assinaturaBase76));
        Checar("76n. assinatura de REEMITIR_TICKET_DE_EMERGENCIA NAO autoriza RESET_DE_EMERGENCIA",
            !Confere76(pemPublico76, desafioBase76, assinaturaReemissao76));

        // (11) cross-target
        var desafioOutroAlvo76 = Desafio76(operacaoReset76, "outra.conta@cartorio.example", operador76, custodiante76, motivo76, nonce76);
        Checar("76o. assinatura para a conta A NAO autoriza a conta B",
            !Confere76(pemPublico76, desafioOutroAlvo76, assinaturaBase76));

        // (12) motivo trocado depois de assinado
        var desafioOutroMotivo76 = Desafio76(operacaoReset76, alvo76, operador76, custodiante76, "motivo diferente do assinado", nonce76);
        Checar("76p. trocar o MOTIVO depois da assinatura invalida a autorizacao",
            !Confere76(pemPublico76, desafioOutroMotivo76, assinaturaBase76));

        // (13) operador trocado depois de assinado
        var desafioOutroOperador76 = Desafio76(operacaoReset76, alvo76, "outro.operador@SERVIDOR", custodiante76, motivo76, nonce76);
        Checar("76q. trocar o OPERADOR depois da assinatura invalida a autorizacao",
            !Confere76(pemPublico76, desafioOutroOperador76, assinaturaBase76));

        // (14) nonce: entropia e frescor
        var nonces76 = Enumerable.Range(0, 200).Select(_ => Nonce76()).ToArray();
        var bytesDoNonce76 = Convert.FromBase64String(
            nonces76[0].Replace('-', '+').Replace('_', '/').PadRight((nonces76[0].Length + 3) / 4 * 4, '='));
        Checar("76r. nonce tem 256 bits (32 bytes) e 200 chamadas produzem 200 valores distintos",
            bytesDoNonce76.Length == 32 && nonces76.Distinct(StringComparer.Ordinal).Count() == 200);

        // (15) determinismo do payload canonico
        var determinista1_76 = Desafio76(operacaoReset76, alvo76, operador76, custodiante76, motivo76, nonce76);
        var determinista2_76 = Desafio76(operacaoReset76, "  " + alvo76.ToUpperInvariant() + " ", operador76, custodiante76, motivo76, nonce76);
        Checar("76s. payload canonico e deterministico para os mesmos campos (e normaliza o alvo)",
            string.Equals(determinista1_76, desafioBase76, StringComparison.Ordinal)
            && string.Equals(determinista2_76, desafioBase76, StringComparison.Ordinal));

        // estrutura do payload: amarra versao, operacao, alvo, operador,
        // custodiante, hash do motivo e nonce — 7 linhas, ordem fixa, LF.
        var linhas76 = desafioBase76.Split('\n');
        Checar("76t. payload amarra versao + operacao + alvo + operador + custodiante + hash do motivo + nonce (7 linhas, LF)",
            linhas76.Length == 7
            && linhas76[0] == "CRM-SEC03-AUTORIZACAO-V1"
            && linhas76[1] == "OPERACAO=RESET_DE_EMERGENCIA"
            && linhas76[2] == $"ALVO={alvo76}"
            && linhas76[3] == $"OPERADOR={operador76}"
            && linhas76[4] == $"CUSTODIANTE={custodiante76}"
            && linhas76[5] == "MOTIVO_SHA256=" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(motivo76)))
            && linhas76[6] == $"NONCE={nonce76}"
            && !desafioBase76.Contains('\r'));

        // O motivo em CLARO nunca entra no payload assinado (so o hash) —
        // evita ambiguidade estrutural com motivo multilinha.
        Checar("76u. o motivo entra por SHA-256, nunca em claro (sem ambiguidade estrutural no payload)",
            !desafioBase76.Contains(motivo76, StringComparison.Ordinal));

        // (16)/(17) identidade para auditoria: as duas pessoas, nenhum segredo
        var metodoFingerprint76 = tipoAutorizacao.GetMethod("FingerprintDaChavePublica", BindingFlags.NonPublic | BindingFlags.Static);
        var fingerprint76 = (string)metodoFingerprint76!.Invoke(null, [pemPublico76])!;
        var fingerprintDeOutro76 = (string)metodoFingerprint76.Invoke(null, [pemPublicoDeOutro76])!;

        Checar("76v. fingerprint da chave publica e deterministico, curto e diferente por chave",
            fingerprint76.Length == 16
            && fingerprint76 == (string)metodoFingerprint76.Invoke(null, [pemPublico76])!
            && fingerprint76 != fingerprintDeOutro76);

        var atorFormatado76 = (string)metodoAtor!.Invoke(null, [operador76, custodiante76, fingerprint76])!;
        Checar("76w. AtorExterno junta operador SSH + custodiante + fingerprint da chave, sem assinatura/chave/segredo",
            atorFormatado76.Contains(operador76) && atorFormatado76.Contains(custodiante76)
            && atorFormatado76.Contains(fingerprint76)
            && !atorFormatado76.Contains(assinaturaBase76) && !atorFormatado76.Contains(pemPublico76)
            && !atorFormatado76.Contains("BEGIN"));

        // Se a coluna apertar, o que sobrevive e o FINGERPRINT (amarra o
        // evento a chave real); o texto livre do identificador e que e cortado.
        var atorLongo76 = (string)metodoAtor.Invoke(null, [operador76, new string('X', 300), fingerprint76])!;
        Checar("76w-truncagem. AtorExterno trunca no limite da coluna (120) preservando o fingerprint (identificador longo)",
            atorLongo76.Length <= 120 && atorLongo76.Contains(fingerprint76));

        // BLOQUEADOR B25 (nona revisao independente) — a versao anterior
        // afirmava que o fingerprint nunca era cortado, mas quando o
        // OPERADOR sozinho ja estourava a coluna (identificador nao tinha
        // nem como ceder espaco suficiente), o fallback cortava a string
        // inteira pelo INICIO (texto[..120]) — descartando o sufixo com o
        // fingerprint junto. Os tres casos abaixo cobrem exatamente isso:
        // operador longo, identificador longo, e os dois longos ao mesmo tempo.
        var atorOperadorLongo76 = (string)metodoAtor.Invoke(null, [new string('O', 300), custodiante76, fingerprint76])!;
        Checar("76w-truncagem-operador. operador de 300 chars -> <=120 e o fingerprint completo sobrevive",
            atorOperadorLongo76.Length <= 120 && atorOperadorLongo76.Contains(fingerprint76));

        var atorAmbosLongos76 = (string)metodoAtor.Invoke(null, [new string('O', 300), new string('C', 300), fingerprint76])!;
        Checar("76w-truncagem-ambos. operador E identificador de 300 chars -> <=120 e o fingerprint completo sobrevive",
            atorAmbosLongos76.Length <= 120 && atorAmbosLongos76.Contains(fingerprint76));

        Checar("76w-truncagem-prioridade. com operador MODERADO e identificador longo, o operador NAO e cortado (identificador cede primeiro)",
            atorLongo76.Contains(operador76));

        // (19)/(21) regressao de codigo-fonte nas DUAS ferramentas
        foreach (var caminhoRelativo76 in new[]
        {
            Path.Combine("CrmCartorio.Api", "Infrastructure", "Security", "FerramentaDeResetDeEmergencia.cs"),
            Path.Combine("CrmCartorio.Api", "Infrastructure", "Security", "FerramentaDeReemissaoDeTicketDeEmergencia.cs")
        })
        {
            var rotulo76 = Path.GetFileNameWithoutExtension(caminhoRelativo76);
            var codigoFonte76 = await File.ReadAllTextAsync(caminhoRelativo76);

            Checar($"76x-{rotulo76}. usa o MESMO componente assimetrico (AutorizacaoDeSegundaPessoa.AutorizarInterativo)",
                codigoFonte76.Contains("AutorizacaoDeSegundaPessoa.AutorizarInterativo("));

            var posicaoAutorizacao76 = codigoFonte76.IndexOf("AutorizacaoDeSegundaPessoa.AutorizarInterativo(", StringComparison.Ordinal);
            var posicaoConnect76 = codigoFonte76.IndexOf("CanConnectAsync", StringComparison.Ordinal);
            Checar($"76y-{rotulo76}. a autorizacao criptografica acontece ANTES de CanConnectAsync",
                posicaoAutorizacao76 >= 0 && posicaoConnect76 >= 0 && posicaoAutorizacao76 < posicaoConnect76);

            Checar($"76z-{rotulo76}. os mecanismos antigos (TOTP compartilhado, PIN, hash fixo) nao existem mais",
                !codigoFonte76.Contains("SegredoTotpDeDuasPessoas")
                && !codigoFonte76.Contains("CodigoConfere")
                && !codigoFonte76.Contains("ControleDeDuasPessoas")
                && !codigoFonte76.Contains("PinDeDuasPessoas")
                && !codigoFonte76.Contains("HashDoPinDeDuasPessoas"));
        }

        var fonteAutorizacao76 = await File.ReadAllTextAsync(
            Path.Combine("CrmCartorio.Api", "Infrastructure", "Security", "AutorizacaoDeSegundaPessoa.cs"));

        // So o CODIGO, sem comentario/doc — os comentarios desta classe
        // citam "ImportFromPem" de proposito, como historico do bloqueador
        // B24 (por que ele foi abandonado). A regressao real e sobre CHAMADA
        // de codigo, nao sobre o texto do arquivo inteiro conter a palavra.
        var codigoSemComentarios76 = string.Join('\n', fonteAutorizacao76
            .Split('\n')
            .Where(linha => !linha.TrimStart().StartsWith("///", StringComparison.Ordinal)
                          && !linha.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        // (20) o CRM nunca carrega/importa chave privada, e nunca assina.
        // CORRECAO (bloqueador B24): a versao anterior desta checagem exigia
        // que o CODIGO chamasse "ImportFromPem" — exatamente o metodo que o
        // B24 identificou como aceitando PRIVATE KEY/EC PRIVATE KEY sem
        // distincao. Agora a regressao e o oposto: nenhuma CHAMADA a
        // ImportFromPem pode existir no componente, e ImportSubjectPublicKeyInfo
        // (que exige SPKI explicitamente) tem que estar presente.
        Checar("76aa. o componente NUNCA importa chave privada nem assina, e o CODIGO nao chama mais ImportFromPem (usa ImportSubjectPublicKeyInfo)",
            !codigoSemComentarios76.Contains("ImportECPrivateKey")
            && !codigoSemComentarios76.Contains("ImportPkcs8PrivateKey")
            && !codigoSemComentarios76.Contains("ImportEncryptedPkcs8PrivateKey")
            && !codigoSemComentarios76.Contains("SignData")
            && !codigoSemComentarios76.Contains("SignHash")
            && !codigoSemComentarios76.Contains("ImportFromPem")
            && codigoSemComentarios76.Contains("ImportSubjectPublicKeyInfo")
            && codigoSemComentarios76.Contains("VerifyData"));

        Checar("76aa-1. a checagem de rotulo do PEM e exigida em texto (\"PUBLIC KEY\") e o parsing usa PemEncoding",
            fonteAutorizacao76.Contains("PemEncoding")
            && fonteAutorizacao76.Contains("\"PUBLIC KEY\""));

        var arquivosDeProducaoDeSeguranca76 = Directory.GetFiles(
            Path.Combine("CrmCartorio.Api", "Infrastructure", "Security"), "*.cs");
        var producaoImportaChavePrivada76 = new List<string>();
        foreach (var arquivo76 in arquivosDeProducaoDeSeguranca76)
        {
            var conteudo76 = await File.ReadAllTextAsync(arquivo76);
            if (conteudo76.Contains("ImportECPrivateKey") || conteudo76.Contains("ImportPkcs8PrivateKey")
                || conteudo76.Contains("ImportFromEncryptedPem") || conteudo76.Contains("ECDsa.Create(ECCurve"))
            {
                producaoImportaChavePrivada76.Add(Path.GetFileName(arquivo76));
            }
        }
        Checar("76bb. NENHUM arquivo de producao em Infrastructure/Security cria ou importa chave privada de custodiante",
            producaoImportaChavePrivada76.Count == 0);

        // (18) configuracao: SOMENTE material publico
        foreach (var exemplo76 in new[]
        {
            "appsettings.Development.example.json",
            "appsettings.Producao.example.json",
            "appsettings.Production.example.json",
            "appsettings.Teste.example.json"
        })
        {
            var caminhoExemplo76 = Path.Combine("CrmCartorio.Api", exemplo76);
            if (!File.Exists(caminhoExemplo76))
            {
                continue;
            }

            var conteudoExemplo76 = await File.ReadAllTextAsync(caminhoExemplo76);
            Checar($"76cc-{exemplo76}. configuracao traz SO chave publica/identificador — nunca segredo TOTP nem chave privada",
                !conteudoExemplo76.Contains("SegredoTotpDeDuasPessoas")
                && !conteudoExemplo76.Contains("HashDoPinDeDuasPessoas")
                && !conteudoExemplo76.Contains("BEGIN EC PRIVATE KEY")
                && !conteudoExemplo76.Contains("BEGIN PRIVATE KEY")
                && conteudoExemplo76.Contains("ChavePublicaDoSegundoCustodiante")
                && conteudoExemplo76.Contains("IdentificadorDoSegundoCustodiante"));
        }
    }

    // ------------------------------------------------------------ cenario 77
    // BLOQUEADOR B20 (setima revisao independente) — reemissao de ticket
    // DURANTE uma recuperacao de emergencia ja aberta: break-glass -> ticket
    // antigo perdido/inutilizavel -> login continua bloqueado -> reemissao
    // -> ticket antigo invalido -> novo ticket matricula -> flag limpa ->
    // login volta. NUNCA reativa MFA nem libera login por si so.

    {
        async Task<ElegibilidadeDeEmergencia> TentarResetarDeEmergenciaViaReflexaoAsync77(
            CrmDbContext db, string email, string hashDoNovoTicket, string motivo, string atorExterno)
        {
            var metodo = typeof(FerramentaDeResetDeEmergencia).GetMethod(
                "TentarResetarDeEmergenciaAsync", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("TentarResetarDeEmergenciaAsync nao encontrado via reflexao.");
            return await (Task<ElegibilidadeDeEmergencia>)metodo.Invoke(
                null, [db, email, hashDoNovoTicket, motivo, atorExterno, CancellationToken.None])!;
        }

        async Task<ElegibilidadeDeReemissaoDeTicket> TentarReemitirTicketViaReflexaoAsync77(
            CrmDbContext db, string email, string hashDoNovoTicket, string motivo, string atorExterno)
        {
            var metodo = typeof(FerramentaDeReemissaoDeTicketDeEmergencia).GetMethod(
                "TentarReemitirTicketAsync", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("TentarReemitirTicketAsync nao encontrado via reflexao.");
            return await (Task<ElegibilidadeDeReemissaoDeTicket>)metodo.Invoke(
                null, [db, email, hashDoNovoTicket, motivo, atorExterno, CancellationToken.None])!;
        }

        async Task<Guid> NovoTenante77(string rotulo)
        {
            await using var db = new CrmDbContext(opcoes);
            var tenant = new Tenant
            {
                RazaoSocial = $"[TESTE SEC-03] Tenant isolado — reemissao ({rotulo})",
                Cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString()
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            tenantIdsDeTeste.Add(tenant.Id);
            return tenant.Id;
        }

        async Task<(Guid Id, string Email, string Segredo)> ContaMatriculada77(Guid tenantId, string rotulo, CargoNaServentia cargo)
        {
            await using var db = new CrmDbContext(opcoes);
            var conta = new Atendente
            {
                TenantId = tenantId,
                Nome = $"[TESTE SEC-03] {rotulo}",
                Email = $"teste-sec-03-77-{Guid.NewGuid():N}@teste.local",
                SenhaHash = hash.Gerar(SenhaBoa),
                Perfil = PerfilDeAtendente.Administrador,
                Cargo = cargo,
                Ativo = true
            };
            db.Atendentes.Add(conta);

            var tokenEmClaro = SegredosAleatoriosMfa.NovoToken();
            db.DesafiosDeMfa.Add(new DesafioDeMfa
            {
                TenantId = tenantId,
                AtendenteId = conta.Id,
                HashDoDesafio = SegredosAleatoriosMfa.Sha256Hex(tokenEmClaro),
                Tipo = TipoDeDesafioMfa.Ticket,
                TokenVersionNoDesafio = conta.TokenVersion,
                ExpiraEm = DateTime.UtcNow.AddHours(24)
            });
            await db.SaveChangesAsync();
            ids.Add(conta.Id);

            var mfa = NovoMfaService(db);
            var sessao = await mfa.IniciarMatriculaPrivilegiadaAsync(
                new MatricularPrivilegiadoRequest(conta.Email, SenhaBoa, tokenEmClaro), null, default);
            var iniciado = await mfa.IniciarViaDesafioAsync(new IniciarViaDesafioRequest(sessao.Desafio), default);
            await mfa.ConfirmarViaDesafioAsync(
                new ConfirmarViaDesafioRequest(sessao.Desafio, CodigoTotp(iniciado.SegredoBase32, -1)), null, default);

            return (conta.Id, conta.Email, iniciado.SegredoBase32);
        }

        // Titular SOLITARIO — nenhum outro privilegiado, entao nem a
        // elegibilidade do break-glass nem a da reemissao esbarram em
        // ExisteAtorAptoParaResetNormal.
        var tenant77 = await NovoTenante77("fluxo principal");
        var titular77 = await ContaMatriculada77(tenant77, "77 - Titular preso", CargoNaServentia.Titular);

        // 1 — abre a emergencia (break-glass).
        var tokenTicketAntigo77 = SegredosAleatoriosMfa.NovoToken();
        var hashTicketAntigo77 = SegredosAleatoriosMfa.Sha256Hex(tokenTicketAntigo77);
        await using (var db = new CrmDbContext(opcoes))
        {
            var resultado = await TentarResetarDeEmergenciaViaReflexaoAsync77(
                db, titular77.Email, hashTicketAntigo77, "cenario 77 — abre a emergencia", "teste@verificador");
            Checar("77-1. break-glass abre a emergencia normalmente", resultado == ElegibilidadeDeEmergencia.Elegivel);
        }

        var estadoAposBreakGlass77 = await Ler(titular77.Id);
        Checar("77-1b. MfaAtivo=false e RematriculaObrigatoriaAposEmergencia=true logo apos o break-glass",
            !estadoAposBreakGlass77.MfaAtivo && estadoAposBreakGlass77.RematriculaObrigatoriaAposEmergencia);

        // 2 — "ticket antigo perdido/expirado": login continua bloqueado.
        await using (var db = new CrmDbContext(opcoes))
        {
            UsuariosServiceException? falha = null;
            try { await NovoAutenticacao(db, Guid.NewGuid()).AutenticarAsync(new LoginRequest(titular77.Email, SenhaBoa)); }
            catch (UsuariosServiceException ex) { falha = ex; }
            Checar("77-2. login continua bloqueado enquanto o ticket antigo esta perdido/inutilizavel",
                falha?.Codigo == "mfa_obrigatorio_nao_matriculado");
        }

        // Elegibilidade da REEMISSAO, exposta publicamente (sem reflexao).
        await using (var db = new CrmDbContext(opcoes))
        {
            Checar("77-3. FerramentaDeReemissaoDeTicketDeEmergencia reconhece a emergencia aberta como Elegivel",
                await FerramentaDeReemissaoDeTicketDeEmergencia.VerificarElegibilidadeAsync(db, titular77.Email)
                    == ElegibilidadeDeReemissaoDeTicket.Elegivel);
        }

        // 3 — reemissao.
        var tokenTicketNovo77 = SegredosAleatoriosMfa.NovoToken();
        ElegibilidadeDeReemissaoDeTicket resultadoReemissao77;
        await using (var db = new CrmDbContext(opcoes))
        {
            resultadoReemissao77 = await TentarReemitirTicketViaReflexaoAsync77(
                db, titular77.Email, SegredosAleatoriosMfa.Sha256Hex(tokenTicketNovo77),
                "cenario 77 — ticket antigo perdido, reemitindo", "teste@verificador (segundo custodiante: Teste 77)");
        }
        Checar("77-4. reemissao reconhece elegibilidade e emite o ticket novo",
            resultadoReemissao77 == ElegibilidadeDeReemissaoDeTicket.Elegivel);

        // 4 — invariante: NUNCA reativa MFA, NUNCA desliga o sinalizador.
        var estadoAposReemissao77 = await Ler(titular77.Id);
        Checar("77-5. apos a reemissao, MfaAtivo CONTINUA false (reemissao nunca reativa MFA)",
            !estadoAposReemissao77.MfaAtivo);
        Checar("77-6. apos a reemissao, RematriculaObrigatoriaAposEmergencia CONTINUA true (reemissao nunca desliga o flag)",
            estadoAposReemissao77.RematriculaObrigatoriaAposEmergencia);

        // 5 — login CONTINUA bloqueado (reemissao nunca libera login por si so).
        await using (var db = new CrmDbContext(opcoes))
        {
            UsuariosServiceException? falha = null;
            try { await NovoAutenticacao(db, Guid.NewGuid()).AutenticarAsync(new LoginRequest(titular77.Email, SenhaBoa)); }
            catch (UsuariosServiceException ex) { falha = ex; }
            Checar("77-7. login CONTINUA bloqueado logo apos a reemissao (so a rematricula libera)",
                falha?.Codigo == "mfa_obrigatorio_nao_matriculado");
        }

        // 6 — ticket ANTIGO invalidado pela reemissao.
        await using (var db = new CrmDbContext(opcoes))
        {
            UsuariosServiceException? falha = null;
            try
            {
                await NovoMfaService(db).IniciarMatriculaPrivilegiadaAsync(
                    new MatricularPrivilegiadoRequest(titular77.Email, SenhaBoa, tokenTicketAntigo77), null, default);
            }
            catch (UsuariosServiceException ex) { falha = ex; }
            Checar("77-8. o ticket ANTIGO (perdido) foi invalidado pela reemissao — nao matricula mais",
                falha?.Codigo == "ticket_invalido");
        }

        await using (var db = new CrmDbContext(opcoes))
        {
            Checar("77-8b. o desafio do ticket antigo esta marcado ConsumidoEm (nao so silenciosamente ignorado)",
                await db.DesafiosDeMfa.AnyAsync(d => d.HashDoDesafio == hashTicketAntigo77 && d.ConsumidoEm != null));
        }

        // 7 — ticket NOVO matricula normalmente.
        IniciarMfaResponse iniciadoRematricula77;
        await using (var db = new CrmDbContext(opcoes))
        {
            var mfa = NovoMfaService(db);
            var sessaoRematricula = await mfa.IniciarMatriculaPrivilegiadaAsync(
                new MatricularPrivilegiadoRequest(titular77.Email, SenhaBoa, tokenTicketNovo77), null, default);
            iniciadoRematricula77 = await mfa.IniciarViaDesafioAsync(new IniciarViaDesafioRequest(sessaoRematricula.Desafio), default);
            await mfa.ConfirmarViaDesafioAsync(
                new ConfirmarViaDesafioRequest(sessaoRematricula.Desafio, CodigoTotp(iniciadoRematricula77.SegredoBase32, -1)), null, default);
        }

        var estadoPosRematricula77 = await Ler(titular77.Id);
        Checar("77-9. rematricula com o ticket NOVO (reemitido) E POSSIVEL — MfaAtivo volta a true",
            estadoPosRematricula77.MfaAtivo);
        Checar("77-10. RematriculaObrigatoriaAposEmergencia volta a false so na confirmacao da matricula",
            !estadoPosRematricula77.RematriculaObrigatoriaAposEmergencia);

        // 8 — login volta.
        await using (var db = new CrmDbContext(opcoes))
        {
            var loginPosRematricula77 = await NovoAutenticacao(db, Guid.NewGuid())
                .AutenticarAsync(new LoginRequest(titular77.Email, SenhaBoa));
            Checar("77-11. SO DEPOIS da rematricula o login volta a funcionar (exigindo MFA de novo)",
                loginPosRematricula77.RequerMfa);
        }

        // 9 — auditoria: evento proprio, exatamente uma vez, identidade das
        // duas pessoas sem nenhum segredo/ticket/codigo nos campos.
        await using (var db = new CrmDbContext(opcoes))
        {
            var eventosDeReemissao = await db.EventosDeSeguranca
                .Where(e => e.AtendenteId == titular77.Id && e.Tipo == TipoDeEventoDeSeguranca.TicketDeRematriculaReemitido)
                .ToListAsync();

            Checar("77-12. TicketDeRematriculaReemitido existe EXATAMENTE uma vez (evento proprio, nao ResetDeEmergencia de novo)",
                eventosDeReemissao.Count == 1);

            var camposAuditaveis77 = eventosDeReemissao.Count == 1
                ? $"{eventosDeReemissao[0].Motivo} {eventosDeReemissao[0].AtorExterno}"
                : string.Empty;
            Checar("77-13. nenhuma credencial (senha/ticket antigo/ticket novo/TOTP) aparece nos campos auditaveis",
                !camposAuditaveis77.Contains(SenhaBoa)
                && !camposAuditaveis77.Contains(tokenTicketAntigo77)
                && !camposAuditaveis77.Contains(tokenTicketNovo77)
                && !camposAuditaveis77.Contains(iniciadoRematricula77.SegredoBase32));

            var eventosDeResetOriginal77 = await db.EventosDeSeguranca
                .Where(e => e.AtendenteId == titular77.Id && e.Tipo == TipoDeEventoDeSeguranca.ResetDeEmergencia)
                .ToListAsync();
            Checar("77-14. o ResetDeEmergencia original continua existindo EXATAMENTE uma vez (nao foi duplicado pela reemissao)",
                eventosDeResetOriginal77.Count == 1);
        }

        // 10 — elegibilidade da reemissao RECUSA existeAtorApto (mesmo
        // criterio do break-glass — cenario 74b/74c espelhado aqui).
        var tenant77b = await NovoTenante77("ator apto — alvo Titular");
        var titular77b = await ContaMatriculada77(tenant77b, "77b - Titular preso", CargoNaServentia.Titular);
        await using (var db = new CrmDbContext(opcoes))
        {
            await TentarResetarDeEmergenciaViaReflexaoAsync77(
                db, titular77b.Email, SegredosAleatoriosMfa.Sha256Hex(SegredosAleatoriosMfa.NovoToken()),
                "cenario 77b — abre emergencia", "teste@verificador");
        }
        // Outro Administrador+Titular com MFA aparece DEPOIS do break-glass —
        // prova que a reemissao reconfere o estado ATUAL, nao um instantaneo antigo.
        await ContaMatriculada77(tenant77b, "77b - outro Titular apto (surge depois)", CargoNaServentia.Titular);
        await using (var db = new CrmDbContext(opcoes))
        {
            Checar("77-15. reemissao RECUSA quando surge um ator apto ao reset normal (mesmo com emergencia aberta)",
                await FerramentaDeReemissaoDeTicketDeEmergencia.VerificarElegibilidadeAsync(db, titular77b.Email)
                    == ElegibilidadeDeReemissaoDeTicket.ExisteAtorAptoParaResetNormal);
        }

        // 11 — reemissao RECUSA para conta comum que nunca passou por
        // break-glass (nem matriculada, nem com o sinalizador ligado).
        var tenant77c = await NovoTenante77("nunca passou por break-glass");
        Guid contaComum77c;
        string emailComum77c;
        await using (var db = new CrmDbContext(opcoes))
        {
            var conta = new Atendente
            {
                TenantId = tenant77c,
                Nome = "[TESTE SEC-03] 77c - privilegiada nunca em emergencia",
                Email = $"teste-sec-03-77-{Guid.NewGuid():N}@teste.local",
                SenhaHash = hash.Gerar(SenhaBoa),
                Perfil = PerfilDeAtendente.Administrador,
                Cargo = CargoNaServentia.Titular,
                Ativo = true
            };
            db.Atendentes.Add(conta);
            await db.SaveChangesAsync();
            ids.Add(conta.Id);
            contaComum77c = conta.Id;
            emailComum77c = conta.Email;
        }
        await using (var db = new CrmDbContext(opcoes))
        {
            Checar("77-16. reemissao RECUSA para conta que NUNCA passou por break-glass (MfaAtivo=false, flag=false)",
                await FerramentaDeReemissaoDeTicketDeEmergencia.VerificarElegibilidadeAsync(db, emailComum77c)
                    == ElegibilidadeDeReemissaoDeTicket.NaoEstaEmRecuperacaoDeEmergencia);
        }
        _ = contaComum77c;

        // Conta normal, matriculada, MfaAtivo=true (nunca teve emergencia) —
        // reemissao tambem recusa (nao ha o que reemitir).
        var contaNormalMatriculada77d = await ContaMatriculada77(
            await NovoTenante77("matriculada normal"), "77d - matriculada sem emergencia", CargoNaServentia.Titular);
        await using (var db = new CrmDbContext(opcoes))
        {
            Checar("77-17. reemissao RECUSA para conta matriculada normalmente (MfaAtivo=true, nunca teve emergencia)",
                await FerramentaDeReemissaoDeTicketDeEmergencia.VerificarElegibilidadeAsync(db, contaNormalMatriculada77d.Email)
                    == ElegibilidadeDeReemissaoDeTicket.NaoEstaEmRecuperacaoDeEmergencia);
        }
    }

    // ------------------------------------------------------------ cenario 78
    // SEC-03 — "confiar neste dispositivo por 30 dias".
    //
    // O que precisa ser provado, e por que cada um importa:
    //
    //   1. A dispensa e REAL (o segundo login no mesmo navegador nao pede
    //      codigo) — senao a feature nao existe.
    //   2. A dispensa e SO daquele navegador, daquela conta, daquele tenant —
    //      senao virou bypass de MFA.
    //   3. Todo estado de autoridade e reconferido NO INSTANTE DO USO
    //      (Ativo/MfaAtivo/TokenVersion/expiracao/revogacao), nunca lido de
    //      algo que o cliente mandou.
    //   4. O token em claro NUNCA existe no banco — so o hash.
    //   5. O uso ROTACIONA o token — o valor apresentado deixa de valer e o
    //      sucessor volta no Set-Cookie — e a rotacao NAO estende a janela de
    //      30 dias. Atencao ao limite: rotacionar invalida copias antigas,
    //      NAO faz do dispositivo uma credencial de uso unico nem detecta
    //      roubo (ver DispositivoConfiavel e o README do modulo).
    //   6. Dispositivo confiavel nao vale para step-up de operacao
    //      administrativa — so para o segundo fator do LOGIN.
    {
        Console.WriteLine();
        Console.WriteLine("Cenario 78 — confiar neste dispositivo por 30 dias.");

        async Task<DispositivoConfiavel?> LerDispositivoDe(Guid atendenteId)
        {
            await using var db = new CrmDbContext(opcoes);
            return await db.DispositivosConfiaveis.AsNoTracking()
                .Where(d => d.AtendenteId == atendenteId)
                .OrderByDescending(d => d.CriadoEm)
                .FirstOrDefaultAsync();
        }

        async Task<int> ContarDispositivosDe(Guid atendenteId)
        {
            await using var db = new CrmDbContext(opcoes);
            return await db.DispositivosConfiaveis.CountAsync(d => d.AtendenteId == atendenteId && d.RevogadoEm == null);
        }

        /// Login completo com MFA por um navegador especifico, opcionalmente
        /// pedindo a confianca de 30 dias.
        async Task<LoginEtapa1Response> LoginPeloNavegador(
            string email, string segredo, NavegadorFalso navegador, bool confiar, int deslocamento = 0)
        {
            var etapa1 = await EtapaUm(email, SenhaBoa, navegador);
            if (!etapa1.RequerMfa || etapa1.Desafio is null) return etapa1;

            await EtapaDois(etapa1.Desafio.Desafio, CodigoTotp(segredo, deslocamento), navegador, confiar);
            return etapa1;
        }

        // -- concessao ------------------------------------------------------

        var conta78 = await NovaContaComMfaAtivo("dispositivo confiavel", PerfilDeAtendente.Atendente);
        var navegadorDaMesa = new NavegadorFalso();

        Checar("78a. sem cookie, o login com MFA abre desafio de segundo fator (comportamento de sempre)",
            (await EtapaUm(conta78.Email, SenhaBoa, navegadorDaMesa)).RequerMfa);

        await LoginPeloNavegador(conta78.Email, conta78.SegredoBase32, navegadorDaMesa, confiar: true);

        Checar("78b. concluir o segundo fator pedindo confianca grava o cookie no navegador",
            navegadorDaMesa.Cookie is not null && navegadorDaMesa.Gravacoes == 1);

        var dispositivo78 = await LerDispositivoDe(conta78.Id);
        Checar("78c. a concessao criou UMA linha em DispositivosConfiaveis", dispositivo78 is not null);

        var cookieConcedido = navegadorDaMesa.Cookie!;
        Checar("78d. o banco guarda o HASH, nunca o token em claro",
            dispositivo78!.HashDoToken == SegredosAleatoriosMfa.Sha256Hex(cookieConcedido)
            && dispositivo78.HashDoToken != cookieConcedido);

        var tokenVersionNaConcessao = (await Ler(conta78.Id)).TokenVersion;
        Checar("78e. TokenVersionNoRegistro e o snapshot do TokenVersion validado no desafio",
            dispositivo78.TokenVersionNoRegistro == tokenVersionNaConcessao);

        var janela78 = dispositivo78.ExpiraEm - dispositivo78.CriadoEm;
        Checar("78f. a janela concedida e de 30 dias",
            janela78 > TimeSpan.FromDays(29.9) && janela78 < TimeSpan.FromDays(30.1));

        Checar("78g. nasce sem revogacao e sem uso", dispositivo78.RevogadoEm is null && dispositivo78.UltimoUsoEm is null);

        // -- a dispensa, e so dela --------------------------------------------

        var segundoLogin = await EtapaUm(conta78.Email, SenhaBoa, navegadorDaMesa);
        Checar("78h. o MESMO navegador nao precisa mais do segundo fator",
            !segundoLogin.RequerMfa && segundoLogin.Sessao is not null && segundoLogin.Desafio is null);

        var senhaErradaRecusada = false;
        try
        {
            await EtapaUm(conta78.Email, SenhaRuim, navegadorDaMesa);
        }
        catch (UsuariosServiceException ex) when (ex.Codigo == "credenciais_invalidas")
        {
            senhaErradaRecusada = true;
        }

        Checar("78i. mesmo dispensando o codigo, a SENHA continua obrigatoria", senhaErradaRecusada);

        var navegadorAnonimo = new NavegadorFalso();
        Checar("78j. navegador SEM o cookie continua exigindo o segundo fator",
            (await EtapaUm(conta78.Email, SenhaBoa, navegadorAnonimo)).RequerMfa);

        // -- rotacao a cada uso -----------------------------------------------

        Checar("78k. o uso ROTACIONOU o cookie — o valor apresentado deu lugar a um sucessor",
            navegadorDaMesa.Gravacoes == 2 && navegadorDaMesa.Cookie != cookieConcedido);

        var apos78 = await LerDispositivoDe(conta78.Id);
        Checar("78l. o hash no banco acompanhou a rotacao",
            apos78!.HashDoToken == SegredosAleatoriosMfa.Sha256Hex(navegadorDaMesa.Cookie!));

        Checar("78m. a rotacao NAO estende a janela de 30 dias (teto duro desde a concessao)",
            apos78.ExpiraEm == dispositivo78.ExpiraEm);

        Checar("78n. o uso carimba UltimoUsoEm", apos78.UltimoUsoEm is not null);

        var cookieVelho = new NavegadorFalso();
        cookieVelho.Definir(cookieConcedido, apos78.ExpiraEm);
        Checar("78o. o cookie ANTERIOR a rotacao nao serve mais (replay recusado)",
            (await EtapaUm(conta78.Email, SenhaBoa, cookieVelho)).RequerMfa);

        // Cookie copiado: dois navegadores com o MESMO valor. So o PRIMEIRO a
        // usar rotaciona e segue confiavel; o segundo cai para MFA completo.
        //
        // Neste teste o legitimo chega primeiro, mas a ordem e o unico
        // criterio: se o atacante chegasse antes, seria ELE a receber o
        // sucessor e a seguir com a cadeia rotacionada (precisando tambem da
        // senha, que continua obrigatoria). O teste prova a invalidacao da
        // copia stale — nao prova, e o codigo nao entrega, deteccao de roubo
        // nem limite de um unico login para quem venceu a corrida.
        var navegadorLegitimo = new NavegadorFalso();
        var conta78Roubo = await NovaContaComMfaAtivo("cookie roubado", PerfilDeAtendente.Atendente);
        await LoginPeloNavegador(conta78Roubo.Email, conta78Roubo.SegredoBase32, navegadorLegitimo, confiar: true);
        var navegadorLadrao = navegadorLegitimo.Clonar();

        var usoDoLegitimo = await EtapaUm(conta78Roubo.Email, SenhaBoa, navegadorLegitimo);
        var usoDoLadrao = await EtapaUm(conta78Roubo.Email, SenhaBoa, navegadorLadrao);
        Checar("78p. cookie copiado so vale para quem usar primeiro; a copia cai para MFA completo",
            !usoDoLegitimo.RequerMfa && usoDoLadrao.RequerMfa);

        // -- fronteiras: conta, tenant, expiracao, revogacao, TokenVersion ----

        var outraConta78 = await NovaContaComMfaAtivo("outra conta", PerfilDeAtendente.Atendente);
        var navegadorCruzado = new NavegadorFalso();
        await LoginPeloNavegador(conta78.Email, conta78.SegredoBase32, navegadorCruzado, confiar: true);
        Checar("78q. cookie de OUTRA conta nao dispensa o segundo fator (fronteira de atendente)",
            (await EtapaUm(outraConta78.Email, SenhaBoa, navegadorCruzado)).RequerMfa);

        {
            var contaTenant78 = await NovaContaComMfaAtivo("fronteira de tenant", PerfilDeAtendente.Atendente);
            var navegadorTenant = new NavegadorFalso();
            await LoginPeloNavegador(contaTenant78.Email, contaTenant78.SegredoBase32, navegadorTenant, confiar: true);

            Guid tenantIntruso;
            await using (var db = new CrmDbContext(opcoes))
            {
                var tenant = new Tenant
                {
                    RazaoSocial = "[TESTE SEC-03] Tenant isolado — dispositivo confiavel",
                    Cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString()
                };
                db.Tenants.Add(tenant);
                await db.SaveChangesAsync();
                tenantIntruso = tenant.Id;
            }
            tenantIdsDeTeste.Add(tenantIntruso);

            await using (var db = new CrmDbContext(opcoes))
            {
                await db.DispositivosConfiaveis.Where(d => d.AtendenteId == contaTenant78.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.TenantId, tenantIntruso));
            }

            Checar("78r. dispositivo de OUTRO tenant nao dispensa o segundo fator (fronteira de tenant)",
                (await EtapaUm(contaTenant78.Email, SenhaBoa, navegadorTenant)).RequerMfa);
        }

        {
            var contaExpirada = await NovaContaComMfaAtivo("dispositivo expirado", PerfilDeAtendente.Atendente);
            var navegador = new NavegadorFalso();
            await LoginPeloNavegador(contaExpirada.Email, contaExpirada.SegredoBase32, navegador, confiar: true);

            await using (var db = new CrmDbContext(opcoes))
            {
                await db.DispositivosConfiaveis.Where(d => d.AtendenteId == contaExpirada.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.ExpiraEm, DateTime.UtcNow.AddMinutes(-1)));
            }

            Checar("78s. dispositivo EXPIRADO volta a exigir o segundo fator",
                (await EtapaUm(contaExpirada.Email, SenhaBoa, navegador)).RequerMfa);
        }

        {
            var contaRevogada = await NovaContaComMfaAtivo("dispositivo revogado", PerfilDeAtendente.Atendente);
            var navegador = new NavegadorFalso();
            await LoginPeloNavegador(contaRevogada.Email, contaRevogada.SegredoBase32, navegador, confiar: true);

            await using (var db = new CrmDbContext(opcoes))
            {
                await db.DispositivosConfiaveis.Where(d => d.AtendenteId == contaRevogada.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.RevogadoEm, DateTime.UtcNow));
            }

            Checar("78t. dispositivo REVOGADO volta a exigir o segundo fator",
                (await EtapaUm(contaRevogada.Email, SenhaBoa, navegador)).RequerMfa);
        }

        // TokenVersion e o mecanismo universal (SEC-01): qualquer evento que ja
        // derruba sessao JWT tem que derrubar a confianca do dispositivo junto,
        // SEM nenhuma bookkeeping nova. Troca de senha e o representante direto.
        {
            var contaSenha = await NovaContaComMfaAtivo("troca de senha invalida", PerfilDeAtendente.Atendente);
            var navegador = new NavegadorFalso();
            await LoginPeloNavegador(contaSenha.Email, contaSenha.SegredoBase32, navegador, confiar: true);

            await using (var db = new CrmDbContext(opcoes))
            {
                await NovoAutenticacao(db, contaSenha.Id)
                    .AlterarSenhaAsync(new AlterarSenhaRequest(SenhaBoa, "OutraSenhaBoa456"));
            }

            Checar("78u. trocar a senha (que sobe TokenVersion) invalida o dispositivo confiavel",
                (await EtapaUm(contaSenha.Email, "OutraSenhaBoa456", navegador)).RequerMfa);
        }

        // Rematricula obrigatoria pos-emergencia: mesmo com dispositivo valido,
        // a conta nao pode entrar sem refazer a matricula.
        {
            var contaRematricula = await NovaContaComMfaAtivo("rematricula obrigatoria", PerfilDeAtendente.Atendente);
            var navegador = new NavegadorFalso();
            await LoginPeloNavegador(contaRematricula.Email, contaRematricula.SegredoBase32, navegador, confiar: true);

            await using (var db = new CrmDbContext(opcoes))
            {
                await db.Atendentes.Where(a => a.Id == contaRematricula.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.RematriculaObrigatoriaAposEmergencia, true));
            }

            Checar("78v. conta em rematricula obrigatoria nao entra por dispositivo confiavel",
                (await EtapaUm(contaRematricula.Email, SenhaBoa, navegador)).RequerMfa);
        }

        // -- nao concessao ----------------------------------------------------

        {
            var contaSemConfianca = await NovaContaComMfaAtivo("sem marcar a caixa", PerfilDeAtendente.Atendente);
            var navegador = new NavegadorFalso();
            await LoginPeloNavegador(contaSemConfianca.Email, contaSemConfianca.SegredoBase32, navegador, confiar: false);

            Checar("78w. sem marcar a caixa, nenhum dispositivo e criado e nenhum cookie e gravado",
                await ContarDispositivosDe(contaSemConfianca.Id) == 0
                && navegador.Cookie is null && navegador.Gravacoes == 0);
        }

        // -- esquecer dispositivos --------------------------------------------

        {
            var contaEsquecer = await NovaContaComMfaAtivo("esquecer dispositivos", PerfilDeAtendente.Atendente);
            var navegadorA = new NavegadorFalso();
            var navegadorB = new NavegadorFalso();
            await LoginPeloNavegador(contaEsquecer.Email, contaEsquecer.SegredoBase32, navegadorA, confiar: true);
            await LoginPeloNavegador(contaEsquecer.Email, contaEsquecer.SegredoBase32, navegadorB, confiar: true, deslocamento: 1);

            var identidade = new IdentidadeParaMfa(tenantId, contaEsquecer.Id);

            await using (var db = new CrmDbContext(opcoes))
            {
                var situacao = await NovoMfaService(db).ObterSituacaoAsync(identidade);
                Checar("78x. a situacao de MFA conta os dispositivos confiaveis ativos",
                    situacao.DispositivosConfiaveisAtivos == 2);
            }

            int revogados;
            var navegadorQueChamou = navegadorA.Clonar();
            await using (var db = new CrmDbContext(opcoes))
            {
                revogados = await NovoMfaService(db, navegadorQueChamou).EsquecerDispositivosConfiaveisAsync(identidade);
            }

            Checar("78y. esquecer revoga TODOS os dispositivos do proprio atendente",
                revogados == 2 && await ContarDispositivosDe(contaEsquecer.Id) == 0);

            Checar("78z. esquecer tambem limpa o cookie do navegador que chamou",
                navegadorQueChamou.Cookie is null);

            Checar("78aa. depois de esquecer, os dois navegadores voltam a pedir o segundo fator",
                (await EtapaUm(contaEsquecer.Email, SenhaBoa, navegadorA)).RequerMfa
                && (await EtapaUm(contaEsquecer.Email, SenhaBoa, navegadorB)).RequerMfa);
        }

        // -- fronteira com step-up --------------------------------------------

        // O cookie NAO e uma credencial de operacao administrativa. Desativar o
        // MFA continua exigindo senha + codigo, mesmo do navegador confiavel.
        {
            var contaStepUp = await NovaContaComMfaAtivo("step-up nao aceita cookie", PerfilDeAtendente.Atendente);
            var navegador = new NavegadorFalso();
            await LoginPeloNavegador(contaStepUp.Email, contaStepUp.SegredoBase32, navegador, confiar: true);

            var recusou = false;
            await using (var db = new CrmDbContext(opcoes))
            {
                try
                {
                    await NovoMfaService(db, navegador).DesativarAsync(
                        new IdentidadeParaMfa(tenantId, contaStepUp.Id),
                        new DesativarMfaRequest(SenhaBoa, "000000"), null);
                }
                catch (UsuariosServiceException) { recusou = true; }
            }

            Checar("78ab. dispositivo confiavel NAO dispensa o step-up de operacao administrativa", recusou);
            Checar("78ac. e o MFA continua ativo depois da recusa", (await Ler(contaStepUp.Id)).MfaAtivo);
        }

        // -- o token em claro nunca encosta no banco --------------------------

        await using (var db = new CrmDbContext(opcoes))
        {
            var todosOsHashes = await db.DispositivosConfiaveis.AsNoTracking()
                .Where(d => ids.Contains(d.AtendenteId))
                .Select(d => d.HashDoToken)
                .ToListAsync();

            Checar("78ad. nenhum token em claro conhecido por este teste aparece no banco",
                todosOsHashes.Count > 0
                && !todosOsHashes.Contains(cookieConcedido)
                && !todosOsHashes.Contains(navegadorDaMesa.Cookie!)
                && todosOsHashes.TrueForAll(h => h.Length == 64));
        }
    }

    // ------------------------------------------------------------ cenario 79
    // SEC-03 — politica do atributo `Secure` do cookie de dispositivo
    // confiavel, por AMBIENTE.
    //
    // Por que este cenario existe: a versao anterior decidia `Secure` por
    // `Request.IsHttps`. Na infraestrutura real o TLS termina na Cloudflare e
    // o IIS recebe a perna interna em HTTP puro — ali `IsHttps` e false com o
    // navegador falando HTTPS, e o cookie de confianca sairia SEM `Secure`.
    // A regra passou a ser "seguro por omissao": so `Development` pode abrir
    // mao, nomeado explicitamente. Ambiente novo nasce protegido.
    //
    // Este bloco testa a CLASSE de producao diretamente contra um
    // HttpContext real, lendo o cabecalho `Set-Cookie` CRU — nao um
    // CookieContainer, que ja teria interpretado e descartado os atributos.
    {
        Console.WriteLine();
        Console.WriteLine("Cenario 79 — Secure do cookie de dispositivo confiavel, por ambiente.");

        static string SetCookieDe(string ambiente, bool limpando = false)
        {
            var contexto = new DefaultHttpContext();
            var cookie = new CookieDeDispositivoConfiavel(
                new HttpContextAccessor { HttpContext = contexto }, new AmbienteFalso(ambiente));

            if (limpando) cookie.Limpar();
            else cookie.Definir("token-descartavel-de-teste", DateTime.UtcNow.AddDays(30));

            return contexto.Response.Headers["Set-Cookie"].ToString();
        }

        static bool Tem(string setCookie, string trecho) =>
            setCookie.Contains(trecho, StringComparison.OrdinalIgnoreCase);

        // O ambiente TESTE e o caso que motivou a mudanca: API local em HTTP
        // puro atras do TLS da Cloudflare.
        var teste79 = SetCookieDe("Teste");
        Checar("79a. ambiente Teste emite o cookie COM Secure, mesmo sem HTTPS na requisicao",
            Tem(teste79, "secure"));

        var producao79 = SetCookieDe("Production");
        Checar("79b. ambiente Production emite o cookie COM Secure", Tem(producao79, "secure"));

        // Seguro por omissao: qualquer nome que nao seja Development e tratado
        // como ambiente real. Um ambiente novo (Staging, Homologacao, o que
        // vier) nao precisa lembrar de configurar nada para nascer protegido.
        Checar("79c. ambiente Staging (nao previsto hoje) tambem emite com Secure",
            Tem(SetCookieDe("Staging"), "secure"));
        Checar("79d. ambiente de nome desconhecido tambem emite com Secure — a excecao e so Development",
            Tem(SetCookieDe("AmbienteQueNinguemInventouAinda"), "secure"));

        // Development e a UNICA excecao, e e deliberada: painel em
        // http://localhost:5173 contra API em http://localhost:5210 — com
        // Secure fixo o navegador descartaria o cookie em silencio.
        var desenvolvimento79 = SetCookieDe("Development");
        Checar("79e. Development (unica excecao, documentada) emite sem Secure",
            !Tem(desenvolvimento79, "secure"));

        // A politica NAO pode depender do esquema da requisicao: mesmo
        // ambiente, HTTP e HTTPS tem que dar o mesmo resultado.
        Checar("79f. o Secure NAO acompanha Request.IsHttps — em Teste da o mesmo resultado que em Production",
            Tem(teste79, "secure") == Tem(producao79, "secure"));

        // Os demais atributos continuam em todos os ambientes — inclusive no
        // unico que abre mao do Secure.
        foreach (var ambiente in new[] { "Development", "Teste", "Production", "Staging" })
        {
            var setCookie = SetCookieDe(ambiente);

            Checar($"79g[{ambiente}]. HttpOnly presente — XSS nunca le o token", Tem(setCookie, "httponly"));
            Checar($"79h[{ambiente}]. SameSite=Strict presente — o cookie nao viaja em requisicao de outro site",
                Tem(setCookie, "samesite=strict"));
            Checar($"79i[{ambiente}]. Path e EXATAMENTE /api/autenticacao/login — nao acompanha as rotas irmas",
                CaminhoDoSetCookie(setCookie) == "/api/autenticacao/login");
            Checar($"79j[{ambiente}]. o cookie sai com expiracao explicita", Tem(setCookie, "expires="));
        }

        // A EXCLUSAO tem que repetir os mesmos atributos: navegador so apaga
        // um cookie quando eles batem com os da gravacao. Se a rotina de
        // limpar divergisse, "esquecer dispositivos" deixaria cookie orfao
        // vivo no navegador.
        foreach (var ambiente in new[] { "Teste", "Production" })
        {
            var aoLimpar = SetCookieDe(ambiente, limpando: true);

            Checar($"79k[{ambiente}]. a EXCLUSAO do cookie repete Secure", Tem(aoLimpar, "secure"));
            Checar($"79l[{ambiente}]. a EXCLUSAO do cookie repete SameSite=Strict", Tem(aoLimpar, "samesite=strict"));
            Checar($"79m[{ambiente}]. a EXCLUSAO do cookie repete o Path exato /api/autenticacao/login",
                CaminhoDoSetCookie(aoLimpar) == "/api/autenticacao/login");
        }

        Checar("79n. a exclusao em Development segue a mesma decisao da gravacao (sem Secure)",
            !Tem(SetCookieDe("Development", limpando: true), "secure"));

        // ---------------------------------------------------------------
        // O Path e uma decisao de SUPERFICIE: cada rota irma que passa a
        // receber o cookie e uma chance a mais de a credencial vazar (log de
        // proxy, header dump, erro que ecoa a requisicao). O caminho tem que
        // cobrir as duas rotas de login e absolutamente mais nada.
        // ---------------------------------------------------------------
        {
            var caminho79 = CaminhoDoSetCookie(SetCookieDe("Production"));

            // Regra de casamento de caminho do RFC 6265, 5.1.4: o navegador
            // envia o cookie quando o caminho da requisicao e igual ao do
            // cookie, ou quando o do cookie e prefixo e o proximo caractere
            // do caminho da requisicao e "/". Reproduzida aqui para provar
            // COMPORTAMENTO (quais rotas recebem), nao so o literal gravado.
            static bool NavegadorEnviaria(string caminhoDoCookie, string rota) =>
                rota.Equals(caminhoDoCookie, StringComparison.Ordinal)
                || (rota.StartsWith(caminhoDoCookie, StringComparison.Ordinal)
                    && rota.Length > caminhoDoCookie.Length
                    && (caminhoDoCookie.EndsWith('/') || rota[caminhoDoCookie.Length] == '/'));

            Checar("79r. o cookie e enviado para POST /api/autenticacao/login (etapa 1)",
                NavegadorEnviaria(caminho79, "/api/autenticacao/login"));
            Checar("79s. o cookie e enviado para POST /api/autenticacao/login/mfa (etapa 2)",
                NavegadorEnviaria(caminho79, "/api/autenticacao/login/mfa"));

            // As rotas irmas REAIS do AutenticacaoController — nenhuma delas
            // precisa da credencial de dispositivo confiavel.
            foreach (var rotaIrma in new[]
                     {
                         "/api/autenticacao/serventia",
                         "/api/autenticacao/sessao",
                         "/api/autenticacao/senha",
                         "/api/autenticacao",
                         "/api/mfa/situacao",
                         "/api/mfa/dispositivos-confiaveis/esquecer"
                     })
            {
                Checar($"79t. o cookie NAO e enviado para {rotaIrma}",
                    !NavegadorEnviaria(caminho79, rotaIrma));
            }

            // Rota irma FUTURA: o valor estreito protege sozinho o que ainda
            // nao existe. Se o caminho voltar a ser o controller inteiro,
            // esta linha quebra sem ninguem precisar lembrar de revisar.
            Checar("79u. rota irma que ainda nem existe ja nasce fora do alcance do cookie",
                !NavegadorEnviaria(caminho79, "/api/autenticacao/rota-que-ainda-nao-existe"));

            // "loginX" nao e "login/": prefixo textual nao pode virar
            // casamento de caminho.
            Checar("79v. prefixo textual nao basta — /api/autenticacao/loginqualquer fica de fora",
                !NavegadorEnviaria(caminho79, "/api/autenticacao/loginqualquer"));

            Checar("79w. o caminho gravado e o literal esperado, sem barra no fim",
                caminho79 == "/api/autenticacao/login");
        }

        // Regressao estrutural: se alguem reintroduzir Request.IsHttps na
        // decisao do cookie, este teste quebra sozinho — sem depender de
        // revisao humana notar.
        {
            var fonte = await File.ReadAllTextAsync(Path.Combine(
                "CrmCartorio.Api", "Infrastructure", "Security", "CookieDeDispositivoConfiavel.cs"));

            var fonteSemComentarios = string.Join('\n', fonte
                .Split('\n')
                .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)
                         && !l.TrimStart().StartsWith("///", StringComparison.Ordinal)
                         && !l.TrimStart().StartsWith("*", StringComparison.Ordinal)));

            Checar("79o. a decisao de Secure nao volta a depender de Request.IsHttps (regressao estrutural)",
                !fonteSemComentarios.Contains("IsHttps", StringComparison.Ordinal));

            Checar("79p. Secure sai de IsDevelopment(), nao do esquema da requisicao",
                fonteSemComentarios.Contains("Secure = !ambiente.IsDevelopment()", StringComparison.Ordinal));

            // Sem forwarded headers global so por causa deste cookie — seria
            // uma mudanca de pipeline muito maior, afetando toda a API.
            var programa = await File.ReadAllTextAsync(Path.Combine("CrmCartorio.Api", "Program.cs"));
            Checar("79q. nenhuma configuracao global de ForwardedHeaders foi introduzida para resolver este cookie",
                !programa.Contains("ForwardedHeaders", StringComparison.Ordinal)
                && !programa.Contains("UseForwardedHeaders", StringComparison.Ordinal));

            Checar("79x. o caminho no codigo e o estreito, nao o do controller inteiro (regressao estrutural)",
                fonteSemComentarios.Contains("Caminho = \"/api/autenticacao/login\"", StringComparison.Ordinal));

            // Gravacao, rotacao e exclusao tem que sair da MESMA rotina de
            // opcoes. Se alguem montar um CookieOptions a parte para o
            // Delete, os atributos podem divergir e o navegador deixa de
            // apagar o cookie — orfao vivo com credencial dentro.
            Checar("79y. gravacao e exclusao usam a mesma rotina de opcoes (uma unica MontarOpcoes)",
                System.Text.RegularExpressions.Regex.Matches(
                    fonteSemComentarios, @"CookieOptions MontarOpcoes").Count == 1
                && fonteSemComentarios.Contains("Append(Nome, tokenEmClaro, MontarOpcoes(", StringComparison.Ordinal)
                && fonteSemComentarios.Contains("Delete(Nome, MontarOpcoes(", StringComparison.Ordinal));
        }
    }

    // ------------------------------------------------------------ cenario 80
    // SEC-03 — a CONTAGEM de dispositivos confiaveis ativos, mostrada na tela
    // de Seguranca, tem que enxergar exatamente o que o login enxerga.
    //
    // O defeito que este cenario tranca: a contagem filtrava por conta,
    // tenant, revogacao e expiracao, mas NAO por TokenVersionNoRegistro.
    // Resultado: trocar a senha invalidava o dispositivo de verdade (o login
    // voltava a exigir o segundo fator), mas a tela continuava dizendo
    // "1 dispositivo confiavel ativo". Informacao de seguranca errada — a
    // pessoa via um risco que nao existia mais, e o botao de "esquecer" nao
    // mudava nada.
    {
        Console.WriteLine();
        Console.WriteLine("Cenario 80 — a tela conta o mesmo que o login aceita.");

        var conta80 = await NovaContaComMfaAtivo("contagem x TokenVersion", PerfilDeAtendente.Atendente);
        var navegador80 = new NavegadorFalso();
        var identidade80 = new IdentidadeParaMfa(tenantId, conta80.Id);

        async Task<int> ContarNaTelaAsync()
        {
            await using var db = new CrmDbContext(opcoes);
            return (await NovoMfaService(db).ObterSituacaoAsync(identidade80)).DispositivosConfiaveisAtivos;
        }

        // 1) Concede a confianca pelo fluxo real de login.
        var etapa1De80 = await EtapaUm(conta80.Email, SenhaBoa, navegador80);
        await EtapaDois(etapa1De80.Desafio!.Desafio, CodigoTotp(conta80.SegredoBase32), navegador80,
            confiarNesteDispositivo: true);

        Checar("80a. concedido o dispositivo, a situacao mostra 1 ativo", await ContarNaTelaAsync() == 1);

        // 2) Troca a senha pelo servico real — sobe TokenVersion (SEC-01).
        await using (var db = new CrmDbContext(opcoes))
        {
            await NovoAutenticacao(db, conta80.Id)
                .AlterarSenhaAsync(new AlterarSenhaRequest(SenhaBoa, "SenhaTrocada80x"));
        }

        // 3) O login com o MESMO cookie volta a exigir o segundo fator.
        Checar("80b. depois da troca de senha, o cookie nao dispensa mais o segundo fator",
            (await EtapaUm(conta80.Email, "SenhaTrocada80x", navegador80)).RequerMfa);

        // 4) E a tela para de contar o registro que o login ja recusa.
        Checar("80c. a situacao passa a mostrar 0 — a tela nao promete confianca que o login nega",
            await ContarNaTelaAsync() == 0);

        // A linha continua existindo no banco (nao ha faxina automatica); o
        // que mudou e so ela ter deixado de ser CONTADA como ativa. Sem esta
        // checagem, um "0" vindo de um DELETE acidental passaria por correto.
        await using (var db = new CrmDbContext(opcoes))
        {
            var aindaExiste = await db.DispositivosConfiaveis
                .AsNoTracking()
                .AnyAsync(d => d.AtendenteId == conta80.Id && d.RevogadoEm == null);

            Checar("80d. a linha continua no banco, apenas inerte — o 0 vem do filtro, nao de exclusao",
                aindaExiste);
        }

        // A contagem da tela e o gate do login precisam usar o MESMO criterio.
        // Se um dia divergirem de novo, esta prova estrutural quebra.
        {
            var fonteDoMfa = await File.ReadAllTextAsync(Path.Combine(
                "CrmCartorio.Api", "Modules", "Usuarios", "Application", "MfaService.cs"));

            Checar("80e. ObterSituacaoAsync filtra por TokenVersionNoRegistro (regressao estrutural)",
                fonteDoMfa.Contains("d.TokenVersionNoRegistro == atendente.TokenVersion", StringComparison.Ordinal));
        }
    }

    // ------------------------------------------------------------ cenario 81
    // MUDANCA DE REGRA DE PRODUTO — ativacao voluntaria de MFA permitida para
    // qualquer perfil (cenario 55). A UNICA trava de privilegio que sobrevive
    // e a rematricula obrigatoria pos-emergencia: uma conta resetada por
    // break-glass NAO pode reativar o MFA pelo fluxo voluntario, contornando
    // o motivo de o break-glass ter existido — ela tem que passar pelo ticket
    // que o proprio break-glass emitiu (mesmo fluxo do cenario 75).
    {
        Console.WriteLine();
        Console.WriteLine("Cenario 81 — rematricula obrigatoria pos-emergencia nao e contornavel pelo fluxo voluntario.");

        async Task<ElegibilidadeDeEmergencia> TentarResetarDeEmergenciaViaReflexaoAsync81(
            CrmDbContext db, string email, string hashDoNovoTicket, string motivo, string atorExterno)
        {
            var metodo = typeof(FerramentaDeResetDeEmergencia).GetMethod(
                "TentarResetarDeEmergenciaAsync", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("TentarResetarDeEmergenciaAsync nao encontrado via reflexao.");

            var tarefa = (Task<ElegibilidadeDeEmergencia>)metodo.Invoke(
                null, [db, email, hashDoNovoTicket, motivo, atorExterno, CancellationToken.None])!;

            return await tarefa;
        }

        Guid tenantIsolado81;
        await using (var db = new CrmDbContext(opcoes))
        {
            var tenant = new Tenant
            {
                RazaoSocial = "[TESTE SEC-03] Tenant isolado — rematricula obrigatoria (81)",
                Cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString()
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            tenantIsolado81 = tenant.Id;
        }
        tenantIdsDeTeste.Add(tenantIsolado81);

        // Conta Administrador MATRICULADA de verdade (via ticket, como exige
        // a primeira matricula privilegiada) — break-glass so faz sentido
        // para quem ja tinha MFA e perdeu o acesso a ele.
        Guid contaId81;
        string email81;
        await using (var db = new CrmDbContext(opcoes))
        {
            var conta = new Atendente
            {
                TenantId = tenantIsolado81,
                Nome = "[TESTE SEC-03] cenario81 - administrador com MFA",
                Email = $"teste-sec-03-81-{Guid.NewGuid():N}@teste.local",
                SenhaHash = hash.Gerar(SenhaBoa),
                Perfil = PerfilDeAtendente.Administrador,
                Ativo = true
            };
            db.Atendentes.Add(conta);

            var tokenEmClaro = SegredosAleatoriosMfa.NovoToken();
            db.DesafiosDeMfa.Add(new DesafioDeMfa
            {
                TenantId = tenantIsolado81,
                AtendenteId = conta.Id,
                HashDoDesafio = SegredosAleatoriosMfa.Sha256Hex(tokenEmClaro),
                Tipo = TipoDeDesafioMfa.Ticket,
                TokenVersionNoDesafio = conta.TokenVersion,
                ExpiraEm = DateTime.UtcNow.AddHours(24)
            });
            await db.SaveChangesAsync();
            ids.Add(conta.Id);
            contaId81 = conta.Id;
            email81 = conta.Email;

            var mfa = NovoMfaService(db);
            var sessao = await mfa.IniciarMatriculaPrivilegiadaAsync(
                new MatricularPrivilegiadoRequest(conta.Email, SenhaBoa, tokenEmClaro), null, default);
            var iniciado = await mfa.IniciarViaDesafioAsync(new IniciarViaDesafioRequest(sessao.Desafio), default);
            await mfa.ConfirmarViaDesafioAsync(
                new ConfirmarViaDesafioRequest(sessao.Desafio, CodigoTotp(iniciado.SegredoBase32, -1)), null, default);
        }

        Checar("81-preparo. Administrador matriculado via ticket esta com MfaAtivo=true antes do break-glass",
            (await Ler(contaId81)).MfaAtivo);

        var tokenDeRematricula81 = SegredosAleatoriosMfa.NovoToken();
        ElegibilidadeDeEmergencia resultadoBreakGlass81;
        await using (var db = new CrmDbContext(opcoes))
        {
            resultadoBreakGlass81 = await TentarResetarDeEmergenciaViaReflexaoAsync81(
                db, email81, SegredosAleatoriosMfa.Sha256Hex(tokenDeRematricula81),
                "teste automatizado do cenario 81 — break-glass", "teste@verificador");
        }

        Checar("81-1. break-glass reconhece elegibilidade e reseta a conta", resultadoBreakGlass81 == ElegibilidadeDeEmergencia.Elegivel);

        var estadoPosBreakGlass81 = await Ler(contaId81);
        Checar("81-2. MFA removido pelo break-glass (MfaAtivo=false)", !estadoPosBreakGlass81.MfaAtivo);
        Checar("81-3. RematriculaObrigatoriaAposEmergencia = true logo apos o break-glass",
            estadoPosBreakGlass81.RematriculaObrigatoriaAposEmergencia);

        // O NUCLEO deste cenario: mesmo com a REGRA NOVA permitindo qualquer
        // perfil ativar voluntariamente, esta conta especifica — em
        // rematricula obrigatoria — continua RECUSADA no fluxo voluntario.
        await using (var db = new CrmDbContext(opcoes))
        {
            UsuariosServiceException? falhaIniciar = null;
            try
            {
                await NovoMfaService(db).IniciarAsync(
                    new IdentidadeParaMfa(tenantIsolado81, contaId81), new IniciarMfaRequest(SenhaBoa, null), null, default);
            }
            catch (UsuariosServiceException ex) { falhaIniciar = ex; }

            Checar("81-4. IniciarAsync (fluxo voluntario) RECUSA a conta em rematricula obrigatoria, mesmo com senha correta",
                falhaIniciar?.Codigo == "matricula_privilegiada_exige_ticket");
        }

        Checar("81-5. nenhum segredo pendente foi gravado pela tentativa recusada",
            (await Ler(contaId81)).MfaAtivo == false);

        // Mesmo se, por qualquer falha futura, um segredo pendente existisse
        // (defesa em profundidade — o porteiro final de ConfirmarNucleoAsync
        // reconfere isto independente do que IniciarAsync deixou passar),
        // confirmar sem ticket tambem tem que falhar.
        await using (var db = new CrmDbContext(opcoes))
        {
            await db.Atendentes.Where(a => a.Id == contaId81)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.MfaSegredoPendenteCifrado, cifra.Cifrar(ValidadorDeTotp.GerarSegredoBase32(), tenantIsolado81, contaId81))
                    .SetProperty(a => a.MfaPendenteExpiraEm, DateTime.UtcNow.AddMinutes(10))
                    .SetProperty(a => a.MfaPendenteTokenVersion, estadoPosBreakGlass81.TokenVersion));
        }

        await using (var db = new CrmDbContext(opcoes))
        {
            UsuariosServiceException? falhaConfirmar = null;
            try
            {
                await NovoMfaService(db).ConfirmarAsync(
                    new IdentidadeParaMfa(tenantIsolado81, contaId81), new ConfirmarMfaRequest("000000"), null, default);
            }
            catch (UsuariosServiceException ex) { falhaConfirmar = ex; }

            Checar("81-6. ConfirmarAsync (porteiro final, mesmo com segredo pendente forcado) tambem RECUSA sem ticket",
                falhaConfirmar?.Codigo == "matricula_privilegiada_exige_ticket");
        }

        Checar("81-7. a conta continua com MfaAtivo=false — a rematricula obrigatoria nao foi contornada",
            !(await Ler(contaId81)).MfaAtivo);

        // A rematricula LEGITIMA (com o ticket que o break-glass emitiu)
        // continua funcionando normalmente — a trava e so contra o atalho
        // voluntario, nunca contra o fluxo que ela existe para exigir.
        await using (var db = new CrmDbContext(opcoes))
        {
            var mfa = NovoMfaService(db);
            var sessaoRematricula = await mfa.IniciarMatriculaPrivilegiadaAsync(
                new MatricularPrivilegiadoRequest(email81, SenhaBoa, tokenDeRematricula81), null, default);
            var iniciadoRematricula = await mfa.IniciarViaDesafioAsync(new IniciarViaDesafioRequest(sessaoRematricula.Desafio), default);
            var confirmadoRematricula = await mfa.ConfirmarViaDesafioAsync(
                new ConfirmarViaDesafioRequest(sessaoRematricula.Desafio, CodigoTotp(iniciadoRematricula.SegredoBase32, -1)), null, default);

            Checar("81-8. rematricula com o ticket do break-glass FUNCIONA normalmente (fluxo que a trava protege)",
                confirmadoRematricula.CodigosDeRecuperacao.Count == 10);
        }

        var estadoFinal81 = await Ler(contaId81);
        Checar("81-9. apos a rematricula legitima, MfaAtivo volta a true", estadoFinal81.MfaAtivo);
        Checar("81-10. apos a rematricula legitima, RematriculaObrigatoriaAposEmergencia volta a false",
            !estadoFinal81.RematriculaObrigatoriaAposEmergencia);

        // E, agora que a rematricula obrigatoria acabou, esta MESMA conta
        // volta a poder usar o fluxo voluntario normalmente no futuro (troca
        // de autenticador, step-up de sempre) — a trava era so temporaria,
        // presa ao sinalizador, nunca ao fato de ser Administrador.
        await using (var db = new CrmDbContext(opcoes))
        {
            var trocaVoluntaria = await NovoMfaService(db).IniciarAsync(
                new IdentidadeParaMfa(tenantIsolado81, contaId81),
                new IniciarMfaRequest(SenhaBoa, CodigoTotp((await mfaSegredoAtualDoAtendente81Async(db, contaId81)))), null, default);

            Checar("81-11. depois da rematricula, o fluxo voluntario (troca de autenticador) volta a funcionar normalmente",
                trocaVoluntaria is not null);
        }

        async Task<string> mfaSegredoAtualDoAtendente81Async(CrmDbContext dbLeitura, Guid id)
        {
            var segredoCifrado = await dbLeitura.Atendentes.AsNoTracking()
                .Where(a => a.Id == id).Select(a => a.MfaSegredoCifrado).SingleAsync();
            return cifra.Decifrar(segredoCifrado!, tenantIsolado81, id);
        }
    }

    // =====================================================================
    // Permissoes granulares (Equipe — permissoes granulares + redesenho)
    // =====================================================================

    // ------------------------------------------------------------ cenario 82
    // A fundacao: NULL segue a Classe da conta, explicito vence a classe,
    // efeito imediato sem revogar a sessao, chave desconhecida ignorada em
    // silencio, fail-closed com holder vazio, isolamento de tenant.

    {
        Console.WriteLine();
        Console.WriteLine("Cenario 82 — permissoes granulares: a fundacao.");

        var (idAtend82, _) = await NovaContaPrivilegiada("82 Atendente", PerfilDeAtendente.Atendente);
        var (idSup82, _) = await NovaContaPrivilegiada("82 Supervisor", PerfilDeAtendente.Supervisor);
        var (idAdm82, _) = await NovaContaPrivilegiada("82 Administrador", PerfilDeAtendente.Administrador);

        async Task<bool> PodeAsync82(Guid id, PermissaoDoSistema permissao)
        {
            await using var db = new CrmDbContext(opcoes);
            var atendente = await db.Atendentes.AsNoTracking().SingleAsync(a => a.Id == id);
            var conjunto = atendente.Permissoes is null
                ? CatalogoDePermissoes.ConjuntoDaClasse(atendente.Perfil)
                : CatalogoDePermissoes.Desserializar(atendente.Permissoes);
            return conjunto.Contains(permissao);
        }

        Checar("82-1a. Atendente com Permissoes=NULL tem exatamente o preset Balcao (abre novo, mas nao reabre)",
            await PodeAsync82(idAtend82, PermissaoDoSistema.AtendimentoAbrirNovo)
            && !await PodeAsync82(idAtend82, PermissaoDoSistema.AtendimentoReabrir));
        Checar("82-1b. Supervisor com Permissoes=NULL tem o preset Supervisao (reabrir incluso)",
            await PodeAsync82(idSup82, PermissaoDoSistema.AtendimentoReabrir));
        Checar("82-1c. Administrador com Permissoes=NULL tem tudo, exceto simular_cliente",
            await PodeAsync82(idAdm82, PermissaoDoSistema.EquipePermissoes)
            && !await PodeAsync82(idAdm82, PermissaoDoSistema.AtendimentoSimularCliente));

        await using (var db = new CrmDbContext(opcoes))
        {
            await db.Atendentes.Where(a => a.Id == idAtend82).ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Permissoes, CatalogoDePermissoes.Serializar([PermissaoDoSistema.EquipeVer])));
        }
        Checar("82-2a. Permissoes explicito ganha EquipeVer, fora do preset Balcao",
            await PodeAsync82(idAtend82, PermissaoDoSistema.EquipeVer));
        Checar("82-2b. e PERDE o que o preset Balcao dava (o explicito e total, nao um adicional)",
            !await PodeAsync82(idAtend82, PermissaoDoSistema.AtendimentoAbrirNovo));

        // Efeito imediato: grava a permissao nova e confere que o MESMO token
        // (nao revogado, TokenVersion intocada) ja enxerga o conjunto novo na
        // proxima leitura via CarregarSessaoAsync.
        var estadoAntes82 = await Ler(idAtend82);
        var (token82, _) = tokens.Gerar(estadoAntes82);
        await using (var db = new CrmDbContext(opcoes))
        {
            await db.Atendentes.Where(a => a.Id == idAtend82).ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Permissoes, CatalogoDePermissoes.Serializar([PermissaoDoSistema.RelatoriosVer])));
        }
        var estadoDepois82 = await Ler(idAtend82);
        Checar("82-3a. TokenVersion nao muda so por trocar Permissoes",
            estadoDepois82.TokenVersion == estadoAntes82.TokenVersion);
        await using (var db = new CrmDbContext(opcoes))
        {
            var sessao82 = await NovoValidador(db).CarregarSessaoAsync(idAtend82, tenantId, LerVersaoDaClaim(token82));
            Checar("82-3b. o MESMO token antigo (nao revogado) ja enxerga o conjunto NOVO, sem novo login",
                sessao82 is not null
                && sessao82.Permissoes.Contains(PermissaoDoSistema.RelatoriosVer)
                && !sessao82.Permissoes.Contains(PermissaoDoSistema.EquipeVer));
        }

        var conjuntoComLixo82 = CatalogoDePermissoes.Desserializar("|atendimento.ver|isto_nao_existe|clientes.ver|");
        Checar("82-4. chave desconhecida e ignorada em silencio (nunca lanca), as validas continuam",
            conjuntoComLixo82.Count == 2
            && conjuntoComLixo82.Contains(PermissaoDoSistema.AtendimentoVer)
            && conjuntoComLixo82.Contains(PermissaoDoSistema.ClientesVer));

        var holderVazio82 = new PermissoesDaRequisicao();
        Checar("82-5. PermissoesDaRequisicao nao-definido nega QUALQUER permissao — fail-closed",
            !holderVazio82.Definido && !holderVazio82.Pode(PermissaoDoSistema.AtendimentoVer));

        Guid tenantIsolado82;
        await using (var db = new CrmDbContext(opcoes))
        {
            var tenant = new Tenant
            {
                RazaoSocial = "[TESTE] Tenant isolado — permissoes (cenario 82)",
                Cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString()
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            tenantIsolado82 = tenant.Id;
        }
        tenantIdsDeTeste.Add(tenantIsolado82);

        Guid idOutroTenant82;
        await using (var db = new CrmDbContext(opcoes))
        {
            var conta = new Atendente
            {
                TenantId = tenantIsolado82,
                Nome = "[TESTE] 82 outro tenant",
                Email = $"teste-82-{Guid.NewGuid():N}@teste.local",
                SenhaHash = hash.Gerar(SenhaBoa),
                Perfil = PerfilDeAtendente.Atendente,
                Permissoes = CatalogoDePermissoes.Serializar([PermissaoDoSistema.EquipePermissoes]),
                Ativo = true
            };
            db.Atendentes.Add(conta);
            await db.SaveChangesAsync();
            idOutroTenant82 = conta.Id;
            ids.Add(conta.Id);
        }
        await using (var db = new CrmDbContext(opcoes))
        {
            var sessaoDoTenantErrado82 = await NovoValidador(db).CarregarSessaoAsync(idOutroTenant82, tenantId, 0);
            Checar("82-6. atendente de outro tenant, consultado com o TenantId errado, nao acha ninguem -> sessao nula",
                sessaoDoTenantErrado82 is null);
        }
    }

    // ------------------------------------------------------------ cenario 83
    // A meta-permissao equipe.permissoes: as travas de DefinirPermissoesAsync.

    {
        Console.WriteLine();
        Console.WriteLine("Cenario 83 — equipe.permissoes: as travas.");

        Guid tenantIsolado83;
        await using (var db = new CrmDbContext(opcoes))
        {
            var tenant = new Tenant
            {
                RazaoSocial = "[TESTE] Tenant isolado — travas de equipe.permissoes (cenario 83)",
                Cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString()
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            tenantIsolado83 = tenant.Id;
        }
        tenantIdsDeTeste.Add(tenantIsolado83);

        async Task<Guid> ContaNoIsolado83(
            string rotulo, PerfilDeAtendente perfil,
            CargoNaServentia cargo = CargoNaServentia.NaoInformado,
            IReadOnlySet<PermissaoDoSistema>? permissoes = null)
        {
            await using var db = new CrmDbContext(opcoes);
            var conta = new Atendente
            {
                TenantId = tenantIsolado83,
                Nome = $"[TESTE] 83 {rotulo}",
                Email = $"teste-83-{Guid.NewGuid():N}@teste.local",
                SenhaHash = hash.Gerar(SenhaBoa),
                Perfil = perfil,
                Cargo = cargo,
                Permissoes = permissoes is null ? null : CatalogoDePermissoes.Serializar(permissoes),
                Ativo = true
            };
            db.Atendentes.Add(conta);
            await db.SaveChangesAsync();
            ids.Add(conta.Id);
            return conta.Id;
        }

        IAtendenteService ServicoComo83(
            CrmDbContext db, Guid atorId, PerfilDeAtendente perfilDoAtor,
            IReadOnlySet<PermissaoDoSistema>? permissoesDoAtor = null) =>
            new AtendenteService(db, new ContextoFixo(tenantIsolado83, atorId, "Ator83", perfilDoAtor, permissoesDoAtor), hash);

        async Task<string?> CodigoDoErro83(Func<Task> acao)
        {
            try
            {
                await acao();
                return null;
            }
            catch (UsuariosServiceException ex)
            {
                return ex.Codigo;
            }
        }

        var todasMenosSimular83 = CatalogoDePermissoes.ConjuntoDoModelo(ModeloDePermissao.Administracao);
        var semEquipePermissoes83 = todasMenosSimular83.Where(p => p != PermissaoDoSistema.EquipePermissoes).ToHashSet();

        // 83-1: ator sem equipe.permissoes -> 403 (trave 0)
        {
            var idAtorSemPermissao83 = await ContaNoIsolado83(
                "ator sem equipe.permissoes", PerfilDeAtendente.Administrador, permissoes: semEquipePermissoes83);
            var idAlvo83a = await ContaNoIsolado83("alvo 83-1", PerfilDeAtendente.Atendente);

            await using var db = new CrmDbContext(opcoes);
            var codigo = await CodigoDoErro83(() => ServicoComo83(db, idAtorSemPermissao83, PerfilDeAtendente.Administrador, semEquipePermissoes83)
                .DefinirPermissoesAsync(idAlvo83a, new DefinirPermissoesRequest(null, ["clientes.ver"]), null));

            Checar("83-1. ator sem equipe.permissoes -> sem_permissao", codigo == "sem_permissao");
        }

        // 83-2: ninguem edita as proprias permissoes (trave a)
        {
            var idSelf83 = await ContaNoIsolado83("edita a si mesmo", PerfilDeAtendente.Administrador, permissoes: todasMenosSimular83);

            await using var db = new CrmDbContext(opcoes);
            var codigo = await CodigoDoErro83(() => ServicoComo83(db, idSelf83, PerfilDeAtendente.Administrador, todasMenosSimular83)
                .DefinirPermissoesAsync(idSelf83, new DefinirPermissoesRequest(null, ["clientes.ver"]), null));

            Checar("83-2. ninguem edita as proprias permissoes -> nao_pode_editar_as_proprias_permissoes",
                codigo == "nao_pode_editar_as_proprias_permissoes");
        }

        // 83-3: anti-escalada — nao concede o que nao tem, e o banco NAO muda
        {
            var permissoesLimitadas83 = new HashSet<PermissaoDoSistema>
            {
                PermissaoDoSistema.EquipePermissoes, PermissaoDoSistema.EquipeVer, PermissaoDoSistema.ClientesVer,
            };
            var idAtorLimitado83 = await ContaNoIsolado83("ator com poucas permissoes", PerfilDeAtendente.Administrador, permissoes: permissoesLimitadas83);
            var idAlvo83c = await ContaNoIsolado83("alvo 83-3", PerfilDeAtendente.Atendente);

            await using var db = new CrmDbContext(opcoes);
            var codigo = await CodigoDoErro83(() => ServicoComo83(db, idAtorLimitado83, PerfilDeAtendente.Administrador, permissoesLimitadas83)
                .DefinirPermissoesAsync(idAlvo83c, new DefinirPermissoesRequest(null, ["clientes.ver", "whatsapp.parear"]), null));

            Checar("83-3a. conceder o que o ator nao tem -> nao_pode_conceder_o_que_nao_tem",
                codigo == "nao_pode_conceder_o_que_nao_tem");

            var alvoDepois83c = await Ler(idAlvo83c);
            Checar("83-3b. o banco NAO mudou (a tentativa recusada nao deixou rastro)",
                alvoDepois83c.Permissoes is null);
        }

        // 83-4: sobra pelo menos um administrador ativo (trave c, caso simples)
        {
            var idUnicoAdmin83 = await ContaNoIsolado83("unico com equipe.permissoes", PerfilDeAtendente.Administrador, permissoes: todasMenosSimular83);

            await using var db = new CrmDbContext(opcoes);
            var codigo = await CodigoDoErro83(() => ServicoComo83(db, idUnicoAdmin83, PerfilDeAtendente.Administrador, todasMenosSimular83)
                .DefinirPermissoesAsync(idUnicoAdmin83, new DefinirPermissoesRequest(null, ["clientes.ver"]), null));

            // Nao pode editar as proprias (83-2) impede testar diretamente
            // "o unico se tira a si mesmo" — o caso real e um OUTRO ator
            // tirando do unico administrador restante.
            _ = codigo;

            var idOutroAtor83d = await ContaNoIsolado83("outro ator, sem contar como admin", PerfilDeAtendente.Administrador, permissoes: todasMenosSimular83);
            await using var db2 = new CrmDbContext(opcoes);
            var codigo2 = await CodigoDoErro83(() => ServicoComo83(db2, idOutroAtor83d, PerfilDeAtendente.Administrador, todasMenosSimular83)
                .DefinirPermissoesAsync(idUnicoAdmin83, new DefinirPermissoesRequest(null, ["clientes.ver"]), null));

            // idOutroAtor83d TAMBEM tem equipe.permissoes, entao ele proprio
            // sustenta a invariante — o alvo perde sem problema.
            Checar("83-4a. com outro administrador ativo presente, o alvo perde equipe.permissoes normalmente",
                codigo2 is null);

            var alvoFinal83d = await Ler(idUnicoAdmin83);
            Checar("83-4b. o alvo de fato perdeu equipe.permissoes",
                !CatalogoDePermissoes.Desserializar(alvoFinal83d.Permissoes).Contains(PermissaoDoSistema.EquipePermissoes));

            // NAO existe um "83-4c" sincrono testando "tirar do ULTIMO que
            // tem" com um terceiro ator: quem executa DefinirPermissoesAsync
            // PRECISA ter equipe.permissoes (trave 0) e e sempre DIFERENTE do
            // alvo (trave a) — entao o proprio ator, sozinho, ja sustenta a
            // invariante em qualquer chamada isolada, nao-concorrente. A
            // trave (c) so tem como ser alcancada de verdade sob CORRIDA —
            // dois atores tirando a permissao um do outro ao mesmo tempo,
            // cada um "gastando" o proprio saldo de administrador no exato
            // instante em que teria sustentado o do outro. E o cenario 83-5,
            // logo abaixo.
        }

        // 83-5: a MESMA trava sob corrida — dois administradores tentam tirar
        // a permissao um do outro ao mesmo tempo (cada um e o unico "outro"
        // administrador aos olhos do outro no instante da leitura).
        //
        // TENANT PROPRIO, isolado dos demais sub-cenarios de 83: qualquer
        // OUTRO administrador presente no mesmo tenant sustentaria a
        // invariante para os dois lados ao mesmo tempo e mascararia a
        // corrida — o teste so vale alguma coisa com EXATAMENTE dois
        // administradores no tenant inteiro.
        {
            Guid tenantIsolado83e;
            await using (var db = new CrmDbContext(opcoes))
            {
                var tenant = new Tenant
                {
                    RazaoSocial = "[TESTE] Tenant isolado — corrida do ultimo administrador (cenario 83-5)",
                    Cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString()
                };
                db.Tenants.Add(tenant);
                await db.SaveChangesAsync();
                tenantIsolado83e = tenant.Id;
            }
            tenantIdsDeTeste.Add(tenantIsolado83e);

            async Task<Guid> ContaNoIsolado83e(string rotulo)
            {
                await using var db = new CrmDbContext(opcoes);
                var conta = new Atendente
                {
                    TenantId = tenantIsolado83e,
                    Nome = $"[TESTE] 83-5 {rotulo}",
                    Email = $"teste-83-5-{Guid.NewGuid():N}@teste.local",
                    SenhaHash = hash.Gerar(SenhaBoa),
                    Perfil = PerfilDeAtendente.Administrador,
                    Permissoes = CatalogoDePermissoes.Serializar(todasMenosSimular83),
                    Ativo = true
                };
                db.Atendentes.Add(conta);
                await db.SaveChangesAsync();
                ids.Add(conta.Id);
                return conta.Id;
            }

            var idParA83 = await ContaNoIsolado83e("par A (corrida)");
            var idParB83 = await ContaNoIsolado83e("par B (corrida)");

            async Task<bool> TentarTirarDoOutro83(Guid atorId, Guid alvoId)
            {
                await using var db = new CrmDbContext(opcoes);
                var ctxAtor = new ContextoFixo(tenantIsolado83e, atorId, "Ator83-5", PerfilDeAtendente.Administrador, todasMenosSimular83);
                var codigo = await CodigoDoErro83(() => new AtendenteService(db, ctxAtor, hash)
                    .DefinirPermissoesAsync(alvoId, new DefinirPermissoesRequest(null, ["clientes.ver"]), null));
                return codigo is null;
            }

            var tarefaAB = TentarTirarDoOutro83(idParA83, idParB83);
            var tarefaBA = TentarTirarDoOutro83(idParB83, idParA83);
            await Task.WhenAll(tarefaAB, tarefaBA);

            var estadoFinalA83 = await Ler(idParA83);
            var estadoFinalB83 = await Ler(idParB83);
            var aIntacto = CatalogoDePermissoes.Desserializar(estadoFinalA83.Permissoes).Contains(PermissaoDoSistema.EquipePermissoes);
            var bIntacto = CatalogoDePermissoes.Desserializar(estadoFinalB83.Permissoes).Contains(PermissaoDoSistema.EquipePermissoes);

            Checar($"83-5. corrida A<->B tirando equipe.permissoes um do outro, TENANT SO COM OS DOIS: NUNCA os dois perdem ao mesmo tempo " +
                   $"(A intacto={aIntacto}, B intacto={bIntacto}, AB venceu={await tarefaAB}, BA venceu={await tarefaBA})",
                aIntacto ^ bIntacto);
        }

        // 83-6: Titular protegido — Administrador nao-Titular nao altera
        // permissao de um Titular (espelha MfaService.GarantirTitularProtegido).
        {
            var idTitular83 = await ContaNoIsolado83("Titular", PerfilDeAtendente.Administrador, CargoNaServentia.Titular, todasMenosSimular83);
            var idAdminComum83 = await ContaNoIsolado83("Administrador comum (nao-Titular)", PerfilDeAtendente.Administrador, permissoes: todasMenosSimular83);

            await using var db = new CrmDbContext(opcoes);
            var codigo = await CodigoDoErro83(() => ServicoComo83(db, idAdminComum83, PerfilDeAtendente.Administrador, todasMenosSimular83)
                .DefinirPermissoesAsync(idTitular83, new DefinirPermissoesRequest(null, ["clientes.ver"]), null));

            Checar("83-6a. Administrador nao-Titular nao altera permissao de Titular -> apenas_titular_altera_permissao_de_titular",
                codigo == "apenas_titular_altera_permissao_de_titular");

            var idOutroTitular83 = await ContaNoIsolado83("outro Titular", PerfilDeAtendente.Administrador, CargoNaServentia.Titular, todasMenosSimular83);
            await using var db2 = new CrmDbContext(opcoes);
            var codigo2 = await CodigoDoErro83(() => ServicoComo83(db2, idOutroTitular83, PerfilDeAtendente.Administrador, todasMenosSimular83)
                .DefinirPermissoesAsync(idTitular83, new DefinirPermissoesRequest(null, ["clientes.ver"]), null));

            Checar("83-6b. mas OUTRO Titular pode alterar a permissao de um Titular",
                codigo2 is null);
        }

        // 83-7/83-8: TokenVersion nao sobe ao salvar permissao (efeito
        // imediato, sem derrubar sessao), e o EventoDeSeguranca e gravado
        // sem as chaves no Motivo.
        {
            var idAtor83g = await ContaNoIsolado83("ator 83-7/8", PerfilDeAtendente.Administrador, permissoes: todasMenosSimular83);
            var idAlvo83g = await ContaNoIsolado83("alvo 83-7/8", PerfilDeAtendente.Atendente);
            var estadoAntes83g = await Ler(idAlvo83g);

            await using (var db = new CrmDbContext(opcoes))
            {
                await ServicoComo83(db, idAtor83g, PerfilDeAtendente.Administrador, todasMenosSimular83)
                    .DefinirPermissoesAsync(idAlvo83g, new DefinirPermissoesRequest(ModeloDePermissao.Catalogo,
                        [.. CatalogoDePermissoes.ConjuntoDoModelo(ModeloDePermissao.Catalogo).Select(p => CatalogoDePermissoes.Descrever(p).ChaveTexto)]),
                        "203.0.113.83");
            }

            var estadoDepois83g = await Ler(idAlvo83g);
            Checar("83-7. TokenVersion nao sobe ao salvar permissao (efeito ja e imediato via CarregarSessaoAsync)",
                estadoDepois83g.TokenVersion == estadoAntes83g.TokenVersion);
            Checar("83-7b. ModeloDePermissao gravado como rotulo do preset aplicado",
                estadoDepois83g.ModeloDePermissao == ModeloDePermissao.Catalogo);

            await using var dbEvento = new CrmDbContext(opcoes);
            var evento83g = await dbEvento.EventosDeSeguranca.AsNoTracking()
                .Where(e => e.AtendenteId == idAlvo83g && e.Tipo == TipoDeEventoDeSeguranca.PermissoesAlteradas)
                .SingleAsync();

            Checar("83-8a. EventoDeSeguranca(PermissoesAlteradas) registrado com o ator correto",
                evento83g.AtorAtendenteId == idAtor83g);
            Checar("83-8b. Motivo nunca carrega as chaves concedidas (so o fato, nunca a lista)",
                evento83g.Motivo is null);
            Checar("83-8c. IP registrado",
                evento83g.IpDeOrigem == "203.0.113.83");
        }
    }
}
finally
{
    await using var db = new CrmDbContext(opcoes);

    // SEC-03: as tres tabelas novas referenciam Atendentes com FK Restrict —
    // precisam sair primeiro, ou o DELETE de Atendentes falha.
    await db.DesafiosDeMfa.Where(d => ids.Contains(d.AtendenteId)).ExecuteDeleteAsync();
    await db.CodigosDeRecuperacaoMfa.Where(c => ids.Contains(c.AtendenteId)).ExecuteDeleteAsync();
    await db.EventosDeSeguranca.Where(e => ids.Contains(e.AtendenteId)).ExecuteDeleteAsync();
    await db.DispositivosConfiaveis.Where(d => ids.Contains(d.AtendenteId)).ExecuteDeleteAsync();

    var removidos = await db.Atendentes.Where(a => ids.Contains(a.Id)).ExecuteDeleteAsync();

    // SEC-03 (cenario 52): tenant isolado, criado so para testar o flip de
    // politica sem mexer no tenant compartilhado. So pode sair DEPOIS dos
    // Atendentes acima (FK Restrict).
    var tenantsRemovidos = tenantIdsDeTeste.Count == 0
        ? 0
        : await db.Tenants.Where(t => tenantIdsDeTeste.Contains(t.Id)).ExecuteDeleteAsync();

    Console.WriteLine();
    Console.WriteLine($"Limpeza: {removidos} atendente(s) e {tenantsRemovidos} tenant(s) de teste removido(s).");
}

Console.WriteLine();
Console.WriteLine(falhas == 0 ? "TODOS OS TESTES PASSARAM." : $"{falhas} TESTE(S) FALHARAM.");

return falhas == 0 ? 0 : 1;

// ---------------------------------------------------------------------------

/// <summary>
/// SEC-02 (cenario 23): delega ao hash de verdade e so conta as verificacoes.
/// </summary>
/// <remarks>
/// Contar a INVOCACAO, e nao medir tempo, e deliberado: o que precisa ser provado
/// e que nao existe atalho de saida antes do PBKDF2 em nenhum dos quatro casos de
/// recusa. Um teste de milissegundos provaria a mesma coisa de forma fragil, com
/// falso negativo em maquina carregada — e nao entraria num verificador de rotina.
/// </remarks>
file sealed class HashEspiao(IHashDeSenha real) : IHashDeSenha
{
    public int Verificacoes { get; private set; }

    public string Gerar(string senha) => real.Gerar(senha);

    public bool Verificar(string senha, string hashArmazenado)
    {
        Verificacoes++;
        return real.Verificar(senha, hashArmazenado);
    }
}

/// <summary>
/// SEC-03 (cenario 79) — ambiente de hospedagem com nome escolhido pelo teste.
/// So o <c>EnvironmentName</c> importa: e dele que sai a decisao de `Secure`.
/// </summary>
file sealed class AmbienteFalso(string nome) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = nome;
    public string ApplicationName { get; set; } = "CrmCartorio.Api";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

/// <summary>
/// SEC-03 ("confiar neste dispositivo") — o estado que fica NO NAVEGADOR.
/// Uma instancia = um navegador; instancias diferentes = navegadores
/// diferentes (ou uma aba anonima). Guarda o token EM CLARO porque e
/// exatamente isso que o navegador real guarda; o servidor so ve o hash.
/// </summary>
file sealed class NavegadorFalso : ICookieDeDispositivoConfiavel
{
    public string? Cookie { get; private set; }

    /// <summary>Quantas vezes o servidor mandou gravar — a rotacao vira contagem observavel.</summary>
    public int Gravacoes { get; private set; }

    public DateTime? ExpiraEmGravado { get; private set; }

    public string? Ler() => Cookie;

    public void Definir(string tokenEmClaro, DateTime expiraEm)
    {
        Cookie = tokenEmClaro;
        ExpiraEmGravado = expiraEm;
        Gravacoes++;
    }

    public void Limpar()
    {
        Cookie = null;
        ExpiraEmGravado = null;
    }

    /// <summary>Copia deste navegador — o equivalente de teste a um cookie ROUBADO.</summary>
    public NavegadorFalso Clonar()
    {
        var copia = new NavegadorFalso();
        if (Cookie is not null) copia.Definir(Cookie, ExpiraEmGravado ?? DateTime.UtcNow.AddDays(30));
        return copia;
    }
}

/// <summary>
/// SEC-03 (revisao independente do PR #46, bloqueador 13) — fault injection
/// de teste: lanca ao detectar um EventoDeSeguranca do Tipo indicado sendo
/// salvo. Prova que "evento falhou" implica "a mutacao tambem nao persiste"
/// — nao um schema/trigger novo, so um SaveChangesInterceptor que so existe
/// na configuracao de DbContext deste verificador, nunca em producao.
/// </summary>
file sealed class InterceptorDeFalhaDeEvento(TipoDeEventoDeSeguranca tipoParaFalhar) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        var contexto = eventData.Context;
        if (contexto is not null)
        {
            var temEventoAlvo = contexto.ChangeTracker.Entries<EventoDeSeguranca>()
                .Any(e => e.State == EntityState.Added && e.Entity.Tipo == tipoParaFalhar);

            if (temEventoAlvo)
            {
                throw new InvalidOperationException(
                    $"[FALHA INJETADA PELO TESTE] EventoDeSeguranca.Tipo={tipoParaFalhar}");
            }
        }

        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default) =>
        new(SavingChanges(eventData, result));
}

/// <summary>
/// Identidade fixa, no lugar das claims do token — mesmo padrao de
/// verificar-concorrencia.cs. <paramref name="permissoes"/> omitido deriva o
/// conjunto do Perfil (CatalogoDePermissoes.ConjuntoDaClasse), o mesmo
/// fallback que ValidadorDeSessao usa quando Atendente.Permissoes e NULL —
/// cenario passa um conjunto explicito so quando precisa simular
/// personalizacao.
/// </summary>
file sealed class ContextoFixo(
    Guid tenantId,
    Guid atendenteId,
    string nome,
    PerfilDeAtendente perfil,
    IReadOnlySet<PermissaoDoSistema>? permissoes = null)
    : IContextoDoUsuario
{
    public bool Autenticado => true;
    public Guid TenantId => tenantId;
    public Guid AtendenteId => atendenteId;
    public string Nome => nome;
    public PerfilDeAtendente Perfil => perfil;

    public bool Pode(PermissaoDoSistema permissao) =>
        (permissoes ?? CatalogoDePermissoes.ConjuntoDaClasse(perfil)).Contains(permissao);
}
