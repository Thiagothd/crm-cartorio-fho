namespace CrmCartorio.Api.Modules.RespostaInteligente.Application.Cota;

public sealed record DecisaoDeSelecao(bool TentarOGemini, string? MotivoParaPular, string? ChaveDeProjeto, bool EhSonda)
{
    public static DecisaoDeSelecao TentarGemini(string chaveDeProjeto, bool ehSonda) =>
        new(true, null, chaveDeProjeto, ehSonda);

    public static DecisaoDeSelecao PularGemini(string motivo, string? chaveDeProjeto = null) =>
        new(false, motivo, chaveDeProjeto, false);
}

/// <summary>
/// Decide, antes de qualquer chamada HTTP, se o Gemini deve ser tentado agora —
/// junta o disjuntor reativo (§4 do plano) com o controle preventivo de cota
/// (§3). Não faz chamada nenhuma; só olha o estado já mantido pelos dois.
/// </summary>
public class PoliticaDeSelecao(ContadorDeCotaDoGemini contador, DisjuntorDoGemini disjuntor)
{
    public async Task<DecisaoDeSelecao> DecidirAsync(OpcoesDoGemini opcoesGemini, CancellationToken ct)
    {
        if (!opcoesGemini.Habilitado || string.IsNullOrWhiteSpace(opcoesGemini.ApiKey))
        {
            return DecisaoDeSelecao.PularGemini("Gemini desligado ou sem chave configurada.");
        }

        var chaveDeProjeto = FingerprintDeChave.Calcular(opcoesGemini.ApiKey);

        if (!disjuntor.PodeTentar(chaveDeProjeto, out var ehSonda))
        {
            var situacao = disjuntor.Consultar(chaveDeProjeto);
            var quando = situacao.VoltaATentarEm is { } instante
                ? $" (nova tentativa após {instante:yyyy-MM-dd HH:mm} UTC)"
                : string.Empty;

            return DecisaoDeSelecao.PularGemini($"Disjuntor do Gemini aberto{quando}.", chaveDeProjeto);
        }

        // A sonda do meio-aberto pula o controle preventivo de propósito: é uma
        // única mensagem real testando recuperação, não vale a pena negá-la por
        // margem de cota — o risco de uma chamada a mais é desprezível perto do
        // benefício de voltar a usar o provedor gratuito.
        if (!ehSonda)
        {
            var decisaoDeCota = await contador.AvaliarEReservarAsync(chaveDeProjeto, opcoesGemini, ct);

            if (!decisaoDeCota.Permitido)
            {
                return DecisaoDeSelecao.PularGemini(
                    decisaoDeCota.MotivoDoBloqueio ?? "Margem de segurança do Gemini atingida.", chaveDeProjeto);
            }
        }

        return DecisaoDeSelecao.TentarGemini(chaveDeProjeto, ehSonda);
    }
}
