using CrmCartorio.Api.Modules.Consultas.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrmCartorio.Api.Controllers;

/// <summary>
/// Preenchimento assistido de cadastro a partir de fonte publica.
/// </summary>
/// <remarks>
/// Exige autenticacao mesmo sem tocar em dado do cartorio: sem isso o servidor da
/// serventia viraria proxy aberto de consulta para quem achasse a URL.
///
/// Nao ha, e nao havera, rota de CPF. Ver o README do modulo Consultas.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/consultas")]
public class ConsultasController(IConsultaPublicaService servico) : ControllerBase
{
    [HttpGet("cep/{cep}")]
    public async Task<ActionResult<EnderecoConsultado>> Cep(string cep, CancellationToken ct) =>
        Ok(await servico.ConsultarCepAsync(cep, ct));

    [HttpGet("cnpj/{cnpj}")]
    public async Task<ActionResult<EmpresaConsultada>> Cnpj(string cnpj, CancellationToken ct) =>
        Ok(await servico.ConsultarCnpjAsync(cnpj, ct));
}
