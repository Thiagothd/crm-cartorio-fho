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
    /// dedicado, conforme docs/DEPLOY-WHATSAPP.md.
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

    public bool Ativo { get; set; } = true;
}
