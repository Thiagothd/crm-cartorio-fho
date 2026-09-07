using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Tenants.Domain.Enums;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Tenants.Application;

public class TenantService(CrmDbContext db, IContextoDoUsuario ctx) : ITenantService
{
    public async Task<AparenciaResponse> ObterAparenciaAsync(CancellationToken ct = default)
    {
        var aparencia = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == ctx.TenantId)
            .Select(t => t.AparenciaDaConversa)
            .FirstOrDefaultAsync(ct);

        return new AparenciaResponse(
            aparencia == default ? AparenciaDaConversa.PapelLiso : aparencia);
    }

    public async Task<AparenciaResponse> DefinirAparenciaAsync(
        SalvarAparenciaRequest request,
        CancellationToken ct = default)
    {
        // Mesma exigencia dos demais cadastros da tela de Configuracoes: e a cara
        // do cartorio para todo mundo, nao preferencia de quem esta logado.
        if (!ctx.Pode(PermissaoDoSistema.AparenciaEditar))
        {
            throw new TenantsServiceException(
                "sem_permissao", "Seu perfil nao permite alterar a aparencia.", 403);
        }

        if (!Enum.IsDefined(request.AparenciaDaConversa))
        {
            throw new TenantsServiceException(
                "aparencia_invalida", "Aparencia desconhecida.", 400);
        }

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == ctx.TenantId, ct)
            ?? throw new TenantsServiceException(
                "tenant_nao_encontrado", "Serventia nao encontrada.", 404);

        tenant.AparenciaDaConversa = request.AparenciaDaConversa;

        await db.SaveChangesAsync(ct);

        return new AparenciaResponse(tenant.AparenciaDaConversa);
    }

    /// <summary>Menor e maior tempo aceito, em minutos.</summary>
    /// <remarks>
    /// O piso de 5 minutos existe para o teste do proprio recurso: e o que permite
    /// ver o ciclo inteiro (pergunta, espera, encerramento) em cerca de dez minutos,
    /// sem esperar um turno de trabalho. O teto de 30 dias e o ponto em que
    /// "encerramento automatico" ja nao significa mais nada.
    /// </remarks>
    private const int MinutosMinimos = 5;
    private const int MinutosMaximos = 43_200;

    public async Task<EncerramentoAutomaticoResponse> ObterEncerramentoAutomaticoAsync(
        CancellationToken ct = default)
    {
        // Leitura tambem exige gestao, diferente da aparencia: aquela e lida pelo fio
        // de mensagens de qualquer atendente para desenhar o fundo, esta so existe
        // para a tela de configuracao.
        GarantirGestao();

        return await db.Tenants.AsNoTracking()
            .Where(t => t.Id == ctx.TenantId)
            .Select(t => new EncerramentoAutomaticoResponse(
                t.MinutosParaPerguntarEncerramento,
                t.MinutosParaEncerrarSemResposta,
                t.EncerramentoHoraInicial,
                t.EncerramentoHoraFinal))
            .FirstOrDefaultAsync(ct)
            ?? throw new TenantsServiceException(
                "tenant_nao_encontrado", "Serventia nao encontrada.", 404);
    }

    public async Task<EncerramentoAutomaticoResponse> DefinirEncerramentoAutomaticoAsync(
        SalvarEncerramentoAutomaticoRequest request,
        CancellationToken ct = default)
    {
        GarantirGestao();

        // Os dois minutos andam juntos: com so um preenchido o sistema perguntaria
        // sem nunca encerrar, ou o contrario. Ou o recurso esta configurado, ou esta
        // desligado.
        var desligando = request.MinutosParaPerguntar is null && request.MinutosParaEncerrar is null;

        if (!desligando && (request.MinutosParaPerguntar is null || request.MinutosParaEncerrar is null))
        {
            throw new TenantsServiceException(
                "configuracao_incompleta",
                "Informe os dois tempos, ou deixe os dois vazios para desligar.",
                400);
        }

        ValidarMinutos(request.MinutosParaPerguntar, "para perguntar");
        ValidarMinutos(request.MinutosParaEncerrar, "para encerrar sem resposta");

        // Janela tambem e tudo-ou-nada: uma ponta so nao descreve intervalo nenhum.
        if (request.HoraInicial is null != (request.HoraFinal is null))
        {
            throw new TenantsServiceException(
                "janela_incompleta",
                "Informe as duas horas da janela, ou deixe as duas vazias para qualquer hora.",
                400);
        }

        ValidarHora(request.HoraInicial);
        ValidarHora(request.HoraFinal);

        if (request.HoraInicial == request.HoraFinal && request.HoraInicial is not null)
        {
            throw new TenantsServiceException(
                "janela_vazia",
                "A hora inicial e a final nao podem ser iguais — isso nao deixaria nenhum horario disponivel.",
                400);
        }

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == ctx.TenantId, ct)
            ?? throw new TenantsServiceException(
                "tenant_nao_encontrado", "Serventia nao encontrada.", 404);

        tenant.MinutosParaPerguntarEncerramento = request.MinutosParaPerguntar;
        tenant.MinutosParaEncerrarSemResposta = request.MinutosParaEncerrar;
        tenant.EncerramentoHoraInicial = request.HoraInicial;
        tenant.EncerramentoHoraFinal = request.HoraFinal;

        await db.SaveChangesAsync(ct);

        if (desligando)
        {
            // Desligar precisa limpar as perguntas pendentes. Sem isto, as conversas
            // que ja receberam "deseja encerrar?" ficariam com o carimbo para sempre:
            // nunca encerradas, porque a varredura parou, e com o despacho tratando a
            // proxima mensagem do cliente como resposta a uma pergunta que ninguem
            // mais faz.
            await db.Conversas
                .Where(c => c.TenantId == ctx.TenantId && c.PerguntaDeEncerramentoEnviadaEm != null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.PerguntaDeEncerramentoEnviadaEm, (DateTime?)null), ct);
        }

        return new EncerramentoAutomaticoResponse(
            tenant.MinutosParaPerguntarEncerramento,
            tenant.MinutosParaEncerrarSemResposta,
            tenant.EncerramentoHoraInicial,
            tenant.EncerramentoHoraFinal);
    }

    private void GarantirGestao()
    {
        if (!ctx.Pode(PermissaoDoSistema.AutomacaoEncerramentoEditar))
        {
            throw new TenantsServiceException(
                "sem_permissao",
                "Seu perfil nao permite ver ou alterar o encerramento automatico.",
                403);
        }
    }

    private static void ValidarMinutos(int? minutos, string qual)
    {
        if (minutos is { } valor && (valor < MinutosMinimos || valor > MinutosMaximos))
        {
            throw new TenantsServiceException(
                "tempo_invalido",
                $"O tempo {qual} precisa ficar entre {MinutosMinimos} minutos e {MinutosMaximos / 1440} dias.",
                400);
        }
    }

    private static void ValidarHora(int? hora)
    {
        if (hora is { } valor && valor is < 0 or > 23)
        {
            throw new TenantsServiceException(
                "hora_invalida", "A hora precisa ficar entre 0 e 23.", 400);
        }
    }
}

public class TenantsServiceException(string codigo, string mensagem, int statusHttp = 400)
    : ExcecaoDeAplicacao(codigo, mensagem, statusHttp);
