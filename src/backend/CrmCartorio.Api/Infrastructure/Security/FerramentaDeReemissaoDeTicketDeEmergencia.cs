using System.Data;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CrmCartorio.Api.Infrastructure.Security;

/// <summary>
/// Motivo pelo qual a reemissão recusou no instante exato da gravação — a
/// elegibilidade pode ter mudado entre a verificação inicial (mostrada ao
/// operador) e a confirmação digitada. Ver
/// <see cref="FerramentaDeReemissaoDeTicketDeEmergencia"/>.
/// </summary>
public sealed class ReemissaoNaoElegivelException(ElegibilidadeDeReemissaoDeTicket motivoAtual)
    : InvalidOperationException($"Reemissao de ticket de emergencia nao elegivel no instante da gravacao: {motivoAtual}.")
{
    public ElegibilidadeDeReemissaoDeTicket MotivoAtual { get; } = motivoAtual;
}

public enum ElegibilidadeDeReemissaoDeTicket
{
    Elegivel,
    ContaInexistente,
    ContaInativa,

    /// <summary>
    /// A conta não está numa recuperação de emergência aberta — ou nunca
    /// passou por break-glass (<c>RematriculaObrigatoriaAposEmergencia = false</c>),
    /// ou já concluiu a rematrícula (<c>MfaAtivo = true</c>, o que já zera o
    /// sinalizador na mesma transação). Reemissão nunca é um segundo caminho
    /// de bootstrap/ticket normal para quem nunca teve uma emergência aberta.
    /// </summary>
    NaoEstaEmRecuperacaoDeEmergencia,

    /// <summary>Mesmo critério de <see cref="ElegibilidadeDeEmergencia.ExisteAtorAptoParaResetNormal"/>.</summary>
    ExisteAtorAptoParaResetNormal
}

/// <summary>
/// SEC-03 (bloqueador B20, sétima revisão independente) — reemissão de
/// ticket de rematrícula DURANTE uma recuperação de emergência já aberta
/// pelo break-glass (<see cref="FerramentaDeResetDeEmergencia"/>).
/// </summary>
/// <remarks>
/// <b>O problema que isto resolve:</b> o break-glass exige
/// <c>MfaAtivo = true</c> no alvo (bloqueador B18) — depois de rodar, o alvo
/// fica com <c>MfaAtivo = false</c> e um ticket de rematrícula com TTL de
/// 24h. Se esse ticket for perdido, expirar, ou ficar inutilizável antes da
/// rematrícula, o alvo fica preso de NOVO: o break-glass original agora
/// recusa por <see cref="ElegibilidadeDeEmergencia.ContaSemMfaAtivo"/>
/// (MfaAtivo já é false), o bootstrap continua fechado (outro privilegiado
/// com MFA já existe), o Administrador não-Titular continua proibido de
/// resetar/emitir para o Titular, e o login continua bloqueado por
/// <c>RematriculaObrigatoriaAposEmergencia</c>. Mesmo deadlock de novo, sem
/// nenhum caminho de dentro do processo para sair.
///
/// <b>O invariante que autoriza a reemissão</b> (nunca reativa MFA, nunca
/// desliga o sinalizador de rematrícula obrigatória, nunca libera login —
/// só troca o ticket):
/// <code>
/// MfaAtivo = false
/// E RematriculaObrigatoriaAposEmergencia = true
/// E nenhum ator normal apto (mesmo critério do break-glass)
/// E controle técnico de duas pessoas válido
/// =&gt; pode invalidar ticket/desafio de rematrícula anterior e emitir um novo
/// </code>
///
/// <b>Mesma disciplina do break-glass e do bootstrap:</b>
/// <list type="bullet">
/// <item>SÓ CLI/manual, execução única, sem endpoint HTTP.</item>
/// <item>SÓ interativo — recusa se entrada OU saída estiver redirecionada,
/// ou em CI/GITHUB_ACTIONS.</item>
/// <item>MESMA autorização assimétrica da segunda pessoa
/// (<see cref="AutorizacaoDeSegundaPessoa"/>, bloqueadores B22/B23) — não um
/// mecanismo mais fraco só porque "já é uma emergência aberta", e nunca uma
/// segunda implementação criptográfica: só o tipo da operação muda
/// (<see cref="OperacaoDeEmergencia.ReemitirTicketDeEmergencia"/>), o que
/// impede uma assinatura de reset de autorizar uma reemissão.</item>
/// <item>Motivo obrigatório.</item>
/// <item>Auditada com o PRÓPRIO tipo de evento
/// (<see cref="TipoDeEventoDeSeguranca.TicketDeRematriculaReemitido"/>, não
/// <c>ResetDeEmergencia</c> de novo — um auditor precisa distinguir "uma
/// emergência foi aberta" de "um ticket foi reemitido dentro de uma
/// emergência já aberta").</item>
/// <item>Invalida QUALQUER desafio de rematrícula ainda não consumido do
/// alvo antes de emitir o novo — cobre perdido, expirado OU simplesmente
/// "ainda tecnicamente válido mas inutilizável" (ex.: ilegível, corrompido
/// na entrega).</item>
/// <item>Novo ticket vinculado à <c>TokenVersion</c> ATUAL da linha (a
/// mesma que o break-glass original já deixou gravada — não sobe de novo,
/// porque nada sobre a autorização do ator mudou).</item>
/// <item>Atômica, <c>SERIALIZABLE</c> — reconfere elegibilidade no instante
/// exato da gravação, mesmo padrão de bootstrap/break-glass.</item>
/// <item>Recusa para conta comum que nunca passou por break-glass
/// (<see cref="ElegibilidadeDeReemissaoDeTicket.NaoEstaEmRecuperacaoDeEmergencia"/>) —
/// nunca um segundo caminho de matrícula fora do ticket normal/bootstrap.</item>
/// </list>
/// </remarks>
public static class FerramentaDeReemissaoDeTicketDeEmergencia
{
    private static readonly TimeSpan TtlDoTicket = TimeSpan.FromHours(24);

