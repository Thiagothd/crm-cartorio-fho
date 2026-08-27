#:project CrmCartorio.Api/CrmCartorio.Api.csproj
#:property PublishAot=false

using CrmCartorio.Api.Infrastructure.Identity;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Usuarios.Application;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

// VERIFICADOR DE AUTENTICACAO (SEC-01 — revogacao de sessao)
//
// Rode com: dotnet run verificar-autenticacao.cs ["<connection string>"]
//
// Chama os SERVICOS REAIS (AutenticacaoService, AtendenteService,
// ValidadorDeSessao, GeradorDeToken) contra SQL Server de verdade — nao EF
// InMemory, porque o incremento atomico de TokenVersion usa ExecuteUpdateAsync
// (SET TokenVersion = TokenVersion + 1), que o provedor InMemory nao traduz e
// nao prova nada sobre concorrencia real entre conexoes.
//
// O que este verificador NAO cobre: o handshake HTTP/WebSocket do SignalR em
// si (isto exigiria subir um servidor Kestrel de verdade, fora do padrao dos
// verificadores deste projeto). O que ele prova em vez disso: que
// ValidadorDeSessao — o MESMO componente que SessaoAtivaHandler usa na policy
// padrao de autorizacao (controllers) e que AtendimentoHub.OnConnectedAsync usa
// em defesa de profundidade — recusa corretamente um token revogado. Como os
// dois pontos de entrada delegam para este mesmo metodo, prova-lo aqui prova
// os dois. Ver comentario no cenario 10, abaixo.
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

IAutenticacaoService NovoAutenticacao(CrmDbContext db, Guid atorId) =>
    new AutenticacaoService(db, hash, tokens, new ContextoFixo(tenantId, atorId, "Teste", PerfilDeAtendente.Atendente), NullLogger<AutenticacaoService>.Instance);

IAtendenteService NovoAtendenteService(CrmDbContext db, Guid atorId) =>
    new AtendenteService(db, new ContextoFixo(tenantId, atorId, "Admin", PerfilDeAtendente.Administrador), hash);

IValidadorDeSessao NovoValidador(CrmDbContext db) => new ValidadorDeSessao(db);

int LerVersaoDaClaim(string jwt) =>
    int.Parse(new JsonWebTokenHandler().ReadJsonWebToken(jwt).Claims.First(c => c.Type == ClaimsDoAtendente.TokenVersion).Value);

async Task<Atendente> Ler(Guid id)
{
    await using var db = new CrmDbContext(opcoes);
    return await db.Atendentes.AsNoTracking().SingleAsync(a => a.Id == id);
}

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
        loginNovo = await NovoAutenticacao(db, atendenteId)
            .AutenticarAsync(new LoginRequest(emailAtual, senhaAtual));
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
    // SignalR: SessaoAtivaHandler (policy padrao, controllers) e
    // AtendimentoHub.OnConnectedAsync (defesa em profundidade) chamam os DOIS
    // o mesmo ValidadorDeSessao.SessaoValidaAsync — provar que ele recusa um
    // token revogado prova que um handshake NOVO com esse token e recusado
    // nos dois pontos de entrada. Nao sobe um servidor Kestrel/WebSocket real.

    await using (var db = new CrmDbContext(opcoes))
    {
        Checar("10. Handshake novo (REST e SignalR, via ValidadorDeSessao) com token revogado -> recusado",
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
}
finally
{
    await using var db = new CrmDbContext(opcoes);
    var removidos = await db.Atendentes.Where(a => ids.Contains(a.Id)).ExecuteDeleteAsync();
    Console.WriteLine();
    Console.WriteLine($"Limpeza: {removidos} atendente(s) de teste removido(s).");
}

Console.WriteLine();
Console.WriteLine(falhas == 0 ? "TODOS OS TESTES PASSARAM." : $"{falhas} TESTE(S) FALHARAM.");

return falhas == 0 ? 0 : 1;

// ---------------------------------------------------------------------------

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
