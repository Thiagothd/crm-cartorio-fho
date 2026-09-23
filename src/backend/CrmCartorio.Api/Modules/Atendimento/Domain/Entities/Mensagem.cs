using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;

namespace CrmCartorio.Api.Modules.Atendimento.Domain.Entities;

/// <summary>
/// Uma mensagem no fio da conversa. A linha nunca e apagada.
/// </summary>
/// <remarks>
/// <b>Correcao (2026-09-22).</b> Ate entao esta classe dizia "nunca e apagada nem
/// editada", e o fio era imutavel de ponta a ponta. O atendente agora pode
/// corrigir o texto de uma mensagem que ELE mandou, ou apagar no aparelho do
/// cliente a que saiu por engano — porque o erro chega ao cliente pelo WhatsApp,
/// e o unico remedio antes disso era mandar outra mensagem pedindo desculpa.
///
/// O que NAO mudou: nenhuma linha some do banco, e nenhuma versao do texto se
/// perde. Corrigir grava o texto anterior em <see cref="ConteudoOriginal"/> e
/// assina quem corrigiu; apagar marca <see cref="ApagadaParaTodosEm"/> e mantem
/// o <see cref="Conteudo"/> onde sempre esteve. Some do aparelho do cliente,
/// nunca do registro da serventia — a mesma postura de
/// <see cref="OcultaEm"/> e de conversa encerrada (RNF-008).
/// </remarks>
public class Mensagem : EntidadeBase, IEntidadeDeTenant
{
    public Guid TenantId { get; set; }

    public Guid ConversaId { get; set; }

    public AutorTipo AutorTipo { get; set; }

    /// <summary>
    /// Obrigatorio quando <see cref="AutorTipo"/> e Atendente. E o requisito RF-007
    /// na forma mais literal: o cliente pediu para saber QUAL atendente respondeu,
    /// e a resposta e uma coluna, nao uma inferencia.
    /// Validado no servico e reforcado por CHECK no banco.
    /// </summary>
    public Guid? AtendenteId { get; set; }

    public required string Conteudo { get; set; }

    public TipoMensagem Tipo { get; set; } = TipoMensagem.Texto;

    public StatusEntrega StatusEntrega { get; set; } = StatusEntrega.Enviada;

    /// <summary>Instante em que uma execucao adquiriu o claim exclusivo de envio.</summary>
    public DateTime? EnvioIniciadoEm { get; set; }

    /// <summary>
    /// Instante persistido imediatamente antes da chamada externa. Quando preenchido,
    /// o resultado pode ser ambiguo e a mensagem nao pode ser retentada automaticamente.
    /// </summary>
    public DateTime? EnvioDisparadoEm { get; set; }

    /// <summary>
    /// A mensagem foi gerada por uma regra de <c>MensagemAutomatica</c>, nao escrita
    /// por gente.
    /// </summary>
    /// <remarks>
    /// Combinada com <see cref="AutorTipo"/> = Sistema, e o que separa duas coisas
    /// que sem ela ficariam iguais no banco:
    /// <list type="bullet">
    ///   <item>Sistema + <c>false</c>: anotacao interna do fio ("Conversa assumida
    ///   por Fulano"). O cliente nunca receberia isso.</item>
    ///   <item>Sistema + <c>true</c>: mensagem endereçada AO CLIENTE, que sai pelo
    ///   WhatsApp quando a Fase 2 conectar.</item>
    /// </list>
    /// Por isso a interface as desenha diferente, e por isso o atendente precisa do
    /// rotulo "Automatica": sem ele, ele acharia que um colega escreveu aquilo.
    /// </remarks>
    public bool EhAutomatica { get; set; }

    /// <summary>
    /// A mensagem automática veio da Resposta Inteligente (IA), não de uma regra de
    /// <c>MensagemAutomatica</c> cadastrada.
    /// </summary>
    /// <remarks>
    /// Sempre acompanha <see cref="EhAutomatica"/> = true. Sem este campo as duas
    /// origens ficariam indistinguíveis no banco, e a interface não teria como
    /// rotular "IA" em vez de "Automática" — distinção que importa: uma é resposta
    /// de regra fixa, a outra é gerada por modelo de linguagem.
    /// </remarks>
    public bool GeradaPorIa { get; set; }

