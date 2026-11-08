using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Modules.CanalWhatsApp.Domain.Enums;
using CrmCartorio.Api.Modules.Tenants.Domain.Enums;

namespace CrmCartorio.Api.Modules.Tenants.Domain.Entities;

/// <summary>
/// Cartorio cliente. Na Fase 1 existe um unico registro (o cartorio do cartório parceiro),
/// mas a estrutura ja e multi-tenant.
/// </summary>
public class Tenant : EntidadeBase
{
    public required string RazaoSocial { get; set; }

    public string? NomeFantasia { get; set; }

    /// <summary>Somente digitos.</summary>
    public required string Cnpj { get; set; }

    /// <summary>WABA ID da Meta. Preenchido apenas na Fase 2.</summary>
    public string? WhatsappBusinessAccountId { get; set; }

    /// <summary>Phone Number ID da Cloud API. Preenchido apenas na Fase 2.</summary>
    public string? WhatsappPhoneNumberId { get; set; }

    /// <summary>Numero em formato E.164, apenas para exibicao.</summary>
    public string? WhatsappNumeroExibicao { get; set; }

    /// <summary>
    /// Nome da instancia pareada no gateway Evolution. UMA INSTANCIA = UM TENANT.
    /// </summary>
    /// <remarks>
    /// E por aqui que o webhook descobre de qual serventia e a mensagem. O
    /// endpoint do webhook e anonimo — o gateway nao carrega JWT —, entao nao ha
    /// token de onde ler o TenantId; o payload traz a instancia e este campo faz
    /// a ponte.
    ///
    /// Nulo enquanto o canal nao foi conectado. Preenchido ao parear pela tela de
    /// Configuracoes. Cada ambiente usa sua propria instancia e seu numero
    /// dedicado, conforme docs/INSTALACAO.md.
    /// </remarks>
    public string? WhatsappInstancia { get; set; }

    /// <summary>
    /// Como o canal decide quem pode falar com o numero pareado. Nulo enquanto
    /// ninguem configurou isto pela tela — nesse estado quem manda ainda e
    /// <c>CanalWhatsApp:NumerosDeTeste</c> do appsettings (ver
    /// <c>IRegrasDoCanal</c>). Assim que alguem salva um modo pela tela, o banco
    /// passa a mandar e o appsettings vira letra morta.
    /// </summary>
    public ModoDeRecebimento? WhatsappModoDeRecebimento { get; set; }

    /// <summary>Nome do perfil do WhatsApp da sessao pareada. Lido do gateway ao conectar.</summary>
    public string? WhatsappPerfilNome { get; set; }

    /// <summary>URL da foto de perfil da sessao pareada. Lida do gateway ao conectar.</summary>
    public string? WhatsappPerfilFotoUrl { get; set; }

    /// <summary>Quando a sessao atual pareou. Nulo enquanto desconectado.</summary>
    public DateTime? WhatsappConectadoEm { get; set; }

    /// <summary>
    /// Textura de fundo do fio de mensagens.
    /// </summary>
    /// <remarks>
    /// Por TENANT, e nao por atendente: e a cara do cartorio, e a mesma tela vista
    /// por duas pessoas da mesma serventia nao deveria parecer dois produtos.
    /// </remarks>
    public AparenciaDaConversa AparenciaDaConversa { get; set; } = AparenciaDaConversa.PapelLiso;

    /// <summary>
    /// SEC-03: liga a exigencia de MFA para <c>Administrador</c> e para quem tem
    /// <c>CargoNaServentia.Titular</c>. So pode ser ligado por uma operacao
    /// transacional que confirma que todos os alvos ja tem MFA ativo e sobe o
    /// TokenVersion deles — nunca por um UPDATE direto nesta coluna. Ver
    /// <c>MfaService.AtivarObrigatoriedadeAsync</c>.
    /// </summary>
    public bool MfaObrigatorioParaAdministradores { get; set; }

