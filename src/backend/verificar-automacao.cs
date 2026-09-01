#:project CrmCartorio.Api/CrmCartorio.Api.csproj
#:property PublishAot=false

using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Atendimento.Application;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.Clientes.Domain;
using CrmCartorio.Api.Modules.Clientes.Domain.Entities;
using CrmCartorio.Api.Modules.Configuracoes.Domain;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Entities;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Enums;
using CrmCartorio.Api.Modules.RespostaInteligente.Application;
using CrmCartorio.Api.Modules.RespostaInteligente.Application.Provedores;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// VERIFICADOR DE AUTOMACAO DE MENSAGENS
//
// Rode com:  dotnet run verificar-automacao.cs ["<connection string>"]
//
// Exercita o fluxo inteiro da automacao chamando os SERVICOS REAIS — nao uma copia
// da logica. Mesmo principio de verificar-concorrencia.cs: copia de codigo testa a
// copia, nao o sistema.
//
//   1. Boas-vindas e triagem no primeiro contato
//   2. Resposta que NAO corresponde a opcao nenhuma
//   3. Conversa aberta ja com setor nao recebe menu
//   4. Aviso por mudanca de etapa
//   5. Varredura de prazo, incluindo a garantia de nao avisar duas vezes
//   6. Transferencia entre atendentes (RF-013)
//   9. Pergunta de encerramento por inatividade sai uma vez so
//  10. Leitura da resposta: "sim" encerra, "nao" continua, "assim" NAO encerra
//  11. Sem resposta, a conversa encerra sozinha
//  12. O REQUISITO: quem volta depois de encerrada cai em atendimento novo
//  13. As guardas (mensagem nao lida, canal simulado, sem config, fora da janela) —
//      e a confirmacao de que SLA vivo NAO bloqueia mais (decisao de 2026-08-23)
//  14. Reabrir limpa o carimbo — conversa reaberta nao fecha de novo
//  15. Sem texto cadastrado: nao pergunta, mas a 2a fase encerra assim mesmo
//  16. NomeDeContato: as duas leituras de nome, com vies oposto (sem banco)
//  17. Sem nome utilizavel, o vocativo some — "Ola!", nunca "Ola, !"
//  18. Contato sem nome recebe a PERGUNTA, e nao o menu
//  19. A resposta vira nome, e o menu sai JA com ele
//  20. Resposta que nao e nome nao vira nome — e ninguem fica sem menu
//  21. "menu" no lugar do nome nao deixa a flag presa
//  22. O CAMINHO FELIZ: perfil com nome nao ve diferenca nenhuma
//  23. Gatilho desativado nao deixa o contato mudo; e nao se repergunta
//  24. TelefoneDeContato e ParserDeListaDeContatos: importacao de contatos (sem banco)
//
// ATENCAO: este verificador ESCREVE no banco. Rode SOMENTE contra base de
// desenvolvimento. Ao final remove tudo que criou, inclusive sobras de execucoes
// anteriores.
//
// NAO altera a configuracao existente. Os gatilhos de prazo nascem desativados no
// seed (decisao do cartório parceiro, docs/REQUISITOS.md 5.7) e continuam assim: o teste cria
// regras proprias, marcadas, e as remove no final.

const string PrefixoDeTeste = "[TESTE-AUTO] ";

var conexao = args.Length > 0
    ? args[0]
    : "Server=.\\SQLEXPRESS;Database=CRM_Cartorio_Dev;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

var construtor = new DbContextOptionsBuilder<CrmDbContext>();
ConfiguracaoDoBanco.Configurar(construtor, conexao);
var opcoes = construtor.Options;

await using (var checagem = new CrmDbContext(opcoes))
{
    if (!string.Equals(
            checagem.Database.GetDbConnection().Database,
            "CRM_Cartorio_Dev",
            StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            "Recusado: o verificador so escreve em CRM_Cartorio_Dev.");
    }

    if (!await checagem.Database.CanConnectAsync())
    {
        Console.WriteLine("Nao foi possivel conectar. Verifique a connection string.");
        return 1;
    }

    // SqlQuery<T> exige que a coluna se chame "Value" — sem o alias, o SQL Server
    // devolve coluna sem nome e o EF nao consegue materializar.
    var existe = await checagem.Database
        .SqlQuery<int>($"SELECT CASE WHEN OBJECT_ID('dbo.MensagensAutomaticas') IS NULL THEN 0 ELSE 1 END AS Value")
        .FirstAsync();

    if (existe == 0)
    {
        Console.WriteLine("Tabela MensagensAutomaticas ausente. Rode sql/005-mensagens-automaticas.sql antes.");
        return 1;
    }
}

Guid tenantId;
Guid atendenteId;
string atendenteNome;
Guid outroAtendenteId;
string outroAtendenteNome;
Guid etapaFinalId;
string etapaFinalNome;
Guid etapaInicialId;
Guid servicoId;
string servicoNome;
List<(Guid Id, string Nome, int Ordem)> setores;

await using (var db = new CrmDbContext(opcoes))
{
    tenantId = await db.Tenants.Select(t => t.Id).FirstAsync();

    var equipe = await db.Atendentes
        .Where(a => a.TenantId == tenantId && a.Ativo)
        .OrderBy(a => a.Nome)
        .Select(a => new { a.Id, a.Nome })
        .Take(2)
        .ToListAsync();

    if (equipe.Count < 2)
    {
        Console.WriteLine("Sao necessarios ao menos 2 atendentes ativos. Rode o seed antes.");
        return 1;
    }

    (atendenteId, atendenteNome) = (equipe[0].Id, equipe[0].Nome);
    (outroAtendenteId, outroAtendenteNome) = (equipe[1].Id, equipe[1].Nome);

    setores = await db.Setores
        .Where(s => s.TenantId == tenantId && s.Ativo)
        .OrderBy(s => s.Ordem).ThenBy(s => s.Nome)
        .Select(s => new ValueTuple<Guid, string, int>(s.Id, s.Nome, s.Ordem))
        .ToListAsync();

    var final = await db.EtapasFunil
        .Where(e => e.TenantId == tenantId && e.Ativo && e.EhFinal)
        .Select(e => new { e.Id, e.Nome })
        .FirstOrDefaultAsync();

    if (final is null || setores.Count < 2)
    {
        Console.WriteLine("Configuracao insuficiente: sao necessarios 2 setores ativos e 1 etapa final.");
        return 1;
    }

    (etapaFinalId, etapaFinalNome) = (final.Id, final.Nome);

    etapaInicialId = await db.EtapasFunil
        .Where(e => e.TenantId == tenantId && e.Ativo && !e.EhFinal)
        .OrderBy(e => e.Ordem)
        .Select(e => e.Id)
        .FirstAsync();

    var servico = await db.TiposServico
        .Where(t => t.TenantId == tenantId && t.Ativo)
        .OrderBy(t => t.Nome)
        .Select(t => new { t.Id, t.Nome })
        .FirstAsync();

    (servicoId, servicoNome) = (servico.Id, servico.Nome);
}

Console.WriteLine($"Tenant ....... {tenantId}");
Console.WriteLine($"Atendentes ... {atendenteNome} / {outroAtendenteNome}");
Console.WriteLine($"Setores ...... {string.Join(", ", setores.Select((s, i) => $"{i + 1}={s.Nome}"))}");
Console.WriteLine($"Etapa final .. {etapaFinalNome}");

// Configuracao de encerramento automatico do tenant, como estava ANTES do teste.
// Os testes 9-15 precisam liga-la, e sair deixando o recurso ligado (ou com tempos
// de teste) mudaria o comportamento da base de desenvolvimento para todo mundo. E
// devolvida em LimparAsync, que roda na entrada e na saida.
(int? Perguntar, int? Encerrar, int? HoraInicial, int? HoraFinal) configuracaoOriginal;

await using (var db = new CrmDbContext(opcoes))
{
    var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenantId);

    configuracaoOriginal = (
        tenant.MinutosParaPerguntarEncerramento,
        tenant.MinutosParaEncerrarSemResposta,
        tenant.EncerramentoHoraInicial,
        tenant.EncerramentoHoraFinal);
}

// Limpeza de ENTRADA, alem da de saida. Uma execucao que aborta no meio deixa
// cliente e conversa para tras, e o CriarAsync recusa abrir um segundo atendimento
// para quem ja tem um aberto — sem isto, a primeira falha travaria todas as
// execucoes seguintes num erro que nao tem nada a ver com o que se quer testar.
var residuo = await LimparAsync();

if (residuo > 0)
{
    Console.WriteLine($"Resíduo ...... {residuo} registro(s) de execucao anterior removidos");
}

Console.WriteLine();

var problemas = 0;
var sequencial = 0;

void Checar(string descricao, bool condicao, string? detalhe = null)
{
    if (condicao)
    {
        Console.WriteLine($"    OK    {descricao}");
        return;
    }

    problemas++;
    Console.WriteLine($"    FALHA {descricao}");

    if (detalhe is not null)
    {
        Console.WriteLine($"          {detalhe}");
    }
}

ConversaService NovaConversaService(CrmDbContext db, Guid quem, string nome) =>
    new(db,
        new ContextoFixo(tenantId, quem, nome, PerfilDeAtendente.Administrador),
        new GeradorDeProtocolo(db),
        new NotificadorSilencioso(),
        new DisparadorDeMensagemAutomatica(db, NullLogger<DisparadorDeMensagemAutomatica>.Instance),
        new CanalSilencioso(),
        NullLogger<ConversaService>.Instance);

MensagemService NovaMensagemService(CrmDbContext db, Guid quem, string nome)
{
    var ctx = new ContextoFixo(tenantId, quem, nome, PerfilDeAtendente.Administrador);

    // Habilitada=false: o servico real de RespostaInteligente cai direto no
    // "nao respondeu" sem tentar rede nenhuma (mesma contingencia de producao),
    // entao o verificador exercita o caminho de sempre — triagem sem
    // correspondencia escala para humano — sem precisar de chave da Anthropic.
    var opcoesDaIA = Options.Create(new OpcoesDaIA { Habilitada = false });
    var respostaInteligente = new RespostaInteligenteService(
        db, new OrquestradorNuncaChamado(), opcoesDaIA, NullLogger<RespostaInteligenteService>.Instance);

    return new MensagemService(
        db,
        ctx,
        NovaConversaService(db, quem, nome),
        new NotificadorSilencioso(),
        new DisparadorDeMensagemAutomatica(db, NullLogger<DisparadorDeMensagemAutomatica>.Instance),
        new CanalSilencioso(),
        new AnexosEmMemoria(),
        respostaInteligente,
        opcoesDaIA,
        NullLogger<MensagemService>.Instance);
}

async Task<Guid> NovoClienteAsync(string apelido)
{
    await using var db = new CrmDbContext(opcoes);

    var telefone = $"5538000{++sequencial:D6}";

    var existente = await db.Clientes
        .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.TelefoneWhatsapp == telefone);

    if (existente is not null)
    {
        return existente.Id;
    }

    var cliente = new Cliente
    {
        TenantId = tenantId,
        Nome = $"{PrefixoDeTeste}{apelido}",
        TelefoneWhatsapp = telefone,
        PrimeiroContatoEm = DateTime.UtcNow,
        Ativo = true
    };

    db.Clientes.Add(cliente);
    await db.SaveChangesAsync();

    return cliente.Id;
}

async Task<List<(AutorTipo Autor, bool Automatica, string Conteudo, StatusEntrega Status)>> FioAsync(Guid conversaId)
{
    await using var db = new CrmDbContext(opcoes);

    return await db.Mensagens.AsNoTracking()
        .Where(m => m.ConversaId == conversaId)
        .OrderBy(m => m.EnviadaEm).ThenBy(m => m.Id)
        .Select(m => new ValueTuple<AutorTipo, bool, string, StatusEntrega>(
            m.AutorTipo, m.EhAutomatica, m.Conteudo, m.StatusEntrega))
        .ToListAsync();
}

// ---------------------------------------------------------------------------
// TESTE 1 — boas-vindas e triagem que ACERTA a opcao
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 1 — primeiro contato: menu sai e a resposta certa roteia sozinha");

Guid conversa1;

await using (var db = new CrmDbContext(opcoes))
{
    var criada = await NovaConversaService(db, atendenteId, atendenteNome).CriarAsync(
        new CriarConversaRequest(await NovoClienteAsync("Triagem acerta"), null, null, "Bom dia!"));

    conversa1 = criada.Id;

    Checar("conversa nasce aguardando triagem", criada.AguardandoTriagem);
    Checar("conversa nasce sem setor", criada.SetorId is null);
}

var fio1 = await FioAsync(conversa1);
var boasVindas = fio1.FirstOrDefault(m => m.Automatica);

Checar("mensagem de boas-vindas foi gerada", boasVindas.Conteudo is not null);
Checar("boas-vindas e do Sistema", boasVindas.Autor == AutorTipo.Sistema);
Checar("boas-vindas nasce Pendente (Fase 1 nao entrega nada)", boasVindas.Status == StatusEntrega.Pendente);
Checar("nenhum marcador sobrou sem resolver",
    boasVindas.Conteudo?.Contains('{') != true,
    boasVindas.Conteudo);

foreach (var (id, nome, _) in setores)
{
    _ = id;
    Checar($"menu cita o setor \"{nome}\"", boasVindas.Conteudo?.Contains(nome, StringComparison.Ordinal) == true);
}

// O cliente responde o numero da SEGUNDA opcao. Testar a segunda, e nao a
// primeira, e o que separa "leu a resposta" de "sempre cai no primeiro setor".
var setorEsperado = setores[1];

await using (var db = new CrmDbContext(opcoes))
{
    await NovaMensagemService(db, atendenteId, atendenteNome).SimularRecebimentoAsync(
        conversa1, new SimularMensagemDeClienteRequest("2"));
}

await using (var db = new CrmDbContext(opcoes))
{
    var depois = await db.Conversas.AsNoTracking().FirstAsync(c => c.Id == conversa1);

    Checar($"setor atribuido = \"{setorEsperado.Nome}\"", depois.SetorId == setorEsperado.Id,
        $"veio {depois.SetorId}");
    Checar("flag de triagem foi limpa", !depois.AguardandoTriagem);

    var trilha = await db.HistoricoEtapas.AsNoTracking()
        .Where(h => h.ConversaId == conversa1 && h.TipoMudanca == TipoMudancaHistorico.Setor)
        .ToListAsync();

    Checar("historico registrou a mudanca de setor", trilha.Count == 1);
    Checar("historico marca autoria automatica (AtendenteId nulo)",
        trilha.Count == 1 && trilha[0].AtendenteId is null);

    Checar("aguarda escolha de servico (setor tem servico vinculado)", depois.AguardandoEscolhaDeServico);
}

var perguntaServico = (await FioAsync(conversa1)).LastOrDefault(m => m.Automatica);

Checar("pergunta qual servico foi gerada", perguntaServico.Conteudo is not null);
Checar("pergunta de servico nao deixou marcador sem resolver",
    perguntaServico.Conteudo?.Contains('{') != true, perguntaServico.Conteudo);

// Fase 2D: o setor tem servico vinculado (migracao 014), entao a triagem
// pergunta QUAL antes de fechar — mesmo menu que ServicosDoMenuAsync monta.
List<(Guid Id, string Nome)> servicosDoSetorEsperado;

await using (var db = new CrmDbContext(opcoes))
{
    servicosDoSetorEsperado = await db.TiposServico.AsNoTracking()
        .Where(t => t.TenantId == tenantId && t.SetorId == setorEsperado.Id && t.Ativo)
        .OrderBy(t => t.Nome)
        .Select(t => new ValueTuple<Guid, string>(t.Id, t.Nome))
        .ToListAsync();
}