    /// <summary>
    /// Alguem da serventia respondeu direto pelo WhatsApp — celular ou WhatsApp
    /// Web —, fora do painel.
    /// </summary>
    /// <remarks>
    /// Sempre com <see cref="AutorTipo"/> = Atendente e <see cref="AtendenteId"/>
    /// NULO (CHECK CK_Mensagens_EnviadaPeloAparelho). Atendente, porque e GENTE da
    /// serventia falando com o cliente — tudo o que conta resposta humana (o
    /// "aguardando resposta do cartorio", o tempo de primeira resposta, a
    /// inatividade) precisa enxergar esta mensagem como a de qualquer atendente.
    /// Sem <see cref="AtendenteId"/>, porque o aparelho nao tem login: o sistema
    /// sabe que a serventia respondeu, nao QUEM. Inventar um autor seria pior do
    /// que admitir que nao sabe — e esta marca e o que explica a ausencia, onde
    /// antes a falta do atendente seria defeito (RF-007).
    ///
    /// Chega pelo webhook como "enviada por mim", o mesmo sinal do eco das
    /// mensagens que o proprio painel manda. A separacao entre as duas coisas
    /// mora em <c>IdentificadorDeEco</c> (CanalWhatsApp).
    /// </remarks>
    public bool EnviadaPeloAparelho { get; set; }

    public DateTime EnviadaEm { get; set; } = DateTime.UtcNow;

    public DateTime? LidaEm { get; set; }

    /// <summary>ID da mensagem na Meta. Preenchido apenas na Fase 2.</summary>
    public string? IdExternoWhatsapp { get; set; }

    /// <summary>
    /// Estado do processamento posterior da entrada. Nulo para mensagens que nao
    /// vieram do webhook e para o historico anterior a esta funcionalidade.
    /// </summary>
    public EstadoProcessamentoEntradaWhatsapp? EstadoProcessamentoEntrada { get; set; }

    /// <summary>Instante em que um processador adquiriu o claim desta entrada.</summary>
    public DateTime? ProcessamentoEntradaIniciadoEm { get; set; }

    /// <summary>Instante em que automacoes e transicoes desta entrada foram persistidas.</summary>
    public DateTime? ProcessamentoEntradaConcluidoEm { get; set; }

    /// <summary>Quando uma entrada em falha volta a estar disponivel para tentativa.</summary>
    public DateTime? ProcessamentoEntradaDisponivelEm { get; set; }

    /// <summary>Quantidade de claims adquiridos, inclusive recuperacoes apos falha/crash.</summary>
    public int TentativasProcessamentoEntrada { get; set; }

    /// <summary>Diagnostico curto da ultima falha; nunca contem payload ou segredo.</summary>
    public string? ErroProcessamentoEntrada { get; set; }

    /// <summary>
    /// Nome com que o arquivo chegou. Dado EXIBIDO, nunca caminho.
    /// </summary>
    /// <remarks>
    /// O nome vem do aparelho do cliente e nao e confiavel: um
    /// <c>..\..\web.config</c> daria escrita fora da pasta de anexos se virasse
    /// caminho. O arquivo em disco se chama <c>{Id}{extensao}</c>; este campo so
    /// alimenta o fio e o <c>Content-Disposition</c> do download.
    /// </remarks>
    public string? ArquivoNome { get; set; }

    /// <summary>Tipo MIME ja conferido contra a lista de permitidos.</summary>
    public string? ArquivoTipoMime { get; set; }

    public long? ArquivoTamanhoBytes { get; set; }

    /// <summary>
    /// Caminho do arquivo RELATIVO a raiz configurada.
    /// </summary>
    /// <remarks>
    /// Relativo de proposito: mover a pasta de anexos para outro disco nao pode
    /// invalidar as linhas ja gravadas. O binario nao entra no banco — o .bak
    /// cresceria com cada foto de documento, e sao muitas num cartorio.
    ///
    /// <b>Consequencia para backup:</b> copia so do SQL Server restaura conversa
    /// com anexo quebrado. A pasta tem de entrar na rotina junto.
    /// </remarks>
    public string? ArquivoCaminho { get; set; }

    /// <summary>Tem arquivo gravado e legivel.</summary>
    public bool TemArquivo => !string.IsNullOrWhiteSpace(ArquivoCaminho);

    /// <summary>
    /// Mensagem que esta responde, como no WhatsApp. Nulo na maioria dos casos.
    /// </summary>
    /// <remarks>
    /// Guardar a referencia, e nao um recorte do texto citado, e o que garante que
    /// a citacao continue correta: o trecho e resolvido na leitura a partir da
    /// mensagem original. Copiar o texto no momento do envio criaria duas versoes
    /// da mesma frase no banco.
    /// </remarks>
    public Guid? MensagemRespondidaId { get; set; }

