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
    NaoPrivilegiada,

    /// <summary>
    /// CORRECAO (bloqueador B18, sexta revisao independente) — a conta alvo
    /// nao tem MFA ativo no banco. Break-glass nao e um emissor alternativo
    /// de ticket para quem simplesmente ainda nao se matriculou: essa conta
    /// ja tem o bootstrap (se for a primeira do tenant) ou o fluxo normal de
    /// ticket (step-up de outro privilegiado ja matriculado) a disposicao.
    /// </summary>
    ContaSemMfaAtivo,

    /// <summary>
    /// CORRECAO (bloqueador B18, sexta revisao independente) — existe outro
    /// Administrador (Titular tambem, se o alvo for Titular) ativo e com MFA
    /// ativo no mesmo tenant, capaz de executar
    /// <c>MfaService.ResetarAsync</c> normalmente. Break-glass e SO a saida
    /// de um deadlock real, nunca um atalho quando o caminho normal ja
    /// resolve.
    /// </summary>
    ExisteAtorAptoParaResetNormal
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
/// com MFA ativo, DESDE QUE nenhum deles seja capaz de resolver pelo
/// caminho normal (ver <see cref="ElegibilidadeDeEmergencia.ExisteAtorAptoParaResetNormal"/>).</item>
/// <item><b>Não</b> desliga <c>Tenants.MfaObrigatorioParaAdministradores</c>
/// — a política do tenant nunca é tocada aqui.</item>
/// <item><b>A conta fica sem login até a rematrícula, SEMPRE</b> — não só
/// quando a política do tenant exige MFA. <c>RematriculaObrigatoriaAposEmergencia</c>
/// (migration 036, bloqueador B19) é um sinalizador por conta, independente
/// da política: sem ele, um tenant com a política desligada deixaria a
/// conta resetada voltar a logar só com senha, sem nunca passar pelo ticket
/// de rematrícula que o próprio break-glass emitiu.</item>
/// <item><b>Exclusivamente saída de deadlock</b> (bloqueador B18) — exige
/// MFA ATUALMENTE ativo no alvo (quem ainda não se matriculou tem o
/// bootstrap ou o ticket normal) e recusa se existir outro ator apto a
/// executar <c>MfaService.ResetarAsync</c> normalmente (outro Administrador
/// com MFA — Titular também, se o alvo for Titular).</item>
/// <item><b>Autorização assimétrica da segunda pessoa</b>, verificada por
/// <see cref="AutorizacaoDeSegundaPessoa"/> (bloqueadores B22/B23, oitava
/// revisão independente): o servidor guarda SÓ a chave pública, a chave
/// privada do custodiante nunca entra aqui, e a assinatura cobre um desafio
/// novo (nonce de 256 bits) amarrado a operação, alvo, operador e hash do
/// motivo. Substituiu o TOTP compartilhado da revisão anterior, cujo segredo
/// vivia no próprio servidor (quem tinha SSH gerava os códigos da "segunda
/// pessoa") e cujo código valia mais de uma vez dentro da janela. Sem chave
/// pública configurada, o comando se recusa a rodar — sem fallback.</item>
/// </list>
/// </remarks>
public static class FerramentaDeResetDeEmergencia
{
    private static readonly TimeSpan TtlDoTicket = TimeSpan.FromHours(24);

    /// <summary>
    /// Toda a regra de elegibilidade — sem nenhuma interação de console. É o
    /// método que tanto <see cref="ExecutarAsync"/> quanto o verificador
    /// chamam; nunca há uma segunda cópia desta lógica.
    /// </summary>
    /// <remarks>
    /// CORRECAO (bloqueador B18, sexta revisão independente) — a versão
    /// anterior aceitava qualquer conta ativa e privilegiada, mesmo com
    /// <c>MfaAtivo = false</c> e mesmo quando existia um caminho normal
    /// disponível. Break-glass é EXCLUSIVAMENTE a saída do deadlock descrito
    /// na classe: MFA que continua ativo NO BANCO (a conta perdeu o
    /// autenticador/códigos, não o registro de que está matriculada) e
    /// NENHUM outro ator capaz de resetar pelo caminho normal.
    /// </remarks>
    public static async Task<ElegibilidadeDeEmergencia> VerificarElegibilidadeAsync(
        CrmDbContext db, string email, CancellationToken ct = default)
    {
        var atendente = await db.Atendentes.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Email == email, ct);

