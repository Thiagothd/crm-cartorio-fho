namespace CrmCartorio.Api.Modules.Seguranca.Domain.Enums;

/// <summary>
/// O que um <c>RegistroDeAcesso</c> registra. Faixa 1-9: leitura de dado de
/// cliente pela tela. Faixa 10-19: ciclo de vida do acesso tecnico da
/// suporte tecnico. Faixa 20-29: eventos de protecao de dados (cifra).
/// </summary>
/// <remarks>
/// CHECK CONSTRAINT gravada em <c>sql/045-registros-de-acesso.sql</c> —
/// alargar esta faixa sempre exige uma migration nova, nunca editar a
/// existente (mesma disciplina de <c>TipoDeEventoDeSeguranca</c>).
/// </remarks>
public enum TipoDeAcesso : byte
{
    LeituraDeConversa = 1,
    DownloadDeAnexo = 2,
    LeituraDeFichaDeCliente = 3,

    AcessoTecnicoSolicitado = 10,
    AcessoTecnicoAprovado = 11,
    AcessoTecnicoNegado = 12,
    AcessoTecnicoRevogado = 13,

    ChaveDeDadosRotacionada = 20,
    CifraDeLegadoConcluida = 21
}
