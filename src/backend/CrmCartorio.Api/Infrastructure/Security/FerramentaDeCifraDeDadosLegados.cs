using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Clientes.Domain.Entities;
using CrmCartorio.Api.Modules.Seguranca.Domain.Entities;
using CrmCartorio.Api.Modules.Seguranca.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace CrmCartorio.Api.Infrastructure.Security;

/// <summary>
/// PR3 da blindagem LGPD — comando <c>--cifrar-dados-legados</c>: re-grava,
/// pela cifra ATIVA no momento em que roda, toda linha das colunas marcadas
/// <c>.Cifrada()</c> que ainda estiver em texto puro. Comando de execucao
/// unica (mesma disciplina dos break-glass de MFA), NAO um modo do servidor.
/// </summary>
/// <remarks>
/// <b>Passo do runbook de ativacao (docs/INSTALACAO.md), nunca automatico.</b>
/// So faz sentido DEPOIS que alguem com acesso direto ao servidor provisionou
/// <c>ProtecaoDeDados:Chaves</c>/<c>ChaveDeIndice</c> — roda com
/// <c>ExigirCifrado</c> ainda desligado (janela de migracao: leitura tolera
/// as duas formas), e so DEPOIS de confirmar "0 pendentes" numa segunda
/// execucao e que <c>ExigirCifrado</c> deveria ligar.
///
/// <b>Por que a busca do candidato e SQL cru, nunca LINQ sobre a coluna
/// cifrada.</b> <c>db.Clientes.Where(c => c.Documento.StartsWith("$c1$"))</c>
/// pareceria natural, mas o EF aplicaria o CONVERSOR (cifrar!) ao literal
/// <c>"$c1$"</c> antes de montar o SQL — comparando ciphertext gerado agora
/// contra o que esta gravado, que nunca bate (AES-GCM nunca repete nonce).
/// A busca do CANDIDATO le a coluna crua via SQL; o CARREGAMENTO da entidade,
/// depois, usa o <c>DbSet</c> normal (que decifra certinho pelo Id, uma
/// coluna sem conversor).
///
/// <b>Idempotente por linha, nao por coluna.</b> Encontrado UM candidato
/// (qualquer coluna cifrada ainda sem o prefixo <c>$c1$</c>), a linha inteira
/// e tocada — toda propriedade cifrada dela e marcada modificada e re-gravada
/// pelo <c>ConversorCifrado</c> atual, mesmo a que ja estava cifrada. Mais
/// simples que rastrear coluna a coluna, e sem custo real: re-cifrar um valor
/// que ja era cifrado so troca o nonce, nunca corrompe nada. Por isso a
/// segunda execucao relata 0 em todas as tabelas.
/// </remarks>
public static class FerramentaDeCifraDeDadosLegados
{
    private const int TamanhoDoLote = 500;
    private const string Prefixo = "$c1$";

    /// <summary>
    /// Padrao completo do LIKE, passado como PARAMETRO (nunca concatenado
    /// dentro de aspas na string interpolada) — <c>SqlQuery&lt;T&gt;</c>
    /// troca cada <c>{...}</c> por um parametro de verdade (<c>@p0</c>).
    /// Escrever <c>N'{Prefixo}%'</c> geraria o texto literal <c>N'@p0%'</c> —
    /// o marcador de parametro dentro de aspas, nunca substituido.
    /// </summary>
    private const string PadraoDoPrefixo = Prefixo + "%";

    public static async Task<int> ExecutarAsync(IConfiguration configuracao)
    {
        var opcoes = configuracao.GetSection(OpcoesDeProtecaoDeDados.Secao).Get<OpcoesDeProtecaoDeDados>()
            ?? new OpcoesDeProtecaoDeDados();
        var provedor = new ProvedorDeChaveEmConfiguracao(Options.Create(opcoes));

        if (!provedor.CifraAtiva)
        {
            Console.WriteLine(
                "RECUSADO: ProtecaoDeDados sem chave configurada (cifra dormente). " +
                "Nada a fazer — provisione ProtecaoDeDados:Chaves e ChaveDeIndice em " +
                "appsettings.<Ambiente>.json antes de rodar este comando.");
            return 1;
        }

        var connectionString = configuracao.GetConnectionString("SqlServer");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.WriteLine("RECUSADO: ConnectionStrings:SqlServer nao configurada.");
            return 1;
        }