    /// <summary>
    /// Minutos de silencio antes de perguntar ao cliente se pode encerrar.
    /// <b>Nulo desliga o encerramento automatico inteiro.</b>
    /// </summary>
    /// <remarks>
    /// Mesma disciplina de <see cref="WhatsappModoDeRecebimento"/>: nulo nao e "zero"
    /// nem um padrao implicito, e o estado "ninguem decidiu isto" — e enquanto
    /// ninguem decide, o sistema nao mexe em conversa nenhuma.
    ///
    /// O relogio e <see cref="Atendimento.Domain.Entities.Conversa.UltimaMensagemEm"/>,
    /// que so avanca com mensagem de GENTE (cliente ou atendente): aviso automatico
    /// nao renova a contagem, entao uma conversa que so recebeu robo continua
    /// envelhecendo.
    /// </remarks>
    public int? MinutosParaPerguntarEncerramento { get; set; }

    /// <summary>
    /// Minutos de espera pela resposta antes de encerrar sozinho. Conta a partir de
    /// <see cref="Atendimento.Domain.Entities.Conversa.PerguntaDeEncerramentoEnviadaEm"/>.
    /// </summary>
    public int? MinutosParaEncerrarSemResposta { get; set; }

    /// <summary>
    /// Hora local (0-23) a partir da qual a pergunta de encerramento pode sair.
    /// Nulo nos dois extremos = qualquer hora.
    /// </summary>
    /// <remarks>
    /// Existe porque o cliente do cartorio le a mensagem no celular dele: com poucos
    /// minutos de silencio, uma conversa que parou as 21h seria perguntada de
    /// madrugada. A janela vale so para PERGUNTAR — encerrar e silencioso e pode
    /// acontecer a qualquer hora.
    /// </remarks>
    public int? EncerramentoHoraInicial { get; set; }

    /// <summary>Hora local (0-23) ate a qual a pergunta pode sair. Ver <see cref="EncerramentoHoraInicial"/>.</summary>
    public int? EncerramentoHoraFinal { get; set; }

    /// <summary>
    /// Chave geral das mensagens automaticas. <b>Desligada, nenhum gatilho
    /// dispara</b> — inclusive o bot de triagem (pedido de nome, boas-vindas com
    /// menu de setores, menu de servicos, despedida): o cliente manda "oi" e nao
    /// recebe nada de automatico.
    /// </summary>
    /// <remarks>
    /// Porta unica: <c>DisparadorDeMensagemAutomatica.EnfileirarAsync</c>. O
    /// efeito e o mesmo de desativar TODAS as linhas de MensagemAutomatica uma a
    /// uma — estado que o fluxo ja suporta, porque cada aplicador decide a
    /// proxima flag de espera (AguardandoTriagem, AguardandoNome...) pela
    /// contagem do que foi gerado. A granularidade por regra continua no
    /// <c>Ativo</c> de cada mensagem; isto e o "desliga tudo" sem perder o
    /// cadastro. Nasce ligada (DEFAULT 1 na 049) — e o comportamento de antes.
    /// </remarks>
    public bool MensagensAutomaticasHabilitadas { get; set; } = true;

    /// <summary>
    /// Chave geral da Resposta Inteligente (IA), pela tela. Substitui o antigo
    /// <c>RespostaInteligente:Habilitada</c> do appsettings, que exigia deploy.
    /// </summary>
    /// <remarks>
    /// Porta unica: <c>RespostaInteligenteService.ResponderAsync</c>. Desligada,
    /// devolve o mesmo <c>PodeResponder=false</c> de "IA desligada ou sem
    /// provedor" — o piso de sempre (RespostaSemCorrespondencia / pedido de
    /// atendente) continua valendo, entao o cliente nunca fica sem caminho.
    /// Ligada, a IA so responde se houver provedor configurado (chave no
    /// appsettings) — essa parte continua sendo infraestrutura, nao escolha do
    /// cartorio. Nasce ligada (DEFAULT 1 na 049), preservando o piloto.
    /// </remarks>
    public bool RespostaInteligenteHabilitada { get; set; } = true;

    public bool Ativo { get; set; } = true;
}
