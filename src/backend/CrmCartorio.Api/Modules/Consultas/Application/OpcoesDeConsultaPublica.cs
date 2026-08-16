namespace CrmCartorio.Api.Modules.Consultas.Application;

public class OpcoesDeConsultaPublica
{
    public const string Secao = "ConsultaPublica";

    /// <summary>
    /// Liga a consulta a fonte publica. Desligada, os endpoints devolvem 503 sem
    /// tocar a rede e a tela cai na digitacao manual.
    /// </summary>
    /// <remarks>
    /// Existe porque o produto e self-hosted: ha instalacao de cartorio atras de
    /// rede fechada, sem saida para internet. Nesse caso a consulta e ruido — cada
    /// CEP digitado viraria uma espera de timeout — e o administrador desliga.
    /// </remarks>
    public bool Habilitada { get; set; } = true;

    /// <summary>
    /// Teto de espera por provedor, em segundos.
    /// </summary>
    /// <remarks>
    /// Seis segundos e o limite do que o atendente aguenta esperar de pe no balcao.
    /// Estourou, o CEP cai no ViaCEP; estourou de novo, a tela avisa e o campo
    /// continua editavel. Nunca bloqueia o cadastro.
    /// </remarks>
    public int TimeoutSegundos { get; set; } = 6;

    /// <summary>Base da BrasilAPI. Atende CEP e CNPJ.</summary>
    public string BaseBrasilApi { get; set; } = "https://brasilapi.com.br/api";

    /// <summary>Base do ViaCEP. Reserva, so para CEP — o ViaCEP nao faz CNPJ.</summary>
    public string BaseViaCep { get; set; } = "https://viacep.com.br/ws";
}