    /// <summary>
    /// Toda a regra de elegibilidade — sem nenhuma interação de console. É o
    /// método que tanto <see cref="ExecutarAsync"/> quanto o verificador
    /// chamam; nunca há uma segunda cópia desta lógica.
    /// </summary>
    public static async Task<ElegibilidadeDeReemissaoDeTicket> VerificarElegibilidadeAsync(
        CrmDbContext db, string email, CancellationToken ct = default)
    {
        var atendente = await db.Atendentes.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Email == email, ct);

        if (atendente is null) return ElegibilidadeDeReemissaoDeTicket.ContaInexistente;
        if (!atendente.Ativo) return ElegibilidadeDeReemissaoDeTicket.ContaInativa;

        // A conta so esta "numa emergencia aberta" com AS DUAS condicoes
        // juntas — MfaAtivo=true (rematriculou) OU RematriculaObrigatoria-
        // AposEmergencia=false (nunca passou por break-glass) SIGNIFICAM,
        // as duas, "isto nao e reemissao".
        if (atendente.MfaAtivo || !atendente.RematriculaObrigatoriaAposEmergencia)
        {
            return ElegibilidadeDeReemissaoDeTicket.NaoEstaEmRecuperacaoDeEmergencia;
        }

        var existeAtorApto = await FerramentaDeResetDeEmergencia.ExisteAtorAptoParaResetNormalAsync(db, atendente, ct);

