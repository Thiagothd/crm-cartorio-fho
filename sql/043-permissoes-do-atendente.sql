/* =============================================================================
   CRM-Cartorio — Permissoes granulares por atendente
   -----------------------------------------------------------------------------
   Gerado em .... 2026-09-08
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 001-schema.sql, 021-versao-do-schema.sql

   O QUE FAZ
       dbo.Atendentes += Permissoes         NVARCHAR(4000) NULL
       dbo.Atendentes += ModeloDePermissao  TINYINT        NULL
       Expand-only puro. Nenhum UPDATE de backfill, nenhum DROP, nenhum ALTER
       de constraint existente.

   O PROBLEMA QUE ISTO RESOLVE
       Hoje o acesso e decidido por um enum de 3 valores (PerfilDeAtendente),
       que vira 3 booleanos tudo-ou-nada (PodeGerirCadastros, PodeGerirAtendentes,
       PodeOperarConversaDeTerceiro). Nao ha como dizer "esta pessoa atende e
       cadastra cliente, mas nao ve relatorio da equipe". Permissoes granulares
       resolvem isso guardando, por atendente, o CONJUNTO de permissoes que ele
       tem — ver Modules/Usuarios/Domain/CatalogoDePermissoes.cs (a mesma
       migracao nao precisa mudar quando o catalogo ganha item novo).

   POR QUE NULL SIGNIFICA "SEGUE A CLASSE DA CONTA" — O PONTO MAIS IMPORTANTE
       Permissoes = NULL faz o codigo derivar o conjunto do Perfil atual do
       atendente (Atendente -> preset Balcao, Supervisor -> preset Supervisao,
       Administrador -> preset Administracao — ver CatalogoDePermissoes.ConjuntoDaClasse).
       Isso significa que TODA LINHA EXISTENTE, sem nenhum UPDATE, continua se
       comportando EXATAMENTE como hoje assim que o binario novo sobe — nao por
       cuidado, por construcao. E uma linha inserida pelo binario ANTERIOR a
       esta migracao (que nao conhece a coluna) nasce NULL e se comporta do
       mesmo jeito.

   POR QUE COLUNA ESCALAR, NAO TABELA FILHA
       IValidadorDeSessao.CarregarSessaoAsync roda em TODA requisicao autenticada
       (SEC-01, revogacao de sessao) e agora tambem resolve permissoes no MESMO
       round-trip. Uma tabela filha (PermissaoDoAtendente) viraria um JOIN de
       ~50 linhas por request, ou um segundo round-trip, para servir um dado
       que so e lido como conjunto inteiro — nunca por item. Tambem quebraria a
       invariante atomica de "sobrou pelo menos um administrador ativo", que
       precisa ser uma unica instrucao (ver AtendenteService.DefinirPermissoesAsync).

   POR QUE TEXTO COM PIPE DE CERCA, NAO BITMASK
       O catalogo ja nasce com ~49 chaves; um bitmask BIGINT (64 bits) fica sem
       margem na primeira ampliacao, e a posicao do bit vira contrato nao
       escrito — reordenar o enum corromperia silenciosamente a permissao de
       todo mundo, sem nenhum CHECK capaz de proteger disso. NVARCHAR(4000)
       cabe ~160 chaves (o catalogo cabe tres vezes), fica IN-ROW (MAX sairia
       da linha e custaria lookup), e e legivel em SSMS numa auditoria. O pipe
       nas DUAS pontas (|atendimento.ver|clientes.ver|) faz
       "Permissoes LIKE '%|equipe.permissoes|%'" ser teste de contencao
       CORRETO em SQL — sem isso, "equipe.ver" casaria dentro de
       "equipe.ver_algo".

   POR QUE ModeloDePermissao E SO ROTULO, NUNCA A VERDADE
       Guarda so o ULTIMO preset aplicado. A verdade e sempre a coluna
       Permissoes. Se o preset fosse a fonte de verdade, mudar a definicao de
       "Supervisao" numa release futura alteraria silenciosamente o que meia
       duzia de pessoas pode fazer, sem nenhum registro. Com o modelo como
       rotulo, a tela consegue dizer "Supervisao + 2 ajustes" comparando o
       conjunto efetivo com a definicao em codigo, e a definicao pode evoluir
       sem tocar em ninguem.

   O QUE ESTA MIGRACAO DELIBERADAMENTE NAO TOCA
       CK_Atendentes_Perfil (001-schema.sql) continua IN (1,2,3) — o Perfil
       nao vira "so um preset": ele continua sendo a fronteira de seguranca do
       SEC-03 (Perfil == Administrador || Cargo == Titular decide quem e
       "conta privilegiada", sujeita a MFA obrigatorio e a GarantirTitularProtegido).
       Reescrever essa checagem em termos de permissao faria remover uma
       permissao de alguem tira-lo do MFA obrigatorio sem ninguem perceber —
       ver Modules/Usuarios/README.md. A claim "perfil" do JWT e o
       GeradorDeToken tambem nao mudam.

   IDEMPOTENCIA
       As duas colunas so entram quando ainda nao existem (IF COL_LENGTH).
       Sem elas ja existirem, nao ha nada para este script corrigir numa
       segunda execucao.
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF DB_NAME() NOT IN (N'CRM_Cartorio', N'CRM_Cartorio_Teste', N'CRM_Cartorio_Dev', N'CRM_Cartorio_Demo')
BEGIN
    RAISERROR('Recusado: este script so roda em CRM_Cartorio, CRM_Cartorio_Teste ou CRM_Cartorio_Dev. Confira o banco selecionado.', 16, 1);
    RETURN;
END
GO

IF OBJECT_ID(N'dbo.Atendentes', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode as migracoes anteriores antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

/* --- 1. As colunas ---------------------------------------------------------- */

IF COL_LENGTH(N'dbo.Atendentes', N'Permissoes') IS NULL
    -- NULL = "segue a Classe da conta" (Perfil). Ver o cabecalho acima para o
    -- porque disto ser o ponto central desta migracao.
    ALTER TABLE dbo.Atendentes ADD Permissoes NVARCHAR(4000) NULL;
GO

IF COL_LENGTH(N'dbo.Atendentes', N'ModeloDePermissao') IS NULL
    -- 1 Recepcao, 2 Balcao, 3 Catalogo, 4 Supervisao, 5 Administracao,
    -- 6 Auditoria. So o rotulo do ultimo preset aplicado — a verdade e sempre
    -- Permissoes.
    ALTER TABLE dbo.Atendentes ADD ModeloDePermissao TINYINT NULL;
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'043-permissoes-do-atendente.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'043-permissoes-do-atendente.sql',
                N'Atendentes ganha Permissoes (lista com pipe de cerca, NULL = segue a Classe da conta) e ModeloDePermissao (rotulo do ultimo preset aplicado). Expand-only, sem backfill — ninguem muda de comportamento no dia 1.');
END
GO

COMMIT TRANSACTION;
GO

SELECT COL_LENGTH(N'dbo.Atendentes', N'Permissoes')        AS Permissoes,
       COL_LENGTH(N'dbo.Atendentes', N'ModeloDePermissao') AS ModeloDePermissao;
GO

PRINT 'Migracao 043 concluida.';
GO
