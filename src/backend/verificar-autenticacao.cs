#:project CrmCartorio.Api/CrmCartorio.Api.csproj
#:property PublishAot=false

using CrmCartorio.Api.Infrastructure.Identity;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Usuarios.Application;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Net;

// VERIFICADOR DE AUTENTICACAO
//   SEC-01 — revogacao de sessao (cenarios 1..12)
//   SEC-02 — protecao contra forca bruta no login (cenarios 13..24)
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

/// <summary>Tenta o login pelo servico real. Devolve o token, ou null se recusado.</summary>
async Task<string?> Tentar(string email, string senha)
{
    await using var db = new CrmDbContext(opcoes);
    try
    {
        var resposta = await NovoAutenticacao(db, Guid.NewGuid())
            .AutenticarAsync(new LoginRequest(email, senha));

        return resposta.Token;
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
                db, espiao, tokens,
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
