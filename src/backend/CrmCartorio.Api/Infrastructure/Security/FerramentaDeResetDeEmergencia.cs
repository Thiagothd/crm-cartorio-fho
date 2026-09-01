using System.Data;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CrmCartorio.Api.Infrastructure.Security;

/// <summary>
/// Motivo pelo qual o break-glass recusou resetar no instante exato da
/// gravação — a elegibilidade pode ter mudado entre a verificação inicial
/// (mostrada ao operador) e a confirmação digitada. Ver
/// <see cref="FerramentaDeResetDeEmergencia"/>.
/// </summary>
public sealed class EmergenciaNaoElegivelException(ElegibilidadeDeEmergencia motivoAtual)
    : InvalidOperationException($"Reset de emergencia nao elegivel no instante da gravacao: {motivoAtual}.")
{
    public ElegibilidadeDeEmergencia MotivoAtual { get; } = motivoAtual;
}

public enum ElegibilidadeDeEmergencia
{
    Elegivel,
    ContaInexistente,
    ContaInativa,
    NaoPrivilegiada
}

/// <summary>
/// SEC-03 — break-glass: procedimento de emergência para o único Titular (ou
/// qualquer privilegiado) que perdeu o autenticador E os códigos de
/// recuperação, e por isso não consegue mais logar nem passar pelo próprio
/// step-up para se auto-resetar.
/// </summary>
/// <remarks>
/// <b>Por que isto precisa existir separado do reset administrativo normal
/// (<c>MfaService.ResetarAsync</c>) e do bootstrap
/// (<see cref="FerramentaDeBootstrapMfa"/>):</b>
/// <list type="bullet">
/// <item><c>ResetarAsync</c> exige STEP-UP do próprio ator (senha + fator
/// atual) — inútil aqui, porque é exatamente o fator que se perdeu. E, se o
/// alvo é <c>Cargo.Titular</c>, só um ator que TAMBÉM seja Titular pode
/// resetá-lo (<c>GarantirTitularProtegido</c>) — um Administrador comum,
/// mesmo com MFA ativo, não pode.</item>
/// <item>O bootstrap só serve para o PRIMEIRO privilegiado — se já existe
/// qualquer Administrador/Titular com MFA ativo no tenant (o caso comum:
/// existe um Administrador não-Titular matriculado, só o Titular específico
/// perdeu os fatores), ele se recusa a rodar.</item>
/// </list>
/// Juntas, essas duas proteções criam um DEADLOCK operacional real: ninguém
/// dentro do aplicativo consegue resetar o Titular travado. O break-glass
/// resolve isso do mesmo jeito que o bootstrap resolve o "ninguém matriculado
/// ainda" — um comando de execução única, só interativo, confiado a quem tem
/// acesso SSH ao servidor, nunca a um endpoint HTTP.
///
/// <b>Regras que este comando impõe, todas deliberadas (mesma disciplina do
/// bootstrap):</b>
/// <list type="bullet">
/// <item>SÓ interativo — recusa se entrada OU SAÍDA estiver redirecionada,
/// ou em CI/GITHUB_ACTIONS.</item>
/// <item>O texto do ticket novo SÓ existe em <see cref="Console.WriteLine(string?)"/>
/// — nunca <c>ILogger</c>, nunca disco, nunca banco em claro.</item>
/// <item>Motivo obrigatório, identidade do operador em <c>AtorExterno</c>
/// (nunca em <c>Motivo</c>) — mesmo padrão de <c>EventosDeSeguranca</c> do
/// bootstrap.</item>
/// <item><b>Não</b> exige que o tenant esteja "sem ninguém matriculado" —
/// ao contrário do bootstrap, funciona mesmo com outros privilegiados já
/// com MFA ativo, porque é exatamente esse o cenário que precisa resolver.</item>
/// <item><b>Não</b> desliga <c>Tenants.MfaObrigatorioParaAdministradores</c>
/// — a política do tenant nunca é tocada aqui. Se estiver ligada, a conta
/// resetada fica sem login até a rematrícula, como qualquer reset
/// administrativo normal.</item>
/// </list>
/// </remarks>
public static class FerramentaDeResetDeEmergencia
{
    private static readonly TimeSpan TtlDoTicket = TimeSpan.FromHours(24);

    /// <summary>
    /// Toda a regra de elegibilidade — sem nenhuma interação de console, sem
    /// exigir "ninguém matriculado ainda" (diferença deliberada do
    /// bootstrap). É o método que tanto <see cref="ExecutarAsync"/> quanto o
    /// verificador chamam; nunca há uma segunda cópia desta lógica.
    /// </summary>
    public static async Task<ElegibilidadeDeEmergencia> VerificarElegibilidadeAsync(
        CrmDbContext db, string email, CancellationToken ct = default)
    {
        var atendente = await db.Atendentes.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Email == email, ct);

        if (atendente is null) return ElegibilidadeDeEmergencia.ContaInexistente;
        if (!atendente.Ativo) return ElegibilidadeDeEmergencia.ContaInativa;

