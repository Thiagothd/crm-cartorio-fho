#:project CrmCartorio.Api/CrmCartorio.Api.csproj
#:property PublishAot=false

using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Atendimento.Application;
using CrmCartorio.Api.Modules.Atendimento.Domain;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Enums;
using Microsoft.EntityFrameworkCore;

// VERIFICADOR DE TRADUCAO LINQ -> SQL
//
// Rode com:  dotnet run verificar-traducao.cs
//
// Por que existe: a regra 1 do CLAUDE.md proibe executar DDL/DML, entao nao ha
// banco de desenvolvimento contra o qual exercitar as consultas. Uma consulta que
// nao traduz compila normalmente e so estoura em tempo de execucao — na frente do
// cliente, se ninguem verificar antes.
//
// ToQueryString() gera o SQL sem abrir conexao: exercita o tradutor do provider
// e nada mais. Nao valida se a consulta devolve o resultado certo — so que ela
// consegue virar SQL. Ao mexer em consulta, rode isto.
//
// A connection string abaixo e proposital: aponta para um servidor inexistente,
// deixando explicito que nada aqui toca banco.

var construtor = new DbContextOptionsBuilder<CrmDbContext>();
ConfiguracaoDoBanco.Configurar(construtor, "Server=nao-conecta;Database=x;Trusted_Connection=True;");
var opcoes = construtor.Options;

using var db = new CrmDbContext(opcoes);

var tenantId = Guid.Parse("0199b2c0-0000-7000-8000-000000000001");
var atendenteId = Guid.NewGuid();
var etapaAlvoId = Guid.Parse("0199b2c0-0002-7000-8000-000000000004");
var agora = DateTime.UtcNow;
var busca = "maria";

// Lista em memoria, como o varredor monta antes de filtrar as conversas.
var tenantsComRegra = new List<Guid> { tenantId };

var falhas = 0;

void Checar(string nome, Func<string> gerar)
{
    try
    {
        var sql = gerar();
        Console.WriteLine($"OK   {nome} ({sql.Split('\n').Length} linhas de SQL)");
    }
    catch (Exception ex)
    {
        falhas++;
        Console.WriteLine($"FALHA {nome}");
        Console.WriteLine($"      {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
    }
}

// 1. Projecao da conversa — codigo de producao, usado na inbox e no detalhe.
Checar("ProjecaoDeConversa.De (lista da inbox)", () =>
    ProjecaoDeConversa
        .De(db, db.Conversas.AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.UltimaMensagemEm))
        .Skip(0).Take(30)
        .ToQueryString());

Checar("ProjecaoDeConversa.De (detalhe)", () =>
    ProjecaoDeConversa
        .De(db, db.Conversas.AsNoTracking().Where(c => c.Id == Guid.Empty && c.TenantId == tenantId))
        .ToQueryString());

// 2. Filtro de situacao de prazo — comparacao de datas, sem subconsulta de percentual.
Checar("Filtro: Atrasada", () =>
    db.Conversas.AsNoTracking()
        .Where(c => c.TenantId == tenantId && c.PrazoLimite != null && agora > c.PrazoLimite)
        .ToQueryString());

Checar("Filtro: EmAlerta", () =>
    db.Conversas.AsNoTracking()
        .Where(c => c.TenantId == tenantId
                 && c.PrazoLimite != null
                 && agora <= c.PrazoLimite
                 && c.PrazoAlertaEm != null
                 && agora >= c.PrazoAlertaEm)
        .ToQueryString());

Checar("Filtro: EmDia", () =>
    db.Conversas.AsNoTracking()
        .Where(c => c.TenantId == tenantId
                 && c.PrazoLimite != null
                 && agora <= c.PrazoLimite
                 && (c.PrazoAlertaEm == null || agora < c.PrazoAlertaEm))
        .ToQueryString());

// 3. Busca textual da inbox — Any() cruzando com Clientes.
Checar("Filtro: busca textual", () =>
    db.Conversas.AsNoTracking()
        .Where(c => c.TenantId == tenantId &&
                    (c.Protocolo.Contains(busca) ||
                     db.Clientes.Any(cl => cl.Id == c.ClienteId &&
                        (cl.Nome.Contains(busca) || cl.TelefoneWhatsapp.Contains(busca)))))
        .ToQueryString());

// 4. Fio de mensagens pela projecao REAL: nome do atendente e citacao.
Checar("Mensagens da conversa", () =>
    ProjecaoDeMensagem.De(
        db,
        db.Mensagens.AsNoTracking()
            .Where(m => m.TenantId == tenantId)
            .OrderBy(m => m.EnviadaEm).ThenBy(m => m.Id),
        "Cliente de teste")
        .ToQueryString());

