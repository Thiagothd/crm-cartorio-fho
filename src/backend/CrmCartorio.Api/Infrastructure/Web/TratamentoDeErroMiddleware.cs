using CrmCartorio.Api.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Infrastructure.Web;

/// <summary>
/// Traduz excecao em ProblemDetails. Erro de negocio sai com codigo estavel;
/// qualquer outra coisa vira 500 generico, sem vazar stack trace para o cliente.
/// </summary>
public class TratamentoDeErroMiddleware(RequestDelegate proximo, ILogger<TratamentoDeErroMiddleware> log)
{
    public async Task InvokeAsync(HttpContext contexto)
    {
        try
        {
            await proximo(contexto);
        }
        catch (ExcecaoDeAplicacao ex)
        {
            log.LogInformation(
                "Erro de negocio {Codigo} em {Caminho}: {Mensagem}",
                ex.Codigo, contexto.Request.Path, ex.Message);

            await EscreverAsync(contexto, ex.StatusHttp, ex.Codigo, ex.Message, ex.Detalhes);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Alguem alterou a conversa entre a leitura e a gravacao. E conflito
            // esperado no fluxo de multiplos atendentes, nao falha do sistema.
            log.LogInformation(ex, "Conflito de concorrencia em {Caminho}", contexto.Request.Path);

            await EscreverAsync(
                contexto,
                StatusCodes.Status409Conflict,
                "conflito_de_concorrencia",
                "O registro foi alterado por outro atendente. Recarregue e tente novamente.",
                null);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha nao tratada em {Caminho}", contexto.Request.Path);

            await EscreverAsync(
                contexto,
                StatusCodes.Status500InternalServerError,
                "erro_interno",
                "Erro interno. A ocorrencia foi registrada.",
                null);
        }
    }

    private static async Task EscreverAsync(
        HttpContext contexto,
        int status,
        string codigo,
        string mensagem,
        IDictionary<string, object?>? detalhes)
    {
        if (contexto.Response.HasStarted)
        {
            return;
        }

        var problema = new ProblemDetails
        {
            Status = status,
            Title = mensagem,
            Type = $"https://crm-cartorio/erros/{codigo}",
            Instance = contexto.Request.Path
        };

        problema.Extensions["codigo"] = codigo;

        if (detalhes is not null)
        {
            foreach (var (chave, valor) in detalhes)
            {
                problema.Extensions[chave] = valor;
            }
        }

        contexto.Response.StatusCode = status;
        contexto.Response.ContentType = "application/problem+json";

        await contexto.Response.WriteAsJsonAsync(problema);
    }
}