        if (atendente is null) return ElegibilidadeDeEmergencia.ContaInexistente;
        if (!atendente.Ativo) return ElegibilidadeDeEmergencia.ContaInativa;

        var privilegiado = atendente.Perfil == PerfilDeAtendente.Administrador || atendente.Cargo == CargoNaServentia.Titular;
        if (!privilegiado) return ElegibilidadeDeEmergencia.NaoPrivilegiada;

        if (!atendente.MfaAtivo) return ElegibilidadeDeEmergencia.ContaSemMfaAtivo;

        return await ExisteAtorAptoParaResetNormalAsync(db, atendente, ct)
            ? ElegibilidadeDeEmergencia.ExisteAtorAptoParaResetNormal
            : ElegibilidadeDeEmergencia.Elegivel;
    }

    /// <summary>
    /// O MESMO critério que <c>MfaService.ResetarAsync</c> realmente exige de
    /// um ator: Administrador ativo com MFA ativo — e, se o alvo for
    /// Titular, também <c>Cargo == Titular</c> (<c>GarantirTitularProtegido</c>).
    /// Se existir, o caminho normal (com step-up desse ator) resolve sozinho
    /// — nem o break-glass, nem a reemissão de ticket
    /// (<see cref="FerramentaDeReemissaoDeTicketDeEmergencia"/>) devem
    /// prosseguir. <c>internal</c> especificamente para ser compartilhado
    /// entre as duas ferramentas sem duplicar a consulta — nunca reaberto ao
    /// público.
    /// </summary>
    internal static Task<bool> ExisteAtorAptoParaResetNormalAsync(
        CrmDbContext db, Atendente atendente, CancellationToken ct) =>
        db.Atendentes.AsNoTracking()
            .AnyAsync(a => a.TenantId == atendente.TenantId
                        && a.Id != atendente.Id
                        && a.Ativo
                        && a.MfaAtivo
                        && a.Perfil == PerfilDeAtendente.Administrador
                        && (atendente.Cargo != CargoNaServentia.Titular || a.Cargo == CargoNaServentia.Titular), ct);

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
                // CORRECAO (bloqueador B19, sexta revisao independente): sem
                // isto, um tenant com a politica obrigatoria desligada
                // deixava esta conta voltar a logar so com senha, nunca
                // passando pelo ticket de rematricula emitido abaixo.
                // AutenticacaoService.AutenticarAsync recusa login enquanto
                // este campo for true, INDEPENDENTE da politica do tenant;
                // so volta a false na confirmacao de uma matricula nova
                // (MfaService.ConfirmarNucleoAsync).
                .SetProperty(a => a.RematriculaObrigatoriaAposEmergencia, true)
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

        // CORRECAO (bloqueadores B22/B23, oitava revisao independente) — o
        // alvo e o motivo sao coletados ANTES da autorizacao porque os DOIS
        // entram no desafio assinado: o que o custodiante autoriza e
        // exatamente esta operacao, para este alvo, com este motivo. Depois
        // da assinatura nada disso e perguntado de novo — trocar qualquer um
        // deles exigiria uma assinatura nova.
        Console.WriteLine();
        Console.WriteLine("Use isto SOMENTE quando a conta perdeu autenticador E codigos de");
        Console.WriteLine("recuperacao, e por isso nao consegue mais logar nem se auto-resetar.");
        Console.WriteLine("Isto remove o MFA atual da conta e emite um ticket novo para rematricula.");
        Console.WriteLine();
        Console.Write("E-mail do atendente a resetar (break-glass): ");
        var email = (Console.ReadLine() ?? string.Empty).Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(email))
        {
            Console.WriteLine("RECUSADO: e-mail em branco.");
            return 1;
        }

        Console.Write("Motivo da emergencia (obrigatorio, registrado em EventosDeSeguranca): ");
        var motivo = (Console.ReadLine() ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(motivo))
        {
            Console.WriteLine("RECUSADO: motivo em branco.");
            return 1;
        }

        var operadorSsh = $"{Environment.UserName}@{Environment.MachineName}";

        // A autorizacao da SEGUNDA PESSOA acontece ANTES de qualquer conexao
        // a banco — e assimetrica: o servidor so tem a chave publica, entao
        // quem tem SSH nao consegue produzi-la sozinho (B23), e a assinatura
        // cobre um nonce fresco de 256 bits, entao uma assinatura de execucao
        // anterior nao vale aqui (B22).
        var (autorizado, atorExterno) = AutorizacaoDeSegundaPessoa.AutorizarInterativo(
            configuracao, OperacaoDeEmergencia.ResetDeEmergencia, email, operadorSsh, motivo);

        if (!autorizado)
        {
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

        // Elegibilidade so agora — a autorizacao da segunda pessoa ja foi
        // conferida, e o alvo/motivo usados daqui para frente sao EXATAMENTE
        // os que ela assinou.
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
            case ElegibilidadeDeEmergencia.ContaSemMfaAtivo:
                Console.WriteLine(
                    "RECUSADO: esta conta nao tem MFA ativo — nao e um caso de break-glass. " +
                    "Use o bootstrap (se for a primeira conta privilegiada do tenant) ou o " +
                    "ticket normal (step-up de outro privilegiado ja matriculado).");
                return 1;
            case ElegibilidadeDeEmergencia.ExisteAtorAptoParaResetNormal:
                Console.WriteLine(
                    "RECUSADO: existe outro Administrador (Titular, se o alvo for Titular) " +
                    "ativo e com MFA ativo neste tenant, capaz de resetar esta conta pelo " +
                    "caminho normal (MfaService.ResetarAsync, com step-up dele). Break-glass " +
                    "e so a saida de um deadlock real — nao um atalho.");
                return 1;
        }

        var atendente = await db.Atendentes.AsNoTracking().SingleAsync(a => a.Email == email);
        Console.WriteLine($"Conta encontrada: {atendente.Nome} <{atendente.Email}>.");

        Console.WriteLine();
        Console.WriteLine("ATENCAO: isto REMOVE o MFA atual desta conta — autenticador, codigos de");
        Console.WriteLine("recuperacao, desafios/tickets pendentes — e emite um ticket NOVO para");
        Console.WriteLine("rematricula. A conta fica SEM LOGIN ate concluir a rematricula com esse");
        Console.WriteLine("ticket — SEMPRE, independente da politica obrigatoria do tenant.");
        Console.Write($"Digite RESETAR para confirmar o break-glass de {atendente.Email}: ");

        if (!string.Equals(Console.ReadLine()?.Trim(), "RESETAR", StringComparison.Ordinal))
        {
            Console.WriteLine("Cancelado.");
            return 1;
        }

        // AtorExterno ja veio composto pela propria autorizacao (operador SSH
        // + custodiante + fingerprint da chave que verificou) — nunca a
        // assinatura, nunca chave privada, nunca segredo.
        string tokenEmClaro;
        try
        {
            tokenEmClaro = await ExecutarResetDeEmergenciaAsync(db, email, motivo, atorExterno!);
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
        Console.WriteLine("A politica obrigatoria do tenant NAO foi alterada — a conta fica sem login ate a rematricula, mesmo que a politica esteja desligada.");

        return 0;
    }
}
