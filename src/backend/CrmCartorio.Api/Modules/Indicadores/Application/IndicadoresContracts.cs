using CrmCartorio.Api.Modules.Atendimento.Application;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;

namespace CrmCartorio.Api.Modules.Indicadores.Application;

/// <summary>Filtro comum à Visão Geral e ao Relatório. Datas em UTC.</summary>
public record FiltroDeIndicadores(DateTime? De, DateTime? Ate, string? Escopo);

public record PeriodoResponse(DateTime De, DateTime Ate, string Granularidade);

/// <summary>O instante presente — não muda com o período escolhido.</summary>
public record IndicadoresAgoraResponse(int Ativas, int NaFila, int EmAlerta, int Atrasadas);

/// <summary>
/// A mediana é a métrica honesta no volume de um cartório — um único caso
/// respondido tarde não deveria arrastar a leitura de todo o período. A média e
/// o tamanho da amostra viajam junto para quem quiser conferir.
/// </summary>
public record IndicadoresDoPeriodoResponse(
    int Abertos,
    int Encerrados,
    double? PrimeiraRespostaMedianaMinutos,
    double? PrimeiraRespostaMediaMinutos,
    int AmostraPrimeiraResposta);

/// <summary>
/// Um ponto do gráfico principal.
/// </summary>
/// <remarks>
/// <c>Abertos</c> e <c>Encerrados</c> são FLUXO (o que entrou e saiu naquele
/// balde); <c>EmAberto</c> é ESTOQUE (quantas seguiam abertas ao fim dele). É a
/// diferença entre "movimentou muito" e "está acumulando", e as duas leituras
/// juntas são o que responde se a serventia está dando conta.
///
/// <c>PrimeiraRespostaMedianaMinutos</c> é <c>null</c> quando nenhuma conversa
/// aberta neste balde recebeu resposta humana — nunca zero, que significaria
/// resposta instantânea.
/// </remarks>
public record PontoDoFluxoResponse(
    DateTime Inicio,
    int Abertos,
    int Encerrados,
    int EmAberto,
    double? PrimeiraRespostaMedianaMinutos);

/// <summary>
/// Uma conversa que pede ação. Carrega o mesmo conjunto que a linha da inbox
/// mostra — foto, nome, telefone, etapa, prazo e quem responde — para que a peça
/// da Visão Geral e a lista de atendimentos falem a mesma língua visual em vez
/// de duas leituras diferentes do mesmo atendimento.
/// </summary>
/// <remarks>
/// <c>AtendenteNome</c> nulo significa "na fila", nunca "atendente removido":
/// os grupos só contêm conversas ativas, e conversa ativa sem responsável é
/// exatamente a definição de fila.
/// </remarks>
public record ItemRequerAtencaoResponse(
    Guid ConversaId,
    string Protocolo,
    string ClienteNome,
    string ClienteTelefone,
    string? ClienteFotoPerfilUrl,
    string? EtapaNome,
    string? EtapaCor,
    SituacaoPrazo SituacaoPrazo,
    DateTime? PrazoLimite,
    DateTime UltimaMensagemEm,
    string? AtendenteNome);

public record GrupoRequerAtencaoResponse(
    string Chave,
    string Rotulo,
    int Total,
    IReadOnlyList<ItemRequerAtencaoResponse> Itens);

public record ServicoMaisProcuradoResponse(Guid? TipoServicoId, string Nome, int Total, double Percentual);

public record SaudeDoWhatsAppResponse(bool Habilitado, bool Conectado, string Estado);

public record SaudeDaIaResponse(bool Habilitada, bool EstaConfigurada, string Modelo, int LimiteDiario, int UsadoHoje);

public record SaudeDaAutomacaoResponse(int RegrasAtivas, int RegrasInativas);

