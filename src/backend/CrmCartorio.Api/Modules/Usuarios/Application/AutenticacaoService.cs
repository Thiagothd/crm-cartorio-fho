using CrmCartorio.Api.Infrastructure.Identity;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Usuarios.Application;

public class AutenticacaoService(
    CrmDbContext db,
    IHashDeSenha hash,
    IGeradorDeToken tokens,
    IContextoDoUsuario ctx,
    ILogger<AutenticacaoService> log) : IAutenticacaoService
{
    /// <remarks>
    /// SEC-02 — a decisao de aceitar o login <b>nunca</b> sai de um
    /// <c>BloqueadoAte</c> lido em memoria. Sai sempre do numero de linhas que uma
    /// unica instrucao SQL condicional afetou: o <c>WHERE</c> dela e a checagem de
    /// bloqueio, reavaliada pelo SQL Server no instante da ESCRITA — nao pelo
    /// aplicativo no instante da leitura. E isso, e so isso, que fecha ao mesmo
    /// tempo os dois furos da mesma causa raiz: senha correta furando um bloqueio
    /// vigente, e a corrida entre a 5a falha e um acerto simultaneo (duas
    /// requisicoes que leram o mesmo estado destravado e decidiriam as duas por
    /// conta propria).
    ///
    /// <b>A senha e conferida sempre</b>, antes de qualquer decisao publica —
    /// inexistente, errada, inativo ou bloqueado. Sair antes em qualquer um desses
    /// casos faria o tempo de resposta separar os grupos e devolver ao atacante a
    /// lista de e-mails validos. Ver o risco residual de timing no README do modulo.
    /// </remarks>
    public async Task<LoginResponse> AutenticarAsync(LoginRequest request, CancellationToken ct = default)
    {
        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var agora = DateTime.UtcNow;

        // FASE 3: o e-mail e unico por tenant, nao globalmente. Com mais de um
        // cartorio na base, dois atendentes podem ter o mesmo e-mail e esta busca
        // ficaria ambigua. Antes de ativar o multi-tenant real, o tenant precisa
        // ser resolvido aqui (subdominio, codigo do cartorio na tela de login ou
        // indice unico global de e-mail).
        var atendente = await db.Atendentes.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Email == email, ct);

        // Verifica a senha mesmo com atendente inexistente, inativo ou bloqueado:
        // sair antes faria o tempo de resposta revelar quais e-mails existem na base.
        var hashParaConferir = atendente?.SenhaHash ?? HashDescartavel;
        var senhaConfere = hash.Verificar(request.Senha ?? string.Empty, hashParaConferir);

        if (atendente is null)
        {
            log.LogWarning("Tentativa de login rejeitada para {Email}", email);
            throw UsuariosServiceException.CredenciaisInvalidas();
        }

        if (!senhaConfere)
        {
            await RegistrarFalhaAsync(atendente.Id, agora, ct);

            log.LogWarning("Tentativa de login rejeitada para {Email}", email);
            throw UsuariosServiceException.CredenciaisInvalidas();
        }

        // Senha correta — o que NAO significa login aceito. Este UPDATE e a
        // decisao: so afeta a linha se, no instante da escrita, o atendente
        // continuar ativo E nao houver bloqueio vigente. Zero linhas afetadas =
        // inativo, ou bloqueado (inclusive por uma 5a falha concorrente que
        // commitou entre a leitura acima e esta escrita).
        var aceito = await db.Atendentes
            .Where(a => a.Id == atendente.Id
                     && a.Ativo
                     && (a.BloqueadoAte == null || a.BloqueadoAte <= agora))
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.UltimoAcessoEm, agora)
                .SetProperty(a => a.FalhasDeLoginConsecutivas, 0)
                .SetProperty(a => a.NivelDeBloqueio, (byte)0)
                .SetProperty(a => a.BloqueadoAte, (DateTime?)null), ct);

        if (aceito != 1)
        {
            // Nao registra falha: a senha estava certa. Contar isto como tentativa
            // de invasao deixaria qualquer bloqueio se auto-alimentar enquanto o
            // dono legitimo tenta entrar, e a regra e explicita — tentativa durante
            // bloqueio nao estende bloqueio.
            log.LogWarning(
                "Login recusado para {Email}: conta inativa ou bloqueada (SEC-02).", email);

            throw UsuariosServiceException.CredenciaisInvalidas();
        }

        // O objeto lido acima e AsNoTracking e ainda carrega os campos de antes do
        // UPDATE. Para o token e a resposta, o que vale e o estado ja gravado.
        atendente.UltimoAcessoEm = agora;
        atendente.FalhasDeLoginConsecutivas = 0;
        atendente.NivelDeBloqueio = 0;
        atendente.BloqueadoAte = null;

        var (token, expiraEm) = tokens.Gerar(atendente);

        log.LogInformation("Login de {AtendenteId} ({Nome})", atendente.Id, atendente.Nome);

        return new LoginResponse(
            token,
            expiraEm,
            await MapearAsync(atendente, ct),
            await ObterTenantAsync(atendente.TenantId, ct));
    }

    /// <summary>
    /// Conta a falha e, na <see cref="LimiteDeFalhas"/>-esima seguida, abre o
    /// proximo bloqueio da progressao.
    /// </summary>
    /// <remarks>
    /// Tudo numa instrucao SQL so, com o proprio valor da coluna como base do
    /// calculo (nunca um valor lido antes em memoria) — sob N falhas simultaneas o
    /// SQL Server serializa as escritas na mesma linha e nenhum incremento se perde.
    ///
    /// <b>Duas condicoes no <c>WHERE</c>, as duas deliberadas:</b>
    /// <list type="bullet">
    /// <item><c>BloqueadoAte &lt;= agora</c> — tentativa feita DURANTE um bloqueio
    /// nao afeta linha nenhuma: nao conta, nao escala e, principalmente, nao
    /// estende o bloqueio. Sem isso, um atacante manteria a conta travada para
    /// sempre so continuando a tentar.</item>
    /// <item><c>Ativo</c> — conta desativada executa o PBKDF2 e responde igual a
    /// todas as outras, mas nao acumula lockout. Sem isso, uma conta desligada
    /// podia ser martelada por semanas e voltaria da reativacao ja bloqueada, no
    /// topo da progressao, sem ninguem entender por que.</item>
    /// </list>
    /// </remarks>
    private async Task RegistrarFalhaAsync(Guid atendenteId, DateTime agora, CancellationToken ct) =>
        await db.Atendentes
            .Where(a => a.Id == atendenteId
                     && a.Ativo
                     && (a.BloqueadoAte == null || a.BloqueadoAte <= agora))
            .ExecuteUpdateAsync(s => s
                // Fecha o ciclo: a falha que dispara o bloqueio zera o contador, e
                // o proximo ciclo recomeca do zero em vez de bloquear a cada
                // tentativa dali em diante.
                .SetProperty(a => a.FalhasDeLoginConsecutivas,
                    a => a.FalhasDeLoginConsecutivas + 1 >= LimiteDeFalhas
                        ? 0
                        : a.FalhasDeLoginConsecutivas + 1)
                .SetProperty(a => a.NivelDeBloqueio,
                    a => a.FalhasDeLoginConsecutivas + 1 < LimiteDeFalhas
                        ? a.NivelDeBloqueio
                        : a.NivelDeBloqueio >= TetoDoNivel
                            ? TetoDoNivel
                            : (byte)(a.NivelDeBloqueio + 1))
                // Os limiares comparam o nivel ANTES deste UPDATE (0 => primeiro
                // bloqueio => 1 min; 1 => segundo => 2 min; e assim por diante).
                .SetProperty(a => a.BloqueadoAte,
                    a => a.FalhasDeLoginConsecutivas + 1 < LimiteDeFalhas ? a.BloqueadoAte
                       : a.NivelDeBloqueio >= 5 ? agora.AddMinutes(30)
                       : a.NivelDeBloqueio == 4 ? agora.AddMinutes(16)
                       : a.NivelDeBloqueio == 3 ? agora.AddMinutes(8)
                       : a.NivelDeBloqueio == 2 ? agora.AddMinutes(4)
                       : a.NivelDeBloqueio == 1 ? agora.AddMinutes(2)
                       : agora.AddMinutes(1)), ct);

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
            await MapearAsync(atendente, ct),
            await ObterTenantAsync(atendente.TenantId, ct));
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
        // atomica (UPDATE ... SET TokenVersion = TokenVersion + 1), nao lendo o
        // valor em memoria e somando — sob duas trocas concorrentes isso
        // perderia incremento. Sobe a versao derruba todo JWT emitido antes
        // desta troca, em qualquer dispositivo, no proximo request de cada um.
        await db.Atendentes
            .Where(a => a.Id == atendente.Id && a.TenantId == ctx.TenantId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.SenhaHash, novoHash)
                .SetProperty(a => a.TokenVersion, a => a.TokenVersion + 1), ct);

        log.LogInformation("Senha alterada por {AtendenteId}", atendente.Id);
    }

    private async Task<AtendenteResponse> MapearAsync(Atendente atendente, CancellationToken ct)
    {
        var setor = atendente.SetorPadraoId is null
            ? null
            : await db.Setores.AsNoTracking()
                .Where(s => s.Id == atendente.SetorPadraoId)
                .Select(s => s.Nome)
                .FirstOrDefaultAsync(ct);

        return new AtendenteResponse(
            atendente.Id,
            atendente.Nome,
            atendente.Email,
            atendente.Perfil,
            atendente.Cargo,
            AtendenteService.DescreverCargo(atendente.Cargo),
            atendente.SetorPadraoId,
            setor,
            atendente.Ativo,
            atendente.UltimoAcessoEm);
    }

    public async Task<ServentiaPublicaResponse> ObterServentiaPublicaAsync(CancellationToken ct = default)
    {
        // Mesma regra de {NomeCartorio} nas mensagens automaticas: fantasia, com a
        // razao social como reserva. Os dois lados precisam dizer o mesmo nome.
        var nome = await db.Tenants.AsNoTracking()
            .Where(t => t.Ativo)
            .OrderBy(t => t.CriadoEm)
            .Select(t => t.NomeFantasia ?? t.RazaoSocial)
            .FirstOrDefaultAsync(ct);

        // Base ainda nao semeada: a tela de login precisa abrir mesmo assim, senao
        // o primeiro acesso de uma instalacao nova fica sem porta de entrada.
        return new ServentiaPublicaResponse(nome ?? "Cartório");
    }

    private async Task<TenantResponse> ObterTenantAsync(Guid tenantId, CancellationToken ct)
    {
        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw UsuariosServiceException.TenantNaoEncontrado();

        return new TenantResponse(
            tenant.Id,
            tenant.RazaoSocial,
            tenant.NomeFantasia,
            tenant.Cnpj,
            tenant.WhatsappNumeroExibicao,
            !string.IsNullOrWhiteSpace(tenant.WhatsappPhoneNumberId));
    }

    /// <summary>SEC-02: falhas seguidas que fecham um ciclo e abrem um bloqueio.</summary>
    private const int LimiteDeFalhas = 5;

    /// <summary>
    /// SEC-02: ultimo degrau da progressao (1, 2, 4, 8, 16, 30 minutos). Nivel 6 e
    /// o teto — dali em diante todo bloqueio novo dura 30 minutos, nunca mais.
    /// Meia hora ja torna a forca bruta inviavel sem transformar um erro de senha
    /// repetido num chamado de suporte.
    /// </summary>
    private const byte TetoDoNivel = 6;

    /// <summary>
    /// Hash valido de uma senha aleatoria, usado so para gastar o mesmo tempo de
    /// CPU quando o e-mail nao existe. Nunca confere com senha alguma.
    /// </summary>
    private const string HashDescartavel =
        "pbkdf2.sha256$210000$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
}
