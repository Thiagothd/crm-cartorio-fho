using System.Security.Claims;
using System.Text;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CrmCartorio.Api.Infrastructure.Identity;

public interface IGeradorDeToken
{
    (string Token, DateTime ExpiraEm) Gerar(Atendente atendente);
}

public class GeradorDeToken(IOptions<OpcoesDeJwt> opcoes) : IGeradorDeToken
{
    private readonly OpcoesDeJwt _opcoes = opcoes.Value;

    public (string Token, DateTime ExpiraEm) Gerar(Atendente atendente)
    {
        var expiraEm = DateTime.UtcNow.AddMinutes(_opcoes.ExpiracaoEmMinutos);

        var chave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opcoes.Chave));

        var descritor = new SecurityTokenDescriptor
        {
            Issuer = _opcoes.Emissor,
            Audience = _opcoes.Audiencia,
            Expires = expiraEm,
            SigningCredentials = new SigningCredentials(chave, SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(
            [
                new Claim(ClaimsDoAtendente.AtendenteId, atendente.Id.ToString()),
                new Claim(ClaimsDoAtendente.TenantId, atendente.TenantId.ToString()),
                new Claim(ClaimsDoAtendente.Nome, atendente.Nome),
                new Claim(ClaimsDoAtendente.Perfil, atendente.Perfil.ToString()),
                new Claim(ClaimsDoAtendente.TokenVersion, atendente.TokenVersion.ToString()),
                new Claim(ClaimTypes.NameIdentifier, atendente.Id.ToString()),
                new Claim(ClaimTypes.Name, atendente.Nome),
                new Claim(ClaimTypes.Email, atendente.Email),
                new Claim(ClaimTypes.Role, atendente.Perfil.ToString())
            ])
        };

        var token = new JsonWebTokenHandler().CreateToken(descritor);

        return (token, expiraEm);
    }
}