        var cifra = new CifraDeDados(provedor, Options.Create(opcoes));
        var optionsBuilder = new DbContextOptionsBuilder<CrmDbContext>();
        ConfiguracaoDoBanco.Configurar(optionsBuilder, connectionString);

        await using var db = new CrmDbContext(optionsBuilder.Options, cifra);

        if (!await db.Database.CanConnectAsync())
        {
            Console.WriteLine("RECUSADO: nao foi possivel conectar ao banco configurado.");
            return 1;
        }

        Console.WriteLine($"Cifrando dados legados — versao de chave corrente: {provedor.VersaoCorrente}");
        Console.WriteLine();

        var totalMensagens = await CifrarMensagensAsync(db);
        Console.WriteLine($"Mensagens ...................... {totalMensagens}");

        var totalHistorico = await CifrarHistoricoEtapasAsync(db);
        Console.WriteLine($"HistoricoEtapas ................. {totalHistorico}");

        var totalConversas = await CifrarConversasAsync(db);
        Console.WriteLine($"Conversas (MotivoEncerramento) .. {totalConversas}");

        var totalClientes = await CifrarClientesAsync(db, provedor);
        Console.WriteLine($"Clientes ........................ {totalClientes}");

        if (totalMensagens + totalHistorico + totalConversas + totalClientes > 0)
        {
            // Um registro POR TENANT — a tela "Acesso aos dados" (SegurancaService)
            // filtra "Proteção ativa" pelo tenant da sessão. So um tenant existe
            // hoje (Fase 1), mas gravar por tenant em vez de um valor fixo
            // continua correto se a Fase 3 (multi-tenant) chegar antes deste
            // comando rodar de novo.
            var atorExterno = $"{Environment.UserName}@{Environment.MachineName}";
            var agora = DateTime.UtcNow;
            var tenantIds = await db.Tenants.Select(t => t.Id).ToListAsync();

            foreach (var tenantId in tenantIds)
            {
                db.RegistrosDeAcesso.Add(new RegistroDeAcesso
                {
                    TenantId = tenantId,
                    AtorExterno = atorExterno,
                    Tipo = TipoDeAcesso.CifraDeLegadoConcluida,
                    OcorridoEm = agora
                });
            }

            await db.SaveChangesAsync();
        }

