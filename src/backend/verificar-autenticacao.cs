#:project CrmCartorio.Api/CrmCartorio.Api.csproj
#:property PublishAot=false

using CrmCartorio.Api.Infrastructure.Identity;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Application;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using OtpNet;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
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

IAutenticacaoService NovoAutenticacao(CrmDbContext db, Guid atorId) =>
    new AutenticacaoService(db, hash, tokens, cifra, new ContextoFixo(tenantId, atorId, "Teste", PerfilDeAtendente.Atendente), NullLogger<AutenticacaoService>.Instance);

IAtendenteService NovoAtendenteService(CrmDbContext db, Guid atorId) =>
    new AtendenteService(db, new ContextoFixo(tenantId, atorId, "Admin", PerfilDeAtendente.Administrador), hash);

// Mesma coisa, mas para um tenant ISOLADO — AtualizarAsync filtra por
// `TenantId == ctx.TenantId`, entao o AtendenteService acima (fechado no
// `tenantId` COMPARTILHADO) nunca enxerga conta de tenant isolado.
IAtendenteService NovoAtendenteServiceParaTenant(CrmDbContext db, Guid atorId, Guid tenantAlvo) =>
    new AtendenteService(db, new ContextoFixo(tenantAlvo, atorId, "Admin", PerfilDeAtendente.Administrador), hash);

IMfaService NovoMfaService(CrmDbContext db) =>
    new MfaService(db, hash, cifra, tokens, NullLogger<MfaService>.Instance);

IValidadorDeSessao NovoValidador(CrmDbContext db) => new ValidadorDeSessao(db);

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
async Task<LoginEtapa1Response> EtapaUm(string email, string senha)
{
    await using var db = new CrmDbContext(opcoes);
    return await NovoAutenticacao(db, Guid.NewGuid()).AutenticarAsync(new LoginRequest(email, senha));
}

