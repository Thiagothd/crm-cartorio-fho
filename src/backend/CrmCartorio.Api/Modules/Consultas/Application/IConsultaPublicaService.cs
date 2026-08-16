namespace CrmCartorio.Api.Modules.Consultas.Application;

/// <summary>
/// Consulta a fonte publica para PREENCHER campo de cadastro. Nunca grava nada e
/// nunca decide nada — o atendente edita por cima do resultado.
/// </summary>
/// <remarks>
/// Nao existe, e nao vai existir, consulta por CPF. Ver o README do modulo.
/// </remarks>
public interface IConsultaPublicaService
{
    Task<EnderecoConsultado> ConsultarCepAsync(string cep, CancellationToken ct);

    Task<EmpresaConsultada> ConsultarCnpjAsync(string cnpj, CancellationToken ct);
}
