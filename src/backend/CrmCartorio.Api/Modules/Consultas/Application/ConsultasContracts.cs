namespace CrmCartorio.Api.Modules.Consultas.Application;

/// <summary>
/// Endereco resolvido a partir do CEP, no formato que o cadastro de cliente usa.
/// </summary>
/// <remarks>
/// Deliberadamente nao e o formato de nenhum provedor. BrasilAPI devolve campo em
/// ingles (street, neighborhood, city, state) e ViaCEP em portugues (logradouro,
/// bairro, localidade, uf); os dois viram isto aqui. Trocar de provedor amanha nao
/// mexe no frontend.
/// </remarks>
public record EnderecoConsultado(
    string Cep,
    string? Logradouro,
    string? Bairro,
    string? Cidade,
    string? Uf);

/// <summary>Empresa resolvida a partir do CNPJ.</summary>
public record EmpresaConsultada(
    string Cnpj,
    string? RazaoSocial,
    string? NomeFantasia,
    EnderecoConsultado? Endereco);