    /// <summary>
    /// Mensagem fixada no topo da conversa. Nulo na imensa maioria.
    /// </summary>
    /// <remarks>
    /// UMA por conversa: fixar outra desafixa a anterior. Um mural de mensagens
    /// fixadas resolveria menos do que atrapalha — o topo do fio e o lugar mais
    /// caro da tela, e disputa-lo entre cinco avisos e perde-lo.
    /// </remarks>
    public DateTime? FixadaEm { get; set; }

    public Guid? FixadaPorAtendenteId { get; set; }

    /// <summary>
    /// Mensagem ocultada do fio. A LINHA CONTINUA NO BANCO.
    /// </summary>
    /// <remarks>
    /// Nao existe exclusao de mensagem em caminho nenhum do sistema, e isto nao e
    /// uma limitacao: e a mesma postura de conversa encerrada e cliente desativado
    /// (RNF-008). Em cartorio, o que foi dito fica dito — some da vista de quem
    /// atende, nao do registro.
    ///
    /// Guardar QUEM ocultou e QUANDO e o que separa "ocultar" de "apagar com outro
    /// nome": ha a quem perguntar depois.
    /// </remarks>
    public DateTime? OcultaEm { get; set; }

    public Guid? OcultaPorAtendenteId { get; set; }

    /// <summary>
    /// Texto como saiu da PRIMEIRA vez, guardado na primeira correcao.
    /// </summary>
    /// <remarks>
    /// Guarda o original, nao a versao anterior: a segunda correcao nao
    /// sobrescreve este campo. O que importa para quem audita depois e o que o
    /// cliente recebeu antes de qualquer conserto — as versoes intermediarias sao
    /// tentativas de escrever a mesma frase, e um historico completo de cada
    /// tecla custaria uma tabela inteira para responder uma pergunta que ninguem
    /// faz.
    ///
    /// Nulo enquanto a mensagem nunca foi corrigida. <see cref="Conteudo"/>
    /// continua sendo o texto vigente, em toda consulta, citacao e historico da
    /// IA — quem le o fio le a versao certa, sem saber que houve conserto.
    /// </remarks>
    public string? ConteudoOriginal { get; set; }

    /// <summary>Quando a ultima correcao foi feita. Nulo = nunca corrigida.</summary>
    public DateTime? CorrigidaEm { get; set; }

    /// <summary>Quem corrigiu por ultimo. Sempre um atendente — a IA nunca corrige.</summary>
    public Guid? CorrigidaPorAtendenteId { get; set; }

    /// <summary>
    /// Quando a mensagem foi apagada no aparelho do cliente. A LINHA CONTINUA NO
    /// BANCO, com <see cref="Conteudo"/> intacto.
    /// </summary>
    /// <remarks>
    /// Diferente de <see cref="OcultaEm"/>, que so esconde de quem atende: aqui o
    /// pedido vai ao WhatsApp e o cliente passa a ver "Esta mensagem foi
    /// apagada". Nao ha como fazer isso em silencio, e nao deveria haver — o
    /// cartorio nao reescreve o que o cliente ja leu, so retira o que saiu
    /// errado.
    ///
    /// Marcado somente DEPOIS de o gateway confirmar. Marcar antes faria o fio
    /// afirmar ao atendente algo que nao aconteceu no aparelho do cliente.
    /// </remarks>
    public DateTime? ApagadaParaTodosEm { get; set; }

    /// <summary>Quem apagou. Ha a quem perguntar depois.</summary>
    public Guid? ApagadaParaTodosPorAtendenteId { get; set; }

    /// <summary>
    /// Mensagem de origem, quando esta e um encaminhamento.
    /// </summary>
    /// <remarks>
    /// Mesma escolha de <see cref="MensagemRespondidaId"/>: guarda a referencia, e
    /// nao um recorte do texto. A partir dela se chega a conversa de origem — a
    /// procedencia do que foi encaminhado nao se perde.
    /// </remarks>
    public Guid? EncaminhadaDeMensagemId { get; set; }

    public Conversa? Conversa { get; set; }

    /// <summary>Auto-relacionamento para a mensagem citada.</summary>
    public Mensagem? MensagemRespondida { get; set; }

    /// <summary>Auto-relacionamento para a mensagem encaminhada.</summary>
    public Mensagem? EncaminhadaDeMensagem { get; set; }
}