        Console.WriteLine();
        Console.WriteLine("Concluido. Rode de novo para confirmar 0 pendentes antes de ligar ExigirCifrado.");
        return 0;
    }

    private static void TocarTodasAsPropriedadesCifradas<TEntidade>(CrmDbContext db, TEntidade entidade)
        where TEntidade : class
    {
        var entrada = db.Entry(entidade);

        foreach (var propriedade in entrada.Metadata.GetProperties())
        {
            if (propriedade["CrmCartorio:Cifrada"] is true)
            {
                entrada.Property(propriedade.Name).IsModified = true;
            }
        }
    }

    private static async Task<int> CifrarMensagensAsync(CrmDbContext db)
    {
        var total = 0;

        while (true)
        {
            var ids = await db.Database.SqlQuery<Guid>($"""
                SELECT TOP ({TamanhoDoLote}) Id AS Value FROM dbo.Mensagens
                WHERE Conteudo NOT LIKE {PadraoDoPrefixo}
                   OR (ArquivoNome IS NOT NULL AND ArquivoNome NOT LIKE {PadraoDoPrefixo})
                ORDER BY Id
                """).ToListAsync();

            if (ids.Count == 0)
            {
                return total;
            }

            var lote = await db.Mensagens.Where(m => ids.Contains(m.Id)).ToListAsync();

            foreach (var mensagem in lote)
            {
                db.Entry(mensagem).Property(m => m.Conteudo).IsModified = true;
                if (mensagem.ArquivoNome is not null)
                {
                    db.Entry(mensagem).Property(m => m.ArquivoNome).IsModified = true;
                }
            }

            await db.SaveChangesAsync();
            total += lote.Count;
            Console.Write(".");
        }
    }

    private static async Task<int> CifrarHistoricoEtapasAsync(CrmDbContext db)
    {
        var total = 0;

        while (true)
        {
            var ids = await db.Database.SqlQuery<Guid>($"""
                SELECT TOP ({TamanhoDoLote}) Id AS Value FROM dbo.HistoricoEtapas
                WHERE Observacao IS NOT NULL AND Observacao NOT LIKE {PadraoDoPrefixo}
                ORDER BY Id
                """).ToListAsync();

            if (ids.Count == 0)
            {
                return total;
            }

            var lote = await db.HistoricoEtapas.Where(h => ids.Contains(h.Id)).ToListAsync();

            foreach (var historico in lote)
            {
                TocarTodasAsPropriedadesCifradas(db, historico);
            }

            await db.SaveChangesAsync();
            total += lote.Count;
            Console.Write(".");
        }
    }

    private static async Task<int> CifrarConversasAsync(CrmDbContext db)
    {
        var total = 0;

        while (true)
        {
            var ids = await db.Database.SqlQuery<Guid>($"""
                SELECT TOP ({TamanhoDoLote}) Id AS Value FROM dbo.Conversas
                WHERE MotivoEncerramento IS NOT NULL AND MotivoEncerramento NOT LIKE {PadraoDoPrefixo}
                ORDER BY Id
                """).ToListAsync();

            if (ids.Count == 0)
            {
                return total;
            }

            var lote = await db.Conversas.Where(c => ids.Contains(c.Id)).ToListAsync();

            foreach (var conversa in lote)
            {
                TocarTodasAsPropriedadesCifradas(db, conversa);
            }

            await db.SaveChangesAsync();
            total += lote.Count;
            Console.Write(".");
        }
    }

    private static async Task<int> CifrarClientesAsync(CrmDbContext db, IProvedorDeChaveDeDados provedor)
    {
        var indice = new IndiceCego(provedor);
        var total = 0;

        while (true)
        {
            var ids = await db.Database.SqlQuery<Guid>($"""
                SELECT TOP ({TamanhoDoLote}) Id AS Value FROM dbo.Clientes
                WHERE (TelefoneAlternativo IS NOT NULL AND TelefoneAlternativo NOT LIKE {PadraoDoPrefixo})
                   OR (Email IS NOT NULL AND Email NOT LIKE {PadraoDoPrefixo})
                   OR (Documento IS NOT NULL AND Documento NOT LIKE {PadraoDoPrefixo})
                   OR (Rg IS NOT NULL AND Rg NOT LIKE {PadraoDoPrefixo})
                   OR (OrgaoEmissor IS NOT NULL AND OrgaoEmissor NOT LIKE {PadraoDoPrefixo})
                   OR (UfOrgaoEmissor IS NOT NULL AND UfOrgaoEmissor NOT LIKE {PadraoDoPrefixo})
                   OR (Profissao IS NOT NULL AND Profissao NOT LIKE {PadraoDoPrefixo})
                   OR (Nacionalidade IS NOT NULL AND Nacionalidade NOT LIKE {PadraoDoPrefixo})
                   OR (Naturalidade IS NOT NULL AND Naturalidade NOT LIKE {PadraoDoPrefixo})
                   OR (NomeDoPai IS NOT NULL AND NomeDoPai NOT LIKE {PadraoDoPrefixo})
                   OR (NomeDaMae IS NOT NULL AND NomeDaMae NOT LIKE {PadraoDoPrefixo})
                   OR (RepresentanteLegal IS NOT NULL AND RepresentanteLegal NOT LIKE {PadraoDoPrefixo})
                   OR (Cep IS NOT NULL AND Cep NOT LIKE {PadraoDoPrefixo})
                   OR (Logradouro IS NOT NULL AND Logradouro NOT LIKE {PadraoDoPrefixo})
                   OR (Numero IS NOT NULL AND Numero NOT LIKE {PadraoDoPrefixo})
                   OR (Complemento IS NOT NULL AND Complemento NOT LIKE {PadraoDoPrefixo})
                   OR (Bairro IS NOT NULL AND Bairro NOT LIKE {PadraoDoPrefixo})
                   OR (Observacoes IS NOT NULL AND Observacoes NOT LIKE {PadraoDoPrefixo})
                   OR (DataNascimentoCifrada IS NOT NULL AND DataNascimentoCifrada NOT LIKE {PadraoDoPrefixo})
                ORDER BY Id
                """).ToListAsync();

            if (ids.Count == 0)
            {
                return total;
            }

            var lote = await db.Clientes.Where(c => ids.Contains(c.Id)).ToListAsync();

            foreach (var cliente in lote)
            {
                TocarTodasAsPropriedadesCifradas(db, cliente);

                if (!string.IsNullOrWhiteSpace(cliente.Documento))
                {
                    cliente.DocumentoHash = indice.Calcular(cliente.TenantId, cliente.Documento);
                }
            }

            await db.SaveChangesAsync();
            total += lote.Count;
            Console.Write(".");
        }
    }
}
