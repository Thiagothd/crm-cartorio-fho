using System.Data;
using System.Security.Cryptography;
using CrmCartorio.Api.Infrastructure.Identity;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Usuarios.Application;

/// <inheritdoc cref="IMfaService"/>
public class MfaService(
    CrmDbContext db,
    IHashDeSenha hash,
    ICifraDeSegredoMfa cifra,
    IGeradorDeToken tokens,
    ICookieDeDispositivoConfiavel cookieDeDispositivo,
    ILogger<MfaService> log) : IMfaService
{
    private const int QuantidadeDeCodigosDeRecuperacao = 10;
    private static readonly TimeSpan TtlDoSegredoPendente = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TtlDoDesafioDeMatricula = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan TtlDoTicket = TimeSpan.FromHours(24);

    /// <summary>
    /// SEC-03 (revisao independente do PR #46, bloqueador 15) — mesmo teto do
    /// segundo fator no login (<c>AutenticacaoService.LimiteDeTentativasPorDesafio</c>),
    /// duplicado aqui de proposito (cada classe ja tem seus proprios TTLs
    /// privados — nao ha um lugar compartilhado para constantes de SEC-03) —
    /// aplicado ao desafio de MatriculaPrivilegiada, que ate aqui nao tinha
    /// nenhum teto de tentativas.
    /// </summary>
    private const int LimiteDeTentativasPorDesafio = 5;

    /// <summary>Mesma disciplina anti-timing de <see cref="AutenticacaoService"/> — usado so na matricula privilegiada, onde tambem ha busca por e-mail.</summary>
    private const string HashDescartavel =
        "pbkdf2.sha256$210000$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    // ------------------------------------------------------------ sessao JWT

    public async Task<IniciarMfaResponse> IniciarAsync(
        IdentidadeParaMfa identidade, IniciarMfaRequest request, string? ip, CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;
        var atendente = await CarregarAsync(identidade, ct);

        if (!hash.Verificar(request.SenhaAtual ?? string.Empty, atendente.SenhaHash))
        {
            throw UsuariosServiceException.SenhaAtualIncorreta();
        }

        // CORRECAO (revisao independente do PR #46, bloqueador 1): a PRIMEIRA
        // matricula de conta PRIVILEGIADA (Titular ou Administrador) nunca pode
        // acontecer pelo fluxo autenticado comum — JWT + senha nao bastam, por
        // desenho. So chega ao segredo pendente de um privilegiado sem MFA
        // quem passou por ConfirmarViaDesafioAsync com um DesafioDeMfa de
        // matricula valido, que por sua vez so nasce de senha + TICKET fora de
        // banda (IniciarMatriculaPrivilegiadaAsync). Sem esta trava, o proprio
        // endpoint autenticado normal era um atalho que contornava o ticket
        // inteiro — exatamente o que a correcao fecha.
        var privilegiado = atendente.Perfil == PerfilDeAtendente.Administrador
            || atendente.Cargo == CargoNaServentia.Titular;

        if (!atendente.MfaAtivo && privilegiado)
        {
            throw UsuariosServiceException.MatriculaPrivilegiadaExigeTicket();
        }

        // Trocar autenticador com MFA ja ativo exige TAMBEM o fator atual — um
        // JWT roubado, mesmo com a senha (que tambem pode ter sido roubada
        // junto), nunca substitui sozinho o segredo de quem ja tem MFA. A
        // FASE 1 do step-up (corretude criptografica) roda fora de qualquer
        // transacao — se o codigo estiver errado, a falha precisa commitar
        // sozinha.
        (bool EhTotp, long PassoValidado)? codigoValidado = null;
        if (atendente.MfaAtivo)
        {
            if (string.IsNullOrWhiteSpace(request.CodigoAtual))
            {
                throw UsuariosServiceException.CodigoMfaInvalido();
            }

            codigoValidado = await ValidarCodigoDeStepUpAsync(atendente.Id, request.CodigoAtual, agora, ct);
        }

        // CORRECAO (revisao independente do PR #46, bloqueador 10): a
        // TokenVersion sob a qual senha (e, se aplicavel, o fator atual) foram
        // validados e capturada AQUI — antes de qualquer escrita — e e essa
        // versao, nunca uma mais nova encontrada depois, que o segredo pendente
        // fica vinculado. A FASE 2 do step-up (o CAS de verdade) e a geracao
        // do segredo pendente vivem na MESMA transacao: se um evento
        // concorrente subir a TokenVersion no meio do caminho, ambos falham
        // fechado, juntos.
        var tokenVersionEsperada = atendente.TokenVersion;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (codigoValidado is { } cv)
        {
            var casOk = await ExecutarCasDeStepUpAsync(
                atendente.Id, atendente.TenantId, tokenVersionEsperada,
                cv.EhTotp, request.CodigoAtual!.Trim(), cv.PassoValidado, agora, ct);

            if (!casOk)
            {
                await tx.RollbackAsync(ct);
                await BloqueioDeMfa.RegistrarFalhaAsync(db, atendente.Id, agora, ct);
                throw UsuariosServiceException.CodigoMfaInvalido();
            }
        }

        var resposta = await GerarSegredoPendenteNaTransacaoAsync(atendente, tokenVersionEsperada, agora, ct);

        if (resposta is null)
        {
            await tx.RollbackAsync(ct);
            throw UsuariosServiceException.OperacaoDesatualizada();
        }

        await tx.CommitAsync(ct);
        return resposta;
    }

    public Task<ConfirmarMfaResponse> ConfirmarAsync(
        IdentidadeParaMfa identidade, ConfirmarMfaRequest request, string? ip, CancellationToken ct = default) =>
        ConfirmarNucleoAsync(identidade, request.Codigo, ip, hashDoDesafioParaConsumir: null, tokenVersionEsperada: null, ct);

    /// <remarks>
    /// CORRECAO (revisao independente do PR #46, bloqueador 11) — step-up e a
    /// desativacao vivem na MESMA transacao. O porteiro final reconfere a
    /// politica obrigatoria (correlacionada contra <c>Tenants</c>) no instante
    /// exato do commit — o estado proibido "politica ligada + privilegiado com
    /// MfaAtivo=false por desativacao voluntaria" nunca acontece: se
    /// <see cref="AtivarObrigatoriedadeAsync"/> vencer a corrida, esta
    /// desativacao falha; se esta vencer, a ativacao da politica aborta por
    /// achar a conta sem MFA.
    /// </remarks>
    public async Task DesativarAsync(
        IdentidadeParaMfa identidade, DesativarMfaRequest request, string? ip, CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;
        var atendente = await CarregarAsync(identidade, ct);

        if (!atendente.MfaAtivo)
        {
            throw UsuariosServiceException.MfaNaoAtivo();
        }

        if (!hash.Verificar(request.SenhaAtual ?? string.Empty, atendente.SenhaHash))
        {
            throw UsuariosServiceException.SenhaAtualIncorreta();
        }

        // Pre-checagem SO para mensagem mais util — o porteiro final abaixo
        // reconfere a mesma coisa no instante do commit.
        if (await PoliticaDeMfa.ExigeAsync(db, atendente.TenantId, atendente.Perfil, atendente.Cargo, ct))
        {
            throw UsuariosServiceException.MfaObrigatorioNaoPodeSerDesativado();
        }

        var (ehTotp, passoValidado) = await ValidarCodigoDeStepUpAsync(atendente.Id, request.Codigo, agora, ct);

        var tokenVersionEsperada = atendente.TokenVersion;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var casOk = await ExecutarCasDeStepUpAsync(
            atendente.Id, atendente.TenantId, tokenVersionEsperada,
            ehTotp, (request.Codigo ?? string.Empty).Trim(), passoValidado, agora, ct);

        if (!casOk)
        {
            await tx.RollbackAsync(ct);
            await BloqueioDeMfa.RegistrarFalhaAsync(db, atendente.Id, agora, ct);
            throw UsuariosServiceException.CodigoMfaInvalido();
        }

        // PORTEIRO FINAL: reconfere Ativo, TokenVersion (implicito — o lock do
        // step-up bem-sucedido acima ja garante que ninguem mais mexeu nesta
        // linha nesta transacao) e, via subconsulta correlacionada contra
        // Tenants, que a politica obrigatoria AINDA nao exige MFA para esta
        // conta no instante exato da escrita.
        var desativou = await db.Atendentes
            .Where(a => a.Id == atendente.Id
                     && a.TenantId == identidade.TenantId
                     && a.Ativo
                     && a.MfaAtivo
                     && !(db.Tenants.Any(t => t.Id == a.TenantId && t.MfaObrigatorioParaAdministradores)
                          && (a.Perfil == PerfilDeAtendente.Administrador || a.Cargo == CargoNaServentia.Titular)))
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

        if (desativou != 1)
        {
            await tx.RollbackAsync(ct);
            throw UsuariosServiceException.MfaObrigatorioNaoPodeSerDesativado();
        }

        await db.CodigosDeRecuperacaoMfa.Where(c => c.AtendenteId == atendente.Id).ExecuteDeleteAsync(ct);
        await db.DesafiosDeMfa
            .Where(d => d.AtendenteId == atendente.Id && d.ConsumidoEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.ConsumidoEm, agora), ct);

        await AutenticacaoService.RegistrarEventoAsync(
            db, identidade.TenantId, atendente.Id, atorAtendenteId: null, atorExterno: null,
            TipoDeEventoDeSeguranca.MfaDesativado, ip, motivo: null, ct);

        await tx.CommitAsync(ct);

        log.LogInformation("MFA desativado por {AtendenteId}", atendente.Id);
    }

    /// <remarks>
    /// CORRECAO (revisao independente do PR #46, bloqueador 11) — step-up e a
    /// substituicao dos codigos vivem na MESMA transacao. Como o alvo E o
    /// proprio ator (auto-servico), o lock de linha que o step-up bem-sucedido
    /// mantem ate o commit ja e suficiente: nenhuma TokenVersion nova pode
    /// aparecer nesta linha antes do fim da transacao.
    /// </remarks>
    public async Task<RegenerarCodigosResponse> RegenerarCodigosAsync(
        IdentidadeParaMfa identidade, RegenerarCodigosRequest request, string? ip, CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;
        var atendente = await CarregarAsync(identidade, ct);

        if (!atendente.MfaAtivo)
        {
            throw UsuariosServiceException.MfaNaoAtivo();
        }

        if (!hash.Verificar(request.SenhaAtual ?? string.Empty, atendente.SenhaHash))
        {
            throw UsuariosServiceException.SenhaAtualIncorreta();
        }

        var (ehTotp, passoValidado) = await ValidarCodigoDeStepUpAsync(atendente.Id, request.Codigo, agora, ct);

        var tokenVersionEsperada = atendente.TokenVersion;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var casOk = await ExecutarCasDeStepUpAsync(
            atendente.Id, atendente.TenantId, tokenVersionEsperada,
            ehTotp, (request.Codigo ?? string.Empty).Trim(), passoValidado, agora, ct);

        if (!casOk)
        {
            await tx.RollbackAsync(ct);
            await BloqueioDeMfa.RegistrarFalhaAsync(db, atendente.Id, agora, ct);
            throw UsuariosServiceException.CodigoMfaInvalido();
        }

        var codigosEmClaro = Enumerable.Range(0, QuantidadeDeCodigosDeRecuperacao)
            .Select(_ => SegredosAleatoriosMfa.NovoCodigoDeRecuperacao())
            .ToList();

        await db.CodigosDeRecuperacaoMfa.Where(c => c.AtendenteId == atendente.Id).ExecuteDeleteAsync(ct);

        db.CodigosDeRecuperacaoMfa.AddRange(codigosEmClaro.Select(codigoClaro => new CodigoDeRecuperacaoMfa
        {
            TenantId = identidade.TenantId,
            AtendenteId = atendente.Id,
            HashDoCodigo = VerificadorDeFatorMfa.HashDoCodigoDeRecuperacao(codigoClaro)
        }));

        await db.SaveChangesAsync(ct);

        await AutenticacaoService.RegistrarEventoAsync(
            db, identidade.TenantId, atendente.Id, atorAtendenteId: null, atorExterno: null,
            TipoDeEventoDeSeguranca.CodigosDeRecuperacaoRegenerados, ip, motivo: null, ct);

        await tx.CommitAsync(ct);

        return new RegenerarCodigosResponse(codigosEmClaro.Select(SegredosAleatoriosMfa.FormatarCodigoDeRecuperacao).ToList());
    }

    public async Task<MfaSituacaoResponse> ObterSituacaoAsync(IdentidadeParaMfa identidade, CancellationToken ct = default)
    {
        var atendente = await CarregarAsync(identidade, ct);

        var obrigatorio = await PoliticaDeMfa.ExigeAsync(db, atendente.TenantId, atendente.Perfil, atendente.Cargo, ct);

        int? restantes = atendente.MfaAtivo
            ? await db.CodigosDeRecuperacaoMfa.CountAsync(c => c.AtendenteId == atendente.Id && c.UsadoEm == null, ct)
            : null;

        var agora = DateTime.UtcNow;

        // Conta so o que ainda dispensaria o segundo fator HOJE: expirado ou
        // revogado ja e inerte, e mostra-los na tela seria oferecer uma acao
        // ("esquecer") que nao muda nada.
        var dispositivosConfiaveis = await db.DispositivosConfiaveis
            .CountAsync(d => d.AtendenteId == atendente.Id
                          && d.TenantId == atendente.TenantId
                          && d.RevogadoEm == null
                          && d.ExpiraEm > agora, ct);

        return new MfaSituacaoResponse(
            atendente.MfaAtivo, obrigatorio, restantes, atendente.MfaConfirmadoEm, dispositivosConfiaveis);
    }

    /// <inheritdoc cref="IMfaService.EsquecerDispositivosConfiaveisAsync"/>
    public async Task<int> EsquecerDispositivosConfiaveisAsync(
        IdentidadeParaMfa identidade, CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;

        // Sem step-up de proposito: revogar confianca so PODE aumentar a
        // seguranca da conta (o proximo login volta a exigir o segundo fator).
        // Mesma razao pela qual sair da sessao nao pede a senha de novo.
        var revogados = await db.DispositivosConfiaveis
            .Where(d => d.AtendenteId == identidade.AtendenteId
                     && d.TenantId == identidade.TenantId
                     && d.RevogadoEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.RevogadoEm, agora), ct);

        // O cookie deste navegador tambem sai — nao e o que garante a
        // revogacao (o registro no banco e), so evita deixar um cookie morto
        // sendo reenviado a cada login.
        cookieDeDispositivo.Limpar();

        log.LogInformation(
            "Dispositivos confiaveis revogados por {AtendenteId}: {Quantidade}",
            identidade.AtendenteId, revogados);

        return revogados;
    }

    // ---------------------------------------------- matricula privilegiada (ticket)

    /// <remarks>
    /// SEC-03 (bootstrap/matricula) — nunca devolve JWT: a conta ainda nao tem
    /// MFA, entao nao ha sessao completa a emitir. O consumo do ticket e
    /// atomico e so no sucesso — a mesma disciplina do desafio de login.
    /// </remarks>
    /// <remarks>
    /// CORRECAO (revisao independente do PR #46, bloqueadores 2 e 3) — duas
    /// falhas na versao anterior:
    /// <list type="number">
    /// <item>O consumo do ticket conferia so identidade/expiracao/uso, nunca
    /// <see cref="DesafioDeMfa.TokenVersionNoDesafio"/> contra o
    /// <see cref="Atendente.TokenVersion"/> ATUAL — um ticket emitido antes de
    /// uma troca de senha, desativacao, mudanca de Perfil/Cargo ou reset de
    /// MFA continuava valendo depois. Corrigido com uma subconsulta
    /// correlacionada dentro do proprio <c>WHERE</c> do <c>ExecuteUpdateAsync</c>
    /// — o SQL Server le o estado ATUAL do Atendente no instante da escrita,
    /// nunca um valor lido antes em memoria neste metodo.</item>
    /// <item>O consumo do ticket e a criacao do desafio de matricula eram DUAS
    /// operacoes separadas, sem transacao — se a segunda falhasse, o ticket
    /// ficava queimado sem produzir desafio nenhum. Agora as duas vivem na
    /// MESMA transacao.</item>
    /// </list>
    /// O consumo em si (o <c>ExecuteUpdateAsync</c> abaixo) e o unico ponto de
    /// serializacao: sob duas requisicoes simultaneas com o MESMO ticket, o SQL
    /// Server serializa as duas escritas na mesma linha e so a primeira encontra
    /// <c>ConsumidoEm IS NULL</c> — a segunda nao afeta linha nenhuma e recebe
    /// <see cref="UsuariosServiceException.TicketInvalido"/>, nunca um segundo
    /// desafio.
    /// </remarks>
    public async Task<DesafioMfaResponse> IniciarMatriculaPrivilegiadaAsync(
        MatricularPrivilegiadoRequest request, string? ip, CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;
        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;

        var atendente = await db.Atendentes.AsNoTracking().FirstOrDefaultAsync(a => a.Email == email, ct);

        // Mesma disciplina anti-timing do login: confere a senha mesmo com
        // e-mail inexistente.
        var hashParaConferir = atendente?.SenhaHash ?? HashDescartavel;
        var senhaConfere = hash.Verificar(request.Senha ?? string.Empty, hashParaConferir);

        if (atendente is null || !senhaConfere)
        {
            throw UsuariosServiceException.CredenciaisInvalidas();
        }

        // Pre-checagens SO para uma mensagem mais util no caso comum, sem
        // corrida — nunca a decisao final. Quem decide de verdade e o WHERE
        // atomico do consumo do ticket, logo abaixo, que revalida tudo isto
        // (e mais TokenVersion) no instante da escrita.
        var privilegiado = atendente.Perfil == PerfilDeAtendente.Administrador || atendente.Cargo == CargoNaServentia.Titular;
        if (!atendente.Ativo)
        {
            throw UsuariosServiceException.CredenciaisInvalidas();
        }
        if (!privilegiado)
        {
            throw UsuariosServiceException.ContaNaoPrivilegiada();
        }
        if (atendente.MfaAtivo)
        {
            throw UsuariosServiceException.MfaJaAtivo();
        }

        var atendenteId = atendente.Id; // Id e TenantId sao imutaveis — seguros vindos da leitura acima.
        var tenantId = atendente.TenantId;
        var hashDoTicket = SegredosAleatoriosMfa.Sha256Hex((request.Ticket ?? string.Empty).Trim());

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // O PORTEIRO UNICO: hash, tipo, identidade, nao consumido, nao
        // expirado — e, via subconsulta correlacionada, o estado ATUAL do
        // atendente (ativo, ainda privilegiado, ainda sem MFA, TokenVersion
        // identica a gravada no ticket na emissao). So consome se TUDO isto
        // for verdade no MESMO instante da escrita.
        var consumiu = await db.DesafiosDeMfa
            .Where(d => d.HashDoDesafio == hashDoTicket
                     && d.Tipo == TipoDeDesafioMfa.Ticket
                     && d.AtendenteId == atendenteId
                     && d.TenantId == tenantId
                     && d.ConsumidoEm == null
                     && d.ExpiraEm > agora
                     && db.Atendentes.Any(a => a.Id == atendenteId
                                            && a.Ativo
                                            && !a.MfaAtivo
                                            && (a.Perfil == PerfilDeAtendente.Administrador
                                                || a.Cargo == CargoNaServentia.Titular)
                                            && a.TokenVersion == d.TokenVersionNoDesafio))
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.ConsumidoEm, agora), ct);

        if (consumiu != 1)
        {
            await tx.RollbackAsync(ct);
            throw UsuariosServiceException.TicketInvalido();
        }

        // TokenVersion ATUAL, relida agora — ja provada igual a gravada no
        // ticket pelo proprio consumo acima; relida em vez de reaproveitar o
        // valor de `atendente` (lido ANTES da transacao) por precisao, nao por
        // necessidade de seguranca (o consumo acima ja e a garantia real).
        var tokenVersionAtual = await db.Atendentes.AsNoTracking()
            .Where(a => a.Id == atendenteId).Select(a => a.TokenVersion).SingleAsync(ct);

        var desafio = await AutenticacaoService.CriarDesafioAsync(
            db, atendenteId, tenantId, tokenVersionAtual,
            TipoDeDesafioMfa.MatriculaPrivilegiada, TtlDoDesafioDeMatricula, ip, ct);

        await tx.CommitAsync(ct);

        return desafio;
    }

    public async Task<IniciarMfaResponse> IniciarViaDesafioAsync(
        IniciarViaDesafioRequest request, CancellationToken ct = default)
    {
        var (identidade, tokenVersionNoDesafio) = await ResolverDesafioDeMatriculaAsync(request.Desafio, ct);
        var atendente = await CarregarAsync(identidade, ct);

        // Sem step-up extra aqui: a posse deste desafio JA prova senha + ticket
        // fora de banda, exigidos para cria-lo.
        //
        // CORRECAO (revisao independente do PR #46, bloqueador 10): o segredo
        // pendente fica vinculado a `tokenVersionNoDesafio` — a TokenVersion
        // sob a qual o ticket foi consumido (IniciarMatriculaPrivilegiadaAsync)
        // — NUNCA descartada como antes. Se a TokenVersion do atendente ja
        // mudou desde a criacao do desafio, a geracao falha fechada: nenhum
        // segredo/QR novo e entregue para um desafio obsoleto.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var resposta = await GerarSegredoPendenteNaTransacaoAsync(atendente, tokenVersionNoDesafio, DateTime.UtcNow, ct);

        if (resposta is null)
        {
            await tx.RollbackAsync(ct);
            throw UsuariosServiceException.DesafioMfaInvalido();
        }

        await tx.CommitAsync(ct);
        return resposta;
    }

    public async Task<ConfirmarMfaResponse> ConfirmarViaDesafioAsync(
        ConfirmarViaDesafioRequest request, string? ip, CancellationToken ct = default)
    {
        var (identidade, tokenVersionNoDesafio) = await ResolverDesafioDeMatriculaAsync(request.Desafio, ct);
        var hashDoDesafio = SegredosAleatoriosMfa.Sha256Hex(request.Desafio ?? string.Empty);

        return await ConfirmarNucleoAsync(identidade, request.Codigo, ip, hashDoDesafio, tokenVersionNoDesafio, ct);
    }

    // ------------------------------------------------------------ administrativo

    /// <remarks>
    /// CORRECAO (revisao independente do PR #46, bloqueador 11) — step-up e a
    /// emissao do ticket agora vivem na MESMA transacao, com um porteiro final
    /// sobre o ALVO (linha diferente da do ator — o lock de linha do step-up
    /// nao protege o alvo). O lock que o step-up mantem ate o commit protege o
    /// PROPRIO ator: nenhuma TokenVersion nova, democao de Perfil, ou perda do
    /// Cargo Titular do ator pode "atravessar" a autorizacao ja concedida. O
    /// alvo, por sua vez, e reconferido ao vivo no instante da escrita:
    /// continua privilegiado e, se e Titular, o ator TAMBEM continua Titular
    /// naquele exato instante — nunca a partir do <c>atorAtendente</c> lido no
    /// inicio do metodo.
    /// </remarks>
    public async Task<TicketResponse> EmitirTicketAsync(
        IdentidadeParaMfa ator, Guid atendenteIdAlvo, EmitirTicketRequest request, string? ip, CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;
        var atorAtendente = await CarregarEGarantirAdministradorAsync(ator, ct);

        if (!hash.Verificar(request.SenhaDoAtor ?? string.Empty, atorAtendente.SenhaHash))
        {
            throw UsuariosServiceException.SenhaAtualIncorreta();
        }

        // Pre-checagens SO para mensagem mais util — o porteiro final abaixo
        // e quem decide de verdade, reconferido no instante da escrita.
        var alvoPreCheck = await db.Atendentes.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == atendenteIdAlvo && a.TenantId == ator.TenantId, ct)
            ?? throw UsuariosServiceException.AtendenteNaoEncontrado();

        var privilegiadoPreCheck = alvoPreCheck.Perfil == PerfilDeAtendente.Administrador || alvoPreCheck.Cargo == CargoNaServentia.Titular;
        if (!privilegiadoPreCheck)
        {
            throw UsuariosServiceException.ContaNaoPrivilegiada();
        }

        GarantirTitularProtegido(alvoPreCheck, atorAtendente);

        var (ehTotp, passoValidado) = await ValidarCodigoDeStepUpAsync(atorAtendente.Id, request.CodigoDoAtor, agora, ct);

        var tokenVersionEsperadaDoAtor = atorAtendente.TokenVersion;
        var tokenEmClaro = SegredosAleatoriosMfa.NovoToken();
        var expiraEm = agora.Add(TtlDoTicket);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var casOk = await ExecutarCasDeStepUpAsync(
            atorAtendente.Id, atorAtendente.TenantId, tokenVersionEsperadaDoAtor,
            ehTotp, (request.CodigoDoAtor ?? string.Empty).Trim(), passoValidado, agora, ct);

        if (!casOk)
        {
            await tx.RollbackAsync(ct);
            await BloqueioDeMfa.RegistrarFalhaAsync(db, atorAtendente.Id, agora, ct);
            throw UsuariosServiceException.CodigoMfaInvalido();
        }

        // PORTEIRO FINAL sobre o ALVO: ainda privilegiado, e se Titular, o
        // ATOR ainda precisa ser Titular AGORA — reconferido ao vivo, nunca a
        // partir do atorAtendente/alvoPreCheck lidos antes da transacao.
        var tokenVersionAlvoAtual = await db.Atendentes.AsNoTracking()
            .Where(a => a.Id == atendenteIdAlvo
                     && a.TenantId == ator.TenantId
                     && (a.Perfil == PerfilDeAtendente.Administrador || a.Cargo == CargoNaServentia.Titular)
                     && !(a.Cargo == CargoNaServentia.Titular
                          && !db.Atendentes.Any(x => x.Id == atorAtendente.Id && x.Cargo == CargoNaServentia.Titular)))
            .Select(a => (int?)a.TokenVersion)
            .SingleOrDefaultAsync(ct);

        if (tokenVersionAlvoAtual is null)
        {
            await tx.RollbackAsync(ct);
            throw UsuariosServiceException.OperacaoDesatualizada();
        }

        db.DesafiosDeMfa.Add(new DesafioDeMfa
        {
            TenantId = ator.TenantId,
            AtendenteId = atendenteIdAlvo,
            HashDoDesafio = SegredosAleatoriosMfa.Sha256Hex(tokenEmClaro),
            Tipo = TipoDeDesafioMfa.Ticket,
            TokenVersionNoDesafio = tokenVersionAlvoAtual.Value,
            ExpiraEm = expiraEm,
            IpDeOrigem = ip
        });

        await db.SaveChangesAsync(ct);

        await AutenticacaoService.RegistrarEventoAsync(
            db, ator.TenantId, atendenteIdAlvo, ator.AtendenteId, atorExterno: null,
            TipoDeEventoDeSeguranca.TicketDeMatriculaEmitido, ip, motivo: null, ct);

        await tx.CommitAsync(ct);

        log.LogInformation("Ticket de matricula de MFA emitido por {Ator} para {Alvo}", ator.AtendenteId, atendenteIdAlvo);

        return new TicketResponse(tokenEmClaro, expiraEm);
    }

    /// <remarks>
    /// CORRECAO (revisao independente do PR #46, bloqueador 11) — mesma
    /// disciplina de <see cref="EmitirTicketAsync"/>: step-up e a mutacao do
    /// alvo na MESMA transacao, com porteiro final que reconfere a protecao do
    /// Titular ao vivo (nao a partir do <c>atorAtendente</c> lido antes).
    /// </remarks>
    public async Task ResetarAsync(
        IdentidadeParaMfa ator, Guid atendenteIdAlvo, ResetarMfaRequest request, string? ip, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Motivo))
        {
            throw UsuariosServiceException.MotivoObrigatorio();
        }

        var agora = DateTime.UtcNow;
        var atorAtendente = await CarregarEGarantirAdministradorAsync(ator, ct);

        if (!hash.Verificar(request.SenhaDoAtor ?? string.Empty, atorAtendente.SenhaHash))
        {
            throw UsuariosServiceException.SenhaAtualIncorreta();
        }

        var alvoPreCheck = await db.Atendentes.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == atendenteIdAlvo && a.TenantId == ator.TenantId, ct)
            ?? throw UsuariosServiceException.AtendenteNaoEncontrado();

        GarantirTitularProtegido(alvoPreCheck, atorAtendente);

        var (ehTotp, passoValidado) = await ValidarCodigoDeStepUpAsync(atorAtendente.Id, request.CodigoDoAtor, agora, ct);

        var tokenVersionEsperadaDoAtor = atorAtendente.TokenVersion;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var casOk = await ExecutarCasDeStepUpAsync(
            atorAtendente.Id, atorAtendente.TenantId, tokenVersionEsperadaDoAtor,
            ehTotp, (request.CodigoDoAtor ?? string.Empty).Trim(), passoValidado, agora, ct);

        if (!casOk)
        {
            await tx.RollbackAsync(ct);
            await BloqueioDeMfa.RegistrarFalhaAsync(db, atorAtendente.Id, agora, ct);
            throw UsuariosServiceException.CodigoMfaInvalido();
        }

        // PORTEIRO FINAL: se o alvo e Titular, o ator ainda precisa ser
        // Titular AGORA — reconferido ao vivo. Conta fica SEM LOGIN ate nova
        // matricula: se a politica exige MFA para ela, o proximo login
        // (AutenticacaoService) recusa com MfaObrigatorioNaoMatriculado ate o
        // alvo passar pelo ticket de novo.
        var resetou = await db.Atendentes
            .Where(a => a.Id == atendenteIdAlvo
                     && a.TenantId == ator.TenantId
                     && !(a.Cargo == CargoNaServentia.Titular
                          && !db.Atendentes.Any(x => x.Id == atorAtendente.Id && x.Cargo == CargoNaServentia.Titular)))
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

        if (resetou != 1)
        {
            await tx.RollbackAsync(ct);
            throw UsuariosServiceException.ApenasTitularReseteiaTitular();
        }

        await db.CodigosDeRecuperacaoMfa.Where(c => c.AtendenteId == atendenteIdAlvo).ExecuteDeleteAsync(ct);
        await db.DesafiosDeMfa
            .Where(d => d.AtendenteId == atendenteIdAlvo && d.ConsumidoEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.ConsumidoEm, agora), ct);

        await AutenticacaoService.RegistrarEventoAsync(
            db, ator.TenantId, atendenteIdAlvo, ator.AtendenteId, atorExterno: null,
            TipoDeEventoDeSeguranca.ResetAdministrativo, ip, request.Motivo, ct);

        await tx.CommitAsync(ct);

        log.LogInformation("MFA de {Alvo} resetado por {Ator}: {Motivo}", atendenteIdAlvo, ator.AtendenteId, request.Motivo);
    }

    /// <remarks>
    /// SEC-03 (regra final 3) — transacional, SERIALIZABLE: entre a checagem
    /// de "todo mundo matriculado" e o UPDATE que liga a politica, ninguem novo
    /// pode aparecer sem MFA e escapar da verificacao. Sobe TokenVersion de
    /// TODOS os afetados (incluindo o proprio ator) — qualquer sessao
    /// administrativa criada ANTES do flip deixa de valer no proximo request.
    /// </remarks>
    public async Task<LoginResponse> AtivarObrigatoriedadeAsync(
        IdentidadeParaMfa ator, AtivarObrigatoriedadeRequest request, string? ip, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Motivo))
        {
            throw UsuariosServiceException.MotivoObrigatorio();
        }

        var agora = DateTime.UtcNow;
        var atorAtendente = await CarregarEGarantirAdministradorAsync(ator, ct);

        if (!hash.Verificar(request.SenhaDoAtor ?? string.Empty, atorAtendente.SenhaHash))
        {
            throw UsuariosServiceException.SenhaAtualIncorreta();
        }

        var (ehTotp, passoValidado) = await ValidarCodigoDeStepUpAsync(atorAtendente.Id, request.CodigoDoAtor, agora, ct);

        var tokenVersionEsperadaDoAtor = atorAtendente.TokenVersion;

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        // CORRECAO (revisao independente do PR #46, bloqueador 11) — o CAS do
        // step-up (fase 2) agora vive DENTRO da mesma transacao SERIALIZABLE
        // do flip, nao antes dela: uma autorizacao validada sob uma
        // TokenVersion antiga nunca commita sob uma nova.
        var casOk = await ExecutarCasDeStepUpAsync(
            atorAtendente.Id, atorAtendente.TenantId, tokenVersionEsperadaDoAtor,
            ehTotp, (request.CodigoDoAtor ?? string.Empty).Trim(), passoValidado, agora, ct);

        if (!casOk)
        {
            await tx.RollbackAsync(ct);
            await BloqueioDeMfa.RegistrarFalhaAsync(db, atorAtendente.Id, agora, ct);
            throw UsuariosServiceException.CodigoMfaInvalido();
        }

        var pendentes = await db.Atendentes.AsNoTracking()
            .Where(a => a.TenantId == ator.TenantId && a.Ativo && !a.MfaAtivo
                     && (a.Perfil == PerfilDeAtendente.Administrador || a.Cargo == CargoNaServentia.Titular))
            .Select(a => a.Email)
            .ToListAsync(ct);

        if (pendentes.Count > 0)
        {
            await tx.RollbackAsync(ct);
            throw UsuariosServiceException.FlipAbortadoContasSemMfa(pendentes);
        }

        var ligou = await db.Tenants
            .Where(t => t.Id == ator.TenantId && !t.MfaObrigatorioParaAdministradores)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.MfaObrigatorioParaAdministradores, true), ct);

        if (ligou != 1)
        {
            // Ja estava ligado (corrida com outra chamada, ou repeticao) — nada
            // a fazer; devolve sessao atual sem reemitir TokenVersion de novo.
            await tx.RollbackAsync(ct);

            return new LoginResponse(
                string.Empty, DateTime.MinValue,
                await MapeadorDeSessao.MapearAtendenteAsync(db, atorAtendente, ct),
                await MapeadorDeSessao.ObterTenantAsync(db, ator.TenantId, ct));
        }

        var afetados = await db.Atendentes
            .Where(a => a.TenantId == ator.TenantId && a.Ativo
                     && (a.Perfil == PerfilDeAtendente.Administrador || a.Cargo == CargoNaServentia.Titular))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.TokenVersion, a => a.TokenVersion + 1), ct);

        await AutenticacaoService.RegistrarEventoAsync(
            db, ator.TenantId, ator.AtendenteId, ator.AtendenteId, atorExterno: null,
            TipoDeEventoDeSeguranca.PoliticaDeMfaAtivada, ip, $"{request.Motivo} ({afetados} conta(s) afetada(s))", ct);

        await tx.CommitAsync(ct);

        log.LogWarning(
            "Politica de MFA obrigatorio ATIVADA para Administrador/Titular no tenant {TenantId} por {Ator} ({Afetados} conta(s))",
            ator.TenantId, ator.AtendenteId, afetados);

        // O proprio ator tambem teve o TokenVersion subido — devolve sessao
        // nova para nao derrubar quem acabou de executar a operacao.
        var atorAtualizado = await db.Atendentes.AsNoTracking().FirstAsync(a => a.Id == ator.AtendenteId, ct);
        var (token, expiraEm) = tokens.Gerar(atorAtualizado);

        return new LoginResponse(
            token, expiraEm,
            await MapeadorDeSessao.MapearAtendenteAsync(db, atorAtualizado, ct),
            await MapeadorDeSessao.ObterTenantAsync(db, ator.TenantId, ct));
    }

    // -------------------------------------------------------------- privado

    private async Task<Atendente> CarregarAsync(IdentidadeParaMfa identidade, CancellationToken ct) =>
        await db.Atendentes.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == identidade.AtendenteId && a.TenantId == identidade.TenantId, ct)
            ?? throw UsuariosServiceException.AtendenteNaoEncontrado();

    private async Task<Atendente> CarregarEGarantirAdministradorAsync(IdentidadeParaMfa ator, CancellationToken ct)
    {
        var atendente = await CarregarAsync(ator, ct);

        if (atendente.Perfil != PerfilDeAtendente.Administrador)
        {
            throw UsuariosServiceException.SemPermissao();
        }

        return atendente;
    }

    /// <summary>Alvo Titular so pode ser resetado/ter ticket emitido por quem tambem e Titular.</summary>
    private static void GarantirTitularProtegido(Atendente alvo, Atendente ator)
    {
        if (alvo.Cargo == CargoNaServentia.Titular && ator.Cargo != CargoNaServentia.Titular)
        {
            throw UsuariosServiceException.ApenasTitularReseteiaTitular();
        }
    }

    /// <summary>
    /// Grava o segredo pendente SO SE a TokenVersion atual da linha ainda for
    /// exatamente <paramref name="tokenVersionEsperada"/> — a versao sob a
    /// qual a autorizacao que originou esta cerimonia (senha, senha+step-up,
    /// ou ticket/desafio) foi validada.
    /// </summary>
    /// <remarks>
    /// CORRECAO (revisao independente do PR #46, bloqueador 10) — a versao
    /// anterior gravava <c>MfaPendenteTokenVersion = a.TokenVersion</c>
    /// auto-referenciada ao valor que estivesse no banco quando o UPDATE
    /// rodasse — o que "adotava" silenciosamente uma TokenVersion NOVA se algo
    /// tivesse subido ela entre a validacao da autorizacao e este UPDATE,
    /// recarimbando uma autorizacao antiga como se fosse da versao nova.
    /// Agora o WHERE exige <c>a.TokenVersion == tokenVersionEsperada</c> —a
    /// mesma variavel que e gravada em <c>MfaPendenteTokenVersion</c> — nunca
    /// um valor lido da linha na hora. Falha fechado (devolve <c>null</c>) se
    /// a linha nao bater mais: o chamador NUNCA devolve segredo/QR nesse caso.
    /// </remarks>
    private async Task<IniciarMfaResponse?> GerarSegredoPendenteNaTransacaoAsync(
        Atendente atendente, int tokenVersionEsperada, DateTime agora, CancellationToken ct)
    {
        var segredoBase32 = ValidadorDeTotp.GerarSegredoBase32();
        var segredoCifrado = cifra.Cifrar(segredoBase32, atendente.TenantId, atendente.Id);
        var expiraEm = agora.Add(TtlDoSegredoPendente);

        var gravou = await db.Atendentes
            .Where(a => a.Id == atendente.Id
                     && a.TenantId == atendente.TenantId
                     && a.Ativo
                     && a.TokenVersion == tokenVersionEsperada)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.MfaSegredoPendenteCifrado, segredoCifrado)
                .SetProperty(a => a.MfaPendenteExpiraEm, expiraEm)
                .SetProperty(a => a.MfaPendenteTokenVersion, tokenVersionEsperada), ct);

        if (gravou != 1)
        {
            return null;
        }

        var uri = ValidadorDeTotp.MontarOtpauthUri(segredoBase32, "CRM Cartório", atendente.Email);

        return new IniciarMfaResponse(uri, segredoBase32, expiraEm);
    }

    /// <remarks>
    /// SEC-03 — porteiro unico da confirmacao. Quando chamado pela matricula
    /// privilegiada, <paramref name="hashDoDesafioParaConsumir"/> e
    /// <paramref name="tokenVersionEsperada"/> fazem o desafio ser reconfirmado
    /// e consumido na MESMA transacao que ativa o MFA — nunca antes, nunca
    /// separado.
    /// </remarks>
    private async Task<ConfirmarMfaResponse> ConfirmarNucleoAsync(
        IdentidadeParaMfa identidade, string? codigo, string? ip,
        string? hashDoDesafioParaConsumir, int? tokenVersionEsperada, CancellationToken ct)
    {
        var agora = DateTime.UtcNow;

        // CORRECAO (revisao independente do PR #46, bloqueador 15) — reserva
        // a tentativa no PROPRIO desafio de matricula ANTES de qualquer outra
        // validacao, mesmo padrao (e mesmo porteiro PERSISTENTE — rate limit
        // HTTP por IP nao substitui isto) do segundo fator no login
        // (AutenticacaoService.ConcluirLoginMfaAsync, "Passo A"). Erro
        // (codigo errado depois) NAO marca ConsumidoEm — so incrementa
        // TentativasDeCodigo, que persiste mesmo que o resto da chamada
        // falhe, porque roda FORA da transacao da ativacao. So se afetar
        // EXATAMENTE 1 linha (hash certo, tipo certo, ainda nao consumido,
        // nao expirado, e AINDA com tentativas sobrando) a validacao segue —
        // um desafio ja esgotado (5 tentativas) fica permanentemente inutil,
        // mesmo que nunca tenha sido formalmente "consumido".
        if (hashDoDesafioParaConsumir is not null)
        {
            var reservou = await db.DesafiosDeMfa
                .Where(d => d.HashDoDesafio == hashDoDesafioParaConsumir
                         && d.Tipo == TipoDeDesafioMfa.MatriculaPrivilegiada
                         && d.ConsumidoEm == null
                         && d.ExpiraEm > agora
                         && d.TentativasDeCodigo < LimiteDeTentativasPorDesafio)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.TentativasDeCodigo, d => d.TentativasDeCodigo + 1), ct);

            if (reservou != 1)
            {
                throw UsuariosServiceException.DesafioMfaInvalido();
            }
        }

        var atendente = await CarregarAsync(identidade, ct);

        if (atendente.MfaSegredoPendenteCifrado is null
            || atendente.MfaPendenteExpiraEm is not { } expiraEm
            || expiraEm <= agora)
        {
            throw UsuariosServiceException.CodigoMfaInvalido();
        }

        // CORRECAO (revisao independente do PR #46, bloqueador 5) — mensagem
        // mais util no caso comum, sem corrida: se isto e uma confirmacao SEM
        // desafio de matricula (hashDoDesafioParaConsumir nulo — ou seja, nao
        // veio do fluxo de ticket) E a conta ainda nao tem MFA ativo E ja e
        // privilegiada, a PRIMEIRA matricula so pode nascer de ticket. Isto
        // fecha a promocao-no-meio-do-caminho: comum inicia (recebe segredo
        // pendente), e promovido a Administrador/Titular ANTES de confirmar, e
        // tenta terminar a matricula sem nunca ter passado por um ticket. Quem
        // decide de verdade e o WHERE atomico logo abaixo, que reconfere a
        // mesma condicao no instante exato do commit — esta e so a mensagem.
        var privilegiadoAgora = atendente.Perfil == PerfilDeAtendente.Administrador
            || atendente.Cargo == CargoNaServentia.Titular;

        if (hashDoDesafioParaConsumir is null && !atendente.MfaAtivo && privilegiadoAgora)
        {
            throw UsuariosServiceException.MatriculaPrivilegiadaExigeTicket();
        }

        string segredoPendente;
        try
        {
            segredoPendente = cifra.Decifrar(atendente.MfaSegredoPendenteCifrado, atendente.TenantId, atendente.Id);
        }
        catch (CryptographicException)
        {
            throw UsuariosServiceException.CodigoMfaInvalido();
        }

        if (!ValidadorDeTotp.Validar(segredoPendente, (codigo ?? string.Empty).Trim(), out var passo))
        {
            // MFA ainda nao esta ativo — nao ha o que bloquear por conta aqui;
            // um TTL curto de 10 min no segredo pendente ja limita a janela.
            throw UsuariosServiceException.CodigoMfaInvalido();
        }

        var codigosEmClaro = Enumerable.Range(0, QuantidadeDeCodigosDeRecuperacao)
            .Select(_ => SegredosAleatoriosMfa.NovoCodigoDeRecuperacao())
            .ToList();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // O PORTEIRO FINAL (bloqueador 5): a MESMA condicao da pre-checagem
        // acima, mas reconferida contra o estado ATUAL da linha no instante do
        // UPDATE, nunca a partir do `atendente` lido no inicio do metodo.
        // Sem desafio de matricula, so pode concluir se: ja tinha MFA ativo
        // (troca de autenticador legitima, ja gated por step-up em
        // IniciarAsync) OU a conta ainda NAO e privilegiada agora. Com
        // desafio de matricula (hashDoDesafioParaConsumir != null), a
        // exigencia de ticket ja foi provada por IniciarMatriculaPrivilegiadaAsync
        // — nada extra a checar aqui.
        //
        // CORRECAO (revisao independente do PR #46, bloqueador 8):
        // `a.TokenVersion == a.MfaPendenteTokenVersion` compara as DUAS
        // colunas da linha ATUAL entre si (nunca um valor lido em memoria) —
        // se qualquer evento subiu a TokenVersion depois que este segredo
        // pendente foi gerado (troca de senha, Perfil/Cargo, reset, politica
        // obrigatoria), a igualdade quebra e a confirmacao falha fechada,
        // mesmo com o codigo TOTP correto. Vale para toda cerimonia sem
        // ticket — voluntaria E troca de autenticador; o fluxo de ticket
        // continua com sua propria protecao (tokenVersionEsperada, acima).
        var confirmou = await db.Atendentes
            .Where(a => a.Id == atendente.Id
                     && a.TenantId == identidade.TenantId
                     && a.Ativo
                     && a.MfaSegredoPendenteCifrado == atendente.MfaSegredoPendenteCifrado
                     && a.TokenVersion == a.MfaPendenteTokenVersion
                     && (tokenVersionEsperada == null || a.TokenVersion == tokenVersionEsperada)
                     && (hashDoDesafioParaConsumir != null
                         || a.MfaAtivo
                         || !(a.Perfil == PerfilDeAtendente.Administrador || a.Cargo == CargoNaServentia.Titular)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.MfaAtivo, true)
                .SetProperty(a => a.MfaSegredoCifrado, atendente.MfaSegredoPendenteCifrado)
                .SetProperty(a => a.MfaSegredoPendenteCifrado, (byte[]?)null)
                .SetProperty(a => a.MfaPendenteExpiraEm, (DateTime?)null)
                .SetProperty(a => a.MfaPendenteTokenVersion, (int?)null)
                .SetProperty(a => a.MfaConfirmadoEm, agora)
                .SetProperty(a => a.MfaUltimoPassoUsado, passo)
                .SetProperty(a => a.MfaFalhasConsecutivas, 0)
                .SetProperty(a => a.MfaNivelDeBloqueio, (byte)0)
                .SetProperty(a => a.MfaBloqueadoAte, (DateTime?)null)
                // CORRECAO (bloqueador B19, sexta revisao independente): toda
                // confirmacao bem-sucedida libera a exigencia de rematricula
                // pos-emergencia, no MESMO UPDATE que reativa o MFA — nao ha
                // distincao entre "confirmando com o ticket do break-glass" e
                // qualquer outra confirmacao, porque o proprio ticket ja e a
                // prova de quem autorizou. Contas que nunca passaram por
                // break-glass ja nascem com este campo em false (no-op).
                .SetProperty(a => a.RematriculaObrigatoriaAposEmergencia, false)
                // CORRECAO (bloqueador 14): grava UltimoAcessoEm no MESMO
                // UPDATE que de fato confirma o MFA — antes disto so existia
                // na entidade em memoria usada pra montar a resposta,
                // divergindo do banco se algo (evento, recovery codes)
                // falhasse depois. So aqui: nunca em IniciarAsync/
                // IniciarViaDesafioAsync, nunca quando este WHERE nao afeta
                // nenhuma linha (codigo errado, TokenVersion perdida,
                // challenge invalido/esgotado).
                .SetProperty(a => a.UltimoAcessoEm, agora)
                .SetProperty(a => a.TokenVersion, a => a.TokenVersion + 1), ct);

        if (confirmou != 1)
        {
            await tx.RollbackAsync(ct);
            throw UsuariosServiceException.CodigoMfaInvalido();
        }

        if (hashDoDesafioParaConsumir is not null)
        {
            var consumiuDesafio = await db.DesafiosDeMfa
                .Where(d => d.HashDoDesafio == hashDoDesafioParaConsumir && d.ConsumidoEm == null && d.ExpiraEm > agora)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.ConsumidoEm, agora), ct);

            if (consumiuDesafio != 1)
            {
                await tx.RollbackAsync(ct);
                throw UsuariosServiceException.DesafioMfaInvalido();
            }
        }

        await db.CodigosDeRecuperacaoMfa.Where(c => c.AtendenteId == atendente.Id).ExecuteDeleteAsync(ct);

        db.CodigosDeRecuperacaoMfa.AddRange(codigosEmClaro.Select(codigoClaro => new CodigoDeRecuperacaoMfa
        {
            TenantId = identidade.TenantId,
            AtendenteId = atendente.Id,
            HashDoCodigo = VerificadorDeFatorMfa.HashDoCodigoDeRecuperacao(codigoClaro)
        }));

        await db.SaveChangesAsync(ct);

        // CORRECAO (revisao independente do PR #46, bloqueador 13A): o
        // evento agora e gravado ANTES do commit, na MESMA transacao que
        // ativa o MFA — nunca depois. Antes, se este registro falhasse apos
        // o commit, MFA/recovery codes ja tinham mudado mas a request podia
        // terminar em erro sem entregar JWT/recovery codes ao chamador.
        await AutenticacaoService.RegistrarEventoAsync(
            db, identidade.TenantId, atendente.Id, atorAtendenteId: null, atorExterno: null,
            TipoDeEventoDeSeguranca.MfaAtivado, ip, motivo: null, ct);

        await tx.CommitAsync(ct);

        atendente.MfaAtivo = true;
        atendente.TenantId = identidade.TenantId;
        atendente.TokenVersion = (tokenVersionEsperada ?? atendente.TokenVersion) + 1;
        atendente.UltimoAcessoEm = agora;

        var (token, expiraJwt) = tokens.Gerar(atendente);

        log.LogInformation("MFA ativado para {AtendenteId}", atendente.Id);

        var sessao = new LoginResponse(
            token, expiraJwt,
            await MapeadorDeSessao.MapearAtendenteAsync(db, atendente, ct),
            await MapeadorDeSessao.ObterTenantAsync(db, identidade.TenantId, ct));

        return new ConfirmarMfaResponse(
            sessao, codigosEmClaro.Select(SegredosAleatoriosMfa.FormatarCodigoDeRecuperacao).ToList());
    }

    /// <summary>
    /// FASE 1 do step-up — so a corretude criptografica do codigo, SEM
    /// transacao aberta. Se o codigo for invalido, registra a falha (que
    /// precisa commitar sozinha, sem risco de ser desfeita por um rollback de
    /// operacao sensivel que ainda nem comecou) e lanca
    /// <see cref="UsuariosServiceException.CodigoMfaInvalido"/> diretamente.
    /// </summary>
    /// <remarks>
    /// CORRECAO (revisao independente do PR #46, bloqueador 11) — dividido em
    /// duas fases de proposito. A versao anterior fazia tudo dentro da PROPRIA
    /// transacao (commitada aqui mesmo, ANTES da operacao sensivel comecar) —
    /// um "boolean stepupOk" desconectado da mutacao final: nada impedia o
    /// estado da conta (TokenVersion, Perfil/Cargo do ator ou do alvo) de
    /// mudar DEPOIS do step-up e ANTES da acao sensivel de fato commitar.
    /// Agora <see cref="ExecutarCasDeStepUpAsync"/> (fase 2) roda DENTRO da
    /// MESMA transacao que a acao sensivel do chamador — commitam juntos ou
    /// revertem juntos, e o CAS exige a TokenVersion continuar sendo a mesma
    /// sob a qual a senha (e este codigo) foram validados.
    /// </remarks>
    private async Task<(bool EhTotp, long PassoValidado)> ValidarCodigoDeStepUpAsync(
        Guid atendenteId, string? codigo, DateTime agora, CancellationToken ct)
    {
        var atual = await db.Atendentes.AsNoTracking().SingleAsync(a => a.Id == atendenteId, ct);

        if (atual.MfaBloqueadoAte is { } bloqueadoAte && bloqueadoAte > agora)
        {
            throw UsuariosServiceException.MfaBloqueado();
        }

        var codigoNormalizado = (codigo ?? string.Empty).Trim();
        var ehTotp = VerificadorDeFatorMfa.PareceCodigoTotp(codigoNormalizado);
        var passoValidado = 0L;

        if (ehTotp && !VerificadorDeFatorMfa.TentarValidarTotp(cifra, atual, codigoNormalizado, out passoValidado))
        {
            await BloqueioDeMfa.RegistrarFalhaAsync(db, atendenteId, agora, ct);
            throw UsuariosServiceException.CodigoMfaInvalido();
        }

        return (ehTotp, passoValidado);
    }

    /// <summary>
    /// FASE 2 do step-up — o CAS de verdade, DENTRO da transacao que o
    /// chamador ja abriu (nunca abre/commita/reverte sozinho). So afeta a
    /// linha se <c>TokenVersion == tokenVersionEsperada</c> — a mesma versao
    /// capturada pelo chamador no instante em que a senha foi validada, nunca
    /// uma nova encontrada depois. Devolve <c>false</c> em qualquer falha
    /// (TokenVersion desatualizada, replay, conta desativada/sem MFA, codigo
    /// de recuperacao ja usado) — o chamador SEMPRE reverte a transacao e
    /// registra a falha em <see cref="BloqueioDeMfa"/> antes de propagar o
    /// erro, nunca some sem deixar rastro.
    /// </summary>
    private async Task<bool> ExecutarCasDeStepUpAsync(
        Guid atendenteId, Guid tenantId, int tokenVersionEsperada,
        bool ehTotp, string codigoNormalizado, long passoValidado, DateTime agora, CancellationToken ct)
    {
        var baseCondicional = db.Atendentes.Where(a => a.Id == atendenteId
            && a.TenantId == tenantId
            && a.Ativo && a.MfaAtivo
            && a.TokenVersion == tokenVersionEsperada
            && (a.MfaBloqueadoAte == null || a.MfaBloqueadoAte <= agora));

        var ok = ehTotp
            ? await baseCondicional
                .Where(a => a.MfaUltimoPassoUsado == null || a.MfaUltimoPassoUsado < passoValidado)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.MfaUltimoPassoUsado, passoValidado)
                    .SetProperty(a => a.MfaFalhasConsecutivas, 0)
                    .SetProperty(a => a.MfaNivelDeBloqueio, (byte)0)
                    .SetProperty(a => a.MfaBloqueadoAte, (DateTime?)null), ct)
            : await baseCondicional
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.MfaFalhasConsecutivas, 0)
                    .SetProperty(a => a.MfaNivelDeBloqueio, (byte)0)
                    .SetProperty(a => a.MfaBloqueadoAte, (DateTime?)null), ct);

        if (ok != 1)
        {
            return false;
        }

        if (!ehTotp)
        {
            var hashDoCodigo = VerificadorDeFatorMfa.HashDoCodigoDeRecuperacao(codigoNormalizado);

            var usou = await db.CodigosDeRecuperacaoMfa
                .Where(c => c.AtendenteId == atendenteId && c.HashDoCodigo == hashDoCodigo && c.UsadoEm == null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsadoEm, agora), ct);

            if (usou != 1)
            {
                return false;
            }

            // Fica na MESMA transacao do chamador de proposito: se a acao
            // sensivel que este step-up autoriza falhar depois e a transacao
            // reverter, o consumo do codigo de recuperacao reverte junto —
            // nunca queima um codigo por uma operacao que nao completou.
            await AutenticacaoService.RegistrarEventoAsync(
                db, tenantId, atendenteId, atorAtendenteId: null, atorExterno: null,
                TipoDeEventoDeSeguranca.CodigoDeRecuperacaoUsado, ip: null, motivo: null, ct);
        }

        return true;
    }

    /// <remarks>
    /// CORRECAO (revisao independente do PR #46, bloqueador 15) —
    /// `TentativasDeCodigo &lt; LimiteDeTentativasPorDesafio` entrou na
    /// condicao: sem isto, um desafio que ja esgotou as 5 tentativas em
    /// ConfirmarViaDesafioAsync (nunca chegou a ser formalmente
    /// <c>ConsumidoEm</c>, porque so erros aconteceram) continuava servindo
    /// para <see cref="IniciarViaDesafioAsync"/> gerar um segredo/QR novo
    /// indefinidamente — reiniciando a cerimonia sem nunca precisar de um
    /// ticket novo. Agora um desafio esgotado fica permanentemente inutil,
    /// os dois metodos que passam por aqui.
    /// </remarks>
    private async Task<(IdentidadeParaMfa Identidade, int TokenVersionNoDesafio)> ResolverDesafioDeMatriculaAsync(
        string? desafioTexto, CancellationToken ct)
    {
        var agora = DateTime.UtcNow;
        var hashDoDesafio = SegredosAleatoriosMfa.Sha256Hex(desafioTexto ?? string.Empty);

        var desafio = await db.DesafiosDeMfa.AsNoTracking()
            .FirstOrDefaultAsync(d => d.HashDoDesafio == hashDoDesafio
                                    && d.Tipo == TipoDeDesafioMfa.MatriculaPrivilegiada
                                    && d.ConsumidoEm == null
                                    && d.ExpiraEm > agora
                                    && d.TentativasDeCodigo < LimiteDeTentativasPorDesafio, ct);

        if (desafio is null)
        {
            throw UsuariosServiceException.DesafioMfaInvalido();
        }

        return (new IdentidadeParaMfa(desafio.TenantId, desafio.AtendenteId), desafio.TokenVersionNoDesafio);
    }

    private async Task LimparMfaAsync(Guid atendenteId, Guid tenantId, CancellationToken ct)
    {
        await db.Atendentes
            .Where(a => a.Id == atendenteId && a.TenantId == tenantId)
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

        await db.CodigosDeRecuperacaoMfa.Where(c => c.AtendenteId == atendenteId).ExecuteDeleteAsync(ct);

        var agora = DateTime.UtcNow;
        await db.DesafiosDeMfa
            .Where(d => d.AtendenteId == atendenteId && d.ConsumidoEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.ConsumidoEm, agora), ct);
    }

}