// 5. Linha do tempo — seis subconsultas de nome no mesmo Select.
Checar("Historico de etapas", () =>
    db.HistoricoEtapas.AsNoTracking()
        .Where(h => h.TenantId == tenantId)
        .OrderBy(h => h.OcorridoEm)
        .Select(h => new HistoricoEtapaResponse(
            h.Id, h.TipoMudanca,
            db.EtapasFunil.Where(e => e.Id == h.EtapaAnteriorId).Select(e => e.Nome).FirstOrDefault(),
            db.EtapasFunil.Where(e => e.Id == h.EtapaNovaId).Select(e => e.Nome).FirstOrDefault(),
            db.Setores.Where(s => s.Id == h.SetorAnteriorId).Select(s => s.Nome).FirstOrDefault(),
            db.Setores.Where(s => s.Id == h.SetorNovoId).Select(s => s.Nome).FirstOrDefault(),
            db.Atendentes.Where(a => a.Id == h.AtendenteId).Select(a => a.Nome).FirstOrDefault(),
            h.OcorridoEm, h.Observacao))
        .ToQueryString());

// 6. Contadores do painel — GroupBy com chave anulavel.
Checar("Resumo: agrupar por etapa", () =>
    db.Conversas.AsNoTracking()
        .Where(c => c.TenantId == tenantId && c.Status == StatusConversa.Ativa)
        .GroupBy(c => c.EtapaId)
        .Select(g => new { EtapaId = g.Key, Total = g.Count() })
        .ToQueryString());

Checar("Resumo: agrupar por setor", () =>
    db.Conversas.AsNoTracking()
        .Where(c => c.TenantId == tenantId && c.Status == StatusConversa.Ativa)
        .GroupBy(c => c.SetorId)
        .Select(g => new { SetorId = g.Key, Total = g.Count() })
        .ToQueryString());

// 7. Lista de clientes com contagem de conversas.
Checar("Clientes com contadores", () =>
    db.Clientes.AsNoTracking()
        .Where(c => c.TenantId == tenantId && c.Ativo)
        .Where(c => c.Nome.Contains(busca) ||
                    (busca.Length > 0 && c.TelefoneWhatsapp.Contains(busca)) ||
                    (busca.Length > 0 && c.Documento != null && c.Documento.Contains(busca)))
        .OrderBy(c => c.Nome)
        .Select(c => new
        {
            c.Id,
            Total = db.Conversas.Count(x => x.ClienteId == c.Id),
            Aberta = db.Conversas.Any(x => x.ClienteId == c.Id && x.Status == StatusConversa.Ativa),
        })
        .ToQueryString());

// 8. Cadastros com contagem de conversas ativas.
Checar("Setores com contadores", () =>
    db.Setores.AsNoTracking()
        .Where(s => s.TenantId == tenantId)
        .OrderBy(s => s.Ordem).ThenBy(s => s.Nome)
        .Select(s => new
        {
            s.Id,
            Ativas = db.Conversas.Count(c => c.SetorId == s.Id && c.Status == StatusConversa.Ativa),
        })
        .ToQueryString());

Checar("Etapas com contadores", () =>
    db.EtapasFunil.AsNoTracking()
        .Where(e => e.TenantId == tenantId)
        .OrderBy(e => e.Ordem)
        .Select(e => new
        {
            e.Id,
            Ativas = db.Conversas.Count(c => c.EtapaId == e.Id && c.Status == StatusConversa.Ativa),
        })
        .ToQueryString());

Checar("Servicos com prazo (Include 1:1)", () =>
    db.TiposServico.AsNoTracking()
        .Include(t => t.Prazo)
        .Where(t => t.TenantId == tenantId)
        .OrderBy(t => t.Categoria).ThenBy(t => t.Nome)
        .ToQueryString());

Checar("Servicos: agrupar conversas ativas", () =>
    db.Conversas.AsNoTracking()
        .Where(c => c.TenantId == tenantId && c.Status == StatusConversa.Ativa && c.TipoServicoId != null)
        .GroupBy(c => c.TipoServicoId!.Value)
        .Select(g => new { TipoServicoId = g.Key, Total = g.Count() })
        .ToQueryString());

// 9. Atendentes com o nome do setor padrao.
Checar("Atendentes com setor padrao", () =>
    db.Atendentes.AsNoTracking()
        .Where(a => a.TenantId == tenantId)
        .OrderBy(a => a.Nome)
        .Select(a => new
        {
            a.Id,
            Setor = db.Setores.Where(s => s.Id == a.SetorPadraoId).Select(s => s.Nome).FirstOrDefault(),
        })
        .ToQueryString());

