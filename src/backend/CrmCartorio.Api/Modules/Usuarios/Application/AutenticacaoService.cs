using CrmCartorio.Api.Infrastructure.Identity;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Usuarios.Application;

/// <inheritdoc cref="IAutenticacaoService"/>
public class AutenticacaoService(
    CrmDbContext db,
    IHashDeSenha hash,
    IGeradorDeToken tokens,
    ICifraDeSegredoMfa cifra,
    IContextoDoUsuario ctx,
    ICookieDeDispositivoConfiavel cookieDeDispositivo,
    ILogger<AutenticacaoService> log) : IAutenticacaoService
{
    /// <summary>SEC-02: falhas de SENHA seguidas que fecham um ciclo e abrem um bloqueio.</summary>
    private const int LimiteDeFalhasDeSenha = 5;

    private const byte TetoDoNivel = 6;

    /// <summary>SEC-03: tentativas por DESAFIO (nao por conta) antes dele morrer sozinho.</summary>
    private const int LimiteDeTentativasPorDesafio = 5;

    private static readonly TimeSpan TtlDoDesafioDeSegundoFator = TimeSpan.FromMinutes(5);

    /// <summary>SEC-03: janela de "confiar neste dispositivo". Fixa desde a concessao, nunca estendida por uso.</summary>
    private static readonly TimeSpan JanelaDeDispositivoConfiavel = TimeSpan.FromDays(30);

    /// <remarks>
    /// SEC-03 — a decisao final desta etapa NUNCA sai de um booleano lido antes
    /// da escrita. Tres momentos, cada um so pode agir sobre o que acabou de
    /// confirmar:
    /// <list type="number">
    /// <item>Aceite da SENHA — so os campos de lockout de senha. Nunca
    /// <see cref="Atendente.UltimoAcessoEm"/> aqui: senha correta ainda nao e
    /// login concluido quando ha MFA.</item>
    /// <item>Releitura do estado de seguranca ATUAL (TokenVersion, MfaAtivo,
    /// Perfil/Cargo, politica do tenant) — so uma HIPOTESE, sem autoridade
    /// nenhuma ainda.</item>
    /// <item>Conclusao: se precisa de MFA, abre desafio com o TokenVersion desta
    /// leitura (autoridade nenhuma — o desafio e revalidado inteiro na
    /// conclusao). Se nao precisa, um UNICO UPDATE condicional reafirma TODA a
    /// hipotese do momento 2 no instante da escrita — so aceito == 1 emite JWT,
    /// e so com o TokenVersion que o proprio WHERE acabou de confirmar vigente.
    /// Se a hipotese caiu (0 linhas), repete o momento 2 uma vez; se cair de
    /// novo, falha fechada.</item>
    /// </list>
    /// </remarks>
    public async Task<LoginEtapa1Response> AutenticarAsync(LoginRequest request, CancellationToken ct = default)
    {
        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var agora = DateTime.UtcNow;

        var atendente = await db.Atendentes.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Email == email, ct);

        // Verifica a senha mesmo com atendente inexistente, inativo ou bloqueado:
        // sair antes faria o tempo de resposta revelar quais e-mails existem.
        var hashParaConferir = atendente?.SenhaHash ?? HashDescartavel;
        var senhaConfere = hash.Verificar(request.Senha ?? string.Empty, hashParaConferir);

        if (atendente is null)
        {
            log.LogWarning("Tentativa de login rejeitada para {Email}", email);
            throw UsuariosServiceException.CredenciaisInvalidas();
        }

        if (!senhaConfere)
        {
            await RegistrarFalhaDeSenhaAsync(atendente.Id, agora, ct);
            log.LogWarning("Tentativa de login rejeitada para {Email}", email);
            throw UsuariosServiceException.CredenciaisInvalidas();
        }

        // Momento 1 — SO o lockout de senha. UltimoAcessoEm fica para o fim,
        // depois que soubermos se ha MFA no caminho.
        var senhaAceita = await db.Atendentes
            .Where(a => a.Id == atendente.Id
                     && a.Ativo
                     && (a.BloqueadoAte == null || a.BloqueadoAte <= agora))
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.FalhasDeLoginConsecutivas, 0)
                .SetProperty(a => a.NivelDeBloqueio, (byte)0)
                .SetProperty(a => a.BloqueadoAte, (DateTime?)null), ct);

        if (senhaAceita != 1)
        {
            log.LogWarning("Login recusado para {Email}: conta inativa ou bloqueada (SEC-02).", email);
            throw UsuariosServiceException.CredenciaisInvalidas();
        }

        for (var tentativa = 1; tentativa <= 2; tentativa++)
        {
            // Momento 2 — hipotese, sem autoridade.
            var estado = await db.Atendentes.AsNoTracking()
                .Where(a => a.Id == atendente.Id)
                .Select(a => new
                {
                    a.TenantId, a.TokenVersion, a.MfaAtivo, a.Perfil, a.Cargo, a.Ativo,
                    a.RematriculaObrigatoriaAposEmergencia
                })
                .FirstOrDefaultAsync(ct);

            if (estado is null || !estado.Ativo)
            {
                // Desativado entre o aceite da senha e agora.
                throw UsuariosServiceException.CredenciaisInvalidas();
            }

            var politicaExige = await PoliticaDeMfa.ExigeAsync(db, estado.TenantId, estado.Perfil, estado.Cargo, ct);

            // CORRECAO (bloqueador B19, sexta revisao independente): a
            // exigencia de rematricula depois de um reset de emergencia
            // (break-glass) e POR CONTA, independente da politica do tenant
            // (`politicaExige`). Sem isto, um tenant com
            // MfaObrigatorioParaAdministradores desligado deixava a conta
            // resetada por break-glass logar so com senha, nunca passando
            // pelo ticket de rematricula que o proprio break-glass emitiu.
            if (!estado.MfaAtivo && (politicaExige || estado.RematriculaObrigatoriaAposEmergencia))
            {
                throw UsuariosServiceException.MfaObrigatorioNaoMatriculado();
            }

            if (estado.MfaAtivo)
            {
                // SEC-03 — dispositivo confiavel: se ESTE navegador ja passou
                // pelo segundo fator nos ultimos 30 dias e pediu para ser
                // lembrado, o login conclui aqui, sem desafio. A senha
                // continua obrigatoria (ja foi conferida acima) — o que se
                // dispensa e SOMENTE o segundo fator. Qualquer falha na
                // validacao cai no fluxo normal de MFA (fail closed).
                var porDispositivoConfiavel = await TentarConcluirPorDispositivoConfiavelAsync(
                    atendente, estado.TenantId, estado.TokenVersion, agora, ct);

                if (porDispositivoConfiavel is not null)
                {
                    return porDispositivoConfiavel;
                }

                var desafio = await CriarDesafioAsync(
                    atendente.Id, estado.TenantId, estado.TokenVersion,
                    TipoDeDesafioMfa.SegundoFator, TtlDoDesafioDeSegundoFator, ct);

                return new LoginEtapa1Response(true, null, desafio);
            }

            // Momento 3B — conclusao atomica. O WHERE reafirma TUDO que o
            // momento 2 supôs; so aceito == 1 emite JWT.
            var concluiu = await db.Atendentes
                .Where(a => a.Id == atendente.Id
                         && a.TenantId == estado.TenantId
                         && a.Ativo
                         && a.TokenVersion == estado.TokenVersion
                         && !a.MfaAtivo
                         && !a.RematriculaObrigatoriaAposEmergencia
                         && !db.Tenants.Any(t => t.Id == a.TenantId
                                              && t.MfaObrigatorioParaAdministradores
                                              && (a.Perfil == Domain.Enums.PerfilDeAtendente.Administrador
                                                  || a.Cargo == Domain.Enums.CargoNaServentia.Titular)))
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.UltimoAcessoEm, agora), ct);

            if (concluiu == 1)
            {
                // A hipotese do momento 2 acabou de ser reconfirmada pela propria
                // escrita — usa-la para o JWT (nunca uma releitura: seria a MESMA
                // janela que este desenho existe para fechar).
                atendente.UltimoAcessoEm = agora;
                atendente.TenantId = estado.TenantId;
                atendente.TokenVersion = estado.TokenVersion;

                var (token, expiraEm) = tokens.Gerar(atendente);

                log.LogInformation("Login de {AtendenteId} ({Nome})", atendente.Id, atendente.Nome);

                return new LoginEtapa1Response(false, new LoginResponse(
                    token, expiraEm,
                    await MapeadorDeSessao.MapearAtendenteAsync(db, atendente, ct),
                    await MapeadorDeSessao.ObterTenantAsync(db, estado.TenantId, ct)), null);
            }

            // 0 linhas: algo relevante mudou entre o momento 2 e a escrita — mais
            // provavel, MFA acabou de ser ativado ou a politica acabou de ligar.
            // Uma segunda passada por Momento 2 pega essa mudanca e segue para
            // MFA corretamente; se cair de novo, falha fechada.
        }

        throw UsuariosServiceException.CredenciaisInvalidas();
    }

    /// <summary>
    /// SEC-03 — tenta concluir o login SEM segundo fator, com base no cookie
    /// de dispositivo confiavel deste navegador. Devolve <c>null</c> quando
    /// nao ha cookie, quando ele nao confere, ou quando qualquer reconfirmacao
    /// falha — e o chamador segue para o desafio normal de MFA.
    /// </summary>
    /// <remarks>
    /// Mesma disciplina de porteiro unico do resto do modulo: NENHUMA decisao
    /// sai de valor lido antes da escrita. Duas escritas condicionais dentro
    /// de UMA transacao, na ordem de autoridade (igual ao Passo C1 de
    /// <see cref="ConcluirLoginMfaAsync"/>):
    /// <list type="number">
    /// <item>A CONTA e reconfirmada primeiro (Ativo, MfaAtivo, TokenVersion e
    /// — por defesa em profundidade, ainda que estruturalmente impossivel
    /// junto de MfaAtivo — a ausencia de rematricula pos-emergencia
    /// pendente).</item>
    /// <item>SO ENTAO o DISPOSITIVO e consumido: hash + conta + tenant + nao
    /// revogado + nao expirado + <c>TokenVersionNoRegistro</c> ainda igual a
    /// TokenVersion vigente. O mesmo UPDATE ROTACIONA o hash para um token
    /// novo — uso unico por rotacao.</item>
    /// </list>
    /// Se qualquer uma das duas afetar 0 linhas, as DUAS voltam atras
    /// (rollback): sem isso, uma conta que mudou de estado no meio do caminho
    /// deixaria o dispositivo com hash rotacionado e o navegador legitimo com
    /// um cookie morto, sem nunca ter concluido login nenhum.
    ///
    /// <b>Corrida aceita e deliberada:</b> se o MESMO cookie for apresentado
    /// por duas requisicoes concorrentes (o caso que importa: uma copia
    /// exfiltrada correndo com o navegador legitimo), o UPDATE condicional
    /// serializa: QUEM CHEGA PRIMEIRO rotaciona e segue confiavel, e a outra
    /// encontra 0 linhas e cai no segundo fator completo.
    ///
    /// Nao ha aqui deteccao de roubo, e o vencedor da corrida NAO e
    /// necessariamente o dono legitimo — se o atacante chegar antes, e o dono
    /// que passa a ser desafiado. O que a rotacao entrega, e so isso, e
    /// limitar a janela: um cookie exfiltrado vale por um unico uso, nunca
    /// pelos 30 dias da concessao. O perdedor da corrida precisa do codigo do
    /// autenticador para seguir.
    ///
    /// <b>TokenVersion faz a revogacao de graca:</b> troca de senha, reset,
    /// desativacao ou troca de MFA, break-glass e mudanca de Perfil/Cargo/Ativo
    /// ja sobem <c>Atendente.TokenVersion</c> hoje (SEC-01/SEC-03). Como o
    /// registro guarda a versao vigente na concessao, todos esses eventos
    /// invalidam o dispositivo sem nenhuma linha de codigo de revogacao.
    /// </remarks>
    private async Task<LoginEtapa1Response?> TentarConcluirPorDispositivoConfiavelAsync(
        Atendente atendente, Guid tenantId, int tokenVersion, DateTime agora, CancellationToken ct)
    {
        var tokenApresentado = cookieDeDispositivo.Ler();

        if (string.IsNullOrWhiteSpace(tokenApresentado))
        {
            return null;
        }

        var hashApresentado = SegredosAleatoriosMfa.Sha256Hex(tokenApresentado);

        var tokenRotacionado = SegredosAleatoriosMfa.NovoToken();
        var hashRotacionado = SegredosAleatoriosMfa.Sha256Hex(tokenRotacionado);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // 1) A CONTA, reconfirmada no instante da escrita — mesma condicao do
        //    "Momento 3B" do login sem MFA, so que exigindo MfaAtivo (aqui a
        //    conta TEM segundo fator; o que se dispensa e apresenta-lo agora).
        var contaOk = await db.Atendentes
            .Where(a => a.Id == atendente.Id
                     && a.TenantId == tenantId
                     && a.Ativo
                     && a.MfaAtivo
                     && a.TokenVersion == tokenVersion
                     && !a.RematriculaObrigatoriaAposEmergencia)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.UltimoAcessoEm, agora), ct);

        if (contaOk != 1)
        {
            await tx.RollbackAsync(ct);
            return null;
        }

        // 2) O DISPOSITIVO. O WHERE amarra o cookie a ESTA conta e a ESTE
        //    tenant: um cookie valido de outro atendente (ou de outro
        //    cartorio) nunca casa aqui.
        var dispositivoOk = await db.DispositivosConfiaveis
            .Where(d => d.HashDoToken == hashApresentado
                     && d.AtendenteId == atendente.Id
                     && d.TenantId == tenantId
                     && d.RevogadoEm == null
                     && d.ExpiraEm > agora
                     && d.TokenVersionNoRegistro == tokenVersion)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.HashDoToken, hashRotacionado)
                .SetProperty(d => d.UltimoUsoEm, agora), ct);

        if (dispositivoOk != 1)
        {
            await tx.RollbackAsync(ct);
            return null;
        }

        // ExpiraEm NAO e relido para ser estendido — e relido para o cookie
        // novo herdar exatamente a validade que o registro ja tinha.
        var expiraEm = await db.DispositivosConfiaveis.AsNoTracking()
            .Where(d => d.HashDoToken == hashRotacionado)
            .Select(d => d.ExpiraEm)
            .SingleAsync(ct);

        await tx.CommitAsync(ct);

        cookieDeDispositivo.Definir(tokenRotacionado, expiraEm);

        // Os mesmos valores que as escritas acima acabaram de reconfirmar —
        // nunca uma releitura, que reabriria a janela que este desenho fecha.
        atendente.UltimoAcessoEm = agora;
        atendente.TenantId = tenantId;
        atendente.TokenVersion = tokenVersion;

        var (token, expiraEmToken) = tokens.Gerar(atendente);

        log.LogInformation(
            "Login (dispositivo confiavel, sem segundo fator) de {AtendenteId} ({Nome})",
            atendente.Id, atendente.Nome);

        return new LoginEtapa1Response(false, new LoginResponse(
            token, expiraEmToken,
            await MapeadorDeSessao.MapearAtendenteAsync(db, atendente, ct),
            await MapeadorDeSessao.ObterTenantAsync(db, tenantId, ct)), null);
    }

    /// <remarks>
    /// SEC-03 — o porteiro unico da etapa 2. Reserva de tentativa ANTES de
    /// validar (o teto de 5 vale mesmo se a validacao for interrompida no
    /// meio); consumo do desafio SO no caminho de sucesso, atomico, na MESMA
    /// transacao que reconfirma Ativo + TenantId + TokenVersion + MfaAtivo +
    /// ausencia de bloqueio de MFA — e, no caminho TOTP, o CAS de
    /// <see cref="Atendente.MfaUltimoPassoUsado"/>.
    /// </remarks>
    public async Task<LoginResponse> ConcluirLoginMfaAsync(ConcluirLoginMfaRequest request, CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;
        var hashDoDesafio = SegredosAleatoriosMfa.Sha256Hex(request.Desafio ?? string.Empty);

        // Passo A — reserva a tentativa. Se isto nao afetar 1 linha, o desafio
        // esta desconhecido, expirado, ja consumido ou esgotado — e o proprio
        // esgotamento: nao ha um segundo passo separado de "inutilizar".
        var reservou = await db.DesafiosDeMfa
            .Where(d => d.HashDoDesafio == hashDoDesafio
                     && d.Tipo == TipoDeDesafioMfa.SegundoFator
                     && d.ConsumidoEm == null
                     && d.ExpiraEm > agora
                     && d.TentativasDeCodigo < LimiteDeTentativasPorDesafio)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.TentativasDeCodigo, d => d.TentativasDeCodigo + 1), ct);

        if (reservou != 1)
        {
            throw UsuariosServiceException.DesafioMfaInvalido();
        }

        var desafio = await db.DesafiosDeMfa.AsNoTracking()
            .FirstAsync(d => d.HashDoDesafio == hashDoDesafio, ct);

        var atendente = await db.Atendentes.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == desafio.AtendenteId, ct);

        if (atendente is null)
        {
            throw UsuariosServiceException.DesafioMfaInvalido();
        }

        // Bloqueio de MFA por CONTA — nao por desafio. Quem ja tem a senha pode
        // criar quantos desafios quiser; e este contador, nao o de tentativas
        // por desafio, que impede forca bruta persistente do segundo fator.
        if (atendente.MfaBloqueadoAte is { } bloqueadoAte && bloqueadoAte > agora)
        {
            throw UsuariosServiceException.MfaBloqueado();
        }

        var codigo = (request.Codigo ?? string.Empty).Trim();
        var ehTotp = VerificadorDeFatorMfa.PareceCodigoTotp(codigo);
        var passoValidado = 0L;

        // Passo B (so no caminho TOTP: a "validacao" do codigo de recuperacao E
        // o proprio consumo atomico dentro da transacao — nao ha pre-checagem
        // barata possivel sem tocar o banco).
        if (ehTotp && !VerificadorDeFatorMfa.TentarValidarTotp(cifra, atendente, codigo, out passoValidado))
        {
            await BloqueioDeMfa.RegistrarFalhaAsync(db, atendente.Id, agora, ct);
            throw UsuariosServiceException.CodigoMfaInvalido();
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Passo C1 — porteiro unico: reconfirma TODO o estado de seguranca no
        // instante da escrita, nunca a partir de um valor lido antes.
        var baseCondicional = db.Atendentes
            .Where(a => a.Id == atendente.Id
                     && a.TenantId == desafio.TenantId
                     && a.Ativo
                     && a.MfaAtivo
                     && a.TokenVersion == desafio.TokenVersionNoDesafio
                     && (a.MfaBloqueadoAte == null || a.MfaBloqueadoAte <= agora));

        var estadoOk = ehTotp
            ? await baseCondicional
                .Where(a => a.MfaUltimoPassoUsado == null || a.MfaUltimoPassoUsado < passoValidado)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.MfaUltimoPassoUsado, passoValidado)
                    .SetProperty(a => a.MfaFalhasConsecutivas, 0)
                    .SetProperty(a => a.MfaNivelDeBloqueio, (byte)0)
                    .SetProperty(a => a.MfaBloqueadoAte, (DateTime?)null)
                    .SetProperty(a => a.UltimoAcessoEm, agora), ct)
            : await baseCondicional
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.MfaFalhasConsecutivas, 0)
                    .SetProperty(a => a.MfaNivelDeBloqueio, (byte)0)
                    .SetProperty(a => a.MfaBloqueadoAte, (DateTime?)null)
                    .SetProperty(a => a.UltimoAcessoEm, agora), ct);

        if (estadoOk != 1)
        {
            await tx.RollbackAsync(ct);

            if (ehTotp)
            {
                // TOTP formalmente valido mas o CAS/estado perdeu a corrida —
                // ainda assim conta como falha: um replay nunca deveria passar.
                await BloqueioDeMfa.RegistrarFalhaAsync(db, atendente.Id, agora, ct);
            }

            throw UsuariosServiceException.DesafioMfaInvalido();
        }

        if (!ehTotp)
        {
            var hashDoCodigo = VerificadorDeFatorMfa.HashDoCodigoDeRecuperacao(codigo);

            var usouCodigo = await db.CodigosDeRecuperacaoMfa
                .Where(c => c.AtendenteId == atendente.Id && c.HashDoCodigo == hashDoCodigo && c.UsadoEm == null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsadoEm, agora), ct);

            if (usouCodigo != 1)
            {
                await tx.RollbackAsync(ct);
                await BloqueioDeMfa.RegistrarFalhaAsync(db, atendente.Id, agora, ct);
                throw UsuariosServiceException.CodigoMfaInvalido();
            }
        }

        var consumiu = await db.DesafiosDeMfa
            .Where(d => d.HashDoDesafio == hashDoDesafio && d.ConsumidoEm == null && d.ExpiraEm > agora)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.ConsumidoEm, agora), ct);

        if (consumiu != 1)
        {
            await tx.RollbackAsync(ct);
            throw UsuariosServiceException.DesafioMfaInvalido();
        }

        // CORRECAO (revisao independente do PR #46, bloqueador 13B): o evento
        // agora e gravado ANTES do commit, na MESMA transacao que consome o
        // codigo de recuperacao e o desafio — nunca depois. Antes, se este
        // registro falhasse apos o commit, o login MFA por recovery code ja
        // tinha sido efetivado (codigo/desafio consumidos, UltimoAcessoEm
        // gravado) sem deixar rastro de auditoria correspondente.
        if (!ehTotp)
        {
            await RegistrarEventoAsync(
                desafio.TenantId, atendente.Id, atorAtendenteId: null, atorExterno: null,
                TipoDeEventoDeSeguranca.CodigoDeRecuperacaoUsado, desafio.IpDeOrigem, motivo: null, ct);
        }

        // SEC-03 — "confiar neste dispositivo por 30 dias". So chega aqui
        // depois de o segundo fator ter passado por todo o porteiro acima, e
        // grava na MESMA transacao: ou o login inteiro vale (com a confianca
        // concedida), ou nada vale. TokenVersionNoRegistro fica preso a
        // TokenVersion que o proprio desafio validou — e o que faz qualquer
        // evento de seguranca futuro invalidar este dispositivo sozinho.
        string? tokenDoDispositivo = null;
        var expiraEmDoDispositivo = agora.Add(JanelaDeDispositivoConfiavel);

        if (request.ConfiarNesteDispositivo)
        {
            tokenDoDispositivo = SegredosAleatoriosMfa.NovoToken();

            db.DispositivosConfiaveis.Add(new DispositivoConfiavel
            {
                TenantId = desafio.TenantId,
                AtendenteId = atendente.Id,
                HashDoToken = SegredosAleatoriosMfa.Sha256Hex(tokenDoDispositivo),
                TokenVersionNoRegistro = desafio.TokenVersionNoDesafio,
                ExpiraEm = expiraEmDoDispositivo,
                IpDeOrigem = desafio.IpDeOrigem
            });

            await db.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);

        // Depois do commit de proposito: escrever cabecalho de resposta e
        // efeito de HTTP, nao de banco — e so faz sentido se a transacao de
        // fato valeu.
        if (tokenDoDispositivo is not null)
        {
            cookieDeDispositivo.Definir(tokenDoDispositivo, expiraEmDoDispositivo);
        }

        atendente.TenantId = desafio.TenantId;
        atendente.TokenVersion = desafio.TokenVersionNoDesafio;
        atendente.UltimoAcessoEm = agora;

        var (token, expiraEm) = tokens.Gerar(atendente);

        log.LogInformation("Login (MFA) de {AtendenteId} ({Nome})", atendente.Id, atendente.Nome);

        return new LoginResponse(
            token, expiraEm,
            await MapeadorDeSessao.MapearAtendenteAsync(db, atendente, ct),
            await MapeadorDeSessao.ObterTenantAsync(db, desafio.TenantId, ct));
    }

    public async Task<LoginResponse> ObterSessaoAsync(CancellationToken ct = default)
    {
        var atendente = await db.Atendentes.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == ctx.AtendenteId && a.TenantId == ctx.TenantId, ct)
            ?? throw UsuariosServiceException.AtendenteNaoEncontrado();

        if (!atendente.Ativo)
        {
            throw UsuariosServiceException.CredenciaisInvalidas();
        }

        // Sessao nao emite token novo: o cliente ja tem o dele e a validade nao muda.
        return new LoginResponse(
            string.Empty,
            DateTime.MinValue,
            await MapeadorDeSessao.MapearAtendenteAsync(db, atendente, ct),
            await MapeadorDeSessao.ObterTenantAsync(db, atendente.TenantId, ct));
    }

    public async Task AlterarSenhaAsync(AlterarSenhaRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.NovaSenha) || request.NovaSenha.Length < 8)
        {
            throw UsuariosServiceException.SenhaFraca();
        }

        var atendente = await db.Atendentes.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == ctx.AtendenteId && a.TenantId == ctx.TenantId, ct)
            ?? throw UsuariosServiceException.AtendenteNaoEncontrado();

        if (!hash.Verificar(request.SenhaAtual ?? string.Empty, atendente.SenhaHash))
        {
            throw UsuariosServiceException.SenhaAtualIncorreta();
        }

        var novoHash = hash.Gerar(request.NovaSenha);

        // SEC-01: grava o novo hash e sobe TokenVersion na MESMA instrucao SQL
        // atomica. SEC-03: troca de senha e evento de seguranca — sobe
        // TokenVersion, que ja e o que invalida qualquer DesafioDeMfa em aberto
        // (TokenVersionNoDesafio deixa de bater na conclusao).
        await db.Atendentes
            .Where(a => a.Id == atendente.Id && a.TenantId == ctx.TenantId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.SenhaHash, novoHash)
                .SetProperty(a => a.TokenVersion, a => a.TokenVersion + 1), ct);

        log.LogInformation("Senha alterada por {AtendenteId}", atendente.Id);
    }

    public async Task<ServentiaPublicaResponse> ObterServentiaPublicaAsync(CancellationToken ct = default)
    {
        var nome = await db.Tenants.AsNoTracking()
            .Where(t => t.Ativo)
            .OrderBy(t => t.CriadoEm)
            .Select(t => t.NomeFantasia ?? t.RazaoSocial)
            .FirstOrDefaultAsync(ct);

        return new ServentiaPublicaResponse(nome ?? "Cartório");
    }

    /// <summary>Cria e persiste um desafio, devolvendo o TOKEN EM CLARO (unica vez) e a resposta publica.</summary>
    internal static async Task<DesafioMfaResponse> CriarDesafioAsync(
        CrmDbContext db, Guid atendenteId, Guid tenantId, int tokenVersion,
        TipoDeDesafioMfa tipo, TimeSpan ttl, string? ip, CancellationToken ct)
    {
        var agora = DateTime.UtcNow;
        var tokenEmClaro = SegredosAleatoriosMfa.NovoToken();

        db.DesafiosDeMfa.Add(new DesafioDeMfa
        {
            TenantId = tenantId,
            AtendenteId = atendenteId,
            HashDoDesafio = SegredosAleatoriosMfa.Sha256Hex(tokenEmClaro),
            Tipo = tipo,
            TokenVersionNoDesafio = tokenVersion,
            ExpiraEm = agora.Add(ttl),
            IpDeOrigem = ip
        });

        await db.SaveChangesAsync(ct);

        return new DesafioMfaResponse(tokenEmClaro, tipo.ToString(), agora.Add(ttl));
    }

    private Task<DesafioMfaResponse> CriarDesafioAsync(
        Guid atendenteId, Guid tenantId, int tokenVersion, TipoDeDesafioMfa tipo, TimeSpan ttl, CancellationToken ct) =>
        CriarDesafioAsync(db, atendenteId, tenantId, tokenVersion, tipo, ttl, ip: null, ct);

    /// <summary>SEC-02: mesmo idioma de BUG-WA-IN-02/WA-OUT-01 — CASE em SQL, nunca valor lido antes.</summary>
    private Task RegistrarFalhaDeSenhaAsync(Guid atendenteId, DateTime agora, CancellationToken ct) =>
        db.Atendentes
            .Where(a => a.Id == atendenteId
                     && a.Ativo
                     && (a.BloqueadoAte == null || a.BloqueadoAte <= agora))
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.FalhasDeLoginConsecutivas,
                    a => a.FalhasDeLoginConsecutivas + 1 >= LimiteDeFalhasDeSenha
                        ? 0
                        : a.FalhasDeLoginConsecutivas + 1)
                .SetProperty(a => a.NivelDeBloqueio,
                    a => a.FalhasDeLoginConsecutivas + 1 < LimiteDeFalhasDeSenha
                        ? a.NivelDeBloqueio
                        : a.NivelDeBloqueio >= TetoDoNivel
                            ? TetoDoNivel
                            : (byte)(a.NivelDeBloqueio + 1))
                .SetProperty(a => a.BloqueadoAte,
                    a => a.FalhasDeLoginConsecutivas + 1 < LimiteDeFalhasDeSenha ? a.BloqueadoAte
                       : a.NivelDeBloqueio >= 5 ? agora.AddMinutes(30)
                       : a.NivelDeBloqueio == 4 ? agora.AddMinutes(16)
                       : a.NivelDeBloqueio == 3 ? agora.AddMinutes(8)
                       : a.NivelDeBloqueio == 2 ? agora.AddMinutes(4)
                       : a.NivelDeBloqueio == 1 ? agora.AddMinutes(2)
                       : agora.AddMinutes(1)), ct);

    internal static async Task RegistrarEventoAsync(
        CrmDbContext db, Guid tenantId, Guid atendenteId, Guid? atorAtendenteId, string? atorExterno,
        TipoDeEventoDeSeguranca tipo, string? ip, string? motivo, CancellationToken ct)
    {
        db.EventosDeSeguranca.Add(new EventoDeSeguranca
        {
            TenantId = tenantId,
            AtendenteId = atendenteId,
            AtorAtendenteId = atorAtendenteId,
            AtorExterno = atorExterno,
            Tipo = tipo,
            OcorridoEm = DateTime.UtcNow,
            IpDeOrigem = ip,
            Motivo = motivo
        });

        await db.SaveChangesAsync(ct);
    }

    private Task RegistrarEventoAsync(
        Guid tenantId, Guid atendenteId, Guid? atorAtendenteId, string? atorExterno,
        TipoDeEventoDeSeguranca tipo, string? ip, string? motivo, CancellationToken ct) =>
        RegistrarEventoAsync(db, tenantId, atendenteId, atorAtendenteId, atorExterno, tipo, ip, motivo, ct);

    /// <summary>
    /// Hash valido de uma senha aleatoria, usado so para gastar o mesmo tempo de
    /// CPU quando o e-mail nao existe. Nunca confere com senha alguma.
    /// </summary>
    private const string HashDescartavel =
        "pbkdf2.sha256$210000$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
}
