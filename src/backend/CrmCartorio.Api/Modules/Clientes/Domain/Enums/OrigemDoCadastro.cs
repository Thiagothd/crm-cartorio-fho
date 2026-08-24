namespace CrmCartorio.Api.Modules.Clientes.Domain.Enums;

/// <summary>De onde o cadastro deste cliente veio.</summary>
/// <remarks>
/// Nulo (sem valor) nos clientes que já existiam antes desta coluna — a
/// verdade ali é "não sabemos", não um quinto valor disfarçado. Existe para
/// responder "de onde vieram esses 300?": a diferença entre "essas pessoas
/// falaram com o cartório" e "essas vieram de uma planilha" importa para
/// decidir o que fazer com o contato — e passa a importar mais ainda no dia em
/// que houver um canal para falar com essas pessoas em massa (a via oficial da
/// Meta, quando os pré-requisitos do cartório parceiro fecharem).
/// </remarks>
public enum OrigemDoCadastro : byte
{
    /// <summary>Cliente que mandou mensagem pelo WhatsApp — o caminho normal.</summary>
    Webhook = 1,

    /// <summary>Cadastrado na tela por um atendente, sem o cliente ter escrito.</summary>
    Manual = 2,

    /// <summary>Importado de texto colado ou arquivo CSV.</summary>
    ImportacaoPlanilha = 3,

    /// <summary>Importado da agenda de contatos do WhatsApp pareado.</summary>
    ImportacaoWhatsapp = 4
}
