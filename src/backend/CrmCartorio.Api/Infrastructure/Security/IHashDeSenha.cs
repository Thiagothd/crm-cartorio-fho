namespace CrmCartorio.Api.Infrastructure.Security;

public interface IHashDeSenha
{
    string Gerar(string senha);

    bool Verificar(string senha, string hashArmazenado);
}
