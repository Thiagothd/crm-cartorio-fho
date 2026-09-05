namespace CrmCartorio.Api.Infrastructure.Identity;

public interface IGeradorDeTokenDaCentralDeIa
{
    (string Token, DateTime ExpiraEm) Gerar();
}
