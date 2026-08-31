namespace CrmCartorio.Api.Modules.Usuarios.Domain.Enums;

/// <summary>
/// Funcao do funcionario dentro da serventia, na estrutura da Lei 8.935/94.
/// </summary>
/// <remarks>
/// NAO confundir com <see cref="PerfilDeAtendente"/>, que e permissao no sistema.
/// As duas coisas estavam fundidas numa coluna so e foram separadas porque nao sao
/// a mesma pergunta:
///
/// <list type="bullet">
///   <item><b>Cargo</b> responde "o que esta pessoa e no cartorio". E informacao
///   funcional, existe fora do software e nao concede PERMISSAO nenhuma (isto e,
///   nao controla o que a tela deixa clicar — isso e <see cref="PerfilDeAtendente"/>).</item>
///   <item><b>Perfil</b> responde "o que esta pessoa pode fazer no sistema". E
///   permissao, so existe aqui dentro.</item>
/// </list>
///
/// Fundidas, um escrevente nao podia receber permissao de supervisao sem virar
/// "Supervisor" na tela — cargo que nao existe em cartorio —, e o titular nao podia
/// ter acesso restrito sem parecer rebaixado. Sao eixos independentes: um escrevente
/// autorizado pode ser Administrador do painel, e o tabeliao pode ser so Atendente.
///
/// <b>Excecao deliberada — SEC-03:</b> <see cref="Titular"/> especificamente
/// TEM efeito de seguranca proprio, apesar de nao ser "permissao" no sentido
/// acima. Cruzar a fronteira `Cargo == Titular` (em qualquer direcao, com ou
/// sem o Perfil mudar) conta como evento de seguranca — revoga TokenVersion
/// (sessoes, tickets, desafios e cerimonias de MFA pendentes ficam invalidos)
/// e muda quem pode resetar/emitir ticket para esta conta
/// (`GarantirTitularProtegido`: so outro Administrador que TAMBEM seja
/// Titular). Ver <c>AtendenteService.AtualizarAsync</c> e
/// <c>MfaService.GarantirTitularProtegido</c>.
///
/// Enum, e nao tabela: os cargos vem da lei de delegacao, nao da preferencia do
/// cartorio. O que e configuravel por tenant e quem ocupa cada um.
/// </remarks>
public enum CargoNaServentia : byte
{
    /// <summary>Ainda nao informado. Valor neutro — nunca bloqueia cadastro.</summary>
    NaoInformado = 0,

    /// <summary>Titular da delegacao: tabeliao de notas e/ou oficial de registro civil.</summary>
    Titular = 1,

    /// <summary>Substituto — responde pela serventia no impedimento do titular.</summary>
    Substituto = 2,

    /// <summary>Escrevente autorizado a praticar atos por delegacao do titular.</summary>
    EscreventeAutorizado = 3,

    /// <summary>Escrevente sem autorizacao para praticar atos.</summary>
    Escrevente = 4,

    /// <summary>Auxiliar administrativo.</summary>
    Auxiliar = 5
}