/// <summary>
/// Cada campo é <c>null</c> quando o perfil da requisição não tem permissão
/// para aquele dado — nunca "sem permissão" fingido de dado real.
/// </summary>
public record SaudeDaOperacaoResponse(
    SaudeDoWhatsAppResponse? WhatsApp,
    SaudeDaIaResponse? Ia,
    SaudeDaAutomacaoResponse? Automacao);

/// <summary>
/// <c>CargaDaEquipe</c> é <c>null</c> para quem não gere cadastros — mesma
/// disciplina de <see cref="SaudeDaOperacaoResponse"/>. Não é dado secreto, mas
/// expor a carga alheia a todo atendente seria AMPLIAR permissão, e essa decisão
/// não se toma de passagem: o painel simplesmente não é renderizado, e o espaço
/// vai para a peça ao lado.
/// </summary>
public record VisaoGeralResponse(
    PeriodoResponse Periodo,
    IndicadoresAgoraResponse Agora,
    IndicadoresDoPeriodoResponse NoPeriodo,
    IReadOnlyList<PontoDoFluxoResponse> FluxoDeAtendimentos,
    IReadOnlyList<GrupoRequerAtencaoResponse> RequerAtencao,
    IReadOnlyList<CargaDoAtendenteResponse>? CargaDaEquipe,
    IReadOnlyList<ServicoMaisProcuradoResponse> ServicosMaisProcurados,
    IReadOnlyList<ContagemPorChaveResponse> DistribuicaoPorEtapa,
    SaudeDaOperacaoResponse Saude,
    IReadOnlyList<ContagemPorChaveResponse> MinhaDistribuicaoPorEtapa);

// ------------------------------------------------------- Relatório (gestão)

public record EvolucaoDaPrimeiraRespostaResponse(DateTime Dia, double MedianaMinutos, int Amostra);

public record CargaDoAtendenteResponse(Guid AtendenteId, string AtendenteNome, int Ativas, int EmAlerta, int Atrasadas);

public record EncerramentosResponse(int PorAtendente, int PeloCliente);

public record AbsorcaoDaAutomacaoResponse(int Humanas, int Automaticas, int Ia, double PercentualAutomatizado);

public record RelatorioResponse(
    PeriodoResponse Periodo,
    IReadOnlyList<EvolucaoDaPrimeiraRespostaResponse> EvolucaoDaPrimeiraResposta,
    IReadOnlyList<ContagemPorChaveResponse> DistribuicaoPorSetor,
    IReadOnlyList<ContagemPorChaveResponse> DistribuicaoPorEtapa,
    IReadOnlyList<int> HorariosDeMaiorDemanda,
    IReadOnlyList<CargaDoAtendenteResponse> CargaDaEquipe,
    EncerramentosResponse Encerramentos,
    AbsorcaoDaAutomacaoResponse AbsorcaoDaAutomacao);

// ================================================================= Relatórios
// As quatro abas da tela de Indicadores e Relatórios. Um endpoint por aba, e
// não um payload gigante: quem abre "Clientes" não paga a agregação de
// atendentes, e trocar de aba não recarrega o que já estava na tela.

/// <summary>Um mês da série de fluxo — as mesmas três grandezas do gráfico da Visão Geral.</summary>
/// <remarks>
/// <c>Abertos</c>/<c>Encerrados</c> são fluxo do mês; <c>EmAberto</c> é o estoque
/// ao FIM do mês. Ver <see cref="PontoDoFluxoResponse"/> — é a mesma leitura, em
/// outra granularidade.
/// </remarks>
public record MesDoFluxoResponse(DateTime Mes, int Abertos, int Encerrados, int EmAberto);

/// <summary>Série mensal de uma grandeza só (ex.: clientes novos por mês).</summary>
public record PontoMensalResponse(DateTime Mes, int Total);

/// <summary>
/// Resumo de uma janela fixa — "Hoje", "Últimos 7 dias", "Este mês". Sempre as
/// mesmas quatro medidas, para os três blocos serem comparáveis de relance.
/// </summary>
public record ResumoDeJanelaResponse(
    string Chave,
    string Rotulo,
    DateTime De,
    DateTime Ate,
    int Abertos,
    int Encerrados,
    double? PrimeiraRespostaMedianaMinutos,
    int AmostraPrimeiraResposta);