// 10. Cadastro de mensagem automatica, com o nome da etapa alvo por subconsulta.
Checar("Mensagens automaticas com etapa alvo", () =>
    db.MensagensAutomaticas.AsNoTracking()
        .Where(m => m.TenantId == tenantId)
        .OrderBy(m => m.Gatilho).ThenBy(m => m.CriadoEm)
        .Select(m => new
        {
            m.Id,
            m.Gatilho,
            m.EtapaAlvoId,
            EtapaAlvoNome = db.EtapasFunil
                .Where(e => e.Id == m.EtapaAlvoId)
                .Select(e => e.Nome)
                .FirstOrDefault(),
            m.Conteudo,
            m.Ativo,
        })
        .ToQueryString());

Checar("Respostas rapidas do tenant", () =>
    db.RespostasRapidas.AsNoTracking()
        .Where(r => r.TenantId == tenantId && r.Ativo)
        .OrderBy(r => r.Atalho)
        .ToQueryString());

// 11. Consultas do disparador — as que rodam a cada evento de atendimento.
Checar("Disparador: regras de gatilho de etapa", () =>
    db.MensagensAutomaticas.AsNoTracking()
        .Where(m => m.TenantId == tenantId
                 && m.Ativo
                 && m.Gatilho == GatilhoDeMensagem.EtapaAlterada
                 && m.EtapaAlvoId == etapaAlvoId)
        .OrderBy(m => m.CriadoEm)
        .Select(m => m.Conteudo)
        .ToQueryString());

Checar("Disparador: setores do menu de triagem", () =>
    db.Setores.AsNoTracking()
        .Where(s => s.TenantId == tenantId && s.Ativo)
        .OrderBy(s => s.Ordem).ThenBy(s => s.Nome)
        .Select(s => new TriagemAutomatica.OpcaoDeSetor(s.Id, s.Nome))
        .ToQueryString());

Checar("Disparador: nome fantasia com fallback para razao social", () =>
    db.Tenants.AsNoTracking()
        .Where(t => t.Id == tenantId)
        .Select(t => t.NomeFantasia ?? t.RazaoSocial)
        .ToQueryString());

// 12. Varredura de prazo. Contains() sobre lista em memoria e o ponto fragil aqui:
//     e o que o EF traduz para IN (...), e o que quebraria em silencio se mudasse.
Checar("Varredor: tenants com regra ativa", () =>
    db.MensagensAutomaticas.AsNoTracking()
        .Where(m => m.Ativo && m.Gatilho == GatilhoDeMensagem.PrazoVencido)
        .Select(m => m.TenantId)
        .Distinct()
        .ToQueryString());

Checar("Varredor: candidatas em alerta", () =>
    db.Conversas.AsNoTracking()
        .Where(c => c.Status == StatusConversa.Ativa
                 && c.AvisoDeAlertaEnviadoEm == null
                 && c.PrazoAlertaEm != null
                 && c.PrazoLimite != null
                 && agora >= c.PrazoAlertaEm
                 && agora <= c.PrazoLimite)
        .Where(c => tenantsComRegra.Contains(c.TenantId))
        .OrderBy(c => c.PrazoLimite)
        .Take(200)
        .ToQueryString());

Checar("Varredor: candidatas vencidas", () =>
    db.Conversas.AsNoTracking()
        .Where(c => c.Status == StatusConversa.Ativa
                 && c.AvisoDeVencimentoEnviadoEm == null
                 && c.PrazoLimite != null
                 && agora > c.PrazoLimite)
        .Where(c => tenantsComRegra.Contains(c.TenantId))
        .OrderBy(c => c.PrazoLimite)
        .Take(200)
        .ToQueryString());

// A reivindicacao do aviso e um UPDATE condicional, na mesma disciplina da trava:
// so afeta linha cujo marcador ainda esta nulo.
Checar("Varredor: reivindicacao do aviso (WHERE do UPDATE)", () =>
    db.Conversas
        .Where(c => c.Id == Guid.Empty && c.AvisoDeAlertaEnviadoEm == null)
        .Select(c => new { c.Id, c.AvisoDeAlertaEnviadoEm })
        .ToQueryString());

Console.WriteLine();
Console.WriteLine(falhas == 0
    ? "Todas as consultas traduziram para SQL."
    : $"{falhas} consulta(s) NAO traduziram.");

// Amostra do SQL da trava, para conferencia visual do WHERE condicional.
Console.WriteLine();
Console.WriteLine("--- SQL da trava (UPDATE condicional) ---");
Console.WriteLine(
    db.Conversas
        .Where(c => c.Id == Guid.Empty
                 && c.TenantId == tenantId
                 && c.AtendenteId == null
                 && c.Status == StatusConversa.Ativa)
        .Select(c => new { c.Id, c.AtendenteId, c.Status })
        .ToQueryString());

Console.WriteLine($"(atendente que assumiria: {atendenteId})");

return falhas == 0 ? 0 : 1;