Checar("setor escolhido tem ao menos 1 servico vinculado (migracao 014)", servicosDoSetorEsperado.Count > 0);

if (servicosDoSetorEsperado.Count > 0)
{
    var servicoEsperado = servicosDoSetorEsperado[0];

    await using (var db = new CrmDbContext(opcoes))
    {
        await NovaMensagemService(db, atendenteId, atendenteNome).SimularRecebimentoAsync(
            conversa1, new SimularMensagemDeClienteRequest("1"));
    }

    await using (var db = new CrmDbContext(opcoes))
    {
        var depoisDoServico = await db.Conversas.AsNoTracking().FirstAsync(c => c.Id == conversa1);

        Checar($"servico atribuido = \"{servicoEsperado.Nome}\"",
            depoisDoServico.TipoServicoId == servicoEsperado.Id, $"veio {depoisDoServico.TipoServicoId}");
        Checar("flag de escolha de servico foi limpa", !depoisDoServico.AguardandoEscolhaDeServico);
    }

    var confirmacaoDoServico = (await FioAsync(conversa1)).LastOrDefault(m => m.Automatica);

    Checar("confirmacao com checklist/prazo foi gerada", confirmacaoDoServico.Conteudo is not null);
    Checar("confirmacao nao deixou marcador sem resolver",
        confirmacaoDoServico.Conteudo?.Contains('{') != true, confirmacaoDoServico.Conteudo);

    // Fase 2E.1: o checklist dispara TAMBEM o menu de continuacao.
    await using (var db = new CrmDbContext(opcoes))
    {
        var depoisDoChecklist = await db.Conversas.AsNoTracking().FirstAsync(c => c.Id == conversa1);
        Checar("aguarda menu de continuacao apos o checklist", depoisDoChecklist.AguardandoMenuDeContinuacao);
    }

    var menuDeContinuacao = (await FioAsync(conversa1)).LastOrDefault(m => m.Automatica);
    Checar("menu de continuacao foi gerado", menuDeContinuacao.Conteudo?.Contains("Encerrar") == true,
        menuDeContinuacao.Conteudo);

    // ACHADO AO VIVO, 2026-08-17: com todas as flags desligadas, uma mensagem
    // solta do cliente (aqui simulada por "obrigada", fora das 4 opcoes do menu
    // de continuacao) nao gerava resposta NENHUMA. Este e o teste que prova que
    // o buraco foi fechado.
    var automaticasAntesDoObrigada = (await FioAsync(conversa1)).Count(m => m.Automatica);

    await using (var db = new CrmDbContext(opcoes))
    {
        await NovaMensagemService(db, atendenteId, atendenteNome).SimularRecebimentoAsync(
            conversa1, new SimularMensagemDeClienteRequest("obrigada"));
    }

    var fioAposObrigada = await FioAsync(conversa1);

    Checar("\"obrigada\" NAO fica sem resposta (piso da IA)",
        fioAposObrigada.Count(m => m.Automatica) > automaticasAntesDoObrigada);

    await using (var db = new CrmDbContext(opcoes))
    {
        var depoisDoObrigada = await db.Conversas.AsNoTracking().FirstAsync(c => c.Id == conversa1);
        Checar("flag de menu de continuacao foi limpa", !depoisDoObrigada.AguardandoMenuDeContinuacao);

        // ACHADO AO VIVO, 2026-08-18: ate aqui, "obrigada" (nao e pedido de
        // atendente) escalava do mesmo jeito que um pedido explicito, so porque
        // a IA (Habilitada=false neste verificador) nunca "sabe responder". A
        // conversa precisa continuar OUVINDO a IA, nao desistir dela so porque
        // esta mensagem em especifico nao tinha o que responder.
        Checar("\"obrigada\" NAO desiste da IA — continua aguardando",
            depoisDoObrigada.AguardandoRespostaInteligente);
    }

    var respostaAoObrigada = fioAposObrigada.LastOrDefault(m => m.Automatica);

    // So escala para atendente por PEDIDO EXPLICITO desde 2026-08-18 —
    // "obrigada" nao e isso. O texto certo agora e RespostaSemCorrespondencia,
    // que nunca promete atendente.
    Checar("\"obrigada\" recebe o texto que nao promete atendente",
        respostaAoObrigada.Conteudo?.Contains("fico à disposição") == true,
        respostaAoObrigada.Conteudo);
    Checar("\"obrigada\" NAO recebe o texto de escalonamento (bug achado ao vivo, 2026-08-18)",
        respostaAoObrigada.Conteudo?.Contains("continuar seu atendimento") != true,
        respostaAoObrigada.Conteudo);

    // ACHADO AO VIVO, 2026-08-18 (segunda rodada): neste ponto exato,
    // AguardandoRespostaInteligente esta true (confirmado acima). "menu" so era
    // checado dentro de AplicadorDePosAtendimento, que so roda quando essa flag
    // esta DESLIGADA — cliente pedindo para voltar ao menu ia direto para a IA,
    // que respondia (mal) em vez de reabrir. Frase identica a que o cliente
    // real mandou.
    var automaticasAntesDoMenuPreso = (await FioAsync(conversa1)).Count(m => m.Automatica);

    await using (var db = new CrmDbContext(opcoes))
    {
        await NovaMensagemService(db, atendenteId, atendenteNome).SimularRecebimentoAsync(
            conversa1, new SimularMensagemDeClienteRequest("quero voltar ao menu inicial"));
    }

    var fioAposMenuPreso = await FioAsync(conversa1);
    var respostaAoMenuPreso = fioAposMenuPreso.LastOrDefault(m => m.Automatica);

    Checar("\"voltar ao menu inicial\" gera mensagem nova mesmo com a IA no meio de uma troca",
        fioAposMenuPreso.Count(m => m.Automatica) > automaticasAntesDoMenuPreso);
    Checar("\"voltar ao menu inicial\" reenvia o MENU (nao a resposta generica da IA)",
        respostaAoMenuPreso.Conteudo?.Contains(setorEsperado.Nome) == true,
        respostaAoMenuPreso.Conteudo);

    await using (var db = new CrmDbContext(opcoes))
    {
        var depoisDoMenuPreso = await db.Conversas.AsNoTracking().FirstAsync(c => c.Id == conversa1);
        Checar("\"voltar ao menu inicial\" reabriu a triagem (AguardandoTriagem ligada)",
            depoisDoMenuPreso.AguardandoTriagem);
        Checar("\"voltar ao menu inicial\" desligou AguardandoRespostaInteligente",
            !depoisDoMenuPreso.AguardandoRespostaInteligente);
    }

    // Pedido EXPLICITO de atendente, ao contrario de "obrigada", ainda escala —
    // prova que o caminho antigo continua existindo, so que agora exige a
    // palavra do cliente em vez de disparar sozinho. O "menu" logo acima ja
    // limpou SetorId (reabriu do zero de verdade), entao o texto certo agora e
    // o de PRIMEIRO CONTATO — nao o de meio-de-atendimento, que so faz sentido
    // com setor definido.
    var automaticasAntesDoPedido = (await FioAsync(conversa1)).Count(m => m.Automatica);

    await using (var db = new CrmDbContext(opcoes))
    {
        await NovaMensagemService(db, atendenteId, atendenteNome).SimularRecebimentoAsync(
            conversa1, new SimularMensagemDeClienteRequest("quero falar com um atendente"));
    }

    var fioAposPedido = await FioAsync(conversa1);
    var respostaAoPedido = fioAposPedido.LastOrDefault(m => m.Automatica);

    Checar("pedido explicito de atendente ainda gera mensagem nova",
        fioAposPedido.Count(m => m.Automatica) > automaticasAntesDoPedido);
    Checar("pedido explicito, com setor ja limpo pelo \"menu\", usa o texto de primeiro contato",
        respostaAoPedido.Conteudo?.Contains("assumirá sua solicitação") == true,
        respostaAoPedido.Conteudo);

    await using (var db = new CrmDbContext(opcoes))
    {
        var depoisDoPedido = await db.Conversas.AsNoTracking().FirstAsync(c => c.Id == conversa1);
        Checar("pedido explicito desliga AguardandoRespostaInteligente (escalou de verdade, desta vez)",
            !depoisDoPedido.AguardandoRespostaInteligente);
    }

    // ATUALIZADO EM 2026-08-26 (BUG-ATD-01): ate aqui, "menu" reabria a
    // triagem em QUALQUER ponto da conversa — inclusive depois de um pedido
    // explicito de atendente, mesmo sem nenhum atendente ter assumido ainda.
    // Isso era exatamente a classe de furo que o Human Takeover corrigiu:
    // pedir atendente liga Conversa.EmAtendimentoHumano (ver
    // AplicadorDeRespostaInteligente.EscalarAsync), e a partir dai NENHUMA
    // automacao conversacional pode reagir a mensagem do cliente — nem
    // "menu", que e exatamente "interpretar uma frase comum como fluxo
    // automatico" sobre uma conversa ja entregue a fila humana. O
    // comportamento ANTIGO testado aqui (reabrir a triagem) e o proprio bug
    // relatado, espelhado.
    var automaticasAntesDoMenuEmModoHumano = (await FioAsync(conversa1)).Count(m => m.Automatica);

    await using (var db = new CrmDbContext(opcoes))
    {
        await NovaMensagemService(db, atendenteId, atendenteNome).SimularRecebimentoAsync(
            conversa1, new SimularMensagemDeClienteRequest("menu"));
    }

    await using (var db = new CrmDbContext(opcoes))
    {
        var depoisDoMenu = await db.Conversas.AsNoTracking().FirstAsync(c => c.Id == conversa1);
        Checar("BUG-ATD-01: \"menu\" NAO reabre a triagem depois do pedido de atendente (modo humano)",
            !depoisDoMenu.AguardandoTriagem);
        Checar("BUG-ATD-01: EmAtendimentoHumano continua ligado", depoisDoMenu.EmAtendimentoHumano);
    }

    Checar("BUG-ATD-01: \"menu\" em modo humano nao gera nenhuma mensagem automatica",
        (await FioAsync(conversa1)).Count(m => m.Automatica) == automaticasAntesDoMenuEmModoHumano);
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 2 — resposta que NAO corresponde a opcao nenhuma
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 2 — resposta fora do menu cai para atendimento humano");

Guid conversa2;

await using (var db = new CrmDbContext(opcoes))
{
    var criada = await NovaConversaService(db, atendenteId, atendenteNome).CriarAsync(
        new CriarConversaRequest(await NovoClienteAsync("Triagem erra"), null, null, null));

    conversa2 = criada.Id;
}

// ACHADO NA INVESTIGACAO DO BUG-ATD-01 (2026-08-26): a frase original aqui
// ("quero falar com alguem sobre um assunto") contem "falar com alguem",
// que e' um dos gatilhos LITERAIS de TriagemAutomatica.PediuHumano — este
// teste, sem querer, sempre escalou de verdade, mesmo antes do Human
// Takeover existir. So passou despercebido porque nao havia
// EmAtendimentoHumano para expor a diferenca. Trocada por uma frase que
// realmente NAO corresponde a nenhuma opcao e NAO pede atendente, para
// testar o que o titulo do teste promete: o fallback da IA, sem escalar.
await using (var db = new CrmDbContext(opcoes))
{
    await NovaMensagemService(db, atendenteId, atendenteNome).SimularRecebimentoAsync(
        conversa2, new SimularMensagemDeClienteRequest("preciso de uma informacao qualquer"));
}

await using (var db = new CrmDbContext(opcoes))
{
    var depois = await db.Conversas.AsNoTracking().FirstAsync(c => c.Id == conversa2);

    Checar("setor continua vazio", depois.SetorId is null);
    Checar("flag de triagem foi limpa mesmo sem acerto", !depois.AguardandoTriagem);
}

var fio2 = await FioAsync(conversa2);

Checar("saiu boas-vindas + fallback (2 automaticas)",
    fio2.Count(m => m.Automatica) == 2,
    $"automaticas: {fio2.Count(m => m.Automatica)}");

Checar("nenhum marcador sobrou no fallback",
    fio2.Where(m => m.Automatica).All(m => !m.Conteudo.Contains('{')));

// Segunda mensagem do cliente NAO pode reabrir o MENU (isso sim seria o loop de
// robo original) — mas TAMBEM nao pode ficar muda: nenhuma flag ligada cai no
// piso da IA. "alguem ai?" NAO contem nenhuma das frases de PediuHumano
// ("atendente"/"humano"/"pessoa de verdade"/"falar com alguem" — a palavra
// solta "alguem" nao basta), entao desde 2026-08-18 ela NAO escala de novo:
// recebe RespostaSemCorrespondencia, que nunca promete atendente.
await using (var db = new CrmDbContext(opcoes))
{
    await NovaMensagemService(db, atendenteId, atendenteNome).SimularRecebimentoAsync(
        conversa2, new SimularMensagemDeClienteRequest("alguem ai?"));
}

var fio2DepoisDoSegundoPedido = await FioAsync(conversa2);

Checar("segunda mensagem nao repete o MENU de boas-vindas",
    fio2DepoisDoSegundoPedido.Count(m => m.Automatica && m.Conteudo.Contains("responda com o")) == 1);

Checar("mas tambem nao fica muda (piso da IA)",
    fio2DepoisDoSegundoPedido.Count(m => m.Automatica) > fio2.Count(m => m.Automatica));

Checar("\"alguem ai?\" nao escala de novo — nao e pedido explicito de atendente",
    fio2DepoisDoSegundoPedido.LastOrDefault(m => m.Automatica).Conteudo?.Contains("fico à disposição") == true,
    fio2DepoisDoSegundoPedido.LastOrDefault(m => m.Automatica).Conteudo);

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 3 — conversa aberta JA com setor nao recebe menu
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 3 — atendente ja escolheu o setor: nao faz sentido perguntar");

Guid conversa3;

await using (var db = new CrmDbContext(opcoes))
{
    var criada = await NovaConversaService(db, atendenteId, atendenteNome).CriarAsync(
        new CriarConversaRequest(await NovoClienteAsync("Com setor"), setores[0].Id, null, "Oi"));

    conversa3 = criada.Id;

    Checar("nao entra em triagem", !criada.AguardandoTriagem);
}

Checar("nenhuma mensagem automatica foi gerada",
    (await FioAsync(conversa3)).Count(m => m.Automatica) == 0);

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 4 — aviso por mudanca de etapa
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 4 — mover para a etapa final avisa o cliente");

string protocolo4;

await using (var db = new CrmDbContext(opcoes))
{
    var servico = NovaConversaService(db, atendenteId, atendenteNome);

    var criada = await servico.CriarAsync(
        new CriarConversaRequest(await NovoClienteAsync("Etapa"), setores[0].Id, etapaInicialId, "Oi"));

    protocolo4 = criada.Protocolo;

    await servico.AssumirAsync(criada.Id);
    await servico.VincularServicoAsync(criada.Id, new VincularServicoRequest(servicoId));

    // Etapa intermediaria: nao ha regra para ela, entao nada deve sair.
    await servico.MoverAsync(criada.Id, new MoverConversaRequest(etapaInicialId, null, null));

    var antes = (await FioAsync(criada.Id)).Count(m => m.Automatica);

    await servico.MoverAsync(criada.Id, new MoverConversaRequest(etapaFinalId, null, null));

    var fio = await FioAsync(criada.Id);
    var aviso = fio.LastOrDefault(m => m.Automatica);

    Checar("etapa sem regra nao dispara nada", antes == 0);
    Checar("etapa final disparou exatamente 1 aviso", fio.Count(m => m.Automatica) == 1);
    Checar("aviso resolveu {Protocolo}",
        aviso.Conteudo?.Contains(protocolo4, StringComparison.Ordinal) == true, aviso.Conteudo);
    Checar("aviso resolveu {Servico}",
        aviso.Conteudo?.Contains(servicoNome, StringComparison.Ordinal) == true);
    Checar("nenhum marcador sobrou", aviso.Conteudo?.Contains('{') != true);
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 5 — varredura de prazo
//
// Cria regra PROPRIA para o gatilho: as do seed nascem desativadas por decisao do
// cartório parceiro, e ligar a configuracao dele para rodar um teste seria mudar o produto
// para caber no verificador.
//
// A varredura e por TENANT, nao por conversa: enquanto a regra de teste esta ativa,
// qualquer conversa da demonstracao ja vencida tambem recebe aviso. Por isso
// primeiraPassada pode vir > 1, e por isso a limpeza precisa desfazer o respingo —
// ver LimparAsync.
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 5 — varredura de prazo avisa uma vez e so uma");

var regraDeTesteId = Guid.CreateVersion7();
Guid conversa5;

await using (var db = new CrmDbContext(opcoes))
{
    db.MensagensAutomaticas.Add(new MensagemAutomatica
    {
        Id = regraDeTesteId,
        TenantId = tenantId,
        Gatilho = GatilhoDeMensagem.PrazoVencido,
        Conteudo = $"{PrefixoDeTeste}Protocolo {{Protocolo}} passou do prazo.",
        Ativo = true
    });

    await db.SaveChangesAsync();
}

await using (var db = new CrmDbContext(opcoes))
{
    var servico = NovaConversaService(db, atendenteId, atendenteNome);

    var criada = await servico.CriarAsync(
        new CriarConversaRequest(await NovoClienteAsync("Prazo"), setores[0].Id, etapaInicialId, "Oi"));

    conversa5 = criada.Id;

    await servico.AssumirAsync(conversa5);
    await servico.VincularServicoAsync(conversa5, new VincularServicoRequest(servicoId));
}

// Empurra o prazo para o passado. E fixture: o que esta sob teste e a varredura,
// nao a CalculadoraDePrazo.
await using (var db = new CrmDbContext(opcoes))
{
    var passado = DateTime.UtcNow.AddHours(-2);

    await db.Conversas.Where(c => c.Id == conversa5)
        .ExecuteUpdateAsync(s => s
            .SetProperty(c => c.PrazoLimite, passado)
            .SetProperty(c => c.PrazoAlertaEm, passado.AddHours(-1)));
}

int primeiraPassada;
int segundaPassada;

await using (var db = new CrmDbContext(opcoes))
{
    primeiraPassada = await NovoVarredor(db).ExecutarAsync();
}

await using (var db = new CrmDbContext(opcoes))
{
    segundaPassada = await NovoVarredor(db).ExecutarAsync();
}

await using (var db = new CrmDbContext(opcoes))
{
    var depois = await db.Conversas.AsNoTracking().FirstAsync(c => c.Id == conversa5);
    var fio = await FioAsync(conversa5);

    Checar("primeira passada avisou", primeiraPassada >= 1);
    Checar("marcador de aviso foi gravado", depois.AvisoDeVencimentoEnviadoEm is not null);
    Checar("segunda passada NAO reavisa a mesma conversa", segundaPassada == 0,
        $"segunda passada avisou {segundaPassada}");
    Checar("exatamente 1 aviso no fio", fio.Count(m => m.Automatica) == 1);
    Checar("aviso do gatilho de prazo resolveu o protocolo",
        fio.Any(m => m.Automatica && !m.Conteudo.Contains('{')));

    // UltimaMensagemEm nao pode ter sido tocado: aviso automatico nao reordena a
    // inbox (README do modulo Atendimento).
    Checar("aviso automatico nao reordenou a inbox",
        depois.UltimaMensagemEm < depois.AvisoDeVencimentoEnviadoEm);
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 6 — transferencia entre atendentes (RF-013)
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 6 — transferencia entre atendentes");

await using (var db = new CrmDbContext(opcoes))
{
    var servico = NovaConversaService(db, atendenteId, atendenteNome);

    var criada = await servico.CriarAsync(
        new CriarConversaRequest(await NovoClienteAsync("Transferencia"), setores[0].Id, null, "Oi"));

    await servico.AssumirAsync(criada.Id);

    var transferida = await servico.TransferirAsync(
        criada.Id, new TransferirConversaRequest(outroAtendenteId, "Assunto do outro setor."));

    Checar($"responsavel passou a ser {outroAtendenteNome}", transferida.AtendenteId == outroAtendenteId);

    var fio = await FioAsync(criada.Id);
    var anotacao = fio.LastOrDefault(m => m.Autor == AutorTipo.Sistema && !m.Automatica);

    Checar("anotacao de sistema registrou a transferencia",
        anotacao.Conteudo?.Contains(outroAtendenteNome, StringComparison.Ordinal) == true,
        anotacao.Conteudo);
    Checar("anotacao interna NAO e marcada como automatica",
        fio.Count(m => m.Autor == AutorTipo.Sistema && m.Automatica) == 0);
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 7 — "Encerrar" no menu de continuacao fecha a conversa de verdade
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 7 — menu de continuacao, opcao Encerrar");

Guid conversa7;

await using (var db = new CrmDbContext(opcoes))
{
    var criada = await NovaConversaService(db, atendenteId, atendenteNome).CriarAsync(
        new CriarConversaRequest(await NovoClienteAsync("Encerrar pelo menu"), setores[0].Id, null, "Oi"));

    conversa7 = criada.Id;
}

// Direto no banco: pula a caminhada por triagem + servico so para chegar no
// estado "menu de continuacao no ar", que e o unico pre-requisito do que este
// teste quer provar.
await using (var db = new CrmDbContext(opcoes))
{
    var c = await db.Conversas.FirstAsync(x => x.Id == conversa7);
    c.AguardandoMenuDeContinuacao = true;
    await db.SaveChangesAsync();
}

await using (var db = new CrmDbContext(opcoes))
{
    // "4" = "Encerrar atendimento", a mesma escolha numerica que o cliente
    // manda no WhatsApp de verdade.
    await NovaMensagemService(db, atendenteId, atendenteNome).SimularRecebimentoAsync(
        conversa7, new SimularMensagemDeClienteRequest("4"));
}

await using (var db = new CrmDbContext(opcoes))
{
    var depoisDoEncerrar = await db.Conversas.AsNoTracking().FirstAsync(c => c.Id == conversa7);

    // ACHADO AO VIVO, 2026-08-17: cliente escolhia Encerrar, a despedida saia,
    // mas a conversa continuava Ativa no painel. Este bloco prova que fechou.
    Checar("\"Encerrar\" muda o status para Encerrada",
        depoisDoEncerrar.Status == StatusConversa.Encerrada);
    Checar("EncerradaEm foi gravado", depoisDoEncerrar.EncerradaEm is not null);
    Checar("EncerradaPorAtendenteId fica nulo (foi o cliente, nao um atendente logado)",
        depoisDoEncerrar.EncerradaPorAtendenteId is null);
    Checar("flag de menu de continuacao foi limpa", !depoisDoEncerrar.AguardandoMenuDeContinuacao);
}

var fio7 = await FioAsync(conversa7);

var despedida = fio7.LastOrDefault(m => m.Automatica);
Checar("despedida foi enviada ao cliente", despedida.Conteudo?.Contains("prazer atender") == true,
    despedida.Conteudo);
Checar("despedida nao deixou marcador sem resolver", despedida.Conteudo?.Contains('{') != true,
    despedida.Conteudo);

var anotacaoDeEncerramento = fio7.LastOrDefault(m => m.Autor == AutorTipo.Sistema && !m.Automatica);
Checar("anotacao interna registra que foi o cliente quem encerrou",
    anotacaoDeEncerramento.Conteudo?.Contains("encerrado pelo cliente", StringComparison.OrdinalIgnoreCase) == true,
    anotacaoDeEncerramento.Conteudo);

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 8 — grounding da IA leva o setor de cada servico
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 8 — catalogo da IA inclui o setor de cada servico");

// ACHADO AO VIVO, 2026-08-18: cliente perguntou "quais os servicos de
// tabelionato de notas" e a IA nao soube responder — o catalogo listava nome e
// descricao de cada TipoServico, mas nunca o setor, entao nao havia como a IA
// filtrar por setor sem inventar. Testa direto o texto que MontadorDeContexto
// monta, sem precisar da chamada real a Anthropic (Habilitada=false).
await using (var db = new CrmDbContext(opcoes))
{
    var contexto = await MontadorDeContexto.MontarAsync(db, tenantId, CancellationToken.None);
    var setoresNoCatalogo = setores.Count(s => contexto.Contains($"({s.Nome})"));

    Checar($"catalogo cita o servico \"{servicoNome}\"", contexto.Contains(servicoNome));
    Checar("catalogo cita o setor de CADA servico ativo (os 2 setores aparecem entre parenteses)",
        setoresNoCatalogo == setores.Count,
        $"setores citados: {setoresNoCatalogo}/{setores.Count}");
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTES 9 a 15 — encerramento automatico por inatividade
// ---------------------------------------------------------------------------
//
// A conversa precisa ser de CANAL WHATSAPP: a varredura ignora conversa simulada
// de proposito, para nao desmontar a base de demonstracao. ConversaService.CriarAsync
// abre como Simulado, entao o canal e ajustado por fora — e fixture, nao o que esta
// sob teste.

async Task<Guid> NovaConversaParadaAsync(string apelido, int minutosDeSilencio)
{
    Guid conversaId;

    await using (var db = new CrmDbContext(opcoes))
    {
        var criada = await NovaConversaService(db, atendenteId, atendenteNome).CriarAsync(
            new CriarConversaRequest(await NovoClienteAsync(apelido), null, null, null));

        conversaId = criada.Id;
    }

    await using (var db = new CrmDbContext(opcoes))
    {
        await db.Conversas.Where(c => c.Id == conversaId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Canal, CanalOrigem.WhatsApp)
                .SetProperty(c => c.MensagensNaoLidas, 0)
                .SetProperty(c => c.UltimaMensagemEm, DateTime.UtcNow.AddMinutes(-minutosDeSilencio)));
    }

    return conversaId;
}

async Task ConfigurarEncerramentoAsync(int? perguntar, int? encerrar, int? horaInicial, int? horaFinal)
{
    await using var db = new CrmDbContext(opcoes);

    await db.Tenants.Where(t => t.Id == tenantId)
        .ExecuteUpdateAsync(s => s
            .SetProperty(t => t.MinutosParaPerguntarEncerramento, perguntar)
            .SetProperty(t => t.MinutosParaEncerrarSemResposta, encerrar)
            .SetProperty(t => t.EncerramentoHoraInicial, horaInicial)
            .SetProperty(t => t.EncerramentoHoraFinal, horaFinal));
}

async Task<Conversa> RecarregarAsync(Guid conversaId)
{
    await using var db = new CrmDbContext(opcoes);
    return await db.Conversas.AsNoTracking().FirstAsync(c => c.Id == conversaId);
}

/// <summary>O que o CLIENTE recebeu — so mensagem automatica, sem anotacao interna.</summary>
async Task<string[]> TextosDaConversaAsync(Guid conversaId)
{
    await using var db = new CrmDbContext(opcoes);

    return await db.Mensagens.AsNoTracking()
        .Where(m => m.ConversaId == conversaId && m.EhAutomatica)
        .OrderBy(m => m.EnviadaEm)
        .Select(m => m.Conteudo)
        .ToArrayAsync();
}

/// <summary>
/// Saiu alguma automatica que NAO e a pergunta de nome — ou seja, o atendimento
/// abriu de verdade.
/// </summary>
/// <remarks>
/// Deliberadamente nao procura uma frase do menu: o texto e cadastro e o cartório parceiro
/// pode reescrever a qualquer momento, e um teste que quebra quando alguem edita
/// uma mensagem pela tela testa a redacao, nao o comportamento.
/// </remarks>
bool AbriuAtendimento(string[] textos) =>
    textos.Any(t => !t.Contains("como posso te chamar", StringComparison.OrdinalIgnoreCase));

async Task<Cliente> ClienteDaConversaAsync(Guid conversaId)
{
    await using var db = new CrmDbContext(opcoes);

    var clienteId = await db.Conversas.AsNoTracking()
        .Where(c => c.Id == conversaId)
        .Select(c => c.ClienteId)
        .FirstAsync();

    return await db.Clientes.AsNoTracking().FirstAsync(c => c.Id == clienteId);
}

// Janela nula = qualquer hora. Sem isso os testes so passariam entre 8h e 18h.
await ConfigurarEncerramentoAsync(20, 30, null, null);

// ---------------------------------------------------------------------------
// TESTE 9 — a pergunta sai UMA vez e nao reordena a inbox
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 9 — conversa parada recebe a pergunta de encerramento, e so uma vez");

var conversa9 = await NovaConversaParadaAsync("Parada", 60);

int primeiraPassada9;
int segundaPassada9;

await using (var db = new CrmDbContext(opcoes))
{
    primeiraPassada9 = await NovoVarredorDeInatividade(db).ExecutarAsync();
}

await using (var db = new CrmDbContext(opcoes))
{
    segundaPassada9 = await NovoVarredorDeInatividade(db).ExecutarAsync();
}

{
    var depois = await RecarregarAsync(conversa9);
    var fio = await FioAsync(conversa9);

    Checar("primeira passada perguntou", primeiraPassada9 >= 1);
    Checar("o carimbo da pergunta foi gravado", depois.PerguntaDeEncerramentoEnviadaEm is not null);
    Checar("segunda passada NAO repergunta", segundaPassada9 == 0, $"segunda passada tratou {segundaPassada9}");
    Checar("a pergunta chegou ao fio",
        fio.Any(m => m.Automatica && m.Conteudo.Contains("Posso encerrar")));
    Checar("a pergunta nao reordenou a inbox",
        depois.UltimaMensagemEm < depois.PerguntaDeEncerramentoEnviadaEm);
    Checar("a conversa continua ativa enquanto espera resposta",
        depois.Status == StatusConversa.Ativa);
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 10 — "sim" encerra, "nao" continua, e "assim" NAO encerra
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 10 — leitura da resposta: sim encerra, nao continua, \"assim\" nao e \"sim\"");

// O terceiro caso e o de regressao que mais importa: com Contains de substring,
// "assim" casa com "sim" e a conversa de quem disse que vai voltar seria fechada.
var conversa10Sim = await NovaConversaParadaAsync("Confirma", 60);
var conversa10Nao = await NovaConversaParadaAsync("Recusa", 60);
var conversa10Assim = await NovaConversaParadaAsync("Assim", 60);

await using (var db = new CrmDbContext(opcoes))
{
    await NovoVarredorDeInatividade(db).ExecutarAsync();
}

await using (var db = new CrmDbContext(opcoes))
{
    await NovaMensagemService(db, atendenteId, atendenteNome).SimularRecebimentoAsync(
        conversa10Sim, new SimularMensagemDeClienteRequest("sim, pode encerrar"));
}

await using (var db = new CrmDbContext(opcoes))
{
    await NovaMensagemService(db, atendenteId, atendenteNome).SimularRecebimentoAsync(
        conversa10Nao, new SimularMensagemDeClienteRequest("nao, ainda preciso"));
}

await using (var db = new CrmDbContext(opcoes))
{
    await NovaMensagemService(db, atendenteId, atendenteNome).SimularRecebimentoAsync(
        conversa10Assim, new SimularMensagemDeClienteRequest("assim que eu tiver os documentos eu volto"));
}

{
    var comSim = await RecarregarAsync(conversa10Sim);
    var comNao = await RecarregarAsync(conversa10Nao);
    var comAssim = await RecarregarAsync(conversa10Assim);

    Checar("\"sim, pode encerrar\" encerrou", comSim.Status == StatusConversa.Encerrada);
    Checar("encerramento por confirmacao nao tem atendente",
        comSim.EncerradaPorAtendenteId is null);
    Checar("o carimbo saiu junto com o encerramento",
        comSim.PerguntaDeEncerramentoEnviadaEm is null);

    Checar("\"nao, ainda preciso\" manteve a conversa aberta", comNao.Status == StatusConversa.Ativa);
    Checar("recusa cancelou o carimbo", comNao.PerguntaDeEncerramentoEnviadaEm is null);
    Checar("recusa ofereceu o menu de continuacao", comNao.AguardandoMenuDeContinuacao);

    Checar("\"assim que eu tiver...\" NAO encerrou (regressao de substring)",
        comAssim.Status == StatusConversa.Ativa,
        $"status: {comAssim.Status}");
    Checar("resposta nao entendida tambem cancela o encerramento",
        comAssim.PerguntaDeEncerramentoEnviadaEm is null);
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 11 — sem resposta, encerra sozinha
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 11 — sem resposta dentro do prazo, a conversa encerra sozinha");

var conversa11 = await NovaConversaParadaAsync("Silencio", 60);

await using (var db = new CrmDbContext(opcoes))
{
    await NovoVarredorDeInatividade(db).ExecutarAsync();
}

// Empurra a pergunta para tras do limite de espera. E fixture: o que esta sob teste
// e a segunda fase, nao a passagem do tempo.
await using (var db = new CrmDbContext(opcoes))
{
    await db.Conversas.Where(c => c.Id == conversa11)
        .ExecuteUpdateAsync(s => s
            .SetProperty(c => c.PerguntaDeEncerramentoEnviadaEm, DateTime.UtcNow.AddMinutes(-45)));
}

await using (var db = new CrmDbContext(opcoes))
{
    await NovoVarredorDeInatividade(db).ExecutarAsync();
}

{
    var depois = await RecarregarAsync(conversa11);
    var fio = await FioAsync(conversa11);

    Checar("conversa encerrada", depois.Status == StatusConversa.Encerrada);
    Checar("EncerradaEm gravado", depois.EncerradaEm is not null);
    Checar("sem atendente responsavel pelo encerramento", depois.EncerradaPorAtendenteId is null);
    Checar("motivo registrado",
        depois.MotivoEncerramento is not null && depois.MotivoEncerramento.Contains("inatividade"));
    Checar("carimbo limpo depois de encerrar", depois.PerguntaDeEncerramentoEnviadaEm is null);
    Checar("todas as flags de fluxo zeradas",
        !depois.AguardandoTriagem && !depois.AguardandoEscolhaDeServico
        && !depois.AguardandoMenuDeContinuacao && !depois.AguardandoRespostaInteligente);
    Checar("o aviso de encerramento chegou ao fio",
        fio.Any(m => m.Automatica && m.Conteudo.Contains("não tivemos retorno")));
    Checar("a anotacao de sistema ficou registrada",
        fio.Any(m => m.Autor == AutorTipo.Sistema && m.Conteudo.Contains("automaticamente por inatividade")));
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 12 — O REQUISITO: cliente que volta comeca do zero
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 12 — cliente que volta depois de encerrada cai em atendimento NOVO");

// Este e o teste que prova o pedido original. Os outros provam mecanica; este prova
// que o bug relatado — "cliente que volta nao cai no chat automatico" — acabou.

Guid clienteQueVolta;
string protocoloAntigo;

await using (var db = new CrmDbContext(opcoes))
{
    var encerrada = await db.Conversas.AsNoTracking().FirstAsync(c => c.Id == conversa11);
    clienteQueVolta = encerrada.ClienteId;
    protocoloAntigo = encerrada.Protocolo;
}

Guid conversaNova;

await using (var db = new CrmDbContext(opcoes))
{
    // CriarAsync so recusa segundo atendimento quando ha um ABERTO. Com a anterior
    // encerrada pela varredura, esta chamada e exatamente o que o webhook faria.
    var criada = await NovaConversaService(db, atendenteId, atendenteNome).CriarAsync(
        new CriarConversaRequest(clienteQueVolta, null, null, "Oi, voltei"));

    conversaNova = criada.Id;

    Checar("abriu conversa NOVA, e nao reaproveitou a encerrada", conversaNova != conversa11);
    Checar("protocolo novo", criada.Protocolo != protocoloAntigo,
        $"antigo: {protocoloAntigo}, novo: {criada.Protocolo}");
    Checar("o atendimento automatico recomeca do zero", criada.AguardandoTriagem);
}

{
    var fio = await FioAsync(conversaNova);

    Checar("boas-vindas sairam de novo para quem voltou",
        fio.Any(m => m.Automatica));
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 13 — as guardas: quem NAO pode ser perguntado
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 13 — mensagem nao lida e canal simulado ficam de fora; SLA vivo NAO bloqueia mais");

var conversaComPrazo = await NovaConversaParadaAsync("Com prazo", 60);
var conversaNaoLida = await NovaConversaParadaAsync("Nao lida", 60);
var conversaSimulada = await NovaConversaParadaAsync("Simulada", 60);

await using (var db = new CrmDbContext(opcoes))
{
    // SLA em andamento — decisao de 2026-08-23: nao bloqueia mais a pergunta.
    // O protocolo continua intacto (soft-close), so o CANAL de WhatsApp fecha.
    await db.Conversas.Where(c => c.Id == conversaComPrazo)
        .ExecuteUpdateAsync(s => s
            .SetProperty(c => c.PrazoLimite, DateTime.UtcNow.AddDays(3)));

    // O cartorio esta devendo resposta.
    await db.Conversas.Where(c => c.Id == conversaNaoLida)
        .ExecuteUpdateAsync(s => s.SetProperty(c => c.MensagensNaoLidas, 2));

    // Conversa de demonstracao nao sai pelo gateway.
    await db.Conversas.Where(c => c.Id == conversaSimulada)
        .ExecuteUpdateAsync(s => s.SetProperty(c => c.Canal, CanalOrigem.Simulado));
}

await using (var db = new CrmDbContext(opcoes))
{
    await NovoVarredorDeInatividade(db).ExecutarAsync();
}

{
    Checar("conversa com SLA em andamento FOI perguntada (guarda removida em 2026-08-23)",
        (await RecarregarAsync(conversaComPrazo)).PerguntaDeEncerramentoEnviadaEm is not null);
    Checar("conversa com mensagem nao lida nao foi perguntada",
        (await RecarregarAsync(conversaNaoLida)).PerguntaDeEncerramentoEnviadaEm is null);
    Checar("conversa simulada nao foi perguntada",
        (await RecarregarAsync(conversaSimulada)).PerguntaDeEncerramentoEnviadaEm is null);
}

// Recurso desligado no tenant: nada acontece, nem mesmo nas que ja se qualificavam.
var conversaSemConfig = await NovaConversaParadaAsync("Sem config", 60);

await ConfigurarEncerramentoAsync(null, null, null, null);

int passadaDesligada;

await using (var db = new CrmDbContext(opcoes))
{
    passadaDesligada = await NovoVarredorDeInatividade(db).ExecutarAsync();
}

Checar("tenant sem configuracao nao e varrido", passadaDesligada == 0);
Checar("nenhuma pergunta saiu com o recurso desligado",
    (await RecarregarAsync(conversaSemConfig)).PerguntaDeEncerramentoEnviadaEm is null);

// Fora da janela de horario: configura uma janela que certamente nao contem agora.
var horaLocal = TimeZoneInfo.ConvertTimeFromUtc(
    DateTime.UtcNow,
    TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "E. South America Standard Time" : "America/Sao_Paulo")).Hour;

var inicioFechado = (horaLocal + 2) % 24;
var fimFechado = (horaLocal + 3) % 24;

await ConfigurarEncerramentoAsync(20, 30, inicioFechado, fimFechado);

int passadaForaDaJanela;

await using (var db = new CrmDbContext(opcoes))
{
    passadaForaDaJanela = await NovoVarredorDeInatividade(db).ExecutarAsync();
}

Checar($"fora da janela ({inicioFechado}h-{fimFechado}h, agora {horaLocal}h) nao pergunta",
    passadaForaDaJanela == 0);
Checar("conversa segue sem carimbo fora da janela",
    (await RecarregarAsync(conversaSemConfig)).PerguntaDeEncerramentoEnviadaEm is null);

await ConfigurarEncerramentoAsync(20, 30, null, null);

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 14 — reabrir limpa o carimbo
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 14 — conversa reaberta nao volta a fechar sozinha na passada seguinte");

var conversa14 = await NovaConversaParadaAsync("Reaberta", 60);

await using (var db = new CrmDbContext(opcoes))
{
    await NovoVarredorDeInatividade(db).ExecutarAsync();
}

await using (var db = new CrmDbContext(opcoes))
{
    await db.Conversas.Where(c => c.Id == conversa14)
        .ExecuteUpdateAsync(s => s
            .SetProperty(c => c.PerguntaDeEncerramentoEnviadaEm, DateTime.UtcNow.AddMinutes(-45)));
}

await using (var db = new CrmDbContext(opcoes))
{
    await NovoVarredorDeInatividade(db).ExecutarAsync();
}

Checar("encerrou antes de reabrir",
    (await RecarregarAsync(conversa14)).Status == StatusConversa.Encerrada);

await using (var db = new CrmDbContext(opcoes))
{
    await NovaConversaService(db, atendenteId, atendenteNome).ReabrirAsync(conversa14);
}

{
    var reaberta = await RecarregarAsync(conversa14);

    Checar("reabrir limpou o carimbo", reaberta.PerguntaDeEncerramentoEnviadaEm is null);
}

await using (var db = new CrmDbContext(opcoes))
{
    await NovoVarredorDeInatividade(db).ExecutarAsync();
}

{
    var depoisDaVarredura = await RecarregarAsync(conversa14);

    // Sem a limpeza no ReabrirAsync, a conversa voltaria Ativa com o carimbo antigo
    // e a passada seguinte a fecharia na frente do atendente que acabou de reabrir.
    Checar("conversa reaberta continua aberta apos nova passada",
        depoisDaVarredura.Status == StatusConversa.Ativa,
        $"status: {depoisDaVarredura.Status}");
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 15 — sem texto cadastrado
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 15 — sem texto: nao pergunta; mas na 2a fase encerra mesmo assim");

var conversa15 = await NovaConversaParadaAsync("Sem texto", 60);

// Desativa os dois textos, como se o cartório parceiro os tivesse desligado pela tela.
await using (var db = new CrmDbContext(opcoes))
{
    await db.MensagensAutomaticas
        .Where(m => m.TenantId == tenantId
                 && (m.Gatilho == GatilhoDeMensagem.ConfirmacaoDeEncerramento
                  || m.Gatilho == GatilhoDeMensagem.EncerramentoPorInatividade))
        .ExecuteUpdateAsync(s => s.SetProperty(m => m.Ativo, false));
}

await using (var db = new CrmDbContext(opcoes))
{
    await NovoVarredorDeInatividade(db).ExecutarAsync();
}

Checar("sem texto da pergunta, nada e marcado (encerrar em silencio seria pior)",
    (await RecarregarAsync(conversa15)).PerguntaDeEncerramentoEnviadaEm is null);

// Agora simula o caso real: a pergunta JA tinha saido, e a regra foi desativada
// depois. A segunda fase precisa fechar assim mesmo — deixar a conversa presa nesse
// limbo e exatamente o bug que a varredura existe para desfazer.
await using (var db = new CrmDbContext(opcoes))
{
    await db.Conversas.Where(c => c.Id == conversa15)
        .ExecuteUpdateAsync(s => s
            .SetProperty(c => c.PerguntaDeEncerramentoEnviadaEm, DateTime.UtcNow.AddMinutes(-45)));
}

await using (var db = new CrmDbContext(opcoes))
{
    await NovoVarredorDeInatividade(db).ExecutarAsync();
}

Checar("com a pergunta ja feita, a falta de texto NAO impede o encerramento",
    (await RecarregarAsync(conversa15)).Status == StatusConversa.Encerrada);

await using (var db = new CrmDbContext(opcoes))
{
    await db.MensagensAutomaticas
        .Where(m => m.TenantId == tenantId
                 && (m.Gatilho == GatilhoDeMensagem.ConfirmacaoDeEncerramento
                  || m.Gatilho == GatilhoDeMensagem.EncerramentoPorInatividade))
        .ExecuteUpdateAsync(s => s.SetProperty(m => m.Ativo, true));
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 16 — NomeDeContato, logica pura (sem banco)
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 16 — as duas leituras de nome, com vies oposto");

// EhUtil erra para NAO perguntar: recusa so o que nao tem duas letras.
foreach (var ruim in new[] { "", "   ", ".", "...", "🔥", "A", "5538999000045", "(19) 99000-1488", "2" })
{
    Checar($"EhUtil recusa {(ruim.Length == 0 ? "(vazio)" : ruim)}", !NomeDeContato.EhUtil(ruim));
}

foreach (var bom in new[] { "Zé", "Ana", "JOÃO", "Tia Neuza", "Ana 💅", "d'Ávila", "Ярослав" })
{
    Checar($"EhUtil aceita {bom}", NomeDeContato.EhUtil(bom));
}

// LerRespostaDePergunta erra para NAO gravar: na duvida devolve null.
Checar("le \"João Silva\"", NomeDeContato.LerRespostaDePergunta("João Silva") == "João Silva");
Checar("tira o \"meu nome é\"",
    NomeDeContato.LerRespostaDePergunta("meu nome é João Silva") == "João Silva");
Checar("tira o \"Sou o\" preservando acento e caixa",
    NomeDeContato.LerRespostaDePergunta("Sou o Antônio de Pádua") == "Antônio de Pádua");
Checar("tira o \"me chamo\"", NomeDeContato.LerRespostaDePergunta("me chamo Maria") == "Maria");
Checar("descarta emoji no meio do nome",
    NomeDeContato.LerRespostaDePergunta("Ana 💅") == "Ana");

foreach (var naoEhNome in new[]
{
    "oi", "Bom dia", "2", ".", "obrigado", "quero uma certidão",
    "quero uma certidão de nascimento do meu filho mais novo",
    "preciso falar com um atendente"
})
{
    Checar($"recusa \"{naoEhNome}\"", NomeDeContato.LerRespostaDePergunta(naoEhNome) is null);
}

Checar("recusa resposta longa demais para ser nome",
    NomeDeContato.LerRespostaDePergunta(new string('a', 300)) is null);

// DoPerfil: mesmo resultado de sempre, mais as duas defesas novas.
Checar("DoPerfil mantem o perfil normal", NomeDeContato.DoPerfil("Joana Prado", "5538999000045") == "Joana Prado");
Checar("DoPerfil cai para o telefone quando o perfil e vazio",
    NomeDeContato.DoPerfil("   ", "5538999000045") == "5538999000045");
Checar("DoPerfil apara espaco", NomeDeContato.DoPerfil("  Joana  ", "5538999000045") == "Joana");
Checar("DoPerfil trunca em 200 (a coluna nao aceita mais, e estourava a mensagem)",
    NomeDeContato.DoPerfil(new string('x', 250), "5538999000045").Length == 200);

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 17 — o vocativo some quando nao ha nome utilizavel
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 17 — sem nome, sai o cumprimento e mais nada");

{
    var semNome = new DadosParaMensagem(
        string.Empty, "Cartorio de Teste", null, "202608-0001", string.Empty);

    var comNome = semNome with { NomeCliente = "Joana" };

    foreach (var (modelo, esperado) in new[]
    {
        ("Olá, {NomeCliente}! 👋", "Olá! 👋"),
        ("{NomeCliente}, seu atendimento foi concluído!", "Seu atendimento foi concluído!"),
        ("Posso ajudar em mais alguma coisa, {NomeCliente}?", "Posso ajudar em mais alguma coisa?"),
        ("Não tenho isso por aqui, {NomeCliente}, mas fico à disposição!",
         "Não tenho isso por aqui, mas fico à disposição!"),
        ("Foi um prazer atender você, {NomeCliente}! 👋", "Foi um prazer atender você! 👋")
    })
    {
        var saida = PlaceholdersDeMensagem.Aplicar(modelo, semNome);
        Checar($"sem nome: \"{modelo[..Math.Min(28, modelo.Length)]}...\" nao deixa virgula orfa",
            saida == esperado, saida);
    }

    Checar("com nome, a substituicao continua literal",
        PlaceholdersDeMensagem.Aplicar("Olá, {NomeCliente}! 👋", comNome) == "Olá, Joana! 👋");
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 18 — contato sem nome recebe a PERGUNTA, nao o menu
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 18 — o pedido de nome no primeiro contato");

var telefoneSemNome = $"5538000{++sequencial:D6}";
Guid conversaSemNome;

await using (var db = new CrmDbContext(opcoes))
{
    conversaSemNome = await NovoRecebimento(db).ReceberAsync(
        tenantId,
        new MensagemDoCanal(telefoneSemNome, ".", $"{PrefixoDeTeste}bom dia",
            TipoMensagem.Texto, $"{PrefixoDeTeste}ext-{sequencial}-1"));
}

{
    var conversa = await RecarregarAsync(conversaSemNome);
    var textos = await TextosDaConversaAsync(conversaSemNome);

    Checar("a pergunta de nome saiu", textos.Any(t => t.Contains("como posso te chamar", StringComparison.OrdinalIgnoreCase)));
    Checar("o atendimento NAO abriu junto — so a pergunta saiu", !AbriuAtendimento(textos));
    Checar("AguardandoNome ligada", conversa.AguardandoNome);
    Checar("AguardandoTriagem ainda desligada", !conversa.AguardandoTriagem);
    Checar("o contato ficou carimbado como ja perguntado",
        (await ClienteDaConversaAsync(conversaSemNome)).NomePerguntadoEm is not null);
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 19 — a resposta vira nome, e o menu sai JA com ele
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 19 — nome capturado aparece nas boas-vindas");

await using (var db = new CrmDbContext(opcoes))
{
    await NovoRecebimento(db).ReceberAsync(
        tenantId,
        new MensagemDoCanal(telefoneSemNome, ".", "Joana Prado",
            TipoMensagem.Texto, $"{PrefixoDeTeste}ext-{sequencial}-2"));
}

{
    var conversa = await RecarregarAsync(conversaSemNome);
    var cliente = await ClienteDaConversaAsync(conversaSemNome);
    var textos = await TextosDaConversaAsync(conversaSemNome);

    Checar("o nome foi gravado no cadastro", cliente.Nome == "Joana Prado", cliente.Nome);
    Checar("AguardandoNome saiu", !conversa.AguardandoNome);
    Checar("AguardandoTriagem entrou", conversa.AguardandoTriagem);

    // O assert que prova o flush: se o nome tivesse ficado so no change tracker,
    // o disparador teria lido o "." do banco e a mensagem sairia sem nome.
    Checar("as boas-vindas sairam COM o nome novo",
        textos.Any(t => t.Contains("Joana Prado", StringComparison.Ordinal)));
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 20 — resposta que nao e nome nao vira nome, e ninguem fica sem menu
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 20 — \"quero uma certidão\" nao vira nome de cliente");

var telefoneRecusa = $"5538000{++sequencial:D6}";
Guid conversaRecusa;

await using (var db = new CrmDbContext(opcoes))
{
    conversaRecusa = await NovoRecebimento(db).ReceberAsync(
        tenantId,
        new MensagemDoCanal(telefoneRecusa, "🔥", $"{PrefixoDeTeste}oi",
            TipoMensagem.Texto, $"{PrefixoDeTeste}ext-{sequencial}-1"));
}

await using (var db = new CrmDbContext(opcoes))
{
    await NovoRecebimento(db).ReceberAsync(
        tenantId,
        new MensagemDoCanal(telefoneRecusa, "🔥", "quero uma certidão",
            TipoMensagem.Texto, $"{PrefixoDeTeste}ext-{sequencial}-2"));
}

{
    var conversa = await RecarregarAsync(conversaRecusa);
    var cliente = await ClienteDaConversaAsync(conversaRecusa);
    var textos = await TextosDaConversaAsync(conversaRecusa);

    Checar("o pedido NAO virou nome do cliente", cliente.Nome == "🔥", cliente.Nome);
    Checar("mesmo assim o atendimento abriu — ninguem fica sem resposta", AbriuAtendimento(textos));
    Checar("AguardandoTriagem entrou", conversa.AguardandoTriagem);
    Checar("AguardandoNome saiu", !conversa.AguardandoNome);
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 21 — "menu" no lugar do nome nao deixa a flag presa
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 21 — \"menu\" cancela o pedido de nome");

var telefoneMenu = $"5538000{++sequencial:D6}";
Guid conversaMenu;

await using (var db = new CrmDbContext(opcoes))
{
    conversaMenu = await NovoRecebimento(db).ReceberAsync(
        tenantId,
        new MensagemDoCanal(telefoneMenu, "...", $"{PrefixoDeTeste}ola",
            TipoMensagem.Texto, $"{PrefixoDeTeste}ext-{sequencial}-1"));
}

await using (var db = new CrmDbContext(opcoes))
{
    await NovoRecebimento(db).ReceberAsync(
        tenantId,
        new MensagemDoCanal(telefoneMenu, "...", "menu",
            TipoMensagem.Texto, $"{PrefixoDeTeste}ext-{sequencial}-2"));
}

{
    var conversa = await RecarregarAsync(conversaMenu);

    Checar("ReabrirDoZeroAsync limpou AguardandoNome", !conversa.AguardandoNome);
    Checar("e reabriu a triagem normalmente", conversa.AguardandoTriagem);
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 22 — O CAMINHO FELIZ: com nome no perfil, nada muda
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 22 — perfil com nome nao ve diferenca nenhuma");

var telefoneComNome = $"5538000{++sequencial:D6}";
Guid conversaComNome;

await using (var db = new CrmDbContext(opcoes))
{
    conversaComNome = await NovoRecebimento(db).ReceberAsync(
        tenantId,
        new MensagemDoCanal(telefoneComNome, "Roberto Alves", $"{PrefixoDeTeste}bom dia",
            TipoMensagem.Texto, $"{PrefixoDeTeste}ext-{sequencial}-1"));
}

{
    var conversa = await RecarregarAsync(conversaComNome);
    var cliente = await ClienteDaConversaAsync(conversaComNome);
    var textos = await TextosDaConversaAsync(conversaComNome);

    Checar("o atendimento abriu de primeira, sem turno extra", AbriuAtendimento(textos));
    Checar("nenhuma pergunta de nome saiu",
        !textos.Any(t => t.Contains("como posso te chamar", StringComparison.OrdinalIgnoreCase)));
    Checar("AguardandoNome nunca ligou", !conversa.AguardandoNome);
    Checar("AguardandoTriagem ligada, como sempre foi", conversa.AguardandoTriagem);
    Checar("o contato nao foi carimbado", cliente.NomePerguntadoEm is null);
    Checar("o nome do perfil foi para o cadastro", cliente.Nome == "Roberto Alves");
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 23 — sem texto do gatilho, cai nas boas-vindas na mesma passada;
//            e quem ja foi perguntado nao e perguntado de novo
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 23 — gatilho desativado nao deixa o contato mudo");

await using (var db = new CrmDbContext(opcoes))
{
    await db.MensagensAutomaticas
        .Where(m => m.TenantId == tenantId && m.Gatilho == GatilhoDeMensagem.PedidoDeNome)
        .ExecuteUpdateAsync(s => s.SetProperty(m => m.Ativo, false));
}

var telefoneSemRegra = $"5538000{++sequencial:D6}";
Guid conversaSemRegra;

await using (var db = new CrmDbContext(opcoes))
{
    conversaSemRegra = await NovoRecebimento(db).ReceberAsync(
        tenantId,
        new MensagemDoCanal(telefoneSemRegra, ".", $"{PrefixoDeTeste}bom dia",
            TipoMensagem.Texto, $"{PrefixoDeTeste}ext-{sequencial}-1"));
}

{
    var conversa = await RecarregarAsync(conversaSemRegra);
    var textos = await TextosDaConversaAsync(conversaSemRegra);

    Checar("sem texto do gatilho, o atendimento abriu do mesmo jeito", AbriuAtendimento(textos));
    Checar("AguardandoNome nao ficou ligada sem pergunta nenhuma", !conversa.AguardandoNome);
    Checar("AguardandoTriagem entrou normalmente", conversa.AguardandoTriagem);
    Checar("e o contato NAO foi carimbado — a pergunta nunca saiu",
        (await ClienteDaConversaAsync(conversaSemRegra)).NomePerguntadoEm is null);
}

await using (var db = new CrmDbContext(opcoes))
{
    await db.MensagensAutomaticas
        .Where(m => m.TenantId == tenantId && m.Gatilho == GatilhoDeMensagem.PedidoDeNome)
        .ExecuteUpdateAsync(s => s.SetProperty(m => m.Ativo, true));
}

// Quem ignorou a pergunta uma vez nao e interrogado de novo no atendimento
// seguinte — e por isso que NomePerguntadoEm existe.
await using (var db = new CrmDbContext(opcoes))
{
    await db.Conversas.Where(c => c.Id == conversaRecusa)
        .ExecuteUpdateAsync(s => s
            .SetProperty(c => c.Status, StatusConversa.Encerrada)
            .SetProperty(c => c.EncerradaEm, DateTime.UtcNow));
}

Guid segundaConversa;

await using (var db = new CrmDbContext(opcoes))
{
    segundaConversa = await NovoRecebimento(db).ReceberAsync(
        tenantId,
        new MensagemDoCanal(telefoneRecusa, "🔥", $"{PrefixoDeTeste}voltei",
            TipoMensagem.Texto, $"{PrefixoDeTeste}ext-{sequencial}-9"));
}

{
    var textos = await TextosDaConversaAsync(segundaConversa);

    Checar("atendimento novo, e nao a conversa encerrada", segundaConversa != conversaRecusa);
    Checar("NAO repergunta o nome a quem ja foi perguntado",
        !textos.Any(t => t.Contains("como posso te chamar", StringComparison.OrdinalIgnoreCase)));
    Checar("e o atendimento abriu direto", AbriuAtendimento(textos));
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 24 — TelefoneDeContato e ParserDeListaDeContatos, logica pura (sem banco)
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 24 — importacao de contatos: telefone e parser, sem banco");

// ParaE164Basico: a regra de sempre, sem checagem de DDD — o cadastro manual
// continua usando ela sem mudar nem um bit de comportamento.
Checar("ParaE164Basico acrescenta 55 a numero de 11 digitos",
    TelefoneDeContato.ParaE164Basico("38999000045") == "5538999000045");
Checar("ParaE164Basico acrescenta 55 a numero de 10 digitos",
    TelefoneDeContato.ParaE164Basico("3832000045") == "553832000045");
Checar("ParaE164Basico aceita DDD que nao existe (nao valida DDD)",
    TelefoneDeContato.ParaE164Basico("5500999000045") == "5500999000045");
Checar("ParaE164Basico recusa numero curto demais", TelefoneDeContato.ParaE164Basico("123") is null);
Checar("ParaE164Basico recusa vazio", TelefoneDeContato.ParaE164Basico("") is null);
Checar("ParaE164Basico recusa nulo", TelefoneDeContato.ParaE164Basico(null) is null);

// Interpretar: mais desconfiado, so a importacao em lote usa.
var telefoneValidoComNono = TelefoneDeContato.Interpretar("(38) 99912-0045");
Checar("Interpretar aceita 13 digitos com DDD valido",
    telefoneValidoComNono.Valido && telefoneValidoComNono.E164 == "5538999000045");

var telefoneFixo = TelefoneDeContato.Interpretar("38 3212-0045");
Checar("Interpretar aceita 12 digitos com assinante comecando em 2-5 como fixo, ja completo",
    telefoneFixo.Valido && telefoneFixo.E164 == "553832000045");

var telefoneCelularLegado = TelefoneDeContato.Interpretar("38 98179-660");
Checar("Interpretar insere o nono em 12 digitos com assinante comecando em 6-9 (celular legado)",
    telefoneCelularLegado.Valido && telefoneCelularLegado.E164 == "5538998000660");

var telefoneAmbiguo = TelefoneDeContato.Interpretar("38 01234567");
Checar("Interpretar marca Ambiguo so quando o digito do assinante foge de 2-9 (nao deveria acontecer no Brasil)",
    telefoneAmbiguo.Situacao == SituacaoDoTelefone.Ambiguo && telefoneAmbiguo.E164 is null);

var telefoneComDddInvalido = TelefoneDeContato.Interpretar("5500999000045");
Checar("Interpretar recusa DDD que nao existe",
    telefoneComDddInvalido.Situacao == SituacaoDoTelefone.DddInvalido && telefoneComDddInvalido.E164 is null);

var telefoneComFormatoInvalido = TelefoneDeContato.Interpretar("123");
Checar("Interpretar recusa numero curto demais",
    telefoneComFormatoInvalido.Situacao == SituacaoDoTelefone.FormatoInvalido);

var telefoneInternacional = TelefoneDeContato.Interpretar("+44 20 7946 0958");
Checar("Interpretar aceita numero internacional (12 digitos, nao comeca com 55) sem exigir DDD",
    telefoneInternacional.Valido && telefoneInternacional.E164 == "442079460958");

// ParserDeListaDeContatos: separador, cabecalho em qualquer ordem, sem cabecalho.
var linhasComCabecalho = ParserDeListaDeContatos.Analisar(
    "nome,telefone\nMaria Silva,(38) 99999-0001\nJoao Souza,(38) 98888-0002");
Checar("cabecalho nome,telefone: duas linhas de contato (cabecalho nao vira contato)",
    linhasComCabecalho.Count == 2);
Checar("cabecalho nome,telefone: nome na coluna certa", linhasComCabecalho[0].Nome == "Maria Silva");
Checar("cabecalho nome,telefone: telefone na coluna certa",
    linhasComCabecalho[0].TelefoneBruto == "(38) 99999-0001");

var linhasComCabecalhoInvertido = ParserDeListaDeContatos.Analisar(
    "telefone;nome\n(38) 99999-0001;Maria Silva");
Checar("cabecalho invertido (telefone antes do nome) ainda acha as colunas certas",
    linhasComCabecalhoInvertido[0].Nome == "Maria Silva"
    && linhasComCabecalhoInvertido[0].TelefoneBruto == "(38) 99999-0001");

var linhasSemCabecalho = ParserDeListaDeContatos.Analisar(
    "Maria Silva\t(38) 99999-0001\nJoao Souza\t(38) 98888-0002");
Checar("sem cabecalho, ordem padrao nome-telefone: duas linhas", linhasSemCabecalho.Count == 2);
Checar("sem cabecalho: segunda linha lida certa", linhasSemCabecalho[1].Nome == "Joao Souza");

var linhasSoTelefone = ParserDeListaDeContatos.Analisar("(38) 99999-0001\n(38) 98888-0002");
Checar("linha com uma coluna so: e o telefone, sem adivinhar nome",
    linhasSoTelefone[0].Nome is null && linhasSoTelefone[0].TelefoneBruto == "(38) 99999-0001");

Checar("texto so com linhas vazias nao gera contato nenhum",
    ParserDeListaDeContatos.Analisar("\n\n  \n").Count == 0);

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 25 — Human Takeover (BUG-ATD-01): pedir humano, assumir, responder,
// liberar, transferir — a IA nunca volta a responder em nenhum desses pontos.
// A VIRADA CRITICA e o Liberar: AtendenteId volta a nulo, mas
// EmAtendimentoHumano tem que continuar true.
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 25 — Human Takeover: pedir humano -> assumir -> liberar -> transferir, IA sempre muda");

Guid conversa25;

await using (var db = new CrmDbContext(opcoes))
{
    var criada = await NovaConversaService(db, atendenteId, atendenteNome).CriarAsync(
        new CriarConversaRequest(await NovoClienteAsync("Human Takeover"), null, null, "Oi"));

    conversa25 = criada.Id;

    // Pula direto para o estado "ja passou da triagem, IA esta ouvindo" —
    // o caminho ate aqui ja e coberto pelos testes 1-3.
    var c = await db.Conversas.FirstAsync(x => x.Id == conversa25);
    c.AguardandoTriagem = false;
    c.SetorId = setores[0].Id;
    c.AguardandoRespostaInteligente = true;
    await db.SaveChangesAsync();
}

// 1) Cliente pede humano — a fila humana comeca AQUI, antes de qualquer
// atendente assumir.
await using (var db = new CrmDbContext(opcoes))
{
    await NovaMensagemService(db, atendenteId, atendenteNome).SimularRecebimentoAsync(
        conversa25, new SimularMensagemDeClienteRequest("quero falar com um atendente"));
}

await using (var db = new CrmDbContext(opcoes))
{
    var c = await db.Conversas.AsNoTracking().FirstAsync(x => x.Id == conversa25);
    Checar("pedido de humano liga EmAtendimentoHumano", c.EmAtendimentoHumano);
    Checar("pedido de humano NAO assume sozinho (fila humana, sem dono ainda)", c.AtendenteId is null);
    Checar("AguardandoRespostaInteligente desligou (EscalarAsync)", !c.AguardandoRespostaInteligente);
}

// 2) Mensagem do cliente NA FILA HUMANA (sem atendente ainda) — automacao
// tem que ficar muda.
var automaticasAntesDaFila = (await FioAsync(conversa25)).Count(m => m.Automatica);

await using (var db = new CrmDbContext(opcoes))
{
    await NovaMensagemService(db, atendenteId, atendenteNome).SimularRecebimentoAsync(
        conversa25, new SimularMensagemDeClienteRequest("alguem ai?"));
}

Checar("nenhuma mensagem automatica nova enquanto espera na fila humana",
    (await FioAsync(conversa25)).Count(m => m.Automatica) == automaticasAntesDaFila);

// 3) Assumir.
await using (var db = new CrmDbContext(opcoes))
{
    var resumo = await NovaConversaService(db, atendenteId, atendenteNome).AssumirAsync(conversa25);
    Checar("assumir preenche AtendenteId", resumo.AtendenteId == atendenteId);
}

await using (var db = new CrmDbContext(opcoes))
{
    var c = await db.Conversas.AsNoTracking().FirstAsync(x => x.Id == conversa25);
    Checar("EmAtendimentoHumano continua true depois de assumir", c.EmAtendimentoHumano);
}

// 4) Cliente escreve com atendente na conversa — automacao muda.
var automaticasAntesDoAssumir = (await FioAsync(conversa25)).Count(m => m.Automatica);

await using (var db = new CrmDbContext(opcoes))
{
    await NovaMensagemService(db, atendenteId, atendenteNome).SimularRecebimentoAsync(
        conversa25, new SimularMensagemDeClienteRequest("uma duvida rapida"));
}

Checar("nenhuma mensagem automatica nova com atendente ja atribuido",
    (await FioAsync(conversa25)).Count(m => m.Automatica) == automaticasAntesDoAssumir);

// 5) Liberar — A VIRADA CRITICA da hipotese original.
await using (var db = new CrmDbContext(opcoes))
{
    await NovaConversaService(db, atendenteId, atendenteNome).LiberarAsync(conversa25);
}

await using (var db = new CrmDbContext(opcoes))
{
    var c = await db.Conversas.AsNoTracking().FirstAsync(x => x.Id == conversa25);
    Checar("Liberar zera AtendenteId", c.AtendenteId is null);
    Checar("Liberar NAO desliga EmAtendimentoHumano — bug original seria exatamente isto",
        c.EmAtendimentoHumano);
}

// 6) Cliente escreve com a conversa liberada (fila humana de novo, sem
// dono) — automacao continua muda.
var automaticasAntesDoLiberar = (await FioAsync(conversa25)).Count(m => m.Automatica);

await using (var db = new CrmDbContext(opcoes))
{
    await NovaMensagemService(db, atendenteId, atendenteNome).SimularRecebimentoAsync(
        conversa25, new SimularMensagemDeClienteRequest("ainda estou aqui"));
}

Checar("nenhuma mensagem automatica nova depois de Liberar (a prova central do BUG-ATD-01)",
    (await FioAsync(conversa25)).Count(m => m.Automatica) == automaticasAntesDoLiberar);

// 7) Assume de novo e transfere.
await using (var db = new CrmDbContext(opcoes))
{
    await NovaConversaService(db, atendenteId, atendenteNome).AssumirAsync(conversa25);
    var transferida = await NovaConversaService(db, atendenteId, atendenteNome).TransferirAsync(
        conversa25, new TransferirConversaRequest(outroAtendenteId, null));

    Checar("transferir muda o responsavel", transferida.AtendenteId == outroAtendenteId);
}

await using (var db = new CrmDbContext(opcoes))
{
    var c = await db.Conversas.AsNoTracking().FirstAsync(x => x.Id == conversa25);
    Checar("EmAtendimentoHumano continua true depois de transferir", c.EmAtendimentoHumano);
}

// 8) Cliente escreve depois da transferencia — automacao continua muda.
var automaticasAntesDaTransferencia = (await FioAsync(conversa25)).Count(m => m.Automatica);

await using (var db = new CrmDbContext(opcoes))
{
    await NovaMensagemService(db, outroAtendenteId, outroAtendenteNome).SimularRecebimentoAsync(
        conversa25, new SimularMensagemDeClienteRequest("obrigado"));
}

Checar("nenhuma mensagem automatica nova depois de Transferir",
    (await FioAsync(conversa25)).Count(m => m.Automatica) == automaticasAntesDaTransferencia);

await using (var db = new CrmDbContext(opcoes))
{
    var c = await db.Conversas.AsNoTracking().FirstAsync(x => x.Id == conversa25);
    Checar("MensagensNaoLidas segue contando as mensagens do cliente normalmente",
        c.MensagensNaoLidas >= 4, $"MensagensNaoLidas={c.MensagensNaoLidas}");
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 26 — Human Takeover: responder direto da fila (auto-assume) tambem
// liga o modo humano; encerrar desliga; reabrir religa.
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 26 — Human Takeover: auto-assume ao responder da fila; encerrar/reabrir");

Guid conversa26;

await using (var db = new CrmDbContext(opcoes))
{
    var criada = await NovaConversaService(db, atendenteId, atendenteNome).CriarAsync(
        new CriarConversaRequest(await NovoClienteAsync("Auto-assume"), setores[0].Id, null, "Oi"));

    conversa26 = criada.Id;

    Checar("conversa nasce sem atendente (fila)", criada.AtendenteId is null);
}

await using (var db = new CrmDbContext(opcoes))
{
    var c = await db.Conversas.AsNoTracking().FirstAsync(x => x.Id == conversa26);
    Checar("conversa nasce fora do modo humano (automatica)", !c.EmAtendimentoHumano);
}

// Responde SEM clicar em Assumir — MensagemService.EnviarAsync assume
// implicitamente (PrepararEnvioAsync reusa AssumirAsync).
await using (var db = new CrmDbContext(opcoes))
{
    var resposta = await NovaMensagemService(db, atendenteId, atendenteNome).EnviarAsync(
        conversa26, new EnviarMensagemRequest("Ja te ajudo!"));

    Checar("mensagem manual saiu como Atendente", resposta.AutorTipo == AutorTipo.Atendente);
}

await using (var db = new CrmDbContext(opcoes))
{
    var c = await db.Conversas.AsNoTracking().FirstAsync(x => x.Id == conversa26);
    Checar("responder da fila assume implicitamente", c.AtendenteId == atendenteId);
    Checar("responder da fila liga EmAtendimentoHumano (mesmo sem Assumir explicito)",
        c.EmAtendimentoHumano);
}

var automaticasAntesDoAutoAssume = (await FioAsync(conversa26)).Count(m => m.Automatica);

await using (var db = new CrmDbContext(opcoes))
{
    await NovaMensagemService(db, atendenteId, atendenteNome).SimularRecebimentoAsync(
        conversa26, new SimularMensagemDeClienteRequest("perfeito, obrigado"));
}

Checar("nenhuma automacao depois de responder direto da fila",
    (await FioAsync(conversa26)).Count(m => m.Automatica) == automaticasAntesDoAutoAssume);

// Encerra e confere que o modo humano desliga junto com o resto do ciclo.
await using (var db = new CrmDbContext(opcoes))
{
    await NovaConversaService(db, atendenteId, atendenteNome).EncerrarAsync(
        conversa26, new EncerrarConversaRequest(null));
}

await using (var db = new CrmDbContext(opcoes))
{
    var c = await db.Conversas.AsNoTracking().FirstAsync(x => x.Id == conversa26);
    Checar("EncerrarAsync desliga EmAtendimentoHumano", !c.EmAtendimentoHumano);
    Checar("EncerrarAsync tambem zera as flags Aguardando* (fecha a divergencia com EncerrarPeloCliente)",
        !c.AguardandoTriagem && !c.AguardandoEscolhaDeServico && !c.AguardandoRespostaInteligente
        && !c.AguardandoMenuDeContinuacao && !c.AguardandoNome && c.TentativasRespostaInteligente == 0);
}

// Reabre — religa o modo humano, atribuido a quem reabriu.
await using (var db = new CrmDbContext(opcoes))
{
    var reaberta = await NovaConversaService(db, outroAtendenteId, outroAtendenteNome).ReabrirAsync(conversa26);
    Checar("reabrir atribui a quem reabriu", reaberta.AtendenteId == outroAtendenteId);
}

await using (var db = new CrmDbContext(opcoes))
{
    var c = await db.Conversas.AsNoTracking().FirstAsync(x => x.Id == conversa26);
    Checar("reabrir religa EmAtendimentoHumano", c.EmAtendimentoHumano);
}

var automaticasAntesDaReabertura = (await FioAsync(conversa26)).Count(m => m.Automatica);

await using (var db = new CrmDbContext(opcoes))
{
    await NovaMensagemService(db, outroAtendenteId, outroAtendenteNome).SimularRecebimentoAsync(
        conversa26, new SimularMensagemDeClienteRequest("voltei"));
}

Checar("nenhuma automacao depois de reabrir manualmente",
    (await FioAsync(conversa26)).Count(m => m.Automatica) == automaticasAntesDaReabertura);

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 26B — Assumir idempotente repara a invariante defensivamente.
//
// Nenhum fluxo suportado de UI/API encontrado cria uma conversa ativa com
// AtendenteId preenchido e EmAtendimentoHumano=false. A origem observada em
// TESTE, portanto, nao e atribuida aqui sem evidencia. O estado e montado
// diretamente apenas para provar que uma linha inconsistente preexistente e
// reparada sem duplicar a mensagem de sistema da assuncao normal.
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 26B — Assumir idempotente repara modo humano sem duplicar mensagem");

Guid conversa26B;

await using (var db = new CrmDbContext(opcoes))
{
    var criada = await NovaConversaService(db, atendenteId, atendenteNome).CriarAsync(
        new CriarConversaRequest(await NovoClienteAsync("Assumir idempotente"), setores[0].Id, null, "Oi"));

    conversa26B = criada.Id;

    var antes = await db.Conversas.AsNoTracking().FirstAsync(c => c.Id == conversa26B);
    Checar("cenario normal comeca sem responsavel", antes.AtendenteId is null);
    Checar("cenario normal comeca fora do modo humano", !antes.EmAtendimentoHumano);
}

await using (var db = new CrmDbContext(opcoes))
{
    var assumida = await NovaConversaService(db, atendenteId, atendenteNome).AssumirAsync(conversa26B);
    var depois = await db.Conversas.AsNoTracking().FirstAsync(c => c.Id == conversa26B);

    Checar("assuncao normal continua preenchendo AtendenteId", assumida.AtendenteId == atendenteId);
    Checar("assuncao normal continua ligando EmAtendimentoHumano", depois.EmAtendimentoHumano);
}

int mensagensDeAssuncaoAntesDoReparo;

await using (var db = new CrmDbContext(opcoes))
{
    mensagensDeAssuncaoAntesDoReparo = await db.Mensagens.AsNoTracking()
        .CountAsync(m => m.ConversaId == conversa26B
                      && m.AutorTipo == AutorTipo.Sistema
                      && m.Conteudo.StartsWith("Conversa assumida por "));

    var inconsistente = await db.Conversas.FirstAsync(c => c.Id == conversa26B);
    inconsistente.EmAtendimentoHumano = false;
    await db.SaveChangesAsync();

    Checar("fixture preserva AtendenteId do usuario atual", inconsistente.AtendenteId == atendenteId);
    Checar("fixture monta explicitamente a inconsistencia", !inconsistente.EmAtendimentoHumano);
}

await using (var db = new CrmDbContext(opcoes))
{
    var reparada = await NovaConversaService(db, atendenteId, atendenteNome).AssumirAsync(conversa26B);
    var depois = await db.Conversas.AsNoTracking().FirstAsync(c => c.Id == conversa26B);
    var mensagensDeAssuncaoDepoisDoReparo = await db.Mensagens.AsNoTracking()
        .CountAsync(m => m.ConversaId == conversa26B
                      && m.AutorTipo == AutorTipo.Sistema
                      && m.Conteudo.StartsWith("Conversa assumida por "));

    Checar("ramo idempotente preserva o responsavel", reparada.AtendenteId == atendenteId);
    Checar("ramo idempotente repara EmAtendimentoHumano", depois.EmAtendimentoHumano);
    Checar("ramo idempotente nao duplica mensagem de assuncao",
        mensagensDeAssuncaoDepoisDoReparo == mensagensDeAssuncaoAntesDoReparo,
        $"antes={mensagensDeAssuncaoAntesDoReparo}; depois={mensagensDeAssuncaoDepoisDoReparo}");
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 27 — Human Takeover: zero chamada ao orquestrador de IA em modo
// humano, com a IA REALMENTE armada (nao so desligada) — e a mesma regra
// vale nos dois caminhos de entrada (WhatsApp real x simulacao).
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 27 — Human Takeover: IA armada nunca e chamada em modo humano (simulacao e WhatsApp real)");

Guid conversa27Sim;

await using (var db = new CrmDbContext(opcoes))
{
    var criada = await NovaConversaService(db, atendenteId, atendenteNome).CriarAsync(
        new CriarConversaRequest(await NovoClienteAsync("IA armada simulacao"), setores[0].Id, null, "Oi"));

    conversa27Sim = criada.Id;

    var c = await db.Conversas.FirstAsync(x => x.Id == conversa27Sim);
    c.AtendenteId = atendenteId;
    c.EmAtendimentoHumano = true;
    await db.SaveChangesAsync();
}

var orquestradorSim = new OrquestradorContador();

await using (var db = new CrmDbContext(opcoes))
{
    await NovaMensagemServiceComIaArmada(db, atendenteId, atendenteNome, orquestradorSim).SimularRecebimentoAsync(
        conversa27Sim, new SimularMensagemDeClienteRequest("me ajuda com uma duvida qualquer"));
}

Checar("simulacao: orquestrador de IA nao foi chamado em modo humano", orquestradorSim.Chamadas == 0,
    $"Chamadas={orquestradorSim.Chamadas}");

await using (var db = new CrmDbContext(opcoes))
{
    var count = await db.ChamadasDeIa.AsNoTracking().CountAsync(c => c.ConversaId == conversa27Sim);
    Checar("simulacao: nenhuma linha em ChamadasDeIa para esta conversa", count == 0, $"linhas={count}");
}

// Mesmo cenario, mas pelo WEBHOOK real — nao aceito correcao que funcione so
// na simulacao.
Guid clienteWhatsAppId;
var telefoneWhatsApp27 = "55380009" + Random.Shared.Next(10000, 99999);

await using (var db = new CrmDbContext(opcoes))
{
    clienteWhatsAppId = await NovoClienteAsync("IA armada webhook");

    var criada = await NovaConversaService(db, atendenteId, atendenteNome).CriarAsync(
        new CriarConversaRequest(clienteWhatsAppId, setores[0].Id, null, "Oi"));

    var c = await db.Conversas.FirstAsync(x => x.Id == criada.Id);
    c.AtendenteId = atendenteId;
    c.EmAtendimentoHumano = true;
    c.Canal = CanalOrigem.WhatsApp;

    // O webhook resolve a conversa pelo TELEFONE do cliente, nao pelo Id —
    // precisa bater com MensagemDoCanal.TelefoneE164 abaixo.
    await using var dbCliente = new CrmDbContext(opcoes);
    var cliente = await dbCliente.Clientes.FirstAsync(x => x.Id == clienteWhatsAppId);
    cliente.TelefoneWhatsapp = telefoneWhatsApp27;
    await dbCliente.SaveChangesAsync();

    await db.SaveChangesAsync();
}

var orquestradorWebhook = new OrquestradorContador();

await using (var db = new CrmDbContext(opcoes))
{
    await NovoRecebimentoComIaArmada(db, orquestradorWebhook, new NotificadorSilencioso()).ReceberAsync(
        tenantId,
        new MensagemDoCanal(telefoneWhatsApp27, null, "me ajuda com uma duvida qualquer",
            TipoMensagem.Texto, $"wamid.testeatd01.{Guid.NewGuid():N}"));
}

Checar("webhook real: orquestrador de IA nao foi chamado em modo humano", orquestradorWebhook.Chamadas == 0,
    $"Chamadas={orquestradorWebhook.Chamadas}");

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 28 — Human Takeover: a mensagem continua persistida, no fio,
// atualizando contadores e chegando por SignalR mesmo em modo humano — o
// gate so silencia a AUTOMACAO, nunca a observabilidade normal da conversa.
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 28 — Human Takeover: persistencia e SignalR continuam normais em modo humano");

Guid conversa28;

await using (var db = new CrmDbContext(opcoes))
{
    var criada = await NovaConversaService(db, atendenteId, atendenteNome).CriarAsync(
        new CriarConversaRequest(await NovoClienteAsync("Observabilidade"), setores[0].Id, null, "Oi"));

    conversa28 = criada.Id;

    var c = await db.Conversas.FirstAsync(x => x.Id == conversa28);
    c.AtendenteId = atendenteId;
    c.EmAtendimentoHumano = true;
    await db.SaveChangesAsync();
}

var naoLidasAntes = 0;
await using (var db = new CrmDbContext(opcoes))
{
    naoLidasAntes = (await db.Conversas.AsNoTracking().FirstAsync(x => x.Id == conversa28)).MensagensNaoLidas;
}

var notificador28 = new NotificadorContador();

await using (var db = new CrmDbContext(opcoes))
{
    var ctx = new ContextoFixo(tenantId, atendenteId, atendenteNome, PerfilDeAtendente.Administrador);
    var servico = new MensagemService(
        db, ctx, NovaConversaService(db, atendenteId, atendenteNome),
        notificador28,
        new DisparadorDeMensagemAutomatica(db, NullLogger<DisparadorDeMensagemAutomatica>.Instance),
        new CanalSilencioso(), new AnexosEmMemoria(),
        new RespostaInteligenteService(
            db, new OrquestradorNuncaChamado(), Options.Create(new OpcoesDaIA { Habilitada = false }),
            NullLogger<RespostaInteligenteService>.Instance),
        Options.Create(new OpcoesDaIA { Habilitada = false }),
        NullLogger<MensagemService>.Instance);

    await servico.SimularRecebimentoAsync(conversa28, new SimularMensagemDeClienteRequest("teste de observabilidade"));
}

Checar("SignalR (MensagemRecebidaAsync) continua disparando em modo humano", notificador28.MensagensRecebidas == 1,
    $"MensagensRecebidas={notificador28.MensagensRecebidas}");

await using (var db = new CrmDbContext(opcoes))
{
    var c = await db.Conversas.AsNoTracking().FirstAsync(x => x.Id == conversa28);
    Checar("MensagensNaoLidas incrementou normalmente", c.MensagensNaoLidas == naoLidasAntes + 1,
        $"antes={naoLidasAntes} depois={c.MensagensNaoLidas}");
}

var fio28 = await FioAsync(conversa28);
Checar("mensagem do cliente esta no fio", fio28.Any(m => m.Autor == AutorTipo.Cliente && m.Conteudo == "teste de observabilidade"));
Checar("nenhuma mensagem automatica foi adicionada", fio28.Count(m => m.Automatica) == 0);

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 29 — Human Takeover: novo atendimento depois de encerrado volta a
// nascer automatico (nao herda o modo humano do atendimento anterior).
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 29 — Human Takeover: atendimento novo depois de encerrado volta ao automatico");

Guid clienteQueVoltaAtd01;
var telefoneQueVolta = "55380009" + Random.Shared.Next(10000, 99999);

await using (var db = new CrmDbContext(opcoes))
{
    clienteQueVoltaAtd01 = await NovoClienteAsync("Volta depois de encerrado");

    var criada = await NovaConversaService(db, atendenteId, atendenteNome).CriarAsync(
        new CriarConversaRequest(clienteQueVoltaAtd01, setores[0].Id, null, "Oi"));

    var c = await db.Conversas.FirstAsync(x => x.Id == criada.Id);
    c.AtendenteId = atendenteId;
    c.EmAtendimentoHumano = true;
    c.Canal = CanalOrigem.WhatsApp;
    await db.SaveChangesAsync();

    await using var dbCliente = new CrmDbContext(opcoes);
    var cliente = await dbCliente.Clientes.FirstAsync(x => x.Id == clienteQueVoltaAtd01);
    cliente.TelefoneWhatsapp = telefoneQueVolta;
    await dbCliente.SaveChangesAsync();

    await NovaConversaService(db, atendenteId, atendenteNome).EncerrarAsync(
        criada.Id, new EncerrarConversaRequest(null));
}

// Mesmo telefone, mensagem nova, pelo WEBHOOK — ResolverConversaAsync nao
// reaproveita conversa Encerrada, entao isto cria um atendimento NOVO.
await using (var db = new CrmDbContext(opcoes))
{
    var orquestrador = new OrquestradorContador();

    var novaConversaId = await NovoRecebimentoComIaArmada(db, orquestrador, new NotificadorSilencioso()).ReceberAsync(
        tenantId,
        new MensagemDoCanal(telefoneQueVolta, null, "Oi de novo", TipoMensagem.Texto, $"wamid.testeatd01.{Guid.NewGuid():N}"));

    await using var dbConfere = new CrmDbContext(opcoes);
    var nova = await dbConfere.Conversas.AsNoTracking().FirstAsync(x => x.Id == novaConversaId);

    Checar("atendimento novo nasce SEM modo humano (automatico)", !nova.EmAtendimentoHumano);
    Checar("atendimento novo nasce aguardando triagem (boas-vindas normais)", nova.AguardandoTriagem);
    Checar("e' de fato uma conversa NOVA, nao a antiga reaproveitada",
        nova.Id != Guid.Empty && await dbConfere.Conversas.CountAsync(x => x.ClienteId == clienteQueVoltaAtd01) == 2);
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// TESTE 30 — Human Takeover: Fase 2 da inatividade nao encerra uma conversa
// que esta em modo humano (SS9.2 do plano) — nem quem assumiu, nem quem esta
// na fila humana depois de liberada.
// ---------------------------------------------------------------------------

Console.WriteLine("TESTE 30 — Human Takeover: varredura de inatividade (fase 2) nao encerra conversa em modo humano");

await using (var db = new CrmDbContext(opcoes))
{
    await db.Tenants.Where(t => t.Id == tenantId)
        .ExecuteUpdateAsync(s => s
            .SetProperty(t => t.MinutosParaPerguntarEncerramento, 20)
            .SetProperty(t => t.MinutosParaEncerrarSemResposta, 30)
            .SetProperty(t => t.EncerramentoHoraInicial, (int?)null)
            .SetProperty(t => t.EncerramentoHoraFinal, (int?)null));
}

Guid conversa30Humana, conversa30Automatica;

await using (var db = new CrmDbContext(opcoes))
{
    var humana = await NovaConversaService(db, atendenteId, atendenteNome).CriarAsync(
        new CriarConversaRequest(await NovoClienteAsync("Inatividade em modo humano"), setores[0].Id, null, "Oi"));
    conversa30Humana = humana.Id;

    var automatica = await NovaConversaService(db, atendenteId, atendenteNome).CriarAsync(
        new CriarConversaRequest(await NovoClienteAsync("Inatividade automatica"), setores[0].Id, null, "Oi"));
    conversa30Automatica = automatica.Id;

    var agoraDoTeste = DateTime.UtcNow;

    // As duas ja tem a pergunta de encerramento pendente, vencida ha tempo —
    // so a diferenca entre elas e EmAtendimentoHumano.
    var ch = await db.Conversas.FirstAsync(x => x.Id == conversa30Humana);
    ch.AtendenteId = atendenteId;
    ch.EmAtendimentoHumano = true;
    ch.PerguntaDeEncerramentoEnviadaEm = agoraDoTeste.AddMinutes(-40);
    ch.UltimaMensagemEm = agoraDoTeste.AddMinutes(-40);

    var ca = await db.Conversas.FirstAsync(x => x.Id == conversa30Automatica);
    ca.PerguntaDeEncerramentoEnviadaEm = agoraDoTeste.AddMinutes(-40);
    ca.UltimaMensagemEm = agoraDoTeste.AddMinutes(-40);

    await db.SaveChangesAsync();
}

await using (var db = new CrmDbContext(opcoes))
{
    await NovoVarredorDeInatividade(db).ExecutarAsync();
}

await using (var db = new CrmDbContext(opcoes))
{
    var humana = await db.Conversas.AsNoTracking().FirstAsync(x => x.Id == conversa30Humana);
    var automatica = await db.Conversas.AsNoTracking().FirstAsync(x => x.Id == conversa30Automatica);

    Checar("conversa EM MODO HUMANO nao foi encerrada pela fase 2 da inatividade",
        humana.Status == StatusConversa.Ativa, $"Status={humana.Status}");
    Checar("conversa AUTOMATICA (controle) foi encerrada normalmente pela fase 2",
        automatica.Status == StatusConversa.Encerrada, $"Status={automatica.Status}");
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// Limpeza
// ---------------------------------------------------------------------------

Console.WriteLine();
Console.WriteLine($"Limpeza: {await LimparAsync()} registro(s) removidos.");

Console.WriteLine();
Console.WriteLine(problemas == 0 ? "TODOS OS TESTES PASSARAM." : $"{problemas} VERIFICACAO(OES) FALHARAM.");

return problemas == 0 ? 0 : 1;

/// <summary>
/// Remove tudo que este verificador cria — inclusive o que ele deixou em conversas
/// que NAO sao dele. Roda na entrada e na saida.
/// </summary>
/// <remarks>
/// Aqui SIM ha DELETE, e junto com verificar-concorrencia.cs e a unica parte do
/// repositorio que apaga alguma coisa. O produto nunca apaga conversa — mas o que
/// este arquivo cria nao e atendimento: e fixture.
///
/// ATENCAO ao segundo bloco. A varredura de prazo varre o TENANT INTEIRO, nao a
/// conversa do teste: enquanto a regra de teste esta ativa, qualquer conversa da
/// demonstracao que ja estivesse vencida tambem recebe aviso e tem o marcador
/// gravado. Uma limpeza que so olhasse os clientes de teste deixaria essas
/// mensagens no fio da demonstracao — e, pior, deixaria o marcador preenchido,
/// impedindo o aviso de VERDADE de sair quando o cartório parceiro ativar a regra.
///
/// Por isso a limpeza e por CONTEUDO, no tenant todo, e desfaz o marcador
/// exatamente nas conversas de onde removeu uma mensagem de teste.
/// </remarks>
async Task<int> LimparAsync()
{
    await using var db = new CrmDbContext(opcoes);

    // --- 1. Conversas do proprio verificador -------------------------------
    //
    // Por NOME **ou por TELEFONE**. O nome sozinho nao basta desde que os testes
    // passaram a criar contato pelo webhook real: ali quem escolhe o nome e o
    // pushName que o teste envia — e o caso interessante e justamente o pushName
    // INUTIL ("."), que nao carrega prefixo nenhum. Sem o corte por telefone, esses
    // clientes sobreviviam a limpeza, e a execucao seguinte reencontrava a conversa
    // velha em vez de abrir uma nova: os testes de primeiro contato passavam a
    // medir residuo da rodada anterior.
    //
    // 5538000...: faixa reservada a este arquivo, gerada so aqui.
    const string PrefixoDeTelefone = "5538000";

    var clientes = db.Clientes
        .Where(c => c.TenantId == tenantId
                 && (c.Nome.StartsWith(PrefixoDeTeste) || c.TelefoneWhatsapp.StartsWith(PrefixoDeTelefone)))
        .Select(c => c.Id);

    var conversas = db.Conversas.Where(c => clientes.Contains(c.ClienteId)).Select(c => c.Id);

    var total = await db.Mensagens.Where(x => conversas.Contains(x.ConversaId)).ExecuteDeleteAsync();
    total += await db.HistoricoEtapas.Where(x => conversas.Contains(x.ConversaId)).ExecuteDeleteAsync();
    total += await db.Conversas.Where(x => clientes.Contains(x.ClienteId)).ExecuteDeleteAsync();
    total += await db.Clientes
        .Where(x => x.TenantId == tenantId
                 && (x.Nome.StartsWith(PrefixoDeTeste) || x.TelefoneWhatsapp.StartsWith(PrefixoDeTelefone)))
        .ExecuteDeleteAsync();

    // --- 2. Respingo em conversas de terceiros ------------------------------
    var contaminadas = await db.Mensagens.AsNoTracking()
        .Where(m => m.TenantId == tenantId && m.Conteudo.StartsWith(PrefixoDeTeste))
        .Select(m => m.ConversaId)
        .Distinct()
        .ToListAsync();

    if (contaminadas.Count > 0)
    {
        total += await db.Mensagens
            .Where(m => m.TenantId == tenantId && m.Conteudo.StartsWith(PrefixoDeTeste))
            .ExecuteDeleteAsync();

        // Sem isto a conversa ficaria marcada como "ja avisada" para sempre, por
        // causa de um aviso que so existiu dentro de um teste.
        total += await db.Conversas
            .Where(c => contaminadas.Contains(c.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.AvisoDeVencimentoEnviadoEm, (DateTime?)null)
                .SetProperty(c => c.AvisoDeAlertaEnviadoEm, (DateTime?)null)
                .SetProperty(c => c.PerguntaDeEncerramentoEnviadaEm, (DateTime?)null), default);
    }

    // --- 3. Regras de teste. A configuracao do cartório parceiro nao e tocada. ----------
    total += await db.MensagensAutomaticas
        .Where(x => x.TenantId == tenantId && x.Conteudo.StartsWith(PrefixoDeTeste))
        .ExecuteDeleteAsync();

    // --- 4. Devolve a configuracao de encerramento como estava --------------
    // Os testes 9-15 ligam o recurso e mexem nos tempos. Sair daqui com o valor de
    // teste faria a base de desenvolvimento se comportar diferente da que o cartório parceiro
    // configurou — e a proxima pessoa a rodar a API veria conversa fechando sozinha
    // sem entender por que.
    await db.Tenants.Where(t => t.Id == tenantId)
        .ExecuteUpdateAsync(s => s
            .SetProperty(t => t.MinutosParaPerguntarEncerramento, configuracaoOriginal.Perguntar)
            .SetProperty(t => t.MinutosParaEncerrarSemResposta, configuracaoOriginal.Encerrar)
            .SetProperty(t => t.EncerramentoHoraInicial, configuracaoOriginal.HoraInicial)
            .SetProperty(t => t.EncerramentoHoraFinal, configuracaoOriginal.HoraFinal));

    // Garante que nenhum texto ficou desativado por um TESTE 15 interrompido.
    await db.MensagensAutomaticas
        .Where(m => m.TenantId == tenantId
                 && !m.Ativo
                 && (m.Gatilho == GatilhoDeMensagem.ConfirmacaoDeEncerramento
                  || m.Gatilho == GatilhoDeMensagem.EncerramentoPorInatividade))
        .ExecuteUpdateAsync(s => s.SetProperty(m => m.Ativo, true));

    return total;
}

VarredorDePrazos NovoVarredor(CrmDbContext db) =>
    new(db,
        new DisparadorDeMensagemAutomatica(db, NullLogger<DisparadorDeMensagemAutomatica>.Instance),
        new NotificadorSilencioso(),
        new CanalSilencioso(),
        NullLogger<VarredorDePrazos>.Instance);

VarredorDeInatividade NovoVarredorDeInatividade(CrmDbContext db) =>
    new(db,
        new DisparadorDeMensagemAutomatica(db, NullLogger<DisparadorDeMensagemAutomatica>.Instance),
        new NotificadorSilencioso(),
        new CanalSilencioso(),
        NullLogger<VarredorDeInatividade>.Instance);

// Variante de NovaMensagemService para os testes de Human Takeover
// (BUG-ATD-01): a IA fica REALMENTE "armada" (Habilitada=true, com uma chave
// falsa so para passar EstaConfigurada) em vez de desligada. Se o gate do
// modo humano falhar e a automacao tentar responder mesmo assim, o
// orquestrador passado (normalmente OrquestradorContador) e quem prova isso
// — nao dependemos de "a IA estava desligada de qualquer jeito" como o resto
// deste arquivo.
MensagemService NovaMensagemServiceComIaArmada(
    CrmDbContext db, Guid quem, string nome, IOrquestradorDeProvedores orquestrador)
{
    var ctx = new ContextoFixo(tenantId, quem, nome, PerfilDeAtendente.Administrador);

    var opcoesDaIA = Options.Create(new OpcoesDaIA
    {
        Habilitada = true,
        Anthropic = new OpcoesDoAnthropic { Habilitado = true, ApiKey = "chave-fake-armada-para-teste" }
    });

    var respostaInteligente = new RespostaInteligenteService(
        db, orquestrador, opcoesDaIA, NullLogger<RespostaInteligenteService>.Instance);

    return new MensagemService(
        db,
        ctx,
        NovaConversaService(db, quem, nome),
        new NotificadorSilencioso(),
        new DisparadorDeMensagemAutomatica(db, NullLogger<DisparadorDeMensagemAutomatica>.Instance),
        new CanalSilencioso(),
        new AnexosEmMemoria(),
        respostaInteligente,
        opcoesDaIA,
        NullLogger<MensagemService>.Instance);
}

// Mesma ideia de NovoRecebimento, mas com IA armada e notificador
// substituivel — usado pelos testes de Human Takeover que precisam espiar o
// que o SignalR receberia.
RecebimentoParaTeste NovoRecebimentoComIaArmada(
    CrmDbContext db, IOrquestradorDeProvedores orquestrador, INotificadorDeAtendimento notificador)
{
    var opcoesDaIA = Options.Create(new OpcoesDaIA
    {
        Habilitada = true,
        Anthropic = new OpcoesDoAnthropic { Habilitado = true, ApiKey = "chave-fake-armada-para-teste" }
    });

    var sinalizador = new SinalizadorDeEntradaWhatsapp();
    var anexos = new AnexosEmMemoria();
    var canal = new CanalSilencioso();
    var automaticas = new DisparadorDeMensagemAutomatica(db, NullLogger<DisparadorDeMensagemAutomatica>.Instance);
    var resposta = new RespostaInteligenteService(
        db, orquestrador, opcoesDaIA, NullLogger<RespostaInteligenteService>.Instance);

    return new RecebimentoParaTeste(
        new RecebimentoDeWhatsApp(
            db, anexos, notificador, sinalizador, NullLogger<RecebimentoDeWhatsApp>.Instance),
        new ProcessadorDeEntradaWhatsapp(
            db, automaticas, notificador, canal, resposta, opcoesDaIA,
            NullLogger<ProcessadorDeEntradaWhatsapp>.Instance));
}

// O SERVICO REAL do webhook, com os mesmos dubles silenciosos do resto do
// arquivo. Ate 2026-08-24 nenhum verificador exercitava este caminho —
// verificar-canal-whatsapp.cs para no ProcessadorDeWebhook, com um
// RecebimentoFake no lugar deste. Como o pedido de nome so acontece na PRIMEIRA
// mensagem de um contato novo, e isso e territorio exclusivo do webhook, cobrir
// de verdade custou estas dez linhas.
RecebimentoParaTeste NovoRecebimento(CrmDbContext db)
{
    var opcoesDaIA = Options.Create(new OpcoesDaIA { Habilitada = false });

    var notificador = new NotificadorSilencioso();
    var sinalizador = new SinalizadorDeEntradaWhatsapp();
    var automaticas = new DisparadorDeMensagemAutomatica(db, NullLogger<DisparadorDeMensagemAutomatica>.Instance);
    var resposta = new RespostaInteligenteService(
        db, new OrquestradorNuncaChamado(), opcoesDaIA, NullLogger<RespostaInteligenteService>.Instance);

    return new RecebimentoParaTeste(
        new RecebimentoDeWhatsApp(
            db, new AnexosEmMemoria(), notificador, sinalizador,
            NullLogger<RecebimentoDeWhatsApp>.Instance),
        new ProcessadorDeEntradaWhatsapp(
            db, automaticas, notificador, new CanalSilencioso(), resposta, opcoesDaIA,
            NullLogger<ProcessadorDeEntradaWhatsapp>.Instance));
}

// ---------------------------------------------------------------------------

file sealed class RecebimentoParaTeste(
    IRecebimentoDeWhatsApp recebimento,
    IProcessadorDeEntradaWhatsapp processador)
{
    public async Task<Guid> ReceberAsync(
        Guid tenantId,
        MensagemDoCanal mensagem,
        CancellationToken ct = default)
    {
        var conversaId = await recebimento.ReceberAsync(tenantId, mensagem, ct);
        await processador.ProcessarConversaAsync(conversaId, ct);
        return conversaId;
    }
}

/// <summary>Identidade fixa, no lugar das claims do token.</summary>
file sealed class ContextoFixo(Guid tenantId, Guid atendenteId, string nome, PerfilDeAtendente perfil)
    : IContextoDoUsuario
{
    public bool Autenticado => true;
    public Guid TenantId => tenantId;
    public Guid AtendenteId => atendenteId;
    public string Nome => nome;
    public PerfilDeAtendente Perfil => perfil;

    public bool PodeGerirCadastros =>
        perfil is PerfilDeAtendente.Supervisor or PerfilDeAtendente.Administrador;

    public bool PodeGerirAtendentes => perfil is PerfilDeAtendente.Administrador;

    public bool PodeOperarConversaDeTerceiro =>
        perfil is PerfilDeAtendente.Supervisor or PerfilDeAtendente.Administrador;
}

/// <summary>Sem SignalR aqui: o que esta sob teste e a automacao, nao o hub.</summary>
/// <summary>
/// Canal que nao entrega nada. O verificador exercita a REGRA de atendimento,
/// nao o gateway: sair para o WhatsApp real aqui mandaria mensagem de teste para
/// telefone de gente.
/// </summary>
file sealed class CanalSilencioso : IEnviadorDeCanal
{
    public Task<string?> EnviarAsync(Guid m, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    public Task EnviarPendentesAsync(Guid c, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>
/// Anexo em memoria. Nenhum teste daqui anexa arquivo; existe para satisfazer o
/// construtor sem escrever no disco de quem roda o verificador.
/// </summary>
file sealed class AnexosEmMemoria : IArmazenamentoDeAnexos
{
    private readonly Dictionary<string, byte[]> _guardados = [];

    public long TamanhoMaximoBytes => 16 * 1024 * 1024;

    public bool TipoPermitido(string? tipoMime) => !string.IsNullOrWhiteSpace(tipoMime);

    public Task<AnexoGravado> GravarAsync(
        Guid tenantId, Guid mensagemId, DateTime quando, byte[] conteudo,
        string? nomeOriginal, string? tipoMime, CancellationToken ct = default)
    {
        var caminho = $"memoria/{mensagemId:N}";
        _guardados[caminho] = conteudo;

        return Task.FromResult(new AnexoGravado(
            caminho,
            nomeOriginal ?? "anexo",
            tipoMime ?? "application/octet-stream",
            conteudo.LongLength,
            TipoMensagem.Documento));
    }

    public Task<byte[]?> LerAsync(string caminhoRelativo, CancellationToken ct = default) =>
        Task.FromResult(_guardados.GetValueOrDefault(caminhoRelativo));
}

file sealed class NotificadorSilencioso : INotificadorDeAtendimento
{
    public Task ConversaAtribuidaAsync(Guid t, ConversaResumoResponse c, CancellationToken ct = default) => Task.CompletedTask;
    public Task ConversaLiberadaAsync(Guid t, ConversaResumoResponse c, CancellationToken ct = default) => Task.CompletedTask;
    public Task ConversaTransferidaAsync(Guid t, ConversaResumoResponse c, CancellationToken ct = default) => Task.CompletedTask;
    public Task ConversaAtualizadaAsync(Guid t, ConversaResumoResponse c, CancellationToken ct = default) => Task.CompletedTask;
    public Task ConversaEncerradaAsync(Guid t, ConversaResumoResponse c, CancellationToken ct = default) => Task.CompletedTask;
    public Task ConversaReabertaAsync(Guid t, ConversaResumoResponse c, CancellationToken ct = default) => Task.CompletedTask;
    public Task MensagemRecebidaAsync(Guid t, MensagemResponse m, ConversaResumoResponse c, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>
/// Nunca deveria ser chamado nestes testes — o OpcoesDaIA usado aqui e sempre
/// Habilitada=false, e RespostaInteligenteService.ResponderAsync ja retorna cedo
/// nesse caso, antes de tocar no orquestrador. Existe so para preencher o
/// parametro do construtor; se algum dia for chamado de verdade, o teste deve
/// falhar alto, nao silenciosamente.
/// </summary>
file sealed class OrquestradorNuncaChamado : IOrquestradorDeProvedores
{
    public Task<RespostaDaIA> ResponderAsync(Guid tenantId, Guid? conversaId, PedidoDeIa pedido, CancellationToken ct) =>
        throw new InvalidOperationException(
            "OrquestradorDeProvedores nao deveria ser chamado com RespostaInteligente:Habilitada=false.");
}

/// <summary>
/// Para os testes de Human Takeover (BUG-ATD-01): conta chamadas em vez de
/// lancar, porque RespostaInteligenteService.ResponderAsync engole qualquer
/// excecao do orquestrador e devolve "nao respondeu" — um
/// OrquestradorNuncaChamado que lanca ficaria mascarado, o teste passaria
/// mesmo se o gate do modo humano falhasse. Contando, a asercao
/// "Chamadas == 0" e direta e nao depende de como a excecao e tratada rio
/// acima.
/// </summary>
file sealed class OrquestradorContador : IOrquestradorDeProvedores
{
    public int Chamadas { get; private set; }

    public Task<RespostaDaIA> ResponderAsync(Guid tenantId, Guid? conversaId, PedidoDeIa pedido, CancellationToken ct)
    {
        Chamadas++;
        return Task.FromResult(new RespostaDaIA(false, "", "OrquestradorContador nao deveria ser chamado em modo humano."));
    }
}

/// <summary>
/// Para o teste de Human Takeover que prova que o SignalR continua recebendo
/// eventos em modo humano — NotificadorSilencioso nao permitiria espiar isso.
/// </summary>
file sealed class NotificadorContador : INotificadorDeAtendimento
{
    public int MensagensRecebidas { get; private set; }
    public int ConversasAtualizadas { get; private set; }

    public Task ConversaAtribuidaAsync(Guid t, ConversaResumoResponse c, CancellationToken ct = default) => Task.CompletedTask;
    public Task ConversaLiberadaAsync(Guid t, ConversaResumoResponse c, CancellationToken ct = default) => Task.CompletedTask;
    public Task ConversaTransferidaAsync(Guid t, ConversaResumoResponse c, CancellationToken ct = default) => Task.CompletedTask;

    public Task ConversaAtualizadaAsync(Guid t, ConversaResumoResponse c, CancellationToken ct = default)
    {
        ConversasAtualizadas++;
        return Task.CompletedTask;
    }

    public Task ConversaEncerradaAsync(Guid t, ConversaResumoResponse c, CancellationToken ct = default) => Task.CompletedTask;
    public Task ConversaReabertaAsync(Guid t, ConversaResumoResponse c, CancellationToken ct = default) => Task.CompletedTask;

    public Task MensagemRecebidaAsync(Guid t, MensagemResponse m, ConversaResumoResponse c, CancellationToken ct = default)
    {
        MensagensRecebidas++;
        return Task.CompletedTask;
    }
}
