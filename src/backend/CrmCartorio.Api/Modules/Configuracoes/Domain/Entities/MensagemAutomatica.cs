using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Enums;

namespace CrmCartorio.Api.Modules.Configuracoes.Domain.Entities;

/// <summary>
/// Modelo de mensagem que o sistema envia sozinho quando um evento do atendimento
/// acontece. Cadastro editavel pela tela, no mesmo padrao de <see cref="Setor"/> e
/// <see cref="EtapaFunil"/>.
/// </summary>
/// <remarks>
/// O texto NAO mora em codigo. Nenhuma frase que o cliente final le deveria exigir
/// release para mudar: o cartório parceiro ainda vai reescrever tudo isso na voz do cartorio
/// (docs/REQUISITOS.md, secao 5.7), e uma constante em C# transformaria cada ajuste
/// de vocabulario numa alteracao de codigo.
///
/// O conteudo aceita os marcadores de
/// <see cref="PlaceholdersDeMensagem"/> — {NomeCliente}, {NomeCartorio}, {Servico},
/// {Protocolo} e {MenuSetores} —, resolvidos no instante do disparo.
/// </remarks>
public class MensagemAutomatica : EntidadeBase, IEntidadeDeTenant
{
    public Guid TenantId { get; set; }

    public GatilhoDeMensagem Gatilho { get; set; }

    /// <summary>
    /// Etapa que dispara a mensagem. Obrigatorio quando
    /// <see cref="Gatilho"/> e <see cref="GatilhoDeMensagem.EtapaAlterada"/>, nulo
    /// em todos os outros gatilhos.
    /// </summary>
    /// <remarks>
    /// Nao aceita nulo com gatilho de etapa de proposito: uma regra "qualquer etapa"
    /// dispararia mensagem ao cliente em toda movimentacao interna do funil, o que
    /// e exatamente o oposto do que se quer.
    /// </remarks>
    public Guid? EtapaAlvoId { get; set; }

    /// <summary>Modelo do texto, com marcadores. Limite menor que o de Mensagem — ver configuration.</summary>
    public required string Conteudo { get; set; }

    /// <summary>Desativar para de disparar; nao apaga mensagem alguma ja enviada.</summary>
    public bool Ativo { get; set; } = true;
}
