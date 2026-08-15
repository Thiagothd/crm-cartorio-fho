using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Atendimento.Domain;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.Clientes.Domain.Entities;
using CrmCartorio.Api.Modules.Configuracoes.Domain;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Entities;
using CrmCartorio.Api.Modules.Servicos.Domain;
using CrmCartorio.Api.Modules.Servicos.Domain.Entities;
using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Infrastructure.Persistence.Seeding;

/// <summary>
/// Popula a base com o cenario de demonstracao da Fase 1.
/// </summary>
/// <remarks>
/// Desligado por padrao — so roda com Seed:Habilitado = true. NAO cria schema:
/// o DDL fica em sql/001-schema.sql para execucao manual, conforme a regra 1 do
/// CLAUDE.md. Se as tabelas nao existirem, o semeador avisa e desiste em vez de
/// tentar criar qualquer coisa.
/// </remarks>
public class SemeadorDeDemonstracao(
    CrmDbContext db,
    IHashDeSenha hash,
    ILogger<SemeadorDeDemonstracao> log)
{
    /// <summary>Senha de todos os usuarios de demonstracao. Descartavel.</summary>
    public const string SenhaPadrao = "cartorio2026";

    /// <summary>
    /// Nome fantasia da serventia. Const porque entra em dois lugares: no cadastro do
    /// tenant e na resolucao de {NomeCartorio} nas mensagens automaticas semeadas.
    /// </summary>
    private const string NomeFantasiaDaServentia = "Cartório de Registro Civil e Tabelionato de Notas";

    public async Task ExecutarAsync(CancellationToken ct = default)
    {
        if (!await db.Database.CanConnectAsync(ct))
        {
            log.LogWarning(
                "Semeador ignorado: nao foi possivel conectar ao banco. " +
                "Confira a connection string e execute sql/001-schema.sql antes.");
            return;
        }

        bool temTenant;

        try
        {
            temTenant = await db.Tenants.AnyAsync(ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(
                ex,
                "Semeador ignorado: o schema parece nao existir. " +
                "Execute sql/001-schema.sql manualmente antes de subir a API.");
            return;
        }

        var agora = DateTime.UtcNow;

        // As duas metades sao independentes de proposito. A configuracao pode vir
        // do sql/002-seed-simulado.sql; os atendimentos NUNCA vao para .sql porque
        // dependem de datas relativas ao momento da demonstracao — uma conversa
        // "atrasada" gravada com data fixa envelhece e para de fazer sentido.
        if (!temTenant)
        {
            log.LogInformation("Semeando a configuracao de demonstracao...");

            // Ordem importa: os atendentes apontam para um setor padrao. O EF ja
            // sabe ordenar os INSERTs pelas FKs declaradas, mas semear na ordem
            // das dependencias mantem o codigo legivel para quem ajustar depois.
            SemearTenant(agora);
            SemearSetores();
            SemearEtapas();
            SemearAtendentes();
            SemearServicos();

            await db.SaveChangesAsync(ct);

            log.LogInformation(
                "Configuracao criada. Usuarios: rafael@cartoriogameleiras.com.br / {Senha}",
                SenhaPadrao);
        }
        else
        {
            log.LogInformation("Configuracao ja existe na base — mantida como esta.");
        }

        await SemearAutomacaoAsync(ct);

        if (await db.Conversas.AnyAsync(ct))
        {
            log.LogInformation("Atendimentos de demonstracao ja existem — nada a fazer.");
            return;
        }

        if (!await ConfiguracaoEsperadaExisteAsync(ct))
        {
            log.LogWarning(
                "Atendimentos de demonstracao nao foram criados: a configuracao da base " +
                "nao usa os identificadores de CatalogoDeDemonstracao. Isso e esperado se " +
                "os cadastros foram feitos a mao.");
            return;
        }

        log.LogInformation("Semeando os atendimentos de demonstracao...");

        var servicos = await db.TiposServico.AsNoTracking().ToListAsync(ct);
        var prazos = await db.PrazosServico.AsNoTracking().ToListAsync(ct);

        SemearAtendimentos(
            servicos.ToDictionary(t => t.Nome, StringComparer.OrdinalIgnoreCase),
            prazos.ToDictionary(p => p.TipoServicoId),
            agora);

        await db.SaveChangesAsync(ct);

        log.LogInformation("Atendimentos de demonstracao criados.");
    }

    /// <summary>
    /// Os roteiros apontam para setores, etapas e atendentes de Guid fixo. Se a base
    /// foi configurada de outro jeito, inserir as conversas violaria as FKs — melhor
    /// avisar e sair do que estourar no meio da insercao.
    /// </summary>
    private async Task<bool> ConfiguracaoEsperadaExisteAsync(CancellationToken ct)
    {
        var atendentes = new[]
        {
            IdentificadoresDeDemonstracao.Atendentes.Titular,
            IdentificadoresDeDemonstracao.Atendentes.Substituto,
            IdentificadoresDeDemonstracao.Atendentes.Escrevente1,
            IdentificadoresDeDemonstracao.Atendentes.Escrevente2
        };

        var setores = CatalogoDeDemonstracao.Setores.Select(s => s.Id).ToArray();
        var etapas = CatalogoDeDemonstracao.Etapas.Select(e => e.Id).ToArray();
        var servicos = CatalogoDeDemonstracao.Servicos.Select(s => s.Id).ToArray();

        return await db.Tenants.AnyAsync(t => t.Id == IdentificadoresDeDemonstracao.Tenant, ct)
            && await db.Atendentes.CountAsync(a => atendentes.Contains(a.Id), ct) == atendentes.Length
            && await db.Setores.CountAsync(s => setores.Contains(s.Id), ct) == setores.Length
            && await db.EtapasFunil.CountAsync(e => etapas.Contains(e.Id), ct) == etapas.Length
            && await db.TiposServico.CountAsync(t => servicos.Contains(t.Id), ct) == servicos.Length;
    }

    /// <summary>
    /// Dados reais da serventia, conforme o perfil oficial no CNJ.
    /// </summary>
    private void SemearTenant(DateTime agora)
    {
        db.Tenants.Add(new Tenant
        {
            Id = IdentificadoresDeDemonstracao.Tenant,
            RazaoSocial = "Cartório Antonio Campos Nogueira",
            NomeFantasia = NomeFantasiaDaServentia,

            // Informado pelo cliente como 21.35.122/6000-10 — 13 digitos, um a menos
            // que os 14 de um CNPJ. Gravado como veio, so digitos, para conferencia.
            // Consta em docs/REQUISITOS.md, secao 5, como pendencia de confirmacao.
            Cnpj = "2135122600010",

            // Preenchidos apenas na Fase 2: dependem de CNPJ verificado na Meta e
            // de um numero dedicado, que nao pode estar ativo no WhatsApp pessoal.
            WhatsappBusinessAccountId = null,
            WhatsappPhoneNumberId = null,

            // Informado como (38) 99977-891 — 10 digitos; celular com DDD tem 11.
            // Tambem esta na lista de pendencias.
            WhatsappNumeroExibicao = "+55 38 99977-891",
            Ativo = true,
            CriadoEm = agora,
            AtualizadoEm = agora
        });
    }

    private void SemearAtendentes()
    {
        var senha = hash.Gerar(SenhaPadrao);
        var tenant = IdentificadoresDeDemonstracao.Tenant;

        db.Atendentes.AddRange(
            // Titular da serventia. Nome real, conforme o CNJ.
            new Atendente
            {
                Id = IdentificadoresDeDemonstracao.Atendentes.Titular,
                TenantId = tenant,
                Nome = "Helena Campos Nogueira",
                Email = "helena@cartoriogameleiras.com.br",
                SenhaHash = senha,
                Perfil = PerfilDeAtendente.Administrador,
                Cargo = CargoNaServentia.Titular,
                Ativo = true
            },

            // Substituto.
            new Atendente
            {
                Id = IdentificadoresDeDemonstracao.Atendentes.Substituto,
                TenantId = tenant,
                Nome = "Rafael Campos Nogueira",
                Email = "rafael@cartoriogameleiras.com.br",
                SenhaHash = senha,
                Perfil = PerfilDeAtendente.Administrador,
                Cargo = CargoNaServentia.Substituto,
                Ativo = true
            },

            // Os dois abaixo sao NOMES FICTICIOS. Existem para demonstrar a trava de
            // conversa, que precisa de mais de um atendente disputando a mesma fila.
            // Nomes ficticios de demonstracao.
            new Atendente
            {
                Id = IdentificadoresDeDemonstracao.Atendentes.Escrevente1,
                TenantId = tenant,
                Nome = "Carla Menezes",
                Email = "carla@cartoriogameleiras.com.br",
                SenhaHash = senha,

                // Os dois eixos separados, na pratica: cargo de escrevente
                // autorizado com permissao de supervisao no painel. Fundidos numa
                // coluna so, isso era impossivel de representar — ela apareceria
                // como "Supervisor", cargo que nao existe em cartorio.
                Perfil = PerfilDeAtendente.Supervisor,
                Cargo = CargoNaServentia.EscreventeAutorizado,
                SetorPadraoId = IdentificadoresDeDemonstracao.Setores.RegistroCivil,
                Ativo = true
            },
            new Atendente
            {
                Id = IdentificadoresDeDemonstracao.Atendentes.Escrevente2,
                TenantId = tenant,
                Nome = "Paula Ribeiro",
                Email = "paula@cartoriogameleiras.com.br",
                SenhaHash = senha,
                Perfil = PerfilDeAtendente.Atendente,
                Cargo = CargoNaServentia.Escrevente,
                SetorPadraoId = IdentificadoresDeDemonstracao.Setores.Notas,
                Ativo = true
            });
    }

    private void SemearSetores()
    {
        foreach (var s in CatalogoDeDemonstracao.Setores)
        {
            db.Setores.Add(new Setor
            {
                Id = s.Id,
                TenantId = IdentificadoresDeDemonstracao.Tenant,
                Nome = s.Nome,
                Descricao = s.Descricao,
                Ordem = s.Ordem,
                Ativo = true
            });
        }
    }

    private void SemearEtapas()
    {
        foreach (var e in CatalogoDeDemonstracao.Etapas)
        {
            db.EtapasFunil.Add(new EtapaFunil
            {
                Id = e.Id,
                TenantId = IdentificadoresDeDemonstracao.Tenant,
                Nome = e.Nome,
                Descricao = e.Descricao,
                Ordem = e.Ordem,
                Cor = e.Cor,
                EhFinal = e.EhFinal,
                Ativo = true
            });
        }
    }

    /// <summary>
    /// Semeia mensagens automaticas e respostas rapidas, se ainda nao existirem.
    /// </summary>
    /// <remarks>
    /// Verificacao propria, fora do bloco "se nao ha tenant", porque estes dois
    /// cadastros chegaram depois: a base de desenvolvimento ja tem tenant, e o bloco
    /// de cima nunca mais roda nela. Sem isto, quem ja tinha a base montada nunca
    /// veria a automacao funcionando.
    ///
    /// Se as tabelas ainda nao existirem, avisa e sai — nao cria schema, conforme a
    /// regra 1 do CLAUDE.md. O DDL esta em sql/005-mensagens-automaticas.sql.
    /// </remarks>
    private async Task SemearAutomacaoAsync(CancellationToken ct)
    {
        var tenant = IdentificadoresDeDemonstracao.Tenant;

        try
        {
            if (!await db.MensagensAutomaticas.AnyAsync(m => m.TenantId == tenant, ct))
            {
                foreach (var m in CatalogoDeDemonstracao.MensagensAutomaticas)
                {
                    db.MensagensAutomaticas.Add(new MensagemAutomatica
                    {
                        Id = m.Id,
                        TenantId = tenant,
                        Gatilho = m.Gatilho,
                        EtapaAlvoId = m.EtapaAlvoId,
                        Conteudo = m.Conteudo,
                        Ativo = m.Ativo
                    });
                }

                log.LogInformation(
                    "{Total} regras de mensagem automatica semeadas. Os textos sao rascunho " +
                    "e aguardam a redacao do cartório parceiro.",
                    CatalogoDeDemonstracao.MensagensAutomaticas.Length);
            }

            if (!await db.RespostasRapidas.AnyAsync(r => r.TenantId == tenant, ct))
            {
                foreach (var r in CatalogoDeDemonstracao.RespostasRapidas)
                {
                    db.RespostasRapidas.Add(new RespostaRapida
                    {
                        Id = r.Id,
                        TenantId = tenant,
                        Atalho = r.Atalho,
                        Titulo = r.Titulo,
                        Conteudo = r.Conteudo,
                        Ativo = true
                    });
                }

                log.LogInformation(
                    "{Total} respostas rapidas semeadas.", CatalogoDeDemonstracao.RespostasRapidas.Length);
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(
                ex,
                "Automacao nao semeada: as tabelas MensagensAutomaticas/RespostasRapidas " +
                "parecem nao existir. Rode sql/005-mensagens-automaticas.sql antes de subir a API.");
        }
    }

    private void SemearServicos()
    {
        foreach (var s in CatalogoDeDemonstracao.Servicos)
        {
            db.TiposServico.Add(new TipoServico
            {
                Id = s.Id,
                TenantId = IdentificadoresDeDemonstracao.Tenant,
                Nome = s.Nome,
                Codigo = s.Codigo,
                Categoria = s.Categoria,
                Descricao = s.Descricao,
                Ativo = true
            });

            db.PrazosServico.Add(new PrazoServico
            {
                Id = s.PrazoId,
                TenantId = IdentificadoresDeDemonstracao.Tenant,
                TipoServicoId = s.Id,
                PrazoHoras = s.PrazoHoras,
                PercentualAlerta = s.PercentualAlerta,
                ApenasDiasUteis = s.ApenasDiasUteis,
                Ativo = true
            });
        }
    }

    /// <summary>
    /// Monta conversas em varios pontos do funil e em varias situacoes de prazo —
    /// inclusive uma atrasada e duas em alerta —, senao a demonstracao mostraria
    /// um painel todo verde e o cartório parceiro nao veria o produto trabalhando.
    /// </summary>
    /// <remarks>
    /// Todos os atendimentos sao de Tabelionato de Notas ou Registro Civil. Nao ha
    /// registro de imovel, averbacao em matricula nem protesto: nao sao atribuicao
    /// desta serventia, e conversa sobre ato que o cartorio nao pratica destruiria
    /// a credibilidade da demonstracao.
    /// </remarks>
    private void SemearAtendimentos(
        Dictionary<string, TipoServico> servicos,
        Dictionary<Guid, PrazoServico> prazos,
        DateTime agora)
    {
        var tenant = IdentificadoresDeDemonstracao.Tenant;
        var notas = IdentificadoresDeDemonstracao.Setores.Notas;
        var civil = IdentificadoresDeDemonstracao.Setores.RegistroCivil;
        var titular = IdentificadoresDeDemonstracao.Atendentes.Titular;
        var substituto = IdentificadoresDeDemonstracao.Atendentes.Substituto;
        var marta = IdentificadoresDeDemonstracao.Atendentes.Escrevente1;
        var juliana = IdentificadoresDeDemonstracao.Atendentes.Escrevente2;

        var sequencial = 0;
        var competencia = agora.ToString("yyyyMM");

        string ProximoProtocolo() => $"{competencia}-{++sequencial:D4}";

        // Texto do MODELO cadastrado, ainda com os marcadores. A substituicao acontece
        // no laco de insercao, onde o protocolo da conversa ja existe.
        static string TextoAutomatico(Guid regraId) =>
            CatalogoDeDemonstracao.MensagensAutomaticas.First(m => m.Id == regraId).Conteudo;

        // Mesmo menu que o disparador monta em execucao — mesma ordenacao, para que a
        // demonstracao mostre exatamente o que o cliente receberia.
        var menuDeSetores = TriagemAutomatica.MontarMenu(
            [.. CatalogoDeDemonstracao.Setores
                .OrderBy(s => s.Ordem)
                .Select(s => new TriagemAutomatica.OpcaoDeSetor(s.Id, s.Nome))]);

        Cliente NovoCliente(string nome, string telefone, string? documento, int diasAtras)
        {
            var cliente = new Cliente
            {
                TenantId = tenant,
                Nome = nome,
                TelefoneWhatsapp = telefone,
                Documento = documento,
                PrimeiroContatoEm = agora.AddDays(-diasAtras),
                Ativo = true
            };

            db.Clientes.Add(cliente);
            return cliente;
        }

        var roteiros = new List<RoteiroDeConversa>
        {
            // 1. Registro Civil, concluido, no prazo.
            new(
                NovoCliente("Maria Aparecida Souza", "5538999000045", "12345678901", 18),
                civil,
                IdentificadoresDeDemonstracao.Etapas.ProntoEntrega,
                servicos["Certidão de nascimento"],
                marta,
                PrazoIniciadoHorasAtras: 4,
                [
                    (AutorTipo.Cliente, null, "Bom dia! Preciso da certidao de nascimento do meu filho.", 26),
                    (AutorTipo.Atendente, marta,
                        "Bom dia, dona Maria! Me passa o nome completo e a data de nascimento dele, por favor.", 25),
                    (AutorTipo.Cliente, null, "Joao Pedro Souza Lima, nasceu em 12/03/2018 aqui em Gameleiras mesmo.", 24),
                    (AutorTipo.Atendente, marta,
                        "Localizei o registro no livro. Emito a certidao hoje e te aviso por aqui.", 23),
                    (AutorTipo.Atendente, marta,
                        "Dona Maria, a certidao ja esta pronta para retirada no balcao, na Praca Sao Francisco, 70.", 2),

                    // Gerada pela regra de etapa "Pronto / entrega". Mostra o aviso de
                    // conclusao ja no fio, para o cartório parceiro ver o rotulo "Automatica" ao
                    // lado de uma mensagem que nenhum atendente escreveu.
                    //
                    // O texto e o MODELO cadastrado, com marcadores: quem resolve e o
                    // proprio semeador, logo abaixo. Escrever o protocolo a mao aqui
                    // daria certo hoje e erraria no mes que vem.
                    (AutorTipo.Sistema, null, TextoAutomatico(
                        IdentificadoresDeDemonstracao.MensagensAutomaticas.AtoConcluido), 2)
                ]),

            // 2. Registro Civil, em alerta de prazo.
            new(
                NovoCliente("Cleide Moraes Santana", "5538993210987", null, 6),
                civil,
                IdentificadoresDeDemonstracao.Etapas.Captacao,
                servicos["Segunda via de certidão"],
                marta,
                PrazoIniciadoHorasAtras: 42,
                [
                    (AutorTipo.Cliente, null, "Perdi a certidao de obito do meu pai, consigo outra via?", 46),
                    (AutorTipo.Atendente, marta,
                        "Consegue sim. Me informa o nome completo dele e a data do falecimento.", 45),
                    (AutorTipo.Cliente, null, "Sebastiao Moraes Santana, faleceu em 04/2019.", 8)
                ]),

            // 3. Registro Civil, ATRASADA de proposito.
            new(
                NovoCliente("Antonio Batista Nunes", "5538994321098", "32165498700", 30),
                civil,
                IdentificadoresDeDemonstracao.Etapas.EmProcessamento,
                servicos["Certidão de casamento"],
                juliana,
                PrazoIniciadoHorasAtras: 40,
                [
                    (AutorTipo.Cliente, null, "Preciso da certidao de casamento dos meus pais para dar entrada no INSS.", 50),
                    (AutorTipo.Atendente, juliana,
                        "Certo, seu Antonio. O casamento foi registrado aqui em Gameleiras? Me passa o ano.", 49),
                    (AutorTipo.Cliente, null, "Foi aqui sim, em 1979. Nao lembro o mes exato.", 44),
                    (AutorTipo.Atendente, juliana,
                        "Vou precisar procurar no livro do ano todo. Isso leva um pouco mais de tempo.", 43),
                    (AutorTipo.Cliente, null, "Ja conseguiu achar? O INSS marcou pra semana que vem.", 6)
                ]),

            // 4. Notas, EM ALERTA de proposito.
            //
            // O prazo da escritura e de 240 horas em dias UTEIS, o que em horas de
            // calendario passa de 330. Para chegar aos 80% que acendem o alerta,
            // o inicio tem de estar ~290h atras — e a conversa, mais velha ainda.
            // Foi calibrado assim porque um painel sem alerta nenhum nao demonstra
            // o controle de prazo funcionando.
            new(
                NovoCliente("Ana Lucia Martins", "5538997654321", null, 16),
                notas,
                IdentificadoresDeDemonstracao.Etapas.Captacao,
                servicos["Escritura pública de compra e venda"],
                marta,
                PrazoIniciadoHorasAtras: 292,
                [
                    (AutorTipo.Cliente, null, "Oi, quanto fica uma escritura de compra e venda de uma casa de 180 mil?", 300),
                    (AutorTipo.Atendente, marta,
                        "Oi, Ana! O valor segue a tabela de emolumentos do TJMG. Vou levantar e te retorno com o valor exato.", 298),
                    (AutorTipo.Atendente, marta,
                        "Para lavrar preciso de RG, CPF e certidao de casamento das partes, e a matricula atualizada do imovel.", 297),
                    (AutorTipo.Cliente, null, "A matricula eu pego onde? Aqui com voces?", 294),
                    (AutorTipo.Atendente, marta,
                        "Essa e no Registro de Imoveis da comarca. Nos lavramos a escritura; o registro dela e feito la depois.", 293),
                    (AutorTipo.Cliente, null, "Entendi. Vou separar os documentos.", 292)
                ]),

            // 5. Notas, procuracao, no prazo.
            new(
                NovoCliente("Jose Carlos Ferreira", "5538998770132", "98765432100", 9),
                notas,
                IdentificadoresDeDemonstracao.Etapas.EmProcessamento,
                servicos["Procuração pública"],
                juliana,
                PrazoIniciadoHorasAtras: 10,
                [
                    (AutorTipo.Cliente, null, "Boa tarde, preciso de uma procuracao para meu irmao resolver umas coisas por mim.", 14),
                    (AutorTipo.Atendente, juliana,
                        "Boa tarde, seu Jose. Precisa dizer para que serve a procuracao, para eu redigir os poderes certos.", 13),
                    (AutorTipo.Cliente, null, "Para ele vender um carro no meu nome.", 12),
                    (AutorTipo.Atendente, juliana,
                        "Perfeito. Traga seu RG e CPF, e os dados completos do seu irmao. Ja estou preparando a minuta.", 11)
                ]),

            // 6. Na fila, sem responsavel — Notas.
            new(
                NovoCliente("Roberto Alves Pinheiro", "5538996543210", "45678912300", 2),
                notas,
                IdentificadoresDeDemonstracao.Etapas.AtendimentoInicial,
                Servico: null,
                AtendenteId: null,
                PrazoIniciadoHorasAtras: null,
                [
                    (AutorTipo.Cliente, null, "Boa tarde, voces fazem reconhecimento de firma por autenticidade?", 3),
                    (AutorTipo.Cliente, null, "E preciso agendar?", 3)
                ]),

            // 7. Na fila, sem responsavel e SEM setor — mostra a coluna "Sem setor".
            new(
                NovoCliente("Fernanda Ribeiro Gomes", "5538995432109", null, 1),
                SetorId: null,
                IdentificadoresDeDemonstracao.Etapas.AtendimentoInicial,
                Servico: null,
                AtendenteId: null,
                PrazoIniciadoHorasAtras: null,
                [
                    (AutorTipo.Cliente, null, "Ola! Voces abrem no sabado?", 1)
                ]),

            // 8. Notas, ato rapido, concluido.
            new(
                NovoCliente("Paulo Henrique Dias", "5538992109876", "78945612300", 12),
                notas,
                IdentificadoresDeDemonstracao.Etapas.ProntoEntrega,
                servicos["Reconhecimento de firma"],
                titular,
                PrazoIniciadoHorasAtras: 1,
                [
                    (AutorTipo.Cliente, null, "Preciso reconhecer firma em um contrato de aluguel, ainda da tempo hoje?", 3),
                    (AutorTipo.Atendente, titular,
                        "Da sim, seu Paulo. Atendemos ate as 17h. Se a sua ficha de firma estiver em dia, sai na hora.", 2),
                    (AutorTipo.Atendente, titular,
                        "Reconhecimento feito. Pode retirar quando quiser.", 1)
                ]),

            // 9. Notas, encerrada.
            new(
                NovoCliente("Vera Lucia Camargo", "5538991098765", null, 45),
                notas,
                IdentificadoresDeDemonstracao.Etapas.ProntoEntrega,
                servicos["Escritura pública de doação"],
                substituto,
                PrazoIniciadoHorasAtras: 200,
                [
                    (AutorTipo.Cliente, null, "Quero doar um terreno para minha filha. Da para fazer aqui?", 210),
                    (AutorTipo.Atendente, substituto,
                        "Da sim, dona Vera. A escritura de doacao e lavrada aqui. Precisamos dos documentos das duas e da matricula.", 208),
                    (AutorTipo.Atendente, substituto,
                        "Escritura de doacao lavrada e assinada pelas partes. Atendimento concluido.", 190)
                ],
                Encerrada: true,
                EncerradaHorasAtras: 188,
                MotivoEncerramento: "Escritura lavrada e traslado entregue no balcao."),

            // 10. Registro Civil, encerrada.
            new(
                NovoCliente("Marcos Vinicius Teles", "5538990987654", "15975348600", 60),
                civil,
                IdentificadoresDeDemonstracao.Etapas.ProntoEntrega,
                servicos["Certidão de óbito"],
                juliana,
                PrazoIniciadoHorasAtras: 290,
                [
                    (AutorTipo.Cliente, null, "Preciso da certidao de obito da minha avo para o inventario.", 300),
                    (AutorTipo.Atendente, juliana,
                        "Sinto muito, seu Marcos. Me passa o nome completo dela e a data do falecimento.", 298),
                    (AutorTipo.Cliente, null, "Ja peguei ontem no balcao, obrigado pela atencao.", 280)
                ],
                Encerrada: true,
                EncerradaHorasAtras: 278,
                MotivoEncerramento: "Certidao retirada pelo solicitante."),

            // 11. PRIMEIRO CONTATO, aguardando triagem.
            //
            // Existe para a demonstracao ter uma conversa que ja mostra o bot: o
            // cliente escreveu, o menu saiu sozinho e o sistema espera a escolha do
            // setor. Sem responsavel e sem setor de proposito — e exatamente o estado
            // em que a Fase 2 vai receber toda conversa nova pelo webhook.
            new(
                NovoCliente("Sandra Regina Oliveira", "5538991234567", null, 0),
                SetorId: null,
                IdentificadoresDeDemonstracao.Etapas.AtendimentoInicial,
                Servico: null,
                AtendenteId: null,
                PrazoIniciadoHorasAtras: null,
                [
                    (AutorTipo.Cliente, null, "Oi, boa tarde!", 1),
                    (AutorTipo.Sistema, null, TextoAutomatico(
                        IdentificadoresDeDemonstracao.MensagensAutomaticas.BoasVindas), 1)
                ],
                AguardandoTriagem: true)
        };

        foreach (var roteiro in roteiros)
        {
            var conversa = new Conversa
            {
                TenantId = tenant,
                ClienteId = roteiro.Cliente.Id,
                Protocolo = ProximoProtocolo(),
                AtendenteId = roteiro.AtendenteId,
                SetorId = roteiro.SetorId,
                EtapaId = roteiro.EtapaId,
                TipoServicoId = roteiro.Servico?.Id,
                Status = roteiro.Encerrada ? StatusConversa.Encerrada : StatusConversa.Ativa,
                Canal = CanalOrigem.Simulado,
                IniciadaEm = agora.AddHours(-roteiro.Mensagens.Max(m => m.HorasAtras)),
                UltimaMensagemEm = agora.AddHours(-roteiro.Mensagens.Min(m => m.HorasAtras)),
                MensagensNaoLidas = roteiro.Encerrada ? 0 : ContarNaoLidas(roteiro),
                AguardandoTriagem = roteiro.AguardandoTriagem
            };

            if (roteiro.Servico is not null
                && roteiro.PrazoIniciadoHorasAtras is { } horas
                && prazos.TryGetValue(roteiro.Servico.Id, out var prazo))
            {
                var inicio = agora.AddHours(-horas);
                var limite = CalculadoraDePrazo.CalcularLimite(inicio, prazo);

                conversa.PrazoIniciadoEm = inicio;
                conversa.PrazoLimite = limite;
                conversa.PrazoAlertaEm = inicio.AddMinutes(
                    (limite - inicio).TotalMinutes * prazo.PercentualAlerta / 100d);
            }

            if (roteiro.Encerrada)
            {
                conversa.EncerradaEm = agora.AddHours(-roteiro.EncerradaHorasAtras);
                conversa.EncerradaPorAtendenteId = roteiro.AtendenteId;
                conversa.MotivoEncerramento = roteiro.MotivoEncerramento;
            }

            db.Conversas.Add(conversa);

            db.HistoricoEtapas.Add(new HistoricoEtapa
            {
                TenantId = tenant,
                ConversaId = conversa.Id,
                TipoMudanca = TipoMudancaHistorico.Abertura,
                EtapaNovaId = roteiro.EtapaId,
                SetorNovoId = roteiro.SetorId,
                AtendenteId = roteiro.AtendenteId,
                OcorridoEm = conversa.IniciadaEm,
                Observacao = $"Atendimento aberto com o protocolo {conversa.Protocolo}."
            });

            foreach (var (autor, atendenteId, texto, horasAtras) in roteiro.Mensagens)
            {
                var enviadaEm = agora.AddHours(-horasAtras);

                // Toda mensagem de Sistema nos roteiros e mensagem AO CLIENTE gerada
                // por regra automatica — nenhum roteiro usa Sistema para anotacao
                // interna. Por isso EhAutomatica sai do AutorTipo, em vez de exigir
                // uma coluna a mais na tupla e reescrever os dez roteiros validados.
                var automatica = autor == AutorTipo.Sistema;

                // Os marcadores sao resolvidos pelo MESMO codigo que o disparador usa,
                // e nao por texto pronto: assim a mensagem semeada mostra o protocolo
                // real da conversa e continua correta em qualquer mes.
                var conteudo = automatica
                    ? PlaceholdersDeMensagem.Aplicar(
                        texto,
                        new DadosParaMensagem(
                            roteiro.Cliente.Nome,
                            NomeFantasiaDaServentia,
                            roteiro.Servico?.Nome,
                            conversa.Protocolo,
                            menuDeSetores))
                    : texto;

                db.Mensagens.Add(new Mensagem
                {
                    TenantId = tenant,
                    ConversaId = conversa.Id,
                    AutorTipo = autor,
                    AtendenteId = autor == AutorTipo.Atendente ? atendenteId : null,
                    EhAutomatica = automatica,
                    Conteudo = conteudo,
                    Tipo = TipoMensagem.Texto,
                    StatusEntrega = autor switch
                    {
                        // Fase 1 nao entrega nada de verdade; a automatica nasce
                        // Pendente, igual as que o disparador gera em execucao.
                        AutorTipo.Sistema => StatusEntrega.Pendente,
                        AutorTipo.Atendente => StatusEntrega.Enviada,
                        _ => StatusEntrega.Entregue
                    },
                    EnviadaEm = enviadaEm,
                    LidaEm = autor == AutorTipo.Cliente && horasAtras > 12 ? enviadaEm : null
                });
            }
        }
    }

    private static int ContarNaoLidas(RoteiroDeConversa roteiro) =>
        roteiro.Mensagens.Count(m => m.Autor == AutorTipo.Cliente && m.HorasAtras <= 12);

    private sealed record RoteiroDeConversa(
        Cliente Cliente,
        Guid? SetorId,
        Guid? EtapaId,
        TipoServico? Servico,
        Guid? AtendenteId,
        int? PrazoIniciadoHorasAtras,
        List<(AutorTipo Autor, Guid? AtendenteId, string Texto, int HorasAtras)> Mensagens,
        bool Encerrada = false,
        int EncerradaHorasAtras = 0,
        string? MotivoEncerramento = null,
        bool AguardandoTriagem = false);
}