        var privilegiado = atendente.Perfil == PerfilDeAtendente.Administrador || atendente.Cargo == CargoNaServentia.Titular;
        if (!privilegiado) return ElegibilidadeDeEmergencia.NaoPrivilegiada;

        return ElegibilidadeDeEmergencia.Elegivel;
    }

    /// <summary>
    /// Núcleo atômico — reconfere a elegibilidade no instante exato da
    /// gravação e SÓ ENTÃO: remove o MFA do alvo (mesma forma de
    /// <c>MfaService.LimparMfaAsync</c>, replicada aqui porque este arquivo é
    /// transversal e não chama o <c>Application</c> do módulo Usuarios — regra
    /// de vertical slice), emite um ticket NOVO de rematrícula já vinculado à
    /// TokenVersion pós-reset, e grava o <c>EventoDeSeguranca</c> — tudo na
    /// MESMA transação. Deliberadamente <c>private</c>, mesma disciplina do
    /// bootstrap (bloqueador 9): só aceita o HASH do ticket novo, nunca gera
    /// nem devolve texto claro.
    /// </summary>
    private static async Task<ElegibilidadeDeEmergencia> TentarResetarDeEmergenciaAsync(
        CrmDbContext db, string email, string hashDoNovoTicket, string motivo, string atorExterno, CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var elegibilidadeNoInstante = await VerificarElegibilidadeAsync(db, email, ct);
        if (elegibilidadeNoInstante != ElegibilidadeDeEmergencia.Elegivel)
        {
            await tx.RollbackAsync(ct);
            return elegibilidadeNoInstante;
        }

        var atendente = await db.Atendentes.AsNoTracking().SingleAsync(a => a.Email == email, ct);

        await db.Atendentes
            .Where(a => a.Id == atendente.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.MfaAtivo, false)
                .SetProperty(a => a.MfaSegredoCifrado, (byte[]?)null)
                .SetProperty(a => a.MfaSegredoPendenteCifrado, (byte[]?)null)
                .SetProperty(a => a.MfaPendenteExpiraEm, (DateTime?)null)
                .SetProperty(a => a.MfaPendenteTokenVersion, (int?)null)
                .SetProperty(a => a.MfaConfirmadoEm, (DateTime?)null)
                .SetProperty(a => a.MfaUltimoPassoUsado, (long?)null)
                .SetProperty(a => a.MfaFalhasConsecutivas, 0)
                .SetProperty(a => a.MfaNivelDeBloqueio, (byte)0)
                .SetProperty(a => a.MfaBloqueadoAte, (DateTime?)null)
                .SetProperty(a => a.TokenVersion, a => a.TokenVersion + 1), ct);

        await db.CodigosDeRecuperacaoMfa.Where(c => c.AtendenteId == atendente.Id).ExecuteDeleteAsync(ct);
        await db.DesafiosDeMfa
            .Where(d => d.AtendenteId == atendente.Id && d.ConsumidoEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.ConsumidoEm, agora), ct);

        var tokenVersionPosReset = await db.Atendentes.AsNoTracking()
            .Where(a => a.Id == atendente.Id).Select(a => a.TokenVersion).SingleAsync(ct);

        // O caminho REAL de nova matricula segura: um ticket novo, ja
        // vinculado a TokenVersion pos-reset — sem isto o alvo ficaria
        // resetado mas sem NENHUM jeito de voltar a entrar.
        db.DesafiosDeMfa.Add(new DesafioDeMfa
        {
            TenantId = atendente.TenantId,
            AtendenteId = atendente.Id,
            HashDoDesafio = hashDoNovoTicket,
            Tipo = TipoDeDesafioMfa.Ticket,
            TokenVersionNoDesafio = tokenVersionPosReset,
            ExpiraEm = agora.Add(TtlDoTicket)
        });

        db.EventosDeSeguranca.Add(new EventoDeSeguranca
        {
            TenantId = atendente.TenantId,
            AtendenteId = atendente.Id,
            AtorAtendenteId = null,
            AtorExterno = atorExterno,
            Tipo = TipoDeEventoDeSeguranca.ResetDeEmergencia,
            OcorridoEm = agora,
            Motivo = motivo
        });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ElegibilidadeDeEmergencia.Elegivel;
    }

    /// <summary>
    /// O ÚNICO lugar de todo o assembly onde o texto claro do ticket de
    /// rematrícula pós-emergência é gerado ou devolvido. Deliberadamente
    /// <c>private</c>: só <see cref="ExecutarAsync"/>, no mesmo arquivo,
    /// alcança este método — e só depois de passar por todos os guards
    /// interativos.
    /// </summary>
    private static async Task<string> ExecutarResetDeEmergenciaAsync(
        CrmDbContext db, string email, string motivo, string atorExterno, CancellationToken ct = default)
    {
        var tokenEmClaro = SegredosAleatoriosMfa.NovoToken();
        var hashDoTicket = SegredosAleatoriosMfa.Sha256Hex(tokenEmClaro);

        var resultado = await TentarResetarDeEmergenciaAsync(db, email, hashDoTicket, motivo, atorExterno, ct);

        if (resultado != ElegibilidadeDeEmergencia.Elegivel)
        {
            throw new EmergenciaNaoElegivelException(resultado);
        }

        return tokenEmClaro;
    }

    /// <summary>
    /// Decisão PURA de quando recusar por ambiente não-interativo — mesma
    /// disciplina de <c>FerramentaDeBootstrapMfa.AmbienteNaoInterativo</c>
    /// (bloqueador 16), duplicada aqui de propósito: cada comando de
    /// bootstrap/break-glass já é independente, sem um lugar transversal
    /// compartilhado para isto. Testável por reflexão, `private`.
    /// </summary>
    private static bool AmbienteNaoInterativo() =>
        Console.IsInputRedirected
        || Console.IsOutputRedirected
        || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") is not null
        || Environment.GetEnvironmentVariable("CI") is not null;

    public static async Task<int> ExecutarAsync(IConfiguration configuracao)
    {
        Console.WriteLine("=== CRM-Cartorio — RESET DE EMERGENCIA de MFA (SEC-03, break-glass) ===");
        Console.WriteLine();

        if (AmbienteNaoInterativo())
        {
            Console.WriteLine(
                "RECUSADO: este comando so roda de forma interativa, no teclado, via SSH. " +
                "Entrada OU SAIDA redirecionada (pipe, arquivo ou ambiente de CI) nunca e " +
                "aceita.");
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
        await using var db = new CrmDbContext(optionsBuilder.Options);

        if (!await db.Database.CanConnectAsync())
        {
            Console.WriteLine("RECUSADO: nao foi possivel conectar ao banco configurado.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("Use isto SOMENTE quando a conta perdeu autenticador E codigos de");
        Console.WriteLine("recuperacao, e por isso nao consegue mais logar nem se auto-resetar.");
        Console.WriteLine("Isto remove o MFA atual da conta e emite um ticket novo para rematricula.");
        Console.WriteLine();
        Console.Write("E-mail do atendente a resetar (break-glass): ");
        var email = (Console.ReadLine() ?? string.Empty).Trim().ToLowerInvariant();

        var elegibilidade = await VerificarElegibilidadeAsync(db, email);

        switch (elegibilidade)
        {
            case ElegibilidadeDeEmergencia.ContaInexistente:
                Console.WriteLine($"RECUSADO: nenhum atendente com o e-mail '{email}'.");
                return 1;
            case ElegibilidadeDeEmergencia.ContaInativa:
                Console.WriteLine("RECUSADO: a conta esta inativa.");
                return 1;
            case ElegibilidadeDeEmergencia.NaoPrivilegiada:
                Console.WriteLine("RECUSADO: este fluxo e exclusivo para Titular ou Administrador.");
                return 1;
        }

        var atendente = await db.Atendentes.AsNoTracking().SingleAsync(a => a.Email == email);
        Console.WriteLine($"Conta encontrada: {atendente.Nome} <{atendente.Email}>.");
        Console.Write("Motivo da emergencia (obrigatorio, registrado em EventosDeSeguranca): ");
        var motivo = (Console.ReadLine() ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(motivo))
        {
            Console.WriteLine("RECUSADO: motivo em branco.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("ATENCAO: isto REMOVE o MFA atual desta conta — autenticador, codigos de");
        Console.WriteLine("recuperacao, desafios/tickets pendentes — e emite um ticket NOVO para");
        Console.WriteLine("rematricula. A conta fica SEM LOGIN ate a rematricula (se a politica");
        Console.WriteLine("obrigatoria estiver ligada para ela).");
        Console.WriteLine("Confere: duas pessoas devem estar presentes para este procedimento —");
        Console.WriteLine("quem executa este comando (acesso SSH) e o proprio titular da conta,");
        Console.WriteLine("com identidade verificada fora de banda.");
        Console.Write($"Digite RESETAR para confirmar o break-glass de {atendente.Email}: ");

        if (!string.Equals(Console.ReadLine()?.Trim(), "RESETAR", StringComparison.Ordinal))
        {
            Console.WriteLine("Cancelado.");
            return 1;
        }

        var atorExterno = $"{Environment.UserName}@{Environment.MachineName}";
        string tokenEmClaro;
        try
        {
            tokenEmClaro = await ExecutarResetDeEmergenciaAsync(db, email, motivo, atorExterno);
        }
        catch (EmergenciaNaoElegivelException ex)
        {
            Console.WriteLine();
            Console.WriteLine($"RECUSADO no instante da gravacao: a elegibilidade mudou ({ex.MotivoAtual}).");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("=== TICKET DE REMATRICULA (mostrado uma UNICA vez — copie agora) ===");
        Console.WriteLine(tokenEmClaro);
        Console.WriteLine("=== FIM DO TICKET ===");
        Console.WriteLine();
        Console.WriteLine("Entregue ao portador por canal fora de banda (voz, presencial) — nunca por e-mail, chat ou SMS.");
        Console.WriteLine($"Registrado em EventosDeSeguranca como ResetDeEmergencia, ator externo '{atorExterno}'.");
        Console.WriteLine("A politica obrigatoria do tenant (se ligada) NAO foi alterada — a conta so volta a logar apos a rematricula.");

        return 0;
    }
}