        return existeAtorApto
            ? ElegibilidadeDeReemissaoDeTicket.ExisteAtorAptoParaResetNormal
            : ElegibilidadeDeReemissaoDeTicket.Elegivel;
    }

    /// <summary>
    /// Núcleo atômico — reconfere a elegibilidade no instante exato da
    /// gravação e SÓ ENTÃO: invalida todo desafio de rematrícula ainda não
    /// consumido do alvo, emite um ticket NOVO vinculado à TokenVersion
    /// atual, e grava o <c>EventoDeSeguranca</c> — tudo na MESMA transação.
    /// NUNCA toca <c>MfaAtivo</c> nem <c>RematriculaObrigatoriaAposEmergencia</c>
    /// — é exatamente essa ausência de mutação que garante o invariante da
    /// classe. Deliberadamente <c>private</c>, mesma disciplina do bootstrap
    /// e do break-glass: só aceita o HASH do ticket novo, nunca gera nem
    /// devolve texto claro.
    /// </summary>
    private static async Task<ElegibilidadeDeReemissaoDeTicket> TentarReemitirTicketAsync(
        CrmDbContext db, string email, string hashDoNovoTicket, string motivo, string atorExterno, CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var elegibilidadeNoInstante = await VerificarElegibilidadeAsync(db, email, ct);
        if (elegibilidadeNoInstante != ElegibilidadeDeReemissaoDeTicket.Elegivel)
        {
            await tx.RollbackAsync(ct);
            return elegibilidadeNoInstante;
        }

        var atendente = await db.Atendentes.AsNoTracking().SingleAsync(a => a.Email == email, ct);

        // Invalida QUALQUER desafio de rematricula ainda nao consumido —
        // cobre perdido, expirado, ou so "inutilizavel" (o proprio TTL ja
        // cuidaria do expirado sozinho, mas um ticket perdido pode ainda
        // estar tecnicamente valido e reaparecer depois do novo ser emitido).
        await db.DesafiosDeMfa
            .Where(d => d.AtendenteId == atendente.Id && d.ConsumidoEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.ConsumidoEm, agora), ct);

        // TokenVersion ATUAL — a mesma que o break-glass original ja deixou
        // gravada. Nao sobe de novo: nada sobre a autorizacao do ator mudou,
        // so o ticket esta sendo trocado.
        db.DesafiosDeMfa.Add(new DesafioDeMfa
        {
            TenantId = atendente.TenantId,
            AtendenteId = atendente.Id,
            HashDoDesafio = hashDoNovoTicket,
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
            Tipo = TipoDeEventoDeSeguranca.TicketDeRematriculaReemitido,
            OcorridoEm = agora,
            Motivo = motivo
        });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ElegibilidadeDeReemissaoDeTicket.Elegivel;
    }

    /// <summary>
    /// O ÚNICO lugar de todo o assembly onde o texto claro do ticket
    /// reemitido é gerado ou devolvido. Deliberadamente <c>private</c>: só
    /// <see cref="ExecutarAsync"/>, no mesmo arquivo, alcança este método.
    /// </summary>
    private static async Task<string> ExecutarReemissaoAsync(
        CrmDbContext db, string email, string motivo, string atorExterno, CancellationToken ct = default)
    {
        var tokenEmClaro = SegredosAleatoriosMfa.NovoToken();
        var hashDoTicket = SegredosAleatoriosMfa.Sha256Hex(tokenEmClaro);

        var resultado = await TentarReemitirTicketAsync(db, email, hashDoTicket, motivo, atorExterno, ct);

        if (resultado != ElegibilidadeDeReemissaoDeTicket.Elegivel)
        {
            throw new ReemissaoNaoElegivelException(resultado);
        }

        return tokenEmClaro;
    }

    /// <summary>
    /// Decisão PURA de quando recusar por ambiente não-interativo — mesma
    /// disciplina de <c>FerramentaDeBootstrapMfa.AmbienteNaoInterativo</c> e
    /// <c>FerramentaDeResetDeEmergencia.AmbienteNaoInterativo</c>, duplicada
    /// aqui de propósito: cada comando é independente, sem um lugar
    /// transversal compartilhado para isto. Testável por reflexão, `private`.
    /// </summary>
    private static bool AmbienteNaoInterativo() =>
        Console.IsInputRedirected
        || Console.IsOutputRedirected
        || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") is not null
        || Environment.GetEnvironmentVariable("CI") is not null;

    public static async Task<int> ExecutarAsync(IConfiguration configuracao)
    {
        Console.WriteLine("=== CRM-Cartorio — REEMISSAO de ticket de emergencia (SEC-03, break-glass) ===");
        Console.WriteLine();

        if (AmbienteNaoInterativo())
        {
            Console.WriteLine(
                "RECUSADO: este comando so roda de forma interativa, no teclado, via SSH. " +
                "Entrada OU SAIDA redirecionada (pipe, arquivo ou ambiente de CI) nunca e " +
                "aceita.");
            return 1;
        }

        // CORRECAO (bloqueadores B22/B23, oitava revisao independente) —
        // alvo e motivo coletados ANTES da autorizacao: os dois entram no
        // desafio assinado. Depois da assinatura nada disso e perguntado de
        // novo — a autorizacao vale para ESTA operacao, ESTE alvo, ESTE
        // motivo, e para mais nada.
        Console.WriteLine();
        Console.WriteLine("Use isto SOMENTE quando uma conta JA ESTA numa recuperacao de emergencia");
        Console.WriteLine("aberta (passou pelo --reset-de-emergencia) e o ticket de rematricula foi");
        Console.WriteLine("perdido, expirou, ou ficou inutilizavel antes da rematricula. Isto NAO");
        Console.WriteLine("reativa o MFA nem libera login — so troca o ticket.");
        Console.WriteLine();
        Console.Write("E-mail do atendente (reemissao de ticket de emergencia): ");
        var email = (Console.ReadLine() ?? string.Empty).Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(email))
        {
            Console.WriteLine("RECUSADO: e-mail em branco.");
            return 1;
        }

        Console.Write("Motivo da reemissao (obrigatorio, registrado em EventosDeSeguranca): ");
        var motivo = (Console.ReadLine() ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(motivo))
        {
            Console.WriteLine("RECUSADO: motivo em branco.");
            return 1;
        }

        var operadorSsh = $"{Environment.UserName}@{Environment.MachineName}";

        // MESMO componente do break-glass — nunca uma segunda implementacao
        // criptografica. So o tipo da operacao muda, e e ele que impede uma
        // assinatura de RESET de autorizar uma REEMISSAO.
        var (autorizado, atorExterno) = AutorizacaoDeSegundaPessoa.AutorizarInterativo(
            configuracao, OperacaoDeEmergencia.ReemitirTicketDeEmergencia, email, operadorSsh, motivo);

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
        // conferida, e o alvo/motivo daqui para frente sao EXATAMENTE os que
        // ela assinou.
        var elegibilidade = await VerificarElegibilidadeAsync(db, email);

        switch (elegibilidade)
        {
            case ElegibilidadeDeReemissaoDeTicket.ContaInexistente:
                Console.WriteLine($"RECUSADO: nenhum atendente com o e-mail '{email}'.");
                return 1;
            case ElegibilidadeDeReemissaoDeTicket.ContaInativa:
                Console.WriteLine("RECUSADO: a conta esta inativa.");
                return 1;
            case ElegibilidadeDeReemissaoDeTicket.NaoEstaEmRecuperacaoDeEmergencia:
                Console.WriteLine(
                    "RECUSADO: esta conta nao esta numa recuperacao de emergencia aberta " +
                    "(ou nunca passou por break-glass, ou ja concluiu a rematricula). " +
                    "Reemissao NAO e um segundo caminho de bootstrap/ticket normal.");
                return 1;
            case ElegibilidadeDeReemissaoDeTicket.ExisteAtorAptoParaResetNormal:
                Console.WriteLine(
                    "RECUSADO: existe outro Administrador (Titular, se o alvo for Titular) " +
                    "ativo e com MFA ativo neste tenant, capaz de resolver pelo caminho " +
                    "normal. Reemissao e so a saida de um deadlock real — nao um atalho.");
                return 1;
        }

        var atendente = await db.Atendentes.AsNoTracking().SingleAsync(a => a.Email == email);
        Console.WriteLine($"Conta encontrada: {atendente.Nome} <{atendente.Email}> — em recuperacao de emergencia aberta.");

        Console.WriteLine();
        Console.WriteLine("ATENCAO: isto invalida o ticket de rematricula anterior (perdido, expirado");
        Console.WriteLine("ou nao) e emite um ticket NOVO. NAO reativa o MFA, NAO libera login — a");
        Console.WriteLine("conta continua sem acesso ate concluir a rematricula com o ticket novo.");
        Console.Write($"Digite REEMITIR para confirmar a reemissao para {atendente.Email}: ");

        if (!string.Equals(Console.ReadLine()?.Trim(), "REEMITIR", StringComparison.Ordinal))
        {
            Console.WriteLine("Cancelado.");
            return 1;
        }

        // AtorExterno ja veio composto pela propria autorizacao (operador SSH
        // + custodiante + fingerprint da chave que verificou).
        string tokenEmClaro;
        try
        {
            tokenEmClaro = await ExecutarReemissaoAsync(db, email, motivo, atorExterno!);
        }
        catch (ReemissaoNaoElegivelException ex)
        {
            Console.WriteLine();
            Console.WriteLine($"RECUSADO no instante da gravacao: a elegibilidade mudou ({ex.MotivoAtual}).");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("=== TICKET DE REMATRICULA REEMITIDO (mostrado uma UNICA vez — copie agora) ===");
        Console.WriteLine(tokenEmClaro);
        Console.WriteLine("=== FIM DO TICKET ===");
        Console.WriteLine();
        Console.WriteLine("Entregue ao portador por canal fora de banda (voz, presencial) — nunca por e-mail, chat ou SMS.");
        Console.WriteLine($"Registrado em EventosDeSeguranca como TicketDeRematriculaReemitido, ator externo '{atorExterno}'.");
        Console.WriteLine("MFA continua desativado e a rematricula continua obrigatoria — nada disso mudou.");

        return 0;
    }
}