/// <summary>Etapa 2 pelo servico real. Devolve o token, ou null se recusado (qualquer motivo).</summary>
async Task<string?> EtapaDois(string desafio, string codigo)
{
    await using var db = new CrmDbContext(opcoes);
    try
    {
        var resposta = await NovoAutenticacao(db, Guid.NewGuid())
            .ConcluirLoginMfaAsync(new ConcluirLoginMfaRequest(desafio, codigo));

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
            await NovoValidador(db).SessaoValidaAsync(atendenteId, tenantId, LerVersaoDaClaim(token1)));
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
            !await NovoValidador(db).SessaoValidaAsync(atendenteId, tenantId, versaoAntesDaTroca));
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
            !await NovoValidador(db).SessaoValidaAsync(atendenteId, tenantId, versaoAntesDoAdmin));
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
            !await NovoValidador(db).SessaoValidaAsync(atendenteId, tenantId, versaoAntesDoPerfil));
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
            !await NovoValidador(db).SessaoValidaAsync(atendenteId, tenantId, versaoAntesDaDesativacao));
    }

    // ------------------------------------------------------------- cenario 6

    await using (var db = new CrmDbContext(opcoes))
    {
        Checar("6. Atendente inexistente -> nao autorizado",
            !await NovoValidador(db).SessaoValidaAsync(Guid.NewGuid(), tenantId, 1));
    }

    // ------------------------------------------------------------- cenario 7

    var estadoAtualParaTenant = await Ler(atendenteId);
    await using (var db = new CrmDbContext(opcoes))
    {
        Checar("7. Tenant da claim incompativel -> nao autorizado",
            !await NovoValidador(db).SessaoValidaAsync(atendenteId, Guid.NewGuid(), estadoAtualParaTenant.TokenVersion));
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
            !await NovoValidador(db).SessaoValidaAsync(atendenteId, tenantId, versaoAntesDaDesativacao));
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
            await NovoValidador(db).SessaoValidaAsync(atendenteId, tenantId, LerVersaoDaClaim(loginNovo.Token)));
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
    // MESMO ValidadorDeSessao.SessaoValidaAsync — o que este cenario prova e
    // so que o validador compartilhado recusa corretamente um TokenVersion
    // revogado; nao prova a fiacao REST/SignalR em si, nem o comportamento do
    // handshake HTTP/WebSocket. O handshake SignalR end-to-end (negotiate +
    // upgrade recusados com token revogado, conexao aberta sobrevivendo a
    // revogacao — SEC-01B) sera validado no ambiente TESTE.

    await using (var db = new CrmDbContext(opcoes))
    {
        Checar("10. Validador compartilhado por REST/SignalR recusa TokenVersion revogado",
            !await NovoValidador(db).SessaoValidaAsync(atendenteId, tenantId, versaoAntesDaDesativacao));
    }

    // ------------------------------------------------------------- cenario 11

    await using (var db = new CrmDbContext(opcoes))
    {
        Checar("11. Token valido continua funcionando normalmente",
            await NovoValidador(db).SessaoValidaAsync(atendenteId, tenantId, LerVersaoDaClaim(loginNovo.Token)));
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
                !await NovoValidador(db).SessaoValidaAsync(outroAdmin.Id, tenantIsolado, tokenVersionOutroAntes));
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
    // BLOQUEADOR 1 — a primeira matricula de conta PRIVILEGIADA nunca acontece
    // pelo fluxo autenticado comum (JWT + senha), mesmo com as duas corretas.

    {
        var (idAdmin, emailAdmin) = await NovaContaPrivilegiada("bloqueador1 - admin sem mfa");
        var (idTitular, emailTitular) = await NovaContaPrivilegiada(
            "bloqueador1 - titular sem mfa", perfil: PerfilDeAtendente.Atendente, cargo: CargoNaServentia.Titular);
        var (idComum, emailComum) = await NovaContaPrivilegiada(
            "bloqueador1 - nao privilegiado", perfil: PerfilDeAtendente.Atendente);

        async Task<string?> TentarIniciarComJwtESenha(Guid id)
        {
            await using var db = new CrmDbContext(opcoes);
            try
            {
                await NovoMfaService(db).IniciarAsync(
                    new IdentidadeParaMfa(tenantId, id), new IniciarMfaRequest(SenhaBoa, null), null, default);
                return null;
            }
            catch (UsuariosServiceException ex)
            {
                return ex.Codigo;
            }
        }

        Checar("55a. Administrador sem MFA + JWT + senha correta em /mfa/iniciar -> RECUSADO",
            await TentarIniciarComJwtESenha(idAdmin) == "matricula_privilegiada_exige_ticket");

        Checar("55b. Titular (via Cargo) sem MFA + JWT + senha correta -> RECUSADO",
            await TentarIniciarComJwtESenha(idTitular) == "matricula_privilegiada_exige_ticket");

        Checar("55c. conta NAO privilegiada sem MFA: ativacao voluntaria continua funcionando",
            await TentarIniciarComJwtESenha(idComum) is null);

        // 55d — nenhum OUTRO endpoint (nem confirmar) chega ao segredo sem
        // ticket: como iniciar foi recusado, nao existe segredo pendente, e
        // confirmar tambem falha — nao ha atalho nenhum.
        await using (var db = new CrmDbContext(opcoes))
        {
            UsuariosServiceException? falha = null;
            try
            {
                await NovoMfaService(db).ConfirmarAsync(
                    new IdentidadeParaMfa(tenantId, idAdmin), new ConfirmarMfaRequest("000000"), null, default);
            }
            catch (UsuariosServiceException ex) { falha = ex; }

            Checar("55d. confirmar tambem falha para privilegiado sem ticket (nao ha segredo pendente)",
                falha is not null);
        }

        var estadoAdmin = await Ler(idAdmin);
        Checar("55e. Administrador continua com MfaAtivo=false apos as tentativas recusadas",
            !estadoAdmin.MfaAtivo);
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

        string ticketDoBootstrap;
        await using (var db = new CrmDbContext(opcoes))
        {
            ticketDoBootstrap = await FerramentaDeBootstrapMfa.EmitirTicketAsync(
                db, emailPrimeiroIsolado, "teste automatizado do cenario 60", "teste@verificador");
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
                !await NovoValidador(db).SessaoValidaAsync(idD, tenantIsolado61, versaoAntesD));
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
                !await NovoValidador(db).SessaoValidaAsync(idE, tenantIsolado61e, versaoAntesDaPromocaoE));
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
    // BLOQUEADOR 5 — confirmacao de MFA sem ticket so pode concluir a
    // PRIMEIRA matricula se a conta AINDA NAO for privilegiada no instante do
    // commit (porteiro final em MfaService.ConfirmarNucleoAsync, nao so a
    // pre-checagem em memoria).

    {
        // 62a — comum inicia -> promovido a Administrador (via Perfil) ->
        // confirmar sem ticket e recusado.
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

            Checar("62a. comum inicia MFA -> promovido a Administrador (Perfil) -> confirmar sem ticket e recusado",
                falha?.Codigo == "matricula_privilegiada_exige_ticket");
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

            Checar("62b. comum inicia MFA -> promovido a Titular (Cargo) -> confirmar sem ticket e recusado",
                falha?.Codigo == "matricula_privilegiada_exige_ticket");
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

        // Emissao: deve ser recusada — a elegibilidade mudou.
        await using (var db = new CrmDbContext(opcoes))
        {
            BootstrapNaoElegivelException? falha = null;
            try
            {
                await FerramentaDeBootstrapMfa.EmitirTicketAsync(db, emailAlvo63a, "teste do cenario 63a", "teste@verificador");
            }
            catch (BootstrapNaoElegivelException ex) { falha = ex; }

            Checar("63a. emissao recusada: elegibilidade mudou entre a verificacao e a emissao",
                falha?.MotivoAtual == ElegibilidadeDeBootstrap.JaExistePrivilegiadoMatriculado);
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
            try
            {
                await FerramentaDeBootstrapMfa.EmitirTicketAsync(db, emailAlvo63b, "teste concorrente 63b", "teste@verificador");
                return true;
            }
            catch (BootstrapNaoElegivelException)
            {
                return false;
            }
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

    // ------------------------------------------------------------ cenario 64
    // EVIDENCIA (revisao independente do PR #46) — teste HTTP REAL contra a
    // API publicada, nao chamada direta de servico. Sobe o MESMO assembly que
    // Program.cs gera (dotnet CrmCartorio.Api.dll), com Kestrel escutando em
    // loopback, e bate nele com HttpClient de verdade — prova a trava do
    // bloqueador 1 na borda HTTP real (auth JWT, roteamento, middleware,
    // ProblemDetails), que o cenario 55 (chamada direta ao MfaService) nao
    // alcanca.

    {
        var caminhoDoAssembly = Path.GetFullPath(Path.Combine("CrmCartorio.Api", "bin", "Debug", "net10.0", "CrmCartorio.Api.dll"));
        if (!File.Exists(caminhoDoAssembly))
        {
            throw new InvalidOperationException(
                $"'{caminhoDoAssembly}' nao encontrado — rode 'dotnet build CrmCartorio.Api' antes deste verificador.");
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

            Checar("64-preparo. API real (dotnet CrmCartorio.Api.dll) subiu e /health/pronto respondeu OK", pronto);

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

                var (statusPriv, corpoPriv) = await IniciarMfaHttpAsync(jwtPriv ?? string.Empty);
                Checar("64c. POST /api/mfa/iniciar real (privilegiado sem MFA, JWT+senha corretos) -> 409 Conflict",
                    statusPriv == HttpStatusCode.Conflict);
                Checar("64d. corpo real da recusa traz o codigo estavel matricula_privilegiada_exige_ticket",
                    corpoPriv.Contains("matricula_privilegiada_exige_ticket"));

                var (statusComum, corpoComum) = await IniciarMfaHttpAsync(jwtComum ?? string.Empty);
                Checar("64e. POST /api/mfa/iniciar real (conta comum sem MFA) -> 200, continua funcionando",
                    statusComum == HttpStatusCode.OK);
                Checar("64f. corpo real da conta comum traz um segredoBase32",
                    corpoComum.Contains("segredoBase32"));

                // Nenhum outro caminho HTTP autenticado alcanca a primeira
                // matricula privilegiada sem ticket: como /iniciar recusou, a
                // conta nunca teve segredo pendente — /confirmar tambem falha.
                using var requisicaoConfirmar = new HttpRequestMessage(HttpMethod.Post, "/api/mfa/confirmar")
                {
                    Content = JsonContent.Create(new { codigo = "000000" }, options: jsonOpcoes)
                };
                requisicaoConfirmar.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwtPriv ?? string.Empty);
                using var respostaConfirmar = await http.SendAsync(requisicaoConfirmar);

                Checar("64g. POST /api/mfa/confirmar real (privilegiado sem MFA) tambem falha — nenhum atalho HTTP",
                    !respostaConfirmar.IsSuccessStatusCode);
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

/// <summary>Identidade fixa, no lugar das claims do token — mesmo padrao de verificar-concorrencia.cs.</summary>
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
