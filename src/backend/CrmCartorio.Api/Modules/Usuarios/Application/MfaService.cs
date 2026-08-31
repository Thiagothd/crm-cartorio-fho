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
    ILogger<MfaService> log) : IMfaService
{
    private const int QuantidadeDeCodigosDeRecuperacao = 10;
    private static readonly TimeSpan TtlDoSegredoPendente = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TtlDoDesafioDeMatricula = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan TtlDoTicket = TimeSpan.FromHours(24);

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
        // junto), nunca substitui sozinho o segredo de quem ja tem MFA.
        if (atendente.MfaAtivo)
        {
            if (string.IsNullOrWhiteSpace(request.CodigoAtual))
            {
                throw UsuariosServiceException.CodigoMfaInvalido();
            }

            if (!await ExecutarStepUpAsync(atendente, request.CodigoAtual, agora, ct))
            {
                throw UsuariosServiceException.CodigoMfaInvalido();
            }
        }

        return await GerarSegredoPendenteAsync(atendente, agora, ct);
    }

    public Task<ConfirmarMfaResponse> ConfirmarAsync(
        IdentidadeParaMfa identidade, ConfirmarMfaRequest request, string? ip, CancellationToken ct = default) =>
        ConfirmarNucleoAsync(identidade, request.Codigo, ip, hashDoDesafioParaConsumir: null, tokenVersionEsperada: null, ct);

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

        // REGRA FINAL 1 (revisao de SEC-03): a politica obrigatoria nunca pode
        // ser burlada pelo proprio usuario desativando o proprio MFA.
        if (await PoliticaDeMfa.ExigeAsync(db, atendente.TenantId, atendente.Perfil, atendente.Cargo, ct))
        {
            throw UsuariosServiceException.MfaObrigatorioNaoPodeSerDesativado();
        }

        if (!await ExecutarStepUpAsync(atendente, request.Codigo, agora, ct))
        {
            throw UsuariosServiceException.CodigoMfaInvalido();
        }

        await LimparMfaAsync(atendente.Id, identidade.TenantId, ct);

        await AutenticacaoService.RegistrarEventoAsync(
            db, identidade.TenantId, atendente.Id, atorAtendenteId: null, atorExterno: null,
            TipoDeEventoDeSeguranca.MfaDesativado, ip, motivo: null, ct);

        log.LogInformation("MFA desativado por {AtendenteId}", atendente.Id);
    }

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

        if (!await ExecutarStepUpAsync(atendente, request.Codigo, agora, ct))
        {
            throw UsuariosServiceException.CodigoMfaInvalido();
        }

        var codigos = await SubstituirCodigosDeRecuperacaoAsync(atendente.Id, identidade.TenantId, ct);

        await AutenticacaoService.RegistrarEventoAsync(
            db, identidade.TenantId, atendente.Id, atorAtendenteId: null, atorExterno: null,
            TipoDeEventoDeSeguranca.CodigosDeRecuperacaoRegenerados, ip, motivo: null, ct);

        return new RegenerarCodigosResponse(codigos);
    }

    public async Task<MfaSituacaoResponse> ObterSituacaoAsync(IdentidadeParaMfa identidade, CancellationToken ct = default)
    {
        var atendente = await CarregarAsync(identidade, ct);

        var obrigatorio = await PoliticaDeMfa.ExigeAsync(db, atendente.TenantId, atendente.Perfil, atendente.Cargo, ct);

        int? restantes = atendente.MfaAtivo
            ? await db.CodigosDeRecuperacaoMfa.CountAsync(c => c.AtendenteId == atendente.Id && c.UsadoEm == null, ct)
            : null;

        return new MfaSituacaoResponse(atendente.MfaAtivo, obrigatorio, restantes, atendente.MfaConfirmadoEm);
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
        var (identidade, _) = await ResolverDesafioDeMatriculaAsync(request.Desafio, ct);
        var atendente = await CarregarAsync(identidade, ct);

        // Sem step-up extra aqui: a posse deste desafio JA prova senha + ticket
        // fora de banda, exigidos para cria-lo.
        return await GerarSegredoPendenteAsync(atendente, DateTime.UtcNow, ct);
    }

    public async Task<ConfirmarMfaResponse> ConfirmarViaDesafioAsync(
        ConfirmarViaDesafioRequest request, string? ip, CancellationToken ct = default)
    {
        var (identidade, tokenVersionNoDesafio) = await ResolverDesafioDeMatriculaAsync(request.Desafio, ct);
        var hashDoDesafio = SegredosAleatoriosMfa.Sha256Hex(request.Desafio ?? string.Empty);

        return await ConfirmarNucleoAsync(identidade, request.Codigo, ip, hashDoDesafio, tokenVersionNoDesafio, ct);
    }

    // ------------------------------------------------------------ administrativo

    public async Task<TicketResponse> EmitirTicketAsync(
        IdentidadeParaMfa ator, Guid atendenteIdAlvo, EmitirTicketRequest request, string? ip, CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;
        var atorAtendente = await CarregarEGarantirAdministradorAsync(ator, ct);

        if (!hash.Verificar(request.SenhaDoAtor ?? string.Empty, atorAtendente.SenhaHash))
        {
            throw UsuariosServiceException.SenhaAtualIncorreta();
        }

        if (!await ExecutarStepUpAsync(atorAtendente, request.CodigoDoAtor, agora, ct))
        {
            throw UsuariosServiceException.CodigoMfaInvalido();
        }

        var alvo = await db.Atendentes.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == atendenteIdAlvo && a.TenantId == ator.TenantId, ct)
            ?? throw UsuariosServiceException.AtendenteNaoEncontrado();

        var privilegiado = alvo.Perfil == PerfilDeAtendente.Administrador || alvo.Cargo == CargoNaServentia.Titular;
        if (!privilegiado)
        {
            throw UsuariosServiceException.ContaNaoPrivilegiada();
        }

        GarantirTitularProtegido(alvo, atorAtendente);

        var tokenEmClaro = SegredosAleatoriosMfa.NovoToken();
        var expiraEm = agora.Add(TtlDoTicket);

        db.DesafiosDeMfa.Add(new DesafioDeMfa
        {
            TenantId = ator.TenantId,
            AtendenteId = alvo.Id,
            HashDoDesafio = SegredosAleatoriosMfa.Sha256Hex(tokenEmClaro),
            Tipo = TipoDeDesafioMfa.Ticket,
            TokenVersionNoDesafio = alvo.TokenVersion,
            ExpiraEm = expiraEm,
            IpDeOrigem = ip
        });

        await db.SaveChangesAsync(ct);

        await AutenticacaoService.RegistrarEventoAsync(
            db, ator.TenantId, alvo.Id, ator.AtendenteId, atorExterno: null,
            TipoDeEventoDeSeguranca.TicketDeMatriculaEmitido, ip, motivo: null, ct);

        log.LogInformation("Ticket de matricula de MFA emitido por {Ator} para {Alvo}", ator.AtendenteId, alvo.Id);

        return new TicketResponse(tokenEmClaro, expiraEm);
    }

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

        if (!await ExecutarStepUpAsync(atorAtendente, request.CodigoDoAtor, agora, ct))
        {
            throw UsuariosServiceException.CodigoMfaInvalido();
        }

        var alvo = await db.Atendentes.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == atendenteIdAlvo && a.TenantId == ator.TenantId, ct)
            ?? throw UsuariosServiceException.AtendenteNaoEncontrado();

        GarantirTitularProtegido(alvo, atorAtendente);

        // Conta fica SEM LOGIN ate nova matricula: se a politica exige MFA
        // para ela, o proximo login (AutenticacaoService) recusa com
        // MfaObrigatorioNaoMatriculado ate o alvo passar pelo ticket de novo.
        await LimparMfaAsync(alvo.Id, ator.TenantId, ct);

        await AutenticacaoService.RegistrarEventoAsync(
            db, ator.TenantId, alvo.Id, ator.AtendenteId, atorExterno: null,
            TipoDeEventoDeSeguranca.ResetAdministrativo, ip, request.Motivo, ct);

        log.LogInformation("MFA de {Alvo} resetado por {Ator}: {Motivo}", alvo.Id, ator.AtendenteId, request.Motivo);
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

        if (!await ExecutarStepUpAsync(atorAtendente, request.CodigoDoAtor, agora, ct))
        {
            throw UsuariosServiceException.CodigoMfaInvalido();
        }

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

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

    private async Task<IniciarMfaResponse> GerarSegredoPendenteAsync(Atendente atendente, DateTime agora, CancellationToken ct)
    {
        var segredoBase32 = ValidadorDeTotp.GerarSegredoBase32();
        var segredoCifrado = cifra.Cifrar(segredoBase32, atendente.TenantId, atendente.Id);
        var expiraEm = agora.Add(TtlDoSegredoPendente);

        // CORRECAO (revisao independente do PR #46, bloqueador 8) — grava
        // TokenVersion junto do segredo pendente, na MESMA instrucao atomica,
        // auto-referenciada (`a.TokenVersion`, nunca `atendente.TokenVersion`
        // capturado antes): e o carimbo que a confirmacao final vai exigir
        // continuar batendo, para que troca de senha, mudanca de Perfil/Cargo,
        // reset ou politica obrigatoria ENTRE iniciar e confirmar invalidem a
        // cerimonia — mesma garantia que o fluxo de ticket ja tem via
        // DesafiosDeMfa.TokenVersionNoDesafio, agora tambem para a primeira
        // ativacao voluntaria e a troca de autenticador.
        await db.Atendentes
            .Where(a => a.Id == atendente.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.MfaSegredoPendenteCifrado, segredoCifrado)
                .SetProperty(a => a.MfaPendenteExpiraEm, expiraEm)
                .SetProperty(a => a.MfaPendenteTokenVersion, a => a.TokenVersion), ct);

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
        await tx.CommitAsync(ct);

        await AutenticacaoService.RegistrarEventoAsync(
            db, identidade.TenantId, atendente.Id, atorAtendenteId: null, atorExterno: null,
            TipoDeEventoDeSeguranca.MfaAtivado, ip, motivo: null, ct);

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

    /// <remarks>
    /// SEC-03 — o step-up que TODA operacao sensivel de gestao usa. Mesma
    /// disciplina de porteiro unico do login: reserva/valida/consome sao
    /// atomicos, e uma falha de codigo de recuperacao ou de CAS de TOTP conta
    /// como falha de MFA (<see cref="BloqueioDeMfa"/>) — nunca some sem deixar
    /// rastro.
    /// </remarks>
    private async Task<bool> ExecutarStepUpAsync(Atendente atendente, string? codigo, DateTime agora, CancellationToken ct)
    {
        if (atendente.MfaBloqueadoAte is { } bloqueadoAte && bloqueadoAte > agora)
        {
            throw UsuariosServiceException.MfaBloqueado();
        }

        var codigoNormalizado = (codigo ?? string.Empty).Trim();
        var ehTotp = VerificadorDeFatorMfa.PareceCodigoTotp(codigoNormalizado);
        var passoValidado = 0L;

        if (ehTotp && !VerificadorDeFatorMfa.TentarValidarTotp(cifra, atendente, codigoNormalizado, out passoValidado))
        {
            await BloqueioDeMfa.RegistrarFalhaAsync(db, atendente.Id, agora, ct);
            return false;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var baseCondicional = db.Atendentes.Where(a => a.Id == atendente.Id
            && a.Ativo && a.MfaAtivo
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
            await tx.RollbackAsync(ct);
            if (ehTotp) await BloqueioDeMfa.RegistrarFalhaAsync(db, atendente.Id, agora, ct);
            return false;
        }

        if (!ehTotp)
        {
            var hashDoCodigo = VerificadorDeFatorMfa.HashDoCodigoDeRecuperacao(codigoNormalizado);

            var usou = await db.CodigosDeRecuperacaoMfa
                .Where(c => c.AtendenteId == atendente.Id && c.HashDoCodigo == hashDoCodigo && c.UsadoEm == null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsadoEm, agora), ct);

            if (usou != 1)
            {
                await tx.RollbackAsync(ct);
                await BloqueioDeMfa.RegistrarFalhaAsync(db, atendente.Id, agora, ct);
                return false;
            }
        }

        await tx.CommitAsync(ct);

        if (!ehTotp)
        {
            await AutenticacaoService.RegistrarEventoAsync(
                db, atendente.TenantId, atendente.Id, atorAtendenteId: null, atorExterno: null,
                TipoDeEventoDeSeguranca.CodigoDeRecuperacaoUsado, ip: null, motivo: null, ct);
        }

        return true;
    }

    private async Task<(IdentidadeParaMfa Identidade, int TokenVersionNoDesafio)> ResolverDesafioDeMatriculaAsync(
        string? desafioTexto, CancellationToken ct)
    {
        var agora = DateTime.UtcNow;
        var hashDoDesafio = SegredosAleatoriosMfa.Sha256Hex(desafioTexto ?? string.Empty);

        var desafio = await db.DesafiosDeMfa.AsNoTracking()
            .FirstOrDefaultAsync(d => d.HashDoDesafio == hashDoDesafio
                                    && d.Tipo == TipoDeDesafioMfa.MatriculaPrivilegiada
                                    && d.ConsumidoEm == null
                                    && d.ExpiraEm > agora, ct);

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

    private async Task<IReadOnlyList<string>> SubstituirCodigosDeRecuperacaoAsync(
        Guid atendenteId, Guid tenantId, CancellationToken ct)
    {
        var codigos = Enumerable.Range(0, QuantidadeDeCodigosDeRecuperacao)
            .Select(_ => SegredosAleatoriosMfa.NovoCodigoDeRecuperacao())
            .ToList();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await db.CodigosDeRecuperacaoMfa.Where(c => c.AtendenteId == atendenteId).ExecuteDeleteAsync(ct);

        db.CodigosDeRecuperacaoMfa.AddRange(codigos.Select(codigoClaro => new CodigoDeRecuperacaoMfa
        {
            TenantId = tenantId,
            AtendenteId = atendenteId,
            HashDoCodigo = VerificadorDeFatorMfa.HashDoCodigoDeRecuperacao(codigoClaro)
        }));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return codigos.Select(SegredosAleatoriosMfa.FormatarCodigoDeRecuperacao).ToList();
    }
}
