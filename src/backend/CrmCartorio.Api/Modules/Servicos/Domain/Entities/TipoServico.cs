using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Modules.Servicos.Domain.Enums;

namespace CrmCartorio.Api.Modules.Servicos.Domain.Entities;

/// <summary>
/// Catalogo de atos que a serventia presta. Configuravel por tenant.
/// </summary>
public class TipoServico : EntidadeBase, IEntidadeDeTenant
{
    public Guid TenantId { get; set; }

    /// <summary>Unico por tenant. Ex.: "Reconhecimento de firma".</summary>
    public required string Nome { get; set; }

    /// <summary>Codigo interno do cartorio, se houver.</summary>
    public string? Codigo { get; set; }

    public CategoriaServico Categoria { get; set; } = CategoriaServico.Outros;

    public string? Descricao { get; set; }

    /// <summary>
    /// Setor que oferece este servico no menu de 2o nivel da triagem automatica
    /// (depois que o cliente ja escolheu o setor). Nulo de proposito para servico
    /// ainda "aguardando confirmacao" de atribuicao (ver <see cref="Descricao"/>) —
    /// sem SetorId, o servico nao entra em nenhum menu automatico ate confirmado.
    /// </summary>
    public Guid? SetorId { get; set; }

    /// <summary>
    /// Checklist de documento em texto livre, cadastravel pela tela. Populado no
    /// marcador <c>{DocumentosNecessarios}</c> das mensagens automaticas.
    /// </summary>
    public string? DocumentosNecessarios { get; set; }

    /// <summary>
    /// Posição no menu de serviços do WhatsApp. Menor aparece primeiro; empate (e o
    /// padrão, zero) cai na ordem alfabética — que era a única ordem até 2026-09-29.
    /// </summary>
    public int Ordem { get; set; }

    /// <summary>
    /// Serviço dentro do qual este aparece, formando um submenu. Ex.: "Certidão de
    /// nascimento" dentro de "Segunda via de certidão".
    /// </summary>
    /// <remarks>
    /// Um nível só: quem tem pai não pode ser pai. Com pai, o serviço sai do menu do
    /// setor e só aparece quando o cliente escolhe o pai.
    /// </remarks>
    public Guid? ServicoPaiId { get; set; }

    /// <summary>
    /// Texto que o robô manda quando o cliente escolhe este serviço, no lugar da
    /// regra geral "Serviço escolhido". Aceita os mesmos marcadores; num serviço com
    /// submenu, <c>{MenuServicos}</c> vira a lista dos serviços de dentro.
    /// </summary>
    public string? MensagemAoEscolher { get; set; }

    /// <summary>
    /// Texto que o robô manda depois que o cliente responde à
    /// <see cref="MensagemAoEscolher"/> — ex.: a chave Pix, depois que o cliente
    /// enviou os dados da certidão. Preenchido, liga a sequência dados → este texto
    /// → comprovante → atendente; vazio, o serviço segue para o menu de continuação.
    /// </summary>
    public string? MensagemAposDados { get; set; }

    /// <summary>
    /// Texto que o robô manda quando o cliente envia o comprovante, ao passar o
    /// atendimento para a equipe — ex.: "Recebemos seu comprovante!". Só vale para
    /// serviço com <see cref="MensagemAposDados"/>; vazio, sai o aviso geral de
    /// pedido de atendente.
    /// </summary>
    public string? MensagemAposComprovante { get; set; }

    public bool Ativo { get; set; } = true;

    /// <summary>SLA deste servico. Relacao 1:1, opcional.</summary>
    public PrazoServico? Prazo { get; set; }
}
