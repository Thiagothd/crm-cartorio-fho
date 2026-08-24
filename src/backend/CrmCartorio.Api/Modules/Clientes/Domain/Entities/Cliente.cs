using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Modules.Clientes.Domain.Enums;

namespace CrmCartorio.Api.Modules.Clientes.Domain.Entities;

/// <summary>
/// Contato final que manda mensagem para o cartorio. Nao e usuario do sistema.
/// </summary>
/// <remarks>
/// Guarda a QUALIFICACAO da parte, nao so o contato. Numa serventia de Notas e
/// Registro Civil, quase todo ato exige qualificar quem comparece — nome,
/// nacionalidade, estado civil, profissao, documento, filiacao e endereco entram
/// no corpo da escritura, da procuracao e da certidao. Coletar isso na conversa,
/// antes da pessoa vir ao balcao, e o que o produto tem de util a oferecer aqui.
///
/// Tudo alem de Nome e TelefoneWhatsapp e OPCIONAL de proposito: exigir
/// qualificacao completa no primeiro "bom dia" trava o atendimento.
/// </remarks>
public class Cliente : EntidadeBase, IEntidadeDeTenant
{
    public Guid TenantId { get; set; }

    // --- identificacao -------------------------------------------------------

    /// <summary>Nome do perfil do WhatsApp ate alguem corrigir no cadastro.</summary>
    public required string Nome { get; set; }

    public TipoDePessoa Tipo { get; set; } = TipoDePessoa.Fisica;

    /// <summary>
    /// E.164 sem simbolos (ex.: 5538999000045). E a identidade real do contato:
    /// e por ele que a mensagem chega. Unico por tenant.
    /// </summary>
    public required string TelefoneWhatsapp { get; set; }

    /// <summary>Telefone alternativo, fixo ou recado. Nao precisa ser WhatsApp.</summary>
    public string? TelefoneAlternativo { get; set; }

    public string? Email { get; set; }

    /// <summary>CPF ou CNPJ, so digitos. Opcional.</summary>
    public string? Documento { get; set; }

    // --- pessoa fisica -------------------------------------------------------

    public string? Rg { get; set; }

    /// <summary>Orgao expedidor do RG (SSP, DETRAN, etc.).</summary>
    public string? OrgaoEmissor { get; set; }

    /// <summary>UF do orgao expedidor.</summary>
    public string? UfOrgaoEmissor { get; set; }

    public DateOnly? DataNascimento { get; set; }

    public EstadoCivil EstadoCivil { get; set; } = EstadoCivil.NaoInformado;

    /// <summary>Preenchido apenas quando o estado civil e Casado.</summary>
    public RegimeDeBens RegimeDeBens { get; set; } = RegimeDeBens.NaoInformado;

    public string? Profissao { get; set; }

    public string? Nacionalidade { get; set; }

    /// <summary>Cidade e UF de nascimento, como entra na qualificacao.</summary>
    public string? Naturalidade { get; set; }

    /// <summary>Filiacao. Obrigatoria em registro civil, comum em qualificacao.</summary>
    public string? NomeDoPai { get; set; }

    public string? NomeDaMae { get; set; }

    // --- pessoa juridica -----------------------------------------------------

    /// <summary>Razao social, quando Tipo e Juridica.</summary>
    public string? RazaoSocial { get; set; }

    /// <summary>Quem assina pela empresa.</summary>
    public string? RepresentanteLegal { get; set; }

    // --- endereco ------------------------------------------------------------

    public string? Cep { get; set; }
    public string? Logradouro { get; set; }
    public string? Numero { get; set; }
    public string? Complemento { get; set; }
    public string? Bairro { get; set; }
    public string? Cidade { get; set; }
    public string? Uf { get; set; }

    // --- controle ------------------------------------------------------------

    public string? Observacoes { get; set; }

    /// <summary>
    /// URL direta da foto de perfil no WhatsApp, quando disponivel. Temporaria.
    /// </summary>
    public string? FotoPerfilUrl { get; set; }

    /// <summary>Instante UTC da ultima consulta/atualizacao da foto de perfil.</summary>
    public DateTime? FotoPerfilAtualizadaEm { get; set; }

    /// <summary>
    /// Quando o sistema perguntou a este contato como ele quer ser chamado. Nulo
    /// enquanto nunca perguntamos.
    /// </summary>
    /// <remarks>
    /// Fato datado, no mesmo molde de <c>Conversa.AvisoDeAlertaEnviadoEm</c>: nao
    /// grava "o nome esta pendente" (isso envelhece e se calcula na leitura, com
    /// <see cref="NomeDeContato.EhUtil"/>), grava "a pergunta saiu", que nao muda
    /// mais.
    ///
    /// Serve a uma coisa so: <b>nao perguntar duas vezes</b>. Quem nao quis dizer o
    /// nome seria interrogado a cada atendimento novo sem isto — e atendimento novo
    /// e frequente, porque conversa parada encerra sozinha e o proximo "bom dia"
    /// abre outra.
    ///
    /// De quebra, conta a historia para o atendente que abre a ficha: "ja pediram e
    /// ele nao deu" e diferente de "ninguem tentou".
    /// </remarks>
    public DateTime? NomePerguntadoEm { get; set; }

    /// <summary>De onde este cadastro veio. Nulo nos clientes de antes desta coluna.</summary>
    public OrigemDoCadastro? OrigemDoCadastro { get; set; }

    public DateTime PrimeiroContatoEm { get; set; } = DateTime.UtcNow;

    public bool Ativo { get; set; } = true;

    /// <summary>
    /// Quanto da qualificacao ja foi coletado, de 0 a 100. Calculado na leitura,
    /// nunca persistido — o conjunto de campos considerados muda, e um numero
    /// gravado envelheceria calado.
    /// </summary>
    public int PercentualDeQualificacao()
    {
        var campos = Tipo == TipoDePessoa.Juridica
            ? new[]
            {
                Documento, RazaoSocial, RepresentanteLegal, Email,
                Cep, Logradouro, Numero, Bairro, Cidade, Uf
            }
            :
            [
                Documento, Rg, OrgaoEmissor, Profissao, Nacionalidade, Naturalidade,
                NomeDoPai, NomeDaMae, Cep, Logradouro, Numero, Bairro, Cidade, Uf
            ];

        var preenchidos = campos.Count(c => !string.IsNullOrWhiteSpace(c));
        var total = campos.Length;

        // Data de nascimento e estado civil nao sao string, entram fora do array.
        if (Tipo == TipoDePessoa.Fisica)
        {
            total += 2;

            if (DataNascimento is not null)
            {
                preenchidos++;
            }

            if (EstadoCivil != EstadoCivil.NaoInformado)
            {
                preenchidos++;
            }
        }

        return total == 0 ? 0 : (int)Math.Round(preenchidos * 100d / total);
    }
}
