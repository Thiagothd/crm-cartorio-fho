using System.Data;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CrmCartorio.Api.Infrastructure.Security;

/// <summary>
/// Bootstrap deixou de ser elegivel entre a verificacao inicial (mostrada ao
/// operador) e o instante real da emissao do ticket — outro
/// Administrador/Titular pode ter concluido a propria matricula de MFA
/// enquanto o operador ainda digitava o motivo ou confirmava. Ver
/// <see cref="FerramentaDeBootstrapMfa.EmitirTicketAsync"/>.
/// </summary>
public sealed class BootstrapNaoElegivelException(ElegibilidadeDeBootstrap motivoAtual)
    : InvalidOperationException($"Bootstrap deixou de ser elegivel no instante da emissao: {motivoAtual}.")
{
    public ElegibilidadeDeBootstrap MotivoAtual { get; } = motivoAtual;
}

/// <summary>
/// SEC-03: por que o comando pode recusar emitir o ticket de bootstrap.
/// </summary>
/// <remarks>
/// Separado de <see cref="FerramentaDeBootstrapMfa.ExecutarAsync"/> (o loop
/// interativo) de proposito: <see cref="FerramentaDeBootstrapMfa.VerificarElegibilidadeAsync"/>
/// e a mesma logica, testavel diretamente contra SQL Server real, sem simular
/// stdin — e o que prova o auto-fechamento (revisao independente do PR #46)
/// sem depender de rodar o processo interativo inteiro.
/// </remarks>
public enum ElegibilidadeDeBootstrap
{
    Elegivel,
    ContaInexistente,
    ContaInativa,
    NaoPrivilegiada,
    MfaJaAtivo,
    JaExistePrivilegiadoMatriculado
}

/// <summary>
/// SEC-03 — bootstrap do PRIMEIRO privilegiado (Titular ou Administrador) de um
/// tenant. Emite o ticket fora de banda que a matricula privilegiada exige,
/// para o unico momento em que ainda nao existe ninguem matriculado capaz de
/// emitir um pela tela.
/// </summary>
/// <remarks>
/// <b>Comando de execucao unica no binario publicado</b> — nao um
/// <c>dotnet run</c> de script solto, porque o servidor so tem o Hosting
/// Bundle instalado (docs/DEPLOY.md), sem SDK. Rode com:
/// <c>dotnet CrmCartorio.Api.dll --emitir-ticket-de-bootstrap</c>.
///
/// <b>Regras que este comando impoe, todas deliberadas:</b>
/// <list type="bullet">
/// <item>SO interativo — recusa de cara se a entrada padrao estiver
/// redirecionada (pipe, arquivo, ou variavel de ambiente de CI como
/// <c>GITHUB_ACTIONS</c>). Isto nao e so validacao de forma: e a garantia
/// tecnica de que uma pessoa com acesso SSH ao servidor, no teclado, decidiu
/// emitir o ticket — nunca um pipeline automatizado.</item>
/// <item>O texto do ticket SO existe em <see cref="Console.WriteLine(string?)"/>
/// — nunca <c>ILogger</c> (que pode ir para arquivo, Event Log ou um coletor
/// central), nunca gravado em disco, nunca no banco (so o SHA-256 dele), nunca
/// em <c>appsettings</c>.</item>
/// <item><b>Fecha sozinho</b> depois do primeiro uso: se o tenant ja tiver
/// qualquer Titular/Administrador com <c>MfaAtivo = true</c>, este comando se
/// recusa a rodar — dali em diante, tickets saem pelo fluxo normal
/// (<c>IMfaService.EmitirTicketAsync</c>), que exige step-up de quem ja tem
/// MFA. Isto e o que fecha a corrida "quem sabe a senha primeiro registra o
/// MFA": um atacante remoto com a senha do Titular nao tem acesso SSH ao
/// servidor, entao nunca alcanca este caminho.</item>
/// </list>
/// </remarks>
public static class FerramentaDeBootstrapMfa
{
    private static readonly TimeSpan TtlDoTicket = TimeSpan.FromHours(24);

    /// <summary>
    /// Toda a regra de elegibilidade, inclusive o auto-fechamento — sem
    /// nenhuma interacao de console. E o metodo que tanto
    /// <see cref="ExecutarAsync"/> quanto o verificador chamam; nunca ha uma
    /// segunda copia desta logica.
    /// </summary>
    public static async Task<ElegibilidadeDeBootstrap> VerificarElegibilidadeAsync(
        CrmDbContext db, string email, CancellationToken ct = default)
    {
        var atendente = await db.Atendentes.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Email == email, ct);

