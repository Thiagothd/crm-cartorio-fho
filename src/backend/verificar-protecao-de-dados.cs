#:project CrmCartorio.Api/CrmCartorio.Api.csproj
#:property PublishAot=false

using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Infrastructure.Anexos;
using CrmCartorio.Api.Infrastructure.Auditoria;
using CrmCartorio.Api.Infrastructure.Identity;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Atendimento.Application;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.CanalWhatsApp.Application;
using CrmCartorio.Api.Modules.Clientes.Application;
using CrmCartorio.Api.Modules.Clientes.Domain.Entities;
using CrmCartorio.Api.Modules.RespostaInteligente.Application;
using CrmCartorio.Api.Modules.RespostaInteligente.Application.Provedores;
using CrmCartorio.Api.Modules.Seguranca.Application;
using CrmCartorio.Api.Modules.Seguranca.Domain.Enums;
using CrmCartorio.Api.Modules.Usuarios.Application;
using CrmCartorio.Api.Modules.Usuarios.Domain;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

// VERIFICADOR DE PROTECAO DE DADOS (blindagem LGPD)
//   PR2 — leitura exige permissao + trilha de acesso visivel ao cartorio
//   (cenarios 1..18 abaixo). PR3 (cifra em repouso) e PR4 (acesso tecnico
//   autorizado) acrescentam cenarios aqui quando forem implementados — este
//   arquivo e o mesmo verificador do plano inteiro, nao um por PR.
//
// Rode com: dotnet run verificar-protecao-de-dados.cs ["<connection string>"]
//
// Chama os SERVICOS REAIS (ConversaService, MensagemService, ClienteService,
// SegurancaService, AutenticacaoService) contra SQL Server de verdade — o
// gatilho TR_RegistrosDeAcesso_SomenteInsercao e o CHECK de EventosDeSeguranca
// so existem no banco real, EF InMemory nao os reproduziria.
//
// ATENCAO: este verificador ESCREVE no banco. Rode SOMENTE contra base de
// desenvolvimento. Ao final remove tudo que criou.

var conexao = args.Length > 0
    ? args[0]
    : "Server=.\\SQLEXPRESS;Database=CRM_Cartorio_Dev;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

var construtor = new DbContextOptionsBuilder<CrmDbContext>();
ConfiguracaoDoBanco.Configurar(construtor, conexao);
var opcoes = construtor.Options;
var (provedorDeChaveDormente, cifraDeDados) = FabricaDeCifraDeDados.CriarParDormente();

// PR3 (cifra em repouso): chave FIXA de desenvolvimento, so para os cenarios
// que provam a cifra ATIVA (round-trip, indice cego, ExigirCifrado,
// --cifrar-dados-legados). Todo o resto do arquivo (cenarios 1-33, PR2/PR4)
// continua rodando com `cifraDeDados` dormente — comportamento identico ao
// que ja era antes do PR3 existir.
var (provedorDeChaveAtiva, cifraAtiva) = FabricaDeCifraDeDados.CriarParComChaveDeDesenvolvimento();
var indiceCegoAtivo = new IndiceCego(provedorDeChaveAtiva);

// Mesma disciplina de verificar-autenticacao.cs (bloqueador 13): o
// interceptor de falha injetada so existe aqui, nunca na configuracao real.
DbContextOptions<CrmDbContext> OpcoesComFalhaAoRegistrarAcesso()
{
    var builder = new DbContextOptionsBuilder<CrmDbContext>();
    ConfiguracaoDoBanco.Configurar(builder, conexao);
    builder.AddInterceptors(new InterceptorDeFalhaDeAcesso());
    return builder.Options;
}

