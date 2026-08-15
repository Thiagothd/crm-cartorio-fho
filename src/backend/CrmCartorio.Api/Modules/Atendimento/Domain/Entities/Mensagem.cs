using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;

namespace CrmCartorio.Api.Modules.Atendimento.Domain.Entities;

/// <summary>
/// Uma mensagem no fio da conversa. Nunca e apagada nem editada.
/// </summary>
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

    public DateTime EnviadaEm { get; set; } = DateTime.UtcNow;

    public DateTime? LidaEm { get; set; }

    /// <summary>ID da mensagem na Meta. Preenchido apenas na Fase 2.</summary>
    public string? IdExternoWhatsapp { get; set; }

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

    public Conversa? Conversa { get; set; }

    /// <summary>Auto-relacionamento para a mensagem citada.</summary>
    public Mensagem? MensagemRespondida { get; set; }
}