        if (atendente is null) return ElegibilidadeDeBootstrap.ContaInexistente;
        if (!atendente.Ativo) return ElegibilidadeDeBootstrap.ContaInativa;

        var privilegiado = atendente.Perfil == PerfilDeAtendente.Administrador || atendente.Cargo == CargoNaServentia.Titular;
        if (!privilegiado) return ElegibilidadeDeBootstrap.NaoPrivilegiada;
        if (atendente.MfaAtivo) return ElegibilidadeDeBootstrap.MfaJaAtivo;

        // O AUTO-FECHAMENTO: se QUALQUER privilegiado do tenant ja tem MFA
        // ativo, o bootstrap deixou de ser necessario — e nao pode mais rodar.
        var jaExisteMatriculado = await db.Atendentes.AsNoTracking()
            .AnyAsync(a => a.TenantId == atendente.TenantId && a.Ativo && a.MfaAtivo
                        && (a.Perfil == PerfilDeAtendente.Administrador || a.Cargo == CargoNaServentia.Titular), ct);

        return jaExisteMatriculado
            ? ElegibilidadeDeBootstrap.JaExistePrivilegiadoMatriculado
            : ElegibilidadeDeBootstrap.Elegivel;
    }

    /// <summary>
    /// Nucleo atomico do bootstrap — reconfere a elegibilidade no instante
    /// exato da gravacao e SO ENTAO grava o desafio de ticket e o evento de
    /// auditoria.
    /// </summary>
    /// <remarks>
    /// CORRECAO (revisao independente do PR #46, bloqueador 9) —
    /// deliberadamente <c>private</c> agora, nao mais <c>public</c>. Receber
    /// so o HASH (nunca o texto claro) reduzia a superficie, mas nao
    /// eliminava o problema real: qualquer chamador ainda podia gerar o
    /// proprio <c>tokenEmClaro</c>, calcular o hash, chamar este metodo
    /// publicamente para registra-lo, e guardar o token consigo — o cenario
    /// 63c antigo fazia exatamente isso. A regra e "bootstrap exclusivamente
    /// manual/interativo", nao "so o texto claro precisa ficar escondido" —
    /// nenhum metodo publico pode gerar, registrar OU persistir artefato
    /// nenhum utilizavel como ticket de bootstrap, nem escolher o TTL dele
    /// (por isso o parametro <c>ttl</c> foi removido: usa sempre
    /// <see cref="TtlDoTicket"/>, decidido pelo codigo de producao). O UNICO
    /// caminho publico que continua mutavel e <see cref="ExecutarAsync"/>,
    /// protegido por todos os guards interativos. Testes de concorrencia
    /// chamam este metodo via reflexao a partir do verificador — a reflexao
    /// no teste nao reabre a superficie de producao (ver cenario 65).
    ///
    /// CORRECAO (bloqueador 6, preservada) — TOCTOU: o fluxo interativo
    /// mostra <see cref="VerificarElegibilidadeAsync"/> ao operador, depois
    /// espera motivo e confirmacao digitada (segundos a minutos de interacao
    /// humana) antes de chegar aqui. Nesse intervalo, outro
    /// Administrador/Titular do mesmo tenant pode concluir a propria
    /// matricula de MFA e o bootstrap deveria ter se auto-fechado. Dentro de
    /// uma transacao <c>SERIALIZABLE</c> (mesma tecnica de
    /// <c>MfaService.AtivarObrigatoriedadeAsync</c>), reconfere TUDO — de
    /// novo, do zero, contra o estado atual — e SO ENTAO grava.
    /// </remarks>
    private static async Task<ElegibilidadeDeBootstrap> TentarRegistrarTicketAsync(
        CrmDbContext db, string email, string hashDoTicket, string motivo, string atorExterno, CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var elegibilidadeNoInstante = await VerificarElegibilidadeAsync(db, email, ct);
        if (elegibilidadeNoInstante != ElegibilidadeDeBootstrap.Elegivel)
        {
            await tx.RollbackAsync(ct);
            return elegibilidadeNoInstante;
        }

        var atendente = await db.Atendentes.AsNoTracking().SingleAsync(a => a.Email == email, ct);

        db.DesafiosDeMfa.Add(new DesafioDeMfa
        {
            TenantId = atendente.TenantId,
            AtendenteId = atendente.Id,
            HashDoDesafio = hashDoTicket,
            Tipo = TipoDeDesafioMfa.Ticket,
            TokenVersionNoDesafio = atendente.TokenVersion,
            ExpiraEm = agora.Add(TtlDoTicket)
        });

        db.EventosDeSeguranca.Add(new EventoDeSeguranca
        {
            TenantId = atendente.TenantId,
            AtendenteId = atendente.Id,
            AtorAtendenteId = null,
            AtorExterno = atorExterno,
            Tipo = TipoDeEventoDeSeguranca.TicketDeBootstrapEmitido,
            OcorridoEm = agora,
            Motivo = motivo
        });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ElegibilidadeDeBootstrap.Elegivel;
    }

    /// <summary>
    /// O UNICO lugar de todo o assembly onde o texto claro de um ticket de
    /// bootstrap e gerado ou devolvido. Deliberadamente <c>private</c>: so
    /// <see cref="ExecutarAsync"/>, no mesmo arquivo, alcanca este metodo — e
    /// so depois de passar por todos os guards interativos (stdin nao
    /// redirecionado, sem CI/GITHUB_ACTIONS, confirmacao humana digitada
    /// "SIM").
    /// </summary>
    private static async Task<string> EmitirTicketAsync(
        CrmDbContext db, string email, string motivo, string atorExterno, CancellationToken ct = default)
    {
        var tokenEmClaro = SegredosAleatoriosMfa.NovoToken();
        var hashDoTicket = SegredosAleatoriosMfa.Sha256Hex(tokenEmClaro);

        var resultado = await TentarRegistrarTicketAsync(db, email, hashDoTicket, motivo, atorExterno, ct);

        if (resultado != ElegibilidadeDeBootstrap.Elegivel)
        {
            throw new BootstrapNaoElegivelException(resultado);
        }

        return tokenEmClaro;
    }

    /// <summary>
    /// SEC-03 (revisao independente do PR #46, bloqueador 16) — decisao PURA
    /// (sem efeito colateral algum) de quando o bootstrap deve recusar por
    /// ambiente nao-interativo. Extraida para ser testavel por reflexao SEM
    /// reabrir nenhuma superficie publica mutavel — continua `private`; a
    /// superficie publica da classe permanece exatamente
    /// {ExecutarAsync, VerificarElegibilidadeAsync}, sem novo membro.
    /// </summary>
    /// <remarks>
    /// CORRECAO: a versao anterior so recusava `Console.IsInputRedirected`
    /// (entrada), CI e GITHUB_ACTIONS — mas nunca conferia
    /// `Console.IsOutputRedirected` (saida). Isso deixava passar
    /// <c>dotnet CrmCartorio.Api.dll --emitir-ticket-de-bootstrap &gt; ticket.txt</c>:
    /// entrada continuava interativa (o operador digitava tudo normalmente),
    /// mas o TEXTO CLARO do ticket ia direto para um ARQUIVO — violando "so
    /// no terminal, nunca em arquivo". Agora qualquer um dos dois lados
    /// redirecionado recusa. stderr deliberadamente NAO e checado: nenhum
    /// segredo e escrito la.
    /// </remarks>
    private static bool AmbienteNaoInterativo() =>
        Console.IsInputRedirected
        || Console.IsOutputRedirected
        || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") is not null
        || Environment.GetEnvironmentVariable("CI") is not null;

    public static async Task<int> ExecutarAsync(IConfiguration configuracao)
    {
        Console.WriteLine("=== CRM-Cartorio — bootstrap do primeiro privilegiado de MFA (SEC-03) ===");
        Console.WriteLine();

        if (AmbienteNaoInterativo())
        {
            Console.WriteLine(
                "RECUSADO: este comando so roda de forma interativa, no teclado, via SSH. " +
                "Entrada OU SAIDA redirecionada (pipe, arquivo ou ambiente de CI) nunca e " +
                "aceita — e o que garante que uma pessoa, e nao um pipeline, decidiu emitir " +
                "este ticket, e que o texto claro so pode aparecer no terminal, nunca ser " +
                "redirecionado para um arquivo.");
            return 1;
        }

        var connectionString = configuracao.GetConnectionString("SqlServer");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.WriteLine("RECUSADO: ConnectionStrings:SqlServer nao configurada.");
            return 1;
        }

        var optionsBuilder = new DbContextOptionsBuilder<CrmDbContext>();
        ConfiguracaoDoBanco.Configurar(optionsBuilder, connectionString);
        await using var db = new CrmDbContext(optionsBuilder.Options, FabricaDeCifraDeDados.CriarDeConfiguracao(configuracao));

        if (!await db.Database.CanConnectAsync())
        {
            Console.WriteLine("RECUSADO: nao foi possivel conectar ao banco configurado.");
            return 1;
        }

        Console.Write("E-mail do atendente a matricular: ");
        var email = (Console.ReadLine() ?? string.Empty).Trim().ToLowerInvariant();

        var elegibilidade = await VerificarElegibilidadeAsync(db, email);

        switch (elegibilidade)
        {
            case ElegibilidadeDeBootstrap.ContaInexistente:
                Console.WriteLine($"RECUSADO: nenhum atendente com o e-mail '{email}'.");
                return 1;
            case ElegibilidadeDeBootstrap.ContaInativa:
                Console.WriteLine("RECUSADO: a conta esta inativa.");
                return 1;
            case ElegibilidadeDeBootstrap.NaoPrivilegiada:
                Console.WriteLine("RECUSADO: este fluxo e exclusivo para Titular ou Administrador.");
                return 1;
            case ElegibilidadeDeBootstrap.MfaJaAtivo:
                Console.WriteLine(
                    "RECUSADO: esta conta ja tem MFA ativo — use o ticket normal " +
                    "(IMfaService.EmitirTicketAsync), com step-up de uma conta ja matriculada.");
                return 1;
            case ElegibilidadeDeBootstrap.JaExistePrivilegiadoMatriculado:
                Console.WriteLine(
                    "RECUSADO: ja existe conta privilegiada matriculada neste tenant. " +
                    "O bootstrap so serve para o PRIMEIRO Titular/Administrador — " +
                    "a partir daqui, novos tickets saem do fluxo normal, com step-up de quem ja tem MFA.");
                return 1;
        }

        var atendente = await db.Atendentes.AsNoTracking().SingleAsync(a => a.Email == email);
        Console.WriteLine($"Conta encontrada: {atendente.Nome} <{atendente.Email}>.");
        Console.Write("Motivo (obrigatorio, registrado em EventosDeSeguranca): ");
        var motivo = (Console.ReadLine() ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(motivo))
        {
            Console.WriteLine("RECUSADO: motivo em branco.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("Confere: duas pessoas devem estar presentes para este procedimento —");
        Console.WriteLine("quem executa este comando (acesso SSH) e o proprio titular da conta,");
        Console.WriteLine("com identidade verificada fora de banda.");
        Console.Write($"Digite SIM para confirmar a emissao do ticket para {atendente.Email}: ");

        if (!string.Equals(Console.ReadLine()?.Trim(), "SIM", StringComparison.Ordinal))
        {
            Console.WriteLine("Cancelado.");
            return 1;
        }

        var atorExterno = $"{Environment.UserName}@{Environment.MachineName}";
        string tokenEmClaro;
        try
        {
            tokenEmClaro = await EmitirTicketAsync(db, email, motivo, atorExterno);
        }
        catch (BootstrapNaoElegivelException ex)
        {
            Console.WriteLine();
            Console.WriteLine(
                $"RECUSADO no instante da emissao: a elegibilidade mudou entre a verificacao " +
                $"inicial e agora ({ex.MotivoAtual}). Provavel causa: outro Administrador/Titular " +
                "concluiu a propria matricula de MFA enquanto este comando esperava confirmacao.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("=== TICKET (mostrado uma UNICA vez — copie agora) ===");
        Console.WriteLine(tokenEmClaro);
        Console.WriteLine("=== FIM DO TICKET ===");
        Console.WriteLine();
        Console.WriteLine("Entregue ao portador por canal fora de banda (voz, presencial) — nunca por e-mail, chat ou SMS.");
        Console.WriteLine($"Registrado em EventosDeSeguranca como TicketDeBootstrapEmitido, ator externo '{atorExterno}'.");

        return 0;
    }
}