/// <summary>Aba 1 — Dashboard. Não recebe filtro: as janelas são fixas por definição.</summary>
public record PainelResponse(
    IReadOnlyList<MesDoFluxoResponse> DozeMeses,
    IReadOnlyList<ResumoDeJanelaResponse> Janelas);

/// <summary>Cliente numa lista ordenada por volume de atendimento.</summary>
public record ClienteNoRankingResponse(
    Guid ClienteId,
    string Nome,
    string? FotoPerfilUrl,
    string TelefoneWhatsapp,
    int Total,
    double Percentual,
    DateTime UltimoAtendimentoEm);

/// <summary>Aba 2 — Atendimentos.</summary>
public record RelatorioDeAtendimentosResponse(
    PeriodoResponse Periodo,
    int Abertos,
    int Encerrados,
    int EmAbertoAgora,
    double? PrimeiraRespostaMedianaMinutos,
    int AmostraPrimeiraResposta,
    IReadOnlyList<MesDoFluxoResponse> TotalMensal,
    IReadOnlyList<ClienteNoRankingResponse> ClientesMaisAtendidos,
    int ClientesAtendidosNoPeriodo,
    IReadOnlyList<ContagemPorChaveResponse> DistribuicaoPorSetor,
    IReadOnlyList<ContagemPorChaveResponse> DistribuicaoPorEtapa,
    IReadOnlyList<int> HorariosDeMaiorDemanda,
    AbsorcaoDaAutomacaoResponse AbsorcaoDaAutomacao);

/// <summary>Uma linha da tabela de atendentes.</summary>
/// <remarks>
/// <c>PrimeiraRespostaMedianaMinutos</c> é <c>null</c> quando o atendente não
/// respondeu nada de primeira no período — nunca zero. Ver
/// <see cref="PontoDoFluxoResponse"/> para a mesma disciplina.
/// </remarks>
public record AtendenteNoRelatorioResponse(
    Guid AtendenteId,
    string Nome,
    string CargoDescrito,
    string Perfil,
    int Ativas,
    int EmAlerta,
    int Atrasadas,
    int EncerradosNoPeriodo,
    int PrimeirasRespostasNoPeriodo,
    double? PrimeiraRespostaMedianaMinutos,
    DateTime? UltimoAcessoEm);

/// <summary>Aba 3 — Atendentes.</summary>
public record RelatorioDeAtendentesResponse(
    PeriodoResponse Periodo,
    int TotalDeAtendentes,
    int ComConversaAtiva,
    int NaFilaSemResponsavel,
    EncerramentosResponse Encerramentos,
    IReadOnlyList<AtendenteNoRelatorioResponse> Atendentes);

/// <summary>Aba 4 — Clientes.</summary>
public record RelatorioDeClientesResponse(
    PeriodoResponse Periodo,
    int Total,
    int NovosNoPeriodo,
    int ComAtendimentoAtivo,
    int PessoaFisica,
    int PessoaJuridica,
    int SemDocumento,
    IReadOnlyList<PontoMensalResponse> NovosPorMes,
    IReadOnlyList<ClienteNoRankingResponse> MaisAtendidos);

// ------------------------------------------------------- Alertas (sino)

public enum SeveridadeDeAlerta : byte
{
    Neutra = 0,
    Alerta = 1,
    Critica = 2
}

/// <summary>
/// Alerta operacional ativo — não é notificação persistida nem tem "lido" no
/// servidor. O front decide o que é "novo" comparando <see cref="Quando"/> com
/// a última vez que o painel foi aberto neste navegador.
/// </summary>
public record AlertaOperacionalResponse(
    string Id,
    string Tipo,
    SeveridadeDeAlerta Severidade,
    string Titulo,
    string? Descricao,
    DateTime Quando,
    string Rota);
