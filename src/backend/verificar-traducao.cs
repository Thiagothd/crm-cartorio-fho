#:project CrmCartorio.Api/CrmCartorio.Api.csproj
#:property PublishAot=false

using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Atendimento.Application;
using CrmCartorio.Api.Modules.Atendimento.Domain;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.CanalWhatsApp.Domain.Enums;
using CrmCartorio.Api.Modules.Clientes.Domain.Enums;
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
var setorId = Guid.Parse("0199b2c0-0001-7000-8000-000000000001");
var servicoId = Guid.Parse("0199b2c0-0004-7000-8000-000000000001");
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
        .Select(s => new TriagemAutomatica.OpcaoDeMenu(s.Id, s.Nome))
        .ToQueryString());

Checar("Disparador: servicos do menu de 2o nivel (por setor)", () =>
    db.TiposServico.AsNoTracking()
        .Where(t => t.TenantId == tenantId && t.SetorId == setorId && t.Ativo)
        .OrderBy(t => t.Nome)
        .Select(t => new TriagemAutomatica.OpcaoDeMenu(t.Id, t.Nome))
        .ToQueryString());

Checar("Disparador: dados do servico para {DocumentosNecessarios}/{PrazoServico}", () =>
    db.TiposServico.AsNoTracking()
        .Where(t => t.Id == servicoId)
        .Select(t => new
        {
            t.Nome,
            t.DocumentosNecessarios,
            PrazoHoras = t.Prazo != null && t.Prazo.Ativo ? (int?)t.Prazo.PrazoHoras : null,
            ApenasDiasUteis = t.Prazo != null && t.Prazo.Ativo && t.Prazo.ApenasDiasUteis
        })
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

// 13. Indicadores — Visao Geral, Relatorios e sino de alertas (Fase 3).
var de = agora.AddDays(-7);
var ate = agora;

Checar("Indicadores: 1a resposta humana (subconsulta correlacionada)", () =>
    db.Conversas.AsNoTracking()
        .Where(c => c.TenantId == tenantId && c.IniciadaEm >= de && c.IniciadaEm < ate)
        .Select(c => new
        {
            c.IniciadaEm,
            PrimeiraRespostaEm = db.Mensagens
                .Where(m => m.ConversaId == c.Id && m.AutorTipo == AutorTipo.Atendente)
                .OrderBy(m => m.EnviadaEm)
                .Select(m => (DateTime?)m.EnviadaEm)
                .FirstOrDefault()
        })
        .Where(x => x.PrimeiraRespostaEm != null)
        .ToQueryString());

// Seis subconsultas correlacionadas na MESMA projecao — cliente lido tres vezes
// (nome, telefone, foto), etapa duas (nome, cor) e o atendente uma. E' a peca
// que faz a Visao Geral falar a mesma lingua visual da inbox, e o custo de errar
// aqui e' a Visao Geral inteira falhar em execucao.
Checar("Indicadores: itens de 'requer atencao' (6 subconsultas na projecao)", () =>
    db.Conversas.AsNoTracking()
        .Where(c => c.TenantId == tenantId
                 && c.Status == StatusConversa.Ativa
                 && c.PrazoLimite != null && agora > c.PrazoLimite)
        .OrderBy(c => c.PrazoLimite)
        .Take(6)
        .Select(c => new
        {
            c.Id,
            c.Protocolo,
            ClienteNome = db.Clientes.Where(cl => cl.Id == c.ClienteId).Select(cl => cl.Nome).FirstOrDefault(),
            ClienteTelefone = db.Clientes.Where(cl => cl.Id == c.ClienteId).Select(cl => cl.TelefoneWhatsapp).FirstOrDefault(),
            ClienteFotoPerfilUrl = db.Clientes.Where(cl => cl.Id == c.ClienteId).Select(cl => cl.FotoPerfilUrl).FirstOrDefault(),
            EtapaNome = db.EtapasFunil.Where(e => e.Id == c.EtapaId).Select(e => e.Nome).FirstOrDefault(),
            EtapaCor = db.EtapasFunil.Where(e => e.Id == c.EtapaId).Select(e => e.Cor).FirstOrDefault(),
            AtendenteNome = db.Atendentes.Where(a => a.Id == c.AtendenteId).Select(a => a.Nome).FirstOrDefault(),
            c.PrazoLimite,
            c.PrazoAlertaEm,
            c.UltimaMensagemEm
        })
        .ToQueryString());

Checar("Indicadores: servicos mais procurados (GroupBy)", () =>
    db.Conversas.AsNoTracking()
        .Where(c => c.TenantId == tenantId && c.IniciadaEm >= de && c.IniciadaEm < ate && c.TipoServicoId != null)
        .GroupBy(c => c.TipoServicoId)
        .Select(g => new { TipoServicoId = g.Key!.Value, Total = g.Count() })
        .OrderByDescending(g => g.Total)
        .Take(8)
        .ToQueryString());

Checar("Indicadores: carga da equipe (GroupBy com Count(predicado))", () =>
    db.Conversas.AsNoTracking()
        .Where(c => c.TenantId == tenantId && c.Status == StatusConversa.Ativa && c.AtendenteId != null)
        .GroupBy(c => c.AtendenteId)
        .Select(g => new
        {
            AtendenteId = g.Key!.Value,
            Ativas = g.Count(),
            EmAlerta = g.Count(c =>
                c.PrazoLimite != null && agora <= c.PrazoLimite && c.PrazoAlertaEm != null && agora >= c.PrazoAlertaEm),
            Atrasadas = g.Count(c => c.PrazoLimite != null && agora > c.PrazoLimite)
        })
        .ToQueryString());

Checar("Indicadores: encerramentos por atendente x pelo cliente", () =>
    db.Conversas.AsNoTracking()
        .Where(c => c.TenantId == tenantId
                 && c.Status == StatusConversa.Encerrada
                 && c.EncerradaEm != null && c.EncerradaEm >= de && c.EncerradaEm < ate)
        .Select(c => c.EncerradaPorAtendenteId)
        .ToQueryString());

Checar("Indicadores: absorcao da automacao (contagens por tipo de mensagem)", () =>
    db.Mensagens.AsNoTracking()
        .Where(m => m.TenantId == tenantId && m.EnviadaEm >= de && m.EnviadaEm < ate)
        .Select(m => new { m.AutorTipo, m.EhAutomatica, m.GeradaPorIa })
        .ToQueryString());

Checar("Indicadores: horarios de maior demanda (timestamps brutos p/ bucket local)", () =>
    db.Conversas.AsNoTracking()
        .Where(c => c.TenantId == tenantId && c.IniciadaEm >= de && c.IniciadaEm < ate)
        .Select(c => c.IniciadaEm)
        .ToQueryString());

// `GroupBy(_ => 1)` e' o ponto delicado: agrupamento por constante nao tem
// coluna de chave, e o EF ja precisou de versao especifica para traduzir isso em
// vez de trazer tudo para a memoria. As quatro contagens do bloco AGORA saem
// daqui, entao se este padrao deixar de traduzir a Visao Geral inteira cai.
Checar("Indicadores: bloco AGORA (GroupBy constante, 4 Count com predicado)", () =>
{
    Guid? escopo = null;

    return db.Conversas.AsNoTracking()
        .Where(c => c.TenantId == tenantId && c.Status == StatusConversa.Ativa)
        .GroupBy(_ => 1)
        .Select(g => new
        {
            Ativas = g.Count(c => escopo == null || c.AtendenteId == escopo),
            NaFila = g.Count(c => c.AtendenteId == null),
            EmAlerta = g.Count(c =>
                (escopo == null || c.AtendenteId == escopo)
                && c.PrazoLimite != null
                && agora <= c.PrazoLimite
                && c.PrazoAlertaEm != null
                && agora >= c.PrazoAlertaEm),
            Atrasadas = g.Count(c =>
                (escopo == null || c.AtendenteId == escopo)
                && c.PrazoLimite != null
                && agora > c.PrazoLimite)
        })
        .ToQueryString();
});

// Ponto de partida da curva de "em aberto": conversas que comecaram antes da
// janela e ainda nao tinham encerrado quando ela comecou. O `EncerradaEm == null
// || EncerradaEm >= de` e' o pedaco delicado — comparacao de nullable com OR
// costuma virar SQL estranho.
Checar("Indicadores: em aberto antes do periodo (nullable com OR)", () =>
{
    Guid? escopo = null;

    var query = db.Conversas.AsNoTracking()
        .Where(c => c.TenantId == tenantId && c.IniciadaEm < de && (c.EncerradaEm == null || c.EncerradaEm >= de));

    if (escopo is { } atendente)
    {
        query = query.Where(c => c.AtendenteId == atendente);
    }

    return query.ToQueryString();
});

// Distribuicao global e "minha" no mesmo agrupamento — o que evita ler a tabela
// de conversas e a de etapas duas vezes por carga da Visao Geral.
Checar("Indicadores: distribuicao por etapa, global e minha no mesmo GroupBy", () =>
{
    var eu = Guid.Empty;

    return db.Conversas.AsNoTracking()
        .Where(c => c.TenantId == tenantId && c.Status == StatusConversa.Ativa)
        .GroupBy(c => c.EtapaId)
        .Select(g => new
        {
            EtapaId = g.Key,
            Total = g.Count(),
            Meu = g.Count(c => c.AtendenteId == eu)
        })
        .ToQueryString();
});

// As aberturas do periodo saem numa leitura so, com a primeira resposta humana
// viajando junto como subconsulta correlacionada — e' esta projecao que alimenta
// total de abertos, tempo de resposta, grafico de fluxo e ranking de servicos.
Checar("Indicadores: aberturas do periodo com 1a resposta (projecao unica)", () =>
    db.Conversas.AsNoTracking()
        .Where(c => c.TenantId == tenantId && c.IniciadaEm >= de && c.IniciadaEm < ate)
        .Select(c => new
        {
            c.IniciadaEm,
            c.TipoServicoId,
            PrimeiraRespostaEm = db.Mensagens
                .Where(m => m.ConversaId == c.Id && m.AutorTipo == AutorTipo.Atendente)
                .OrderBy(m => m.EnviadaEm)
                .Select(m => (DateTime?)m.EnviadaEm)
                .FirstOrDefault()
        })
        .ToQueryString());

// ---------------------------------------------------------------------------
// 14. Indicadores — as quatro abas de Relatorios
// ---------------------------------------------------------------------------

// Ranking de clientes por volume: GroupBy com Max() na mesma projecao. O Max
// sobre coluna de data dentro de agrupamento e' o pedaco que costuma nao
// traduzir quando combinado com Count().
Checar("Relatorios: clientes mais atendidos (GroupBy com Count e Max)", () =>
    db.Conversas.AsNoTracking()
        .Where(c => c.TenantId == tenantId && c.IniciadaEm >= de && c.IniciadaEm < ate)
        .GroupBy(c => c.ClienteId)
        .Select(g => new
        {
            ClienteId = g.Key,
            Total = g.Count(),
            UltimoEm = g.Max(c => c.IniciadaEm)
        })
        .ToQueryString());

// Encerramentos por QUEM encerrou, no periodo. `EncerradaPorAtendenteId` nulo e'
// encerramento pelo proprio cliente e fica fora do agrupamento.
Checar("Relatorios: encerramentos por atendente no periodo", () =>
    db.Conversas.AsNoTracking()
        .Where(c => c.TenantId == tenantId
                 && c.EncerradaEm != null
                 && c.EncerradaEm >= de
                 && c.EncerradaEm < ate
                 && c.EncerradaPorAtendenteId != null)
        .GroupBy(c => c.EncerradaPorAtendenteId)
        .Select(g => new { AtendenteId = g.Key!.Value, Total = g.Count() })
        .ToQueryString());

// Primeira resposta COM O AUTOR: a subconsulta projeta um objeto (dois campos),
// nao um escalar. E' mais fragil de traduzir que a versao escalar usada na
// Visao Geral, e e' o que alimenta a mediana por atendente.
Checar("Relatorios: primeira resposta com autor (subconsulta projetando objeto)", () =>
    db.Conversas.AsNoTracking()
        .Where(c => c.TenantId == tenantId && c.IniciadaEm >= de && c.IniciadaEm < ate)
        .Select(c => new
        {
            c.IniciadaEm,
            Primeira = db.Mensagens
                .Where(m => m.ConversaId == c.Id && m.AutorTipo == AutorTipo.Atendente)
                .OrderBy(m => m.EnviadaEm)
                .Select(m => new { m.EnviadaEm, m.AtendenteId })
                .FirstOrDefault()
        })
        .ToQueryString());

// Cinco contagens de cliente numa varredura, com o mesmo `GroupBy(_ => 1)` do
// bloco AGORA. Inclui comparacao de string vazia, que o EF traduz de formas
// diferentes conforme o provedor.
Checar("Relatorios: contagens de clientes (GroupBy constante)", () =>
    db.Clientes.AsNoTracking()
        .Where(cl => cl.TenantId == tenantId)
        .GroupBy(_ => 1)
        .Select(g => new
        {
            Total = g.Count(),
            Novos = g.Count(cl => cl.CriadoEm >= de && cl.CriadoEm < ate),
            Fisica = g.Count(cl => cl.Tipo == TipoDePessoa.Fisica),
            Juridica = g.Count(cl => cl.Tipo == TipoDePessoa.Juridica),
            SemDocumento = g.Count(cl => cl.Documento == null || cl.Documento == "")
        })
        .ToQueryString());

Checar("Relatorios: clientes distintos com atendimento ativo", () =>
    db.Conversas.AsNoTracking()
        .Where(c => c.TenantId == tenantId && c.Status == StatusConversa.Ativa)
        .Select(c => c.ClienteId)
        .Distinct()
        .ToQueryString());

// 14. Canal WhatsApp — regras de numero, historico de conexao e descartes
// (ajuste "integracao-whatsapp": trocar numero, liberar/bloquear numero).
var numeroDeTeste = "5538991234567";

Checar("Canal WhatsApp: numero liberado (enum + AsNoTracking)", () =>
    db.CanalWhatsappNumeros.AsNoTracking()
        .Where(n => n.TenantId == tenantId && n.Telefone == numeroDeTeste && n.Tipo == TipoDeNumero.Liberado)
        .ToQueryString());

Checar("Canal WhatsApp: numero bloqueado (enum + AsNoTracking)", () =>
    db.CanalWhatsappNumeros.AsNoTracking()
        .Where(n => n.TenantId == tenantId && n.Telefone == numeroDeTeste && n.Tipo == TipoDeNumero.Bloqueado)
        .ToQueryString());

Checar("Canal WhatsApp: numero duplicado ao adicionar (AnyAsync)", () =>
    db.CanalWhatsappNumeros.AsNoTracking()
        .Where(n => n.TenantId == tenantId && n.Telefone == numeroDeTeste)
        .ToQueryString());

Checar("Canal WhatsApp: modo de recebimento do tenant (enum nulavel)", () =>
    db.Tenants.AsNoTracking()
        .Where(t => t.Id == tenantId)
        .Select(t => t.WhatsappModoDeRecebimento)
        .ToQueryString());

Checar("Canal WhatsApp: regras — numeros com nome de quem adicionou (subconsulta correlacionada)", () =>
    db.CanalWhatsappNumeros.AsNoTracking()
        .Where(n => n.TenantId == tenantId)
        .OrderByDescending(n => n.CriadoEm)
        .Select(n => new
        {
            n.Id,
            n.Telefone,
            n.Rotulo,
            n.Tipo,
            n.CriadoEm,
            CriadoPorNome = db.Atendentes
                .Where(a => a.Id == n.CriadoPorAtendenteId)
                .Select(a => (string?)a.Nome)
                .FirstOrDefault()
        })
        .ToQueryString());

Checar("Canal WhatsApp: poda de descartes antigos (comparacao datetime2, base do RemoveRange)", () =>
    db.CanalWhatsappDescartes
        .Where(d => d.TenantId == tenantId && d.CriadoEm < agora.AddDays(-7))
        .ToQueryString());

Checar("Canal WhatsApp: descartes recentes (contagem + Take)", () =>
    db.CanalWhatsappDescartes.AsNoTracking()
        .Where(d => d.TenantId == tenantId)
        .OrderByDescending(d => d.CriadoEm)
        .Take(10)
        .Select(d => new { d.Telefone, d.Motivo, d.CriadoEm })
        .ToQueryString());

Checar("Canal WhatsApp: sessao aberta em curso (DesconectadoEm nulo)", () =>
    db.CanalWhatsappConexoes
        .Where(c => c.TenantId == tenantId && c.DesconectadoEm == null)
        .OrderByDescending(c => c.ConectadoEm)
        .ToQueryString());

Checar("Canal WhatsApp: historico de conexoes com nome do atendente (subconsulta correlacionada)", () =>
    db.CanalWhatsappConexoes.AsNoTracking()
        .Where(c => c.TenantId == tenantId)
        .OrderByDescending(c => c.ConectadoEm)
        .Take(20)
        .Select(c => new
        {
            c.Numero,
            c.PerfilNome,
            c.ConectadoEm,
            c.DesconectadoEm,
            AtendenteNome = db.Atendentes
                .Where(a => a.Id == c.AtendenteId)
                .Select(a => (string?)a.Nome)
                .FirstOrDefault(),
            c.Motivo
        })
        .ToQueryString());

Checar("Canal WhatsApp: envio — telefone e tenant da conversa (mesma forma de EnviadorPeloWhatsApp)", () =>
    db.Mensagens.AsNoTracking()
        .Where(m => m.Id == Guid.Empty)
        .Select(m => new
        {
            m.Id,
            m.Conteudo,
            m.StatusEntrega,
            Canal = db.Conversas.Where(c => c.Id == m.ConversaId).Select(c => c.Canal).FirstOrDefault(),
            TenantId = db.Conversas.Where(c => c.Id == m.ConversaId).Select(c => c.TenantId).FirstOrDefault(),
            Telefone = db.Conversas
                .Where(c => c.Id == m.ConversaId)
                .Select(c => db.Clientes
                    .Where(cl => cl.Id == c.ClienteId)
                    .Select(cl => cl.TelefoneWhatsapp)
                    .FirstOrDefault())
                .FirstOrDefault()
        })
        .ToQueryString());

// --- Varredura de inatividade (024) ----------------------------------------

Checar("Inatividade: tenants com o recurso configurado (projecao de nulavel com .Value)", () =>
    db.Tenants.AsNoTracking()
        .Where(t => t.Ativo
                 && t.MinutosParaPerguntarEncerramento != null
                 && t.MinutosParaEncerrarSemResposta != null)
        .Select(t => new
        {
            t.Id,
            Perguntar = t.MinutosParaPerguntarEncerramento!.Value,
            Encerrar = t.MinutosParaEncerrarSemResposta!.Value,
            t.EncerramentoHoraInicial,
            t.EncerramentoHoraFinal
        })
        .ToQueryString());

Checar("Inatividade: fase 1 — candidatas a receber a pergunta (5 guardas + enum de canal)", () =>
    db.Conversas.AsNoTracking()
        .Where(c => c.TenantId == tenantId
                 && c.Status == StatusConversa.Ativa
                 && c.PerguntaDeEncerramentoEnviadaEm == null
                 && c.UltimaMensagemEm < agora.AddMinutes(-20)
                 && c.Canal == CanalOrigem.WhatsApp
                 && c.MensagensNaoLidas == 0
                 && (c.PrazoLimite == null || c.PrazoLimite < agora))
        .OrderBy(c => c.UltimaMensagemEm)
        .Take(200)
        .ToQueryString());

Checar("Inatividade: fase 2 — candidatas a encerrar sem resposta", () =>
    db.Conversas.AsNoTracking()
        .Where(c => c.TenantId == tenantId
                 && c.Status == StatusConversa.Ativa
                 && c.PerguntaDeEncerramentoEnviadaEm != null
                 && c.PerguntaDeEncerramentoEnviadaEm < agora.AddMinutes(-30))
        .OrderBy(c => c.PerguntaDeEncerramentoEnviadaEm)
        .Take(200)
        .ToQueryString());

Checar("Inatividade: tenant tem texto ativo para a pergunta (AnyAsync por gatilho novo)", () =>
    db.MensagensAutomaticas.AsNoTracking()
        .Where(m => m.TenantId == tenantId
                 && m.Ativo
                 && m.Gatilho == GatilhoDeMensagem.ConfirmacaoDeEncerramento)
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
