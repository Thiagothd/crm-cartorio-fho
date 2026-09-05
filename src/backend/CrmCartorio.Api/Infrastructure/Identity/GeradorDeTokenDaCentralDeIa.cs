using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CrmCartorio.Api.Infrastructure.Identity;

/// <summary>
/// Emite o JWT da Central de IA — mesmo emissor/audiência/chave de assinatura do
/// resto do sistema (<see cref="OpcoesDeJwt"/>, a MESMA seção <c>Jwt</c> usada por
/// <see cref="GeradorDeToken"/>), mas um conjunto de claims deliberadamente
/// diferente: só <see cref="ClaimsDaCentralDeIa.Operador"/>, nunca
/// <c>atendente_id</c>/<c>tenant_id</c>/<c>perfil</c>/<c>token_version</c>. Ver
/// Modules/CentralDeIa/README.md para o porquê dessa fronteira ser estrutural, e
/// não uma checagem a mais que alguém poderia esquecer.
/// </summary>
public class GeradorDeTokenDaCentralDeIa(
    IOptions<OpcoesDeJwt> opcoesJwt,
    IOptions<OpcoesDaCentralDeIa> opcoesCentral) : IGeradorDeTokenDaCentralDeIa
{
    private readonly OpcoesDeJwt _opcoesJwt = opcoesJwt.Value;
    private readonly OpcoesDaCentralDeIa _opcoesCentral = opcoesCentral.Value;

    public (string Token, DateTime ExpiraEm) Gerar()
    {
        // Expiracao curta e configuravel: sem TokenVersion nem qualquer Atendente
        // por tras deste token, nao existe revogacao antes do prazo — a unica
        // mitigacao e o prazo em si ser curto. Ver README do modulo.
        var expiraEm = DateTime.UtcNow.AddMinutes(_opcoesCentral.ExpiracaoEmMinutos);

        var chave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opcoesJwt.Chave));

        var descritor = new SecurityTokenDescriptor
        {
            Issuer = _opcoesJwt.Emissor,
            Audience = _opcoesJwt.Audiencia,
            Expires = expiraEm,
            SigningCredentials = new SigningCredentials(chave, SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(
            [
                new Claim(ClaimsDaCentralDeIa.Operador, "true"),
            ])
        };

        var token = new JsonWebTokenHandler().CreateToken(descritor);

        return (token, expiraEm);
    }
}
