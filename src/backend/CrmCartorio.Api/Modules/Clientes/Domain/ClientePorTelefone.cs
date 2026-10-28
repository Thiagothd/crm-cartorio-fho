using CrmCartorio.Api.Modules.Clientes.Domain.Entities;

namespace CrmCartorio.Api.Modules.Clientes.Domain;

/// <summary>
/// Acha o cadastro do contato que mandou (ou recebeu pelo celular) uma
/// mensagem do WhatsApp, pelas duas formas do celular brasileiro.
/// </summary>
/// <remarks>
/// Quem usa: a recepção (<c>Atendimento.Application.RecebimentoDeWhatsApp</c>,
/// tanto a mensagem do cliente quanto a resposta dada pelo celular) e a foto de
/// perfil do webhook (<c>CanalWhatsApp.Application.ProcessadorDeWebhook</c>).
/// Os dois precisam achar o MESMO cliente — por isso a consulta mora num lugar
/// só, no <c>Domain</c> de Clientes (que os dois módulos já leem), em vez de
/// copiada em cada um.
///
/// <b>Por que existe:</b> ver <see cref="TelefoneDeContato.FormasDoMesmoNumero"/>
/// — comparar por igualdade exata criou cliente e atendimento duplicados quando o
/// WhatsApp entregou o celular sem o nono dígito.
///
/// <b>Preferência</b>, quando as duas formas já existem no cadastro (duplicata
/// anterior à correção, ainda não unificada por
/// <c>sql/producao-unificar-clientes-nono-digito.sql</c>):
/// <list type="number">
///   <item>(na recepção, fora desta consulta) o que já tem atendimento WhatsApp
///   ativo — ver <c>RecebimentoDeWhatsApp.ResolverClienteAsync</c>; aqui não,
///   porque Clientes não conhece Conversa;</item>
///   <item>o cliente ativo;</item>
///   <item>entre iguais, o da forma canônica (13 dígitos, com o nono) — é o que o
///   atendente cadastrou e o que o script de unificação mantém;</item>
///   <item>depois, a forma exata que chegou; por fim, o mais antigo.</item>
/// </list>
/// O <c>IN</c> recebe a lista montada em C#, então a consulta traduz para SQL
/// inteira — nada é avaliado no cliente (conferido em
/// <c>verificar-traducao.cs</c>). O número gravado nunca é alterado aqui.
/// </remarks>
public static class ClientePorTelefone
{
    public static IQueryable<Cliente> DoNumeroRecebido(
        this IQueryable<Cliente> clientes,
        Guid tenantId,
        string telefoneE164)
    {
        var formas = TelefoneDeContato.FormasDoMesmoNumero(telefoneE164).ToArray();
        var canonica = formas.FirstOrDefault(f => f.Length == 13) ?? telefoneE164;

        return clientes
            .Where(c => c.TenantId == tenantId && formas.Contains(c.TelefoneWhatsapp))
            .OrderByDescending(c => c.Ativo)
            .ThenByDescending(c => c.TelefoneWhatsapp == canonica)
            .ThenByDescending(c => c.TelefoneWhatsapp == telefoneE164)
            .ThenBy(c => c.CriadoEm);
    }
}