await using (var checagem = new CrmDbContext(opcoes, cifraDeDados))
{
    if (!string.Equals(checagem.Database.GetDbConnection().Database, "CRM_Cartorio_Dev", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Recusado: o verificador so escreve em CRM_Cartorio_Dev.");
    }

    if (!await checagem.Database.CanConnectAsync())
    {
        Console.WriteLine("Nao foi possivel conectar. Verifique a connection string.");
        return 2;
    }

    try
    {
        await checagem.RegistrosDeAcesso.AsNoTracking().Select(r => r.Id).Take(1).ToListAsync();
    }
    catch (Microsoft.Data.SqlClient.SqlException)
    {
        Console.WriteLine("dbo.RegistrosDeAcesso nao existe. Rode sql/045-registros-de-acesso.sql antes deste verificador.");
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
var opcoesDeMfa = Options.Create(new OpcoesDeMfa
{
    VersaoCorrente = "1",
    Chaves = new Dictionary<string, string> { ["1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }
});
var cifra = new CifraDeSegredoMfa(opcoesDeMfa);
var opcoesDaIA = Options.Create(new OpcoesDaIA { Habilitada = false });

// UM cache compartilhado por toda a execucao do verificador — mesmo papel do
// singleton que Program.cs registra via AddMemoryCache(). A deduplicacao de
// RegistradorDeAcesso so faz sentido entre chamadas DIFERENTES (duas
// instancias de ConversaService/MensagemService abertas em pontos diferentes
// do teste, como duas requisicoes HTTP diferentes na producao); um cache novo
// por fabrica quebraria exatamente o que os cenarios 3 e 5b provam.
var cacheDeDeduplicacao = new MemoryCache(new MemoryCacheOptions());

var tenantId = Guid.Empty;
var atendenteIds = new List<Guid>();
var clienteIds = new List<Guid>();
var conversaIds = new List<Guid>();

// ------------------------------------------------------ fabricas de servico

ConversaService NovaConversaService(CrmDbContext db, IContextoDoUsuario ctx) =>
    new(db, ctx, new GeradorDeProtocolo(db), new NotificadorSilencioso(),
        new DisparadorDeMensagemAutomatica(db, NullLogger<DisparadorDeMensagemAutomatica>.Instance),
        new CanalSilencioso(), new FilaSilenciosa(),
        new RegistradorDeAcesso(db, ctx, new HttpContextAccessor(), cacheDeDeduplicacao),
        NullLogger<ConversaService>.Instance);

MensagemService NovaMensagemService(CrmDbContext db, IContextoDoUsuario ctx) =>
    new(db, ctx, NovaConversaService(db, ctx), new NotificadorSilencioso(),
        new DisparadorDeMensagemAutomatica(db, NullLogger<DisparadorDeMensagemAutomatica>.Instance),
        new CanalSilencioso(), new AnexosFixos(),
        new RespostaInteligenteService(db, new OrquestradorNuncaChamado(), opcoesDaIA, NullLogger<RespostaInteligenteService>.Instance),
        opcoesDaIA,
        new RegistradorDeAcesso(db, ctx, new HttpContextAccessor(), cacheDeDeduplicacao),
        NullLogger<MensagemService>.Instance);

ClienteService NovoClienteService(CrmDbContext db, IContextoDoUsuario ctx) =>
    new(db, ctx, new EvolutionClientNuncaChamado(), new HttpClientFactoryFake(), new MemoryCache(new MemoryCacheOptions()),
        Options.Create(new OpcoesDoCanalWhatsApp()),
        new RegistradorDeAcesso(db, ctx, new HttpContextAccessor(), cacheDeDeduplicacao),
        provedorDeChaveDormente, new IndiceCego(provedorDeChaveDormente),
        NullLogger<ClienteService>.Instance);

ClienteService NovoClienteServiceComCifraAtiva(CrmDbContext db, IContextoDoUsuario ctx) =>
    new(db, ctx, new EvolutionClientNuncaChamado(), new HttpClientFactoryFake(), new MemoryCache(new MemoryCacheOptions()),
        Options.Create(new OpcoesDoCanalWhatsApp()),
        new RegistradorDeAcesso(db, ctx, new HttpContextAccessor(), cacheDeDeduplicacao),
        provedorDeChaveAtiva, indiceCegoAtivo,
        NullLogger<ClienteService>.Instance);

SegurancaService NovoSegurancaService(CrmDbContext db, IContextoDoUsuario ctx) => new(db, ctx, provedorDeChaveDormente);
SegurancaService NovoSegurancaServiceComCifraAtiva(CrmDbContext db, IContextoDoUsuario ctx) => new(db, ctx, provedorDeChaveAtiva);

IAutenticacaoService NovoAutenticacao(CrmDbContext db, IContextoDoUsuario ctx) =>
    new AutenticacaoService(db, hash, tokens, cifra, ctx, new NavegadorFalso(), new HttpContextAccessor(), NullLogger<AutenticacaoService>.Instance);

AcessoTecnicoService NovoAcessoTecnicoService(CrmDbContext db, IContextoDoUsuario ctx) =>
    new(db, ctx,
        new RegistradorDeAcesso(db, ctx, new HttpContextAccessor(), cacheDeDeduplicacao),
        new HttpContextAccessor());

async Task<string?> CodigoDaFalhaAsync(Func<Task> acao)
{
    try
    {
        await acao();
        return null;
    }
    catch (ExcecaoDeAplicacao ex)
    {
        return ex.Codigo;
    }
}

async Task<int> ContarAsync(Guid conversaId, TipoDeAcesso tipo)
{
    await using var db = new CrmDbContext(opcoes, cifraDeDados);
    return await db.RegistrosDeAcesso.CountAsync(r => r.ConversaId == conversaId && r.Tipo == tipo);
}

try
{
    // ------------------------------------------------------------- preparo

    await using (var db = new CrmDbContext(opcoes, cifraDeDados))
    {
        tenantId = await db.Tenants.Select(t => t.Id).FirstAsync();

        var leitor = new Atendente
        {
            TenantId = tenantId,
            Nome = "[TESTE PROTECAO-DE-DADOS] Leitor",
            Email = $"teste-protecao-dados-{Guid.NewGuid():N}@teste.local",
            SenhaHash = hash.Gerar("SenhaDeTeste123"),
            Perfil = PerfilDeAtendente.Atendente,
            Cargo = CargoNaServentia.NaoInformado,
            Ativo = true
        };
        db.Atendentes.Add(leitor);
        await db.SaveChangesAsync();
        atendenteIds.Add(leitor.Id);

        var cliente = new Cliente
        {
            TenantId = tenantId,
            Nome = "[TESTE PROTECAO-DE-DADOS] Cliente",
            TelefoneWhatsapp = $"55389{Random.Shared.Next(1000000, 9999999)}",
            PrimeiroContatoEm = DateTime.UtcNow,
            Ativo = true
        };
        db.Clientes.Add(cliente);
        await db.SaveChangesAsync();
        clienteIds.Add(cliente.Id);

        var conversa = new Conversa
        {
            TenantId = tenantId,
            ClienteId = cliente.Id,
            Protocolo = $"TESTE-{Guid.NewGuid():N}"[..20]
        };
        db.Conversas.Add(conversa);
        await db.SaveChangesAsync();
        conversaIds.Add(conversa.Id);

        var mensagemComAnexo = new Mensagem
        {
            TenantId = tenantId,
            ConversaId = conversa.Id,
            AutorTipo = AutorTipo.Cliente,
            Conteudo = "documento anexado",
            Tipo = TipoMensagem.Documento,
            ArquivoCaminho = "irrelevante-para-o-fake",
            ArquivoNome = "documento.pdf",
            ArquivoTipoMime = "application/pdf",
            ArquivoTamanhoBytes = 3
        };
        db.Mensagens.Add(mensagemComAnexo);
        await db.SaveChangesAsync();

        Console.WriteLine($"Tenant .... {tenantId}");
        Console.WriteLine($"Conversa .. {conversa.Protocolo} ({conversa.Id})");
        Console.WriteLine();
    }

    var leitorId = atendenteIds[0];
    var clienteId = clienteIds[0];
    var conversaId = conversaIds[0];

    Guid mensagemComAnexoId;
    await using (var db = new CrmDbContext(opcoes, cifraDeDados))
    {
        mensagemComAnexoId = await db.Mensagens.Where(m => m.ConversaId == conversaId).Select(m => m.Id).FirstAsync();
    }

    var semPermissao = new ContextoFixo(tenantId, leitorId, "Leitor", PerfilDeAtendente.Atendente,
        permissoes: new HashSet<PermissaoDoSistema>());

    var comAtendimentoVer = new ContextoFixo(tenantId, leitorId, "Leitor", PerfilDeAtendente.Atendente,
        permissoes: new HashSet<PermissaoDoSistema> { PermissaoDoSistema.AtendimentoVer });

    var comClientesVer = new ContextoFixo(tenantId, leitorId, "Leitor", PerfilDeAtendente.Atendente,
        permissoes: new HashSet<PermissaoDoSistema> { PermissaoDoSistema.ClientesVer });

    var comSegurancaVer = new ContextoFixo(tenantId, leitorId, "Leitor", PerfilDeAtendente.Atendente,
        permissoes: new HashSet<PermissaoDoSistema> { PermissaoDoSistema.SegurancaEventosVer });

    // ------------------------------------------------------------- cenario 1

    {
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        var codigo = await CodigoDaFalhaAsync(() => NovaConversaService(db, semPermissao).ObterAsync(conversaId));
        Checar("1. ObterAsync sem AtendimentoVer -> sem_permissao", codigo == "sem_permissao");
        Checar("1b. nenhum acesso foi registrado pela tentativa recusada", await ContarAsync(conversaId, TipoDeAcesso.LeituraDeConversa) == 0);
    }

    // ------------------------------------------------------------- cenario 2

    {
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        var detalhe = await NovaConversaService(db, comAtendimentoVer).ObterAsync(conversaId);
        Checar("2. ObterAsync com AtendimentoVer devolve a conversa", detalhe.Conversa.Protocolo != null);
        Checar("2b. exatamente 1 RegistroDeAcesso(LeituraDeConversa) gravado", await ContarAsync(conversaId, TipoDeAcesso.LeituraDeConversa) == 1);
    }

    // ------------------------------------------------------------- cenario 3

    {
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        var servico = NovaConversaService(db, comAtendimentoVer);
        await servico.ObterAsync(conversaId);
        await servico.ObterAsync(conversaId);
        Checar("3. abrir a mesma conversa 3x seguidas ainda conta como 1 (dedup 15 min)", await ContarAsync(conversaId, TipoDeAcesso.LeituraDeConversa) == 1);
    }

    // ------------------------------------------------------------- cenario 4

    {
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        var codigo = await CodigoDaFalhaAsync(() => NovaMensagemService(db, semPermissao).ListarAsync(conversaId));
        Checar("4. MensagemService.ListarAsync sem AtendimentoVer -> sem_permissao", codigo == "sem_permissao");
    }

    // ------------------------------------------------------------- cenario 5

    {
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        var mensagens = await NovaMensagemService(db, comAtendimentoVer).ListarAsync(conversaId);
        Checar("5. MensagemService.ListarAsync com permissao devolve o fio", mensagens.Count >= 1);
        // Mesmo Tipo+alvo do cenario 2/3 (LeituraDeConversa, mesma conversa,
        // mesmo atendente): a deduplicacao colapsa os dois PONTOS DE ENTRADA
        // (tela de detalhe e listagem de mensagens) na MESMA janela — prova
        // que a trilha registra "abriu a conversa", nao "chamou este metodo".
        Checar("5b. continua sendo 1 registro (dedup entre ObterAsync e ListarAsync)", await ContarAsync(conversaId, TipoDeAcesso.LeituraDeConversa) == 1);
    }

    // ------------------------------------------------------------- cenario 6

    {
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        var codigo = await CodigoDaFalhaAsync(() => NovaMensagemService(db, semPermissao).ObterAnexoAsync(conversaId, mensagemComAnexoId));
        Checar("6. ObterAnexoAsync sem AtendimentoVer -> sem_permissao", codigo == "sem_permissao");
    }

    // ------------------------------------------------------------- cenario 7

    {
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        var servico = NovaMensagemService(db, comAtendimentoVer);
        await servico.ObterAnexoAsync(conversaId, mensagemComAnexoId);
        await servico.ObterAnexoAsync(conversaId, mensagemComAnexoId);
        Checar("7. baixar o MESMO anexo 2x seguidas grava 2 registros (DownloadDeAnexo nunca deduplica)",
            await ContarAsync(conversaId, TipoDeAcesso.DownloadDeAnexo) == 2);
    }

    // ------------------------------------------------------------- cenario 8

    {
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        var codigo = await CodigoDaFalhaAsync(() => NovaMensagemService(db, semPermissao).DestinosAsync(conversaId, null));
        Checar("8. DestinosAsync (destinos de encaminhamento) sem AtendimentoVer -> sem_permissao", codigo == "sem_permissao");
    }

    // ------------------------------------------------------------- cenario 9

    {
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        var codigo = await CodigoDaFalhaAsync(() => NovoClienteService(db, semPermissao).ObterAsync(clienteId));
        Checar("9. ClienteService.ObterAsync sem ClientesVer -> sem_permissao", codigo == "sem_permissao");
    }

    // ------------------------------------------------------------ cenario 10

    {
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        var ficha = await NovoClienteService(db, comClientesVer).ObterAsync(clienteId);
        Checar("10. ClienteService.ObterAsync com ClientesVer devolve a ficha", ficha.Id == clienteId);

        await using var verificacao = new CrmDbContext(opcoes, cifraDeDados);
        var total = await verificacao.RegistrosDeAcesso.CountAsync(
            r => r.ClienteId == clienteId && r.Tipo == TipoDeAcesso.LeituraDeFichaDeCliente);
        Checar("10b. RegistroDeAcesso(LeituraDeFichaDeCliente) gravado", total == 1);
    }

    // ------------------------------------------------------------ cenario 11

    {
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        var falhouUpdate = false;
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.RegistrosDeAcesso SET Tipo = 2 WHERE ConversaId = {conversaId}");
        }
        catch (Exception)
        {
            falhouUpdate = true;
        }
        Checar("11. UPDATE direto contra RegistrosDeAcesso e bloqueado pelo gatilho", falhouUpdate);
    }

    // ------------------------------------------------------------ cenario 12

    {
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        var falhouDelete = false;
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.RegistrosDeAcesso WHERE ConversaId = {conversaId}");
        }
        catch (Exception)
        {
            falhouDelete = true;
        }
        Checar("12. DELETE direto contra RegistrosDeAcesso e bloqueado pelo gatilho", falhouDelete);

        await using var verificacao = new CrmDbContext(opcoes, cifraDeDados);
        var aindaExiste = await verificacao.RegistrosDeAcesso.AnyAsync(r => r.ConversaId == conversaId);
        Checar("12b. as linhas continuam no banco depois das tentativas de UPDATE/DELETE", aindaExiste);
    }

    // ------------------------------------------------------------ cenario 13

    {
        // Conversa NOVA, isolada: os cenarios 2/3/5 ja deduplicaram
        // LeituraDeConversa para esta mesma conversa/atendente, e a falha
        // injetada precisa passar pelo caminho de escrita de verdade, nao
        // ser mascarada pelo cache de deduplicacao.
        Guid conversaIsoladaId;
        await using (var prep = new CrmDbContext(opcoes, cifraDeDados))
        {
            var cliente13 = new Cliente
            {
                TenantId = tenantId,
                Nome = "[TESTE PROTECAO-DE-DADOS] Cliente 13",
                TelefoneWhatsapp = $"55389{Random.Shared.Next(1000000, 9999999)}",
                PrimeiroContatoEm = DateTime.UtcNow,
                Ativo = true
            };
            prep.Clientes.Add(cliente13);
            await prep.SaveChangesAsync();
            clienteIds.Add(cliente13.Id);

            var conversa13 = new Conversa
            {
                TenantId = tenantId,
                ClienteId = cliente13.Id,
                Protocolo = $"TESTE13-{Guid.NewGuid():N}"[..20]
            };
            prep.Conversas.Add(conversa13);
            await prep.SaveChangesAsync();
            conversaIsoladaId = conversa13.Id;
            conversaIds.Add(conversaIsoladaId);
        }

        await using var db = new CrmDbContext(OpcoesComFalhaAoRegistrarAcesso(), cifraDeDados);
        var lancou = false;
        try
        {
            await NovaConversaService(db, comAtendimentoVer).ObterAsync(conversaIsoladaId);
        }
        catch (InvalidOperationException)
        {
            lancou = true;
        }
        Checar("13. falha ao gravar o RegistroDeAcesso derruba a leitura (fail-closed)", lancou);
    }

    // ------------------------------------------------------------ cenario 14

    {
        var email = $"teste-login-protecao-dados-{Guid.NewGuid():N}@teste.local";
        Guid contaDeLoginId;
        await using (var db = new CrmDbContext(opcoes, cifraDeDados))
        {
            var conta = new Atendente
            {
                TenantId = tenantId,
                Nome = "[TESTE PROTECAO-DE-DADOS] Login",
                Email = email,
                SenhaHash = hash.Gerar("SenhaDeTeste123"),
                Perfil = PerfilDeAtendente.Atendente,
                Cargo = CargoNaServentia.NaoInformado,
                Ativo = true
            };
            db.Atendentes.Add(conta);
            await db.SaveChangesAsync();
            contaDeLoginId = conta.Id;
            atendenteIds.Add(contaDeLoginId);
        }

        await using (var db = new CrmDbContext(opcoes, cifraDeDados))
        {
            var resposta = await NovoAutenticacao(db, new ContextoFixo(tenantId, contaDeLoginId, "Login", PerfilDeAtendente.Atendente))
                .AutenticarAsync(new LoginRequest(email, "SenhaDeTeste123"));
            Checar("14. login sem MFA conclui e devolve token", !resposta.RequerMfa && resposta.Sessao is not null);
        }

        await using (var verificacao = new CrmDbContext(opcoes, cifraDeDados))
        {
            var registrado = await verificacao.EventosDeSeguranca.AnyAsync(
                e => e.AtendenteId == contaDeLoginId && e.Tipo == TipoDeEventoDeSeguranca.LoginRealizado);
            Checar("14b. EventoDeSeguranca(LoginRealizado) gravado para a conta certa", registrado);
        }
    }

    // ------------------------------------------------------------ cenario 15

    {
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        var codigo = await CodigoDaFalhaAsync(() =>
            NovoSegurancaService(db, semPermissao).ListarAcessosAsync(new FiltroDeAcessos(null, null, null, null)));
        Checar("15. SegurancaService.ListarAcessosAsync sem seguranca.eventos.ver -> sem_permissao", codigo == "sem_permissao");
    }

    // ------------------------------------------------------------ cenario 16

    {
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        var de = DateTime.UtcNow.AddMinutes(-15);
        var ate = DateTime.UtcNow.AddMinutes(5);

        var pagina = await NovoSegurancaService(db, comSegurancaVer)
            .ListarAcessosAsync(new FiltroDeAcessos(de, ate, null, conversaId));

        var linhaDeLeitura = pagina.Itens.FirstOrDefault(i => i.Tipo == TipoDeAcesso.LeituraDeConversa);
        Checar("16. ListarAcessosAsync com permissao devolve os acessos do periodo", pagina.Total > 0);
        Checar("16b. nome do atendente resolvido na projecao", linhaDeLeitura?.AtendenteNome == "[TESTE PROTECAO-DE-DADOS] Leitor");
        Checar("16c. protocolo da conversa resolvido na projecao", linhaDeLeitura?.ConversaProtocolo != null);
    }

    // ------------------------------------------------------------ cenario 17

    {
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        var de = DateTime.UtcNow.AddMinutes(-15);
        var ate = DateTime.UtcNow.AddMinutes(5);

        var pagina = await NovoSegurancaService(db, comSegurancaVer).ListarEventosAsync(new FiltroDeEventos(de, ate));
        Checar("17. ListarEventosAsync inclui o LoginRealizado do cenario 14",
            pagina.Itens.Any(e => e.Tipo == TipoDeEventoDeSeguranca.LoginRealizado));
    }

    // ------------------------------------------------------------ cenario 18

    {
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        var protecao = await NovoSegurancaService(db, comSegurancaVer).ObterProtecaoAtivaAsync();
        Checar("18. ObterProtecaoAtivaAsync honesto: cifra ainda nao ativa neste binario", !protecao.CifraAtiva);
        Checar("18b. contagem de registros de acesso reflete o que foi gravado no teste", protecao.TotalDeRegistrosDeAcesso > 0);
    }

    // =========================================================================
    // PR4 — acesso tecnico autorizado (cenarios 19..33)
    // =========================================================================

    Guid contaTecnicaId;
    Guid aprovadorId;
    string protocoloDaConversa;

    await using (var db = new CrmDbContext(opcoes, cifraDeDados))
    {
        var contaTecnica = new Atendente
        {
            TenantId = tenantId,
            Nome = "[TESTE PROTECAO-DE-DADOS] Conta Tecnica",
            Email = $"teste-conta-tecnica-{Guid.NewGuid():N}@teste.local",
            SenhaHash = hash.Gerar("SenhaDeTeste123"),
            Perfil = PerfilDeAtendente.Atendente,
            Cargo = CargoNaServentia.NaoInformado,
            Ativo = true,
            EhContaTecnica = true,
            Permissoes = CatalogoDePermissoes.Serializar([PermissaoDoSistema.SuporteSolicitarAcesso])
        };
        db.Atendentes.Add(contaTecnica);

        var aprovador = new Atendente
        {
            TenantId = tenantId,
            Nome = "[TESTE PROTECAO-DE-DADOS] Aprovador",
            Email = $"teste-aprovador-{Guid.NewGuid():N}@teste.local",
            SenhaHash = hash.Gerar("SenhaDeTeste123"),
            Perfil = PerfilDeAtendente.Administrador,
            Cargo = CargoNaServentia.NaoInformado,
            Ativo = true
        };
        db.Atendentes.Add(aprovador);

        await db.SaveChangesAsync();
        contaTecnicaId = contaTecnica.Id;
        aprovadorId = aprovador.Id;
        atendenteIds.Add(contaTecnicaId);
        atendenteIds.Add(aprovadorId);

        protocoloDaConversa = await db.Conversas.Where(c => c.Id == conversaId).Select(c => c.Protocolo).FirstAsync();
    }

    var contaTecnicaSemAutorizacao = new ContextoFixo(tenantId, contaTecnicaId, "Tecnico", PerfilDeAtendente.Atendente,
        permissoes: new HashSet<PermissaoDoSistema> { PermissaoDoSistema.SuporteSolicitarAcesso });

    var aprovadorCtx = new ContextoFixo(tenantId, aprovadorId, "Aprovador", PerfilDeAtendente.Administrador);

    // ------------------------------------------------------------ cenario 19

    {
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        var codigo = await CodigoDaFalhaAsync(() => NovaConversaService(db, contaTecnicaSemAutorizacao).ObterAsync(conversaId));
        Checar("19. conta tecnica SEM autorizacao ativa -> sem_permissao ao tentar ler conversa", codigo == "sem_permissao");
    }

    // ------------------------------------------------------------ cenario 20

    {
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        var codigo = await CodigoDaFalhaAsync(() => NovoAcessoTecnicoService(db, aprovadorCtx)
            .SolicitarAsync(new SolicitarAcessoTecnicoRequest("teste", EscopoDeAcesso.Geral, null, 60)));
        Checar("20. SolicitarAsync sem suporte.solicitar_acesso -> sem_permissao", codigo == "sem_permissao");
    }

    // ------------------------------------------------------------ cenario 21

    {
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        var codigo = await CodigoDaFalhaAsync(() => NovoAcessoTecnicoService(db, contaTecnicaSemAutorizacao)
            .SolicitarAsync(new SolicitarAcessoTecnicoRequest("teste", EscopoDeAcesso.Conversa, null, 60)));
        Checar("21. Escopo=Conversa sem protocolo -> escopo_de_conversa_exige_conversa_id", codigo == "escopo_de_conversa_exige_conversa_id");
    }

    // ------------------------------------------------------------ cenario 22

    {
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        var codigo = await CodigoDaFalhaAsync(() => NovoAcessoTecnicoService(db, contaTecnicaSemAutorizacao)
            .SolicitarAsync(new SolicitarAcessoTecnicoRequest("teste", EscopoDeAcesso.Conversa, "PROTOCOLO-INEXISTENTE", 60)));
        Checar("22. protocolo inexistente -> conversa_nao_encontrada", codigo == "conversa_nao_encontrada");
    }

    // ------------------------------------------------------------ cenario 23

    Guid autorizacaoId;
    {
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        var criada = await NovoAcessoTecnicoService(db, contaTecnicaSemAutorizacao)
            .SolicitarAsync(new SolicitarAcessoTecnicoRequest("investigar duvida do cliente", EscopoDeAcesso.Conversa, protocoloDaConversa, 60));
        autorizacaoId = criada.Id;
        Checar("23. SolicitarAsync valido cria Pendente", criada.Estado == EstadoDeAutorizacao.Pendente);
        Checar("23b. ConversaProtocolo resolvido na resposta", criada.ConversaProtocolo == protocoloDaConversa);
    }

    // ------------------------------------------------------------ cenario 24

    {
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        var codigo = await CodigoDaFalhaAsync(() => NovoAcessoTecnicoService(db, contaTecnicaSemAutorizacao)
            .SolicitarAsync(new SolicitarAcessoTecnicoRequest("outro pedido", EscopoDeAcesso.Geral, null, 30)));
        Checar("24. pedido novo com um Pendente em aberto -> ja_existe_solicitacao_em_aberto", codigo == "ja_existe_solicitacao_em_aberto");
    }

    // ------------------------------------------------------------ cenario 25

    {
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        var codigo = await CodigoDaFalhaAsync(() => NovoAcessoTecnicoService(db, contaTecnicaSemAutorizacao)
            .AprovarAsync(autorizacaoId));
        Checar("25. AprovarAsync sem seguranca.acesso_tecnico.aprovar -> sem_permissao", codigo == "sem_permissao");
    }

    // ------------------------------------------------------------ cenario 26
    //
    // Decisao do Thiago em 2026-09-12: AprovarAsync deixou de exigir TOTP do
    // aprovador (removido do plano original de PR4). O aprovador desta
    // secao (Administrador comum, SEM MFA matriculado) prova exatamente
    // isso — aprova mesmo sem nenhum segundo fator configurado, porque a
    // sessao ja autenticada + a permissao seguranca.acesso_tecnico.aprovar
    // sao a trava agora.

    AutorizacaoDeAcessoTecnicoResponse aprovada;
    {
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        aprovada = await NovoAcessoTecnicoService(db, aprovadorCtx).AprovarAsync(autorizacaoId);
        Checar("26. aprovacao NAO exige MFA do aprovador (decisao de 2026-09-12) -> Ativa mesmo sem autenticador", aprovada.Estado == EstadoDeAutorizacao.Ativa);
        Checar("26b. ValidaAte dentro do teto de 8h", aprovada.ValidaAte is { } v && v <= DateTime.UtcNow.AddHours(8).AddMinutes(1));
    }

    // ------------------------------------------------------------ cenario 29

    ContextoFixo contaTecnicaComAutorizacao;
    {
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        var sessao = await new ValidadorDeSessao(db).CarregarSessaoAsync(contaTecnicaId, tenantId, 1);
        Checar("29. ValidadorDeSessao acrescenta AtendimentoVer apos aprovacao", sessao is not null && sessao.Permissoes.Contains(PermissaoDoSistema.AtendimentoVer));
        Checar("29b. Escopo=Conversa NAO acrescenta ClientesVer", sessao is not null && !sessao.Permissoes.Contains(PermissaoDoSistema.ClientesVer));
        Checar("29c. EscopoDeConversaId aponta para a conversa aprovada", sessao?.EscopoDeConversaId == conversaId);

        contaTecnicaComAutorizacao = new ContextoFixo(
            tenantId, contaTecnicaId, "Tecnico", PerfilDeAtendente.Atendente,
            permissoes: sessao!.Permissoes,
            autorizacaoDeAcessoTecnicoId: sessao.AutorizacaoDeAcessoTecnicoId,
            escopoDeConversaId: sessao.EscopoDeConversaId);
    }

    // ------------------------------------------------------------ cenario 30

    {
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        var detalhe = await NovaConversaService(db, contaTecnicaComAutorizacao).ObterAsync(conversaId);
        Checar("30. com autorizacao ativa, le a conversa aprovada", detalhe.Conversa.Id == conversaId);

        await using var verificacao = new CrmDbContext(opcoes, cifraDeDados);
        var carimbado = await verificacao.RegistrosDeAcesso.AnyAsync(
            r => r.ConversaId == conversaId && r.AutorizacaoDeAcessoTecnicoId == autorizacaoId);
        Checar("30b. RegistroDeAcesso carimbado com a AutorizacaoDeAcessoTecnicoId", carimbado);
    }

    // ------------------------------------------------------------ cenario 31

    {
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        var codigo = await CodigoDaFalhaAsync(() => NovaConversaService(db, contaTecnicaComAutorizacao).ObterAsync(conversaIds[1]));
        Checar("31. escopo de UMA conversa nega leitura de OUTRA conversa", codigo == "sem_permissao");
    }

    // ------------------------------------------------------------ cenario 32

    {
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        var revogada = await NovoAcessoTecnicoService(db, aprovadorCtx).RevogarAsync(autorizacaoId);
        Checar("32. RevogarAsync (sem TOTP) muda para Revogada", revogada.Estado == EstadoDeAutorizacao.Revogada);

        await using var verificacaoSessao = new CrmDbContext(opcoes, cifraDeDados);
        var sessaoAposRevogar = await new ValidadorDeSessao(verificacaoSessao).CarregarSessaoAsync(contaTecnicaId, tenantId, 1);
        Checar("32b. apos revogar, a PROXIMA resolucao de sessao (ValidadorDeSessao) NAO tem mais AtendimentoVer",
            sessaoAposRevogar is not null && !sessaoAposRevogar.Permissoes.Contains(PermissaoDoSistema.AtendimentoVer));
    }

    // ------------------------------------------------------------ cenario 33

    {
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        var pedidoParaNegar = await NovoAcessoTecnicoService(db, contaTecnicaSemAutorizacao)
            .SolicitarAsync(new SolicitarAcessoTecnicoRequest("outro pedido apos revogacao", EscopoDeAcesso.Geral, null, 30));

        var negado = await NovoAcessoTecnicoService(db, aprovadorCtx).NegarAsync(pedidoParaNegar.Id);
        Checar("33. NegarAsync muda para Negada", negado.Estado == EstadoDeAutorizacao.Negada);

        var codigo = await CodigoDaFalhaAsync(() => NovoAcessoTecnicoService(db, aprovadorCtx)
            .AprovarAsync(pedidoParaNegar.Id));
        Checar("33b. nao da para aprovar um pedido ja Negado -> autorizacao_nao_esta_pendente", codigo == "autorizacao_nao_esta_pendente");
    }

    // =====================================================================
    // PR3 — cifra em repouso (cenarios 34+)
    // =====================================================================
    // Os cenarios 1-33 acima usam `cifraDeDados` (DORMENTE) o tempo todo —
    // provam que nada muda de comportamento para quem ainda nao tem chave
    // configurada. Daqui pra frente, `cifraAtiva`/`provedorDeChaveAtiva`
    // (chave fixa de desenvolvimento) provam a cifra de verdade: round-trip,
    // indice cego, ExigirCifrado e o comando --cifrar-dados-legados.

    var comCadastrarEVerClientes = new ContextoFixo(tenantId, leitorId, "Leitor", PerfilDeAtendente.Atendente,
        permissoes: new HashSet<PermissaoDoSistema> { PermissaoDoSistema.ClientesCadastrar, PermissaoDoSistema.ClientesVer });

    // ------------------------------------------------------------ cenario 34

    {
        const string rotulo = "Teste.Rotulo";
        const string claro = "um texto qualquer com acento e ç, so um exemplo";

        var cifrado = cifraAtiva.Cifrar(claro, rotulo);
        Checar("34. Cifrar (cifra ativa) produz o prefixo $c1$", cifrado!.StartsWith("$c1$"));
        Checar("34b. Decifrar devolve o texto original", cifraAtiva.Decifrar(cifrado, rotulo) == claro);
        Checar("34c. Com a cifra DORMENTE, Cifrar devolve o mesmo texto (identidade)", cifraDeDados.Cifrar(claro, rotulo) == claro);
        Checar("34d. Com a cifra DORMENTE, Decifrar de um valor sem prefixo devolve o mesmo texto", cifraDeDados.Decifrar(claro, rotulo) == claro);
    }

    // ------------------------------------------------------------ cenario 35/36 — Cliente cifrado, round-trip

    var documentoDoClienteCifrado = $"{Random.Shared.NextInt64(10000000000, 99999999999)}";
    Guid clienteCifradoId;
    {
        await using var db = new CrmDbContext(opcoes, cifraAtiva);
        var criado = await NovoClienteServiceComCifraAtiva(db, comCadastrarEVerClientes).CriarAsync(
            new DadosDeCliente(
                Nome: "[TESTE PROTECAO-DE-DADOS] Cifrado",
                TelefoneWhatsapp: $"55389{Random.Shared.Next(1000000, 9999999)}",
                Documento: documentoDoClienteCifrado,
                DataNascimento: new DateOnly(1990, 5, 20)));
        clienteCifradoId = criado.Id;
        clienteIds.Add(clienteCifradoId);

        Checar("35. ClienteService.ObterAsync (cifra ativa) devolve o Documento em claro", criado.Documento == documentoDoClienteCifrado);
        Checar("35b. ... e a DataNascimento em claro", criado.DataNascimento == new DateOnly(1990, 5, 20));
    }

    {
        await using var db = new CrmDbContext(opcoes, cifraAtiva);
        var documentoCru = await db.Database.SqlQuery<string>(
            $"SELECT Documento AS Value FROM dbo.Clientes WHERE Id = {clienteCifradoId}").SingleAsync();
        var hashGravado = await db.Database.SqlQuery<string?>(
            $"SELECT DocumentoHash AS Value FROM dbo.Clientes WHERE Id = {clienteCifradoId}").SingleAsync();
        var dataCrua = await db.Database.SqlQuery<string?>(
            $"SELECT DataNascimentoCifrada AS Value FROM dbo.Clientes WHERE Id = {clienteCifradoId}").SingleAsync();

        Checar("36. Documento gravado CRU comeca com $c1$ (nunca em claro no banco)", documentoCru.StartsWith("$c1$"));
        Checar("36b. DataNascimentoCifrada gravada CRUA comeca com $c1$", dataCrua is not null && dataCrua.StartsWith("$c1$"));
        Checar("36c. DocumentoHash foi calculado", !string.IsNullOrEmpty(hashGravado));
        Checar("36d. DocumentoHash bate com IndiceCego.Calcular chamado direto",
            hashGravado == indiceCegoAtivo.Calcular(tenantId, documentoDoClienteCifrado));
    }

    // ------------------------------------------------------------ cenario 37 — busca exata via indice cego

    {
        await using var db = new CrmDbContext(opcoes, cifraAtiva);
        var servico = NovoClienteServiceComCifraAtiva(db, comCadastrarEVerClientes);

        var pagina = await servico.ListarAsync(new FiltroDeClientes { Busca = documentoDoClienteCifrado });
        Checar("37. Busca por CPF COMPLETO acha o cliente via indice cego (cifra ativa)",
            pagina.Itens.Any(c => c.Id == clienteCifradoId));

        var paginaParcial = await servico.ListarAsync(new FiltroDeClientes { Busca = documentoDoClienteCifrado[..5] });
        Checar("37b. Busca por CPF PARCIAL nao acha o cliente (limite conhecido da cifra ativa)",
            !paginaParcial.Itens.Any(c => c.Id == clienteCifradoId));
    }

    // ------------------------------------------------------------ cenario 38 — ExigirCifrado fail-closed

    var documentoDoClienteLegado = $"{Random.Shared.NextInt64(10000000000, 99999999999)}";
    Guid clienteLegadoId;
    {
        // Criado com a cifra DORMENTE — Documento fica em claro no banco,
        // exatamente o estado "legado" que --cifrar-dados-legados resolve.
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        var criado = await NovoClienteService(db, comCadastrarEVerClientes).CriarAsync(
            new DadosDeCliente(
                Nome: "[TESTE PROTECAO-DE-DADOS] Legado",
                TelefoneWhatsapp: $"55389{Random.Shared.Next(1000000, 9999999)}",
                Documento: documentoDoClienteLegado));
        clienteLegadoId = criado.Id;
        clienteIds.Add(clienteLegadoId);
    }

    {
        var (_, cifraExigente) = FabricaDeCifraDeDados.CriarParComChaveDeDesenvolvimento(exigirCifrado: true);
        await using var db = new CrmDbContext(opcoes, cifraExigente);

        Exception? falha = null;
        try
        {
            await db.Clientes.AsNoTracking().FirstAsync(c => c.Id == clienteLegadoId);
        }
        catch (Exception ex)
        {
            falha = ex;
        }

        Checar("38. Ler linha legada em claro com ExigirCifrado ligado lanca DadoNaoCifradoException",
            falha is DadoNaoCifradoException);
    }

    // ------------------------------------------------------------ cenario 39 — --cifrar-dados-legados de ponta a ponta

    Guid conversaLegadaId;
    {
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        var conversaLegada = new Conversa
        {
            TenantId = tenantId,
            ClienteId = clienteLegadoId,
            Protocolo = $"TESTE-{Guid.NewGuid():N}"[..20]
        };
        db.Conversas.Add(conversaLegada);
        await db.SaveChangesAsync();
        conversaLegadaId = conversaLegada.Id;
        conversaIds.Add(conversaLegadaId);

        db.Mensagens.Add(new Mensagem
        {
            TenantId = tenantId,
            ConversaId = conversaLegadaId,
            AutorTipo = AutorTipo.Cliente,
            Conteudo = "conteudo legado em claro, gravado antes da cifra existir"
        });
        await db.SaveChangesAsync();
    }

    var configuracaoDeDesenvolvimento = FabricaDeCifraDeDados.CriarConfiguracaoDeDesenvolvimento(conexao);

    var saidaDaFerramenta1 = await FerramentaDeCifraDeDadosLegados.ExecutarAsync(configuracaoDeDesenvolvimento);
    Checar("39. --cifrar-dados-legados roda contra o banco de dev e devolve sucesso", saidaDaFerramenta1 == 0);

    string documentoLegadoAposFerramenta;
    string conteudoLegadoAposFerramenta;
    string? hashLegadoAposFerramenta;
    {
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        documentoLegadoAposFerramenta = await db.Database.SqlQuery<string>(
            $"SELECT Documento AS Value FROM dbo.Clientes WHERE Id = {clienteLegadoId}").SingleAsync();
        hashLegadoAposFerramenta = await db.Database.SqlQuery<string?>(
            $"SELECT DocumentoHash AS Value FROM dbo.Clientes WHERE Id = {clienteLegadoId}").SingleAsync();
        conteudoLegadoAposFerramenta = await db.Database.SqlQuery<string>(
            $"SELECT TOP (1) Conteudo AS Value FROM dbo.Mensagens WHERE ConversaId = {conversaLegadaId}").SingleAsync();
    }

    Checar("39b. Documento do cliente legado virou ciphertext ($c1$)", documentoLegadoAposFerramenta.StartsWith("$c1$"));
    Checar("39c. DocumentoHash do cliente legado foi calculado pela ferramenta", !string.IsNullOrEmpty(hashLegadoAposFerramenta));
    Checar("39d. Conteudo da mensagem legada virou ciphertext ($c1$)", conteudoLegadoAposFerramenta.StartsWith("$c1$"));

    var saidaDaFerramenta2 = await FerramentaDeCifraDeDadosLegados.ExecutarAsync(configuracaoDeDesenvolvimento);
    Checar("39e. segunda execucao tambem devolve sucesso", saidaDaFerramenta2 == 0);

    {
        await using var db = new CrmDbContext(opcoes, cifraDeDados);
        var documentoAposSegunda = await db.Database.SqlQuery<string>(
            $"SELECT Documento AS Value FROM dbo.Clientes WHERE Id = {clienteLegadoId}").SingleAsync();
        Checar("39f. segunda execucao NAO re-cifra (mesmo ciphertext — nenhuma linha era mais candidata)",
            documentoAposSegunda == documentoLegadoAposFerramenta);
    }

    // ------------------------------------------------------------ cenario 40 — anexo cifrado em disco

    {
        var pastaDeAnexos = Path.Combine(Path.GetTempPath(), $"crm-teste-anexos-{Guid.NewGuid():N}");
        try
        {
            var armazenamento = new ArmazenamentoDeAnexosEmDisco(
                Options.Create(new OpcoesDeAnexos { Raiz = pastaDeAnexos }),
                new AmbienteFalso(),
                cifraAtiva,
                provedorDeChaveAtiva,
                NullLogger<ArmazenamentoDeAnexosEmDisco>.Instance);

            var tenantDoAnexo = Guid.NewGuid();
            var mensagemDoAnexo = Guid.NewGuid();
            var conteudoOriginal = "conteudo binario de teste, so para o round-trip"u8.ToArray();

            var gravado = await armazenamento.GravarAsync(
                tenantDoAnexo, mensagemDoAnexo, DateTime.UtcNow, conteudoOriginal, "foto.jpg", "image/jpeg");

            var caminhoAbsoluto = Path.Combine(pastaDeAnexos, gravado.CaminhoRelativo.Replace('/', Path.DirectorySeparatorChar));
            var bytesNoDisco = await File.ReadAllBytesAsync(caminhoAbsoluto);
            Checar("40. Anexo gravado no disco comeca com a marca CRMA (cifra ativa)",
                bytesNoDisco.Length > 4 && System.Text.Encoding.ASCII.GetString(bytesNoDisco, 0, 4) == "CRMA");

            var lido = await armazenamento.LerAsync(gravado.CaminhoRelativo);
            Checar("40b. LerAsync devolve os bytes originais (round-trip)",
                lido is not null && lido.SequenceEqual(conteudoOriginal));

            // Copia o MESMO arquivo cifrado para o caminho de OUTRA mensagem —
            // o AAD derivado do caminho muda, a tag deixa de bater.
            var extensao = Path.GetExtension(gravado.CaminhoRelativo);
            var caminhoDeOutraMensagem = gravado.CaminhoRelativo[..^extensao.Length].Replace(
                mensagemDoAnexo.ToString("N"), Guid.NewGuid().ToString("N")) + extensao;
            var destinoAbsoluto = Path.Combine(pastaDeAnexos, caminhoDeOutraMensagem.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destinoAbsoluto)!);
            File.Copy(caminhoAbsoluto, destinoAbsoluto);

            Exception? falhaDeAad = null;
            try
            {
                await armazenamento.LerAsync(caminhoDeOutraMensagem);
            }
            catch (Exception ex)
            {
                falhaDeAad = ex;
            }

            Checar("40c. Ler o mesmo arquivo sob o caminho de OUTRA mensagem falha (AAD nao bate)",
                falhaDeAad is CryptographicException);
        }
        finally
        {
            if (Directory.Exists(pastaDeAnexos))
            {
                Directory.Delete(pastaDeAnexos, recursive: true);
            }
        }
    }

    // ------------------------------------------------------------ cenario 41 — ProtecaoAtivaResponse com cifra ativa

    {
        await using var db = new CrmDbContext(opcoes, cifraAtiva);
        var protecao = await NovoSegurancaServiceComCifraAtiva(db, comSegurancaVer).ObterProtecaoAtivaAsync();
        Checar("41. ObterProtecaoAtivaAsync com cifra ativa reporta CifraAtiva=true", protecao.CifraAtiva);
        Checar("41b. ... e a VersaoDaChave corrente", protecao.VersaoDaChave == provedorDeChaveAtiva.VersaoCorrente);
        Checar("41c. CifraDesde preenchido (--cifrar-dados-legados ja rodou no cenario 39)", protecao.CifraDesde is not null);
    }
}
finally
{
    await using var db = new CrmDbContext(opcoes, cifraDeDados);

    // O gatilho bloqueia DELETE convencional — desabilita so para a limpeza
    // deste verificador, mesma disciplina admitida no comentario do proprio
    // gatilho (sql/045): contorna o acidente e o atalho, nao o administrador
    // deliberado, e aqui a limpeza E deliberada.
    await db.Database.ExecuteSqlRawAsync(
        "DISABLE TRIGGER TR_RegistrosDeAcesso_SomenteInsercao ON dbo.RegistrosDeAcesso");
    // PR2 (conversa/cliente) + PR4 (ciclo de vida do acesso tecnico, que carimba
    // so AtendenteId + AutorizacaoDeAcessoTecnicoId, nunca ConversaId/ClienteId)
    // + PR3 (CifraDeLegadoConcluida do cenario 39, que --cifrar-dados-legados
    // grava por TENANT — sem ConversaId/ClienteId/AtendenteId nenhum — e por
    // isso precisa do proprio braco, restrito ao tenant deste verificador).
    var registrosRemovidos = await db.RegistrosDeAcesso
        .Where(r => (r.ConversaId != null && conversaIds.Contains(r.ConversaId.Value))
                 || (r.ClienteId != null && clienteIds.Contains(r.ClienteId.Value))
                 || (r.AtendenteId != null && atendenteIds.Contains(r.AtendenteId.Value))
                 || (r.Tipo == TipoDeAcesso.CifraDeLegadoConcluida && r.TenantId == tenantId))
        .ExecuteDeleteAsync();
    await db.Database.ExecuteSqlRawAsync(
        "ENABLE TRIGGER TR_RegistrosDeAcesso_SomenteInsercao ON dbo.RegistrosDeAcesso");

    // PR4: FK para Atendentes/Conversas — sai antes dos dois.
    await db.AutorizacoesDeAcessoTecnico.Where(a => atendenteIds.Contains(a.ContaTecnicaId)).ExecuteDeleteAsync();

    // SEC-03: o aprovador matriculou MFA de verdade neste verificador — as
    // tres tabelas com FK Restrict para Atendentes saem antes dele, mesma
    // disciplina de verificar-autenticacao.cs.
    await db.DesafiosDeMfa.Where(d => atendenteIds.Contains(d.AtendenteId)).ExecuteDeleteAsync();
    await db.CodigosDeRecuperacaoMfa.Where(c => atendenteIds.Contains(c.AtendenteId)).ExecuteDeleteAsync();
    await db.DispositivosConfiaveis.Where(d => atendenteIds.Contains(d.AtendenteId)).ExecuteDeleteAsync();

    await db.EventosDeSeguranca.Where(e => atendenteIds.Contains(e.AtendenteId)).ExecuteDeleteAsync();
    await db.Mensagens.Where(m => conversaIds.Contains(m.ConversaId)).ExecuteDeleteAsync();
    var conversasRemovidas = await db.Conversas.Where(c => conversaIds.Contains(c.Id)).ExecuteDeleteAsync();
    var clientesRemovidos = await db.Clientes.Where(c => clienteIds.Contains(c.Id)).ExecuteDeleteAsync();
    var atendentesRemovidos = await db.Atendentes.Where(a => atendenteIds.Contains(a.Id)).ExecuteDeleteAsync();

    Console.WriteLine();
    Console.WriteLine(
        $"Limpeza: {registrosRemovidos} registro(s) de acesso, {conversasRemovidas} conversa(s), " +
        $"{clientesRemovidos} cliente(s), {atendentesRemovidos} atendente(s) removidos.");
}

Console.WriteLine();
Console.WriteLine(falhas == 0 ? "TODOS OS TESTES PASSARAM." : $"{falhas} TESTE(S) FALHARAM.");
return falhas == 0 ? 0 : 1;

// =============================================================================
// Doubles — cada verificador deste projeto define os proprios (file-scoped),
// mesma disciplina de verificar-automacao.cs/verificar-concorrencia.cs.
// =============================================================================

file sealed class ContextoFixo(
    Guid tenantId,
    Guid atendenteId,
    string nome,
    PerfilDeAtendente perfil,
    IReadOnlySet<PermissaoDoSistema>? permissoes = null,
    Guid? autorizacaoDeAcessoTecnicoId = null,
    Guid? escopoDeConversaId = null)
    : IContextoDoUsuario
{
    public bool Autenticado => true;
    public Guid TenantId => tenantId;
    public Guid AtendenteId => atendenteId;
    public string Nome => nome;
    public PerfilDeAtendente Perfil => perfil;

    public bool Pode(PermissaoDoSistema permissao) =>
        (permissoes ?? CatalogoDePermissoes.ConjuntoDaClasse(perfil)).Contains(permissao);

    public Guid? AutorizacaoDeAcessoTecnicoId => autorizacaoDeAcessoTecnicoId;
    public Guid? EscopoDeConversaId => escopoDeConversaId;
}

file sealed class NavegadorFalso : ICookieDeDispositivoConfiavel
{
    public string? Ler() => null;
    public void Definir(string tokenEmClaro, DateTime expiraEm) { }
    public void Limpar() { }
}

file sealed class CanalSilencioso : IEnviadorDeCanal
{
    public Task<string?> EnviarAsync(Guid m, CancellationToken ct = default) => Task.FromResult<string?>(null);
    public Task EnviarPendentesAsync(Guid c, CancellationToken ct = default) => Task.CompletedTask;
}

file sealed class FilaSilenciosa : IFilaDeEnvioDoCanal
{
    public void Agendar(Guid conversaId) { }
}

file sealed class AnexosFixos : IArmazenamentoDeAnexos
{
    public long TamanhoMaximoBytes => 16 * 1024 * 1024;
    public bool TipoPermitido(string? tipoMime) => true;

    public Task<AnexoGravado> GravarAsync(
        Guid tenantId, Guid mensagemId, DateTime quando, byte[] conteudo,
        string? nomeOriginal, string? tipoMime, CancellationToken ct = default) =>
        throw new NotImplementedException("Este verificador nao exercita o caminho de GRAVAR anexo.");

    /// <summary>Sempre devolve bytes fixos, ignorando o caminho — o teste so precisa provar que ObterAnexoAsync le e registra o acesso.</summary>
    public Task<byte[]?> LerAsync(string caminhoRelativo, CancellationToken ct = default) =>
        Task.FromResult<byte[]?>([1, 2, 3]);
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

/// <summary>Nunca deveria ser chamado — OpcoesDaIA aqui e sempre Habilitada=false.</summary>
file sealed class OrquestradorNuncaChamado : IOrquestradorDeProvedores
{
    public Task<RespostaDaIA> ResponderAsync(Guid tenantId, ContextoDaChamada? contexto, PedidoDeIa pedido, CancellationToken ct) =>
        throw new InvalidOperationException("OrquestradorDeProvedores nao deveria ser chamado com RespostaInteligente:Habilitada=false.");
}

file sealed class EvolutionClientNuncaChamado : IEvolutionClient
{
    private static Exception Erro() => new NotImplementedException("Este verificador nao exercita o gateway do canal.");
    public Task<EstadoDaInstancia> GarantirInstanciaAsync(string urlDoWebhook, CancellationToken ct) => throw Erro();
    public Task<EstadoDaInstancia> ObterEstadoAsync(CancellationToken ct) => throw Erro();
    public Task<EstadoDaInstancia> IniciarConexaoAsync(CancellationToken ct) => throw Erro();
    public Task DesconectarAsync(CancellationToken ct) => throw Erro();
    public Task<PerfilDaInstancia?> ObterPerfilDaInstanciaAsync(CancellationToken ct) => throw Erro();
    public Task<string> EnviarTextoAsync(string telefoneE164, string conteudo, CancellationToken ct) => throw Erro();
    public Task<string> EnviarMidiaAsync(string telefoneE164, byte[] conteudo, string tipoMime, string nomeDoArquivo, string? legenda, CancellationToken ct) => throw Erro();
    public Task<byte[]?> BaixarMidiaAsync(string idExternoWhatsapp, CancellationToken ct) => throw Erro();
    public Task<string?> ObterFotoPerfilUrlAsync(string telefoneE164, CancellationToken ct) => throw Erro();
    public Task<IReadOnlyList<ContatoDoWhatsApp>> ObterContatosAsync(CancellationToken ct) => throw Erro();
}

file sealed class HttpClientFactoryFake : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new();
}

/// <summary>Blindagem LGPD: prova o cenario 13 (fail-closed) — falhar ao gravar o RegistroDeAcesso derruba a leitura inteira.</summary>
file sealed class InterceptorDeFalhaDeAcesso : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        var contexto = eventData.Context;
        if (contexto is not null)
        {
            var temRegistroNovo = contexto.ChangeTracker.Entries<CrmCartorio.Api.Modules.Seguranca.Domain.Entities.RegistroDeAcesso>()
                .Any(e => e.State == EntityState.Added);

            if (temRegistroNovo)
            {
                throw new InvalidOperationException("[FALHA INJETADA PELO TESTE] RegistroDeAcesso");
            }
        }

        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default) =>
        new(SavingChanges(eventData, result));
}

/// <summary>PR3 — so para ArmazenamentoDeAnexosEmDisco: Raiz das opcoes ja e absoluta (pasta temporaria do proprio verificador), entao ContentRootPath nunca e consultado de verdade.</summary>
file sealed class AmbienteFalso : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Teste";
    public string ApplicationName { get; set; } = "CrmCartorio.Api";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
