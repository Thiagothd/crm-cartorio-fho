/* =============================================================================
   CRM-Cartorio — Pedido de nome quando o WhatsApp nao traz um nome usavel
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-24
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 001-schema.sql, 005-mensagens-automaticas.sql,
                  021-versao-do-schema.sql, 024-encerramento-por-inatividade.sql

   O QUE FAZ
       1. dbo.Conversas += AguardandoNome        (flag do despacho)
       2. dbo.Clientes  += NomePerguntadoEm      (nao perguntar duas vezes)
       3. CK_MensagensAutomaticas_Gatilho passa a aceitar 14
       4. O texto do gatilho 14, ativo, espelhando CatalogoDeDemonstracao.cs

   O PROBLEMA QUE ISTO RESOLVE
       Cliente.Nome recebia o pushName do WhatsApp cru, e {NomeCliente} usava esse
       valor direto. Contato com o perfil em "." ou emoji fazia a serventia mandar
       "Ola, .!" para um cliente de verdade; perfil vazio virava o telefone E.164,
       e saia "Ola, 5519990001488!".

       A partir daqui, contato sem nome usavel recebe UMA pergunta ("como posso te
       chamar?") antes do menu — e as boas-vindas ja saem com o nome certo. Quem
       tem nome no perfil nao ve diferenca nenhuma: nenhum turno a mais, nenhuma
       mensagem a mais.

   POR QUE DOIS CAMPOS, EM TABELAS DIFERENTES
       Sao dois fatos, e nenhum deriva do outro. AguardandoNome e da CONVERSA e
       dura uma mensagem: e o que o despacho consulta. NomePerguntadoEm e do
       CLIENTE e dura para sempre: e o que impede reperguntar a cada atendimento
       novo — e atendimento novo e frequente desde o 024, porque conversa parada
       encerra sozinha e o proximo "bom dia" abre outra.

   SEM INDICE, DE PROPOSITO
       AguardandoNome so e lido pela conversa que ja esta em maos no despacho
       (chave primaria); NomePerguntadoEm, pelo cliente idem. Indice aqui seria
       peso de escrita sem nenhuma consulta que o use.

   ESTE SCRIPT NAO LIGA NADA SOZINHO
       Diferente do 024, nao ha o que configurar: o comportamento e automatico e
       so acontece para quem chega sem nome usavel. Para desligar, basta desativar
       a mensagem do gatilho 14 na tela Automacao > Mensagens automaticas — o
       recebimento cai para as boas-vindas de sempre, na mesma passada.

   IDEMPOTENCIA
       Coluna, constraint e texto so entram quando ainda nao existem.
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF DB_NAME() NOT IN (N'CRM_Cartorio', N'CRM_Cartorio_Teste', N'CRM_Cartorio_Dev')
BEGIN
    RAISERROR('Recusado: este script so roda em CRM_Cartorio, CRM_Cartorio_Teste ou CRM_Cartorio_Dev. Confira o banco selecionado.', 16, 1);
    RETURN;
END
GO

IF OBJECT_ID(N'dbo.Conversas', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Clientes', N'U') IS NULL
   OR OBJECT_ID(N'dbo.MensagensAutomaticas', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode as migracoes anteriores antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

/* --- 1. Flag do despacho, na conversa -------------------------------------
   NOT NULL com DEFAULT 0: a ausencia de pergunta pendente e o estado normal de
   toda conversa que ja existe, e um BIT nulavel aqui so criaria um terceiro
   estado sem significado.
   ------------------------------------------------------------------------- */

IF COL_LENGTH(N'dbo.Conversas', N'AguardandoNome') IS NULL
    ALTER TABLE dbo.Conversas
        ADD AguardandoNome BIT NOT NULL
            CONSTRAINT DF_Conversas_AguardandoNome DEFAULT (0);
GO

/* --- 2. Carimbo no cliente ------------------------------------------------
   Fato datado, no molde de Conversas.AvisoDeAlertaEnviadoEm: nao grava "o nome
   esta pendente" (isso envelhece, e se calcula na leitura), grava "a pergunta
   saiu", que nao muda mais.

   Nulo em toda a base existente e a verdade: nunca perguntamos a ninguem ate
   agora. Quem ja esta cadastrado com "." recebe a pergunta no proximo contato.
   ------------------------------------------------------------------------- */

IF COL_LENGTH(N'dbo.Clientes', N'NomePerguntadoEm') IS NULL
    ALTER TABLE dbo.Clientes ADD NomePerguntadoEm DATETIME2(3) NULL;
GO

/* --- 3. Gatilho 14 no CHECK -----------------------------------------------
   Sem isto o INSERT do passo 4 falha. Mesmo padrao do 019 (10 para 11) e do 024
   (11 para 13).
   ------------------------------------------------------------------------- */

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_MensagensAutomaticas_Gatilho')
    ALTER TABLE dbo.MensagensAutomaticas DROP CONSTRAINT CK_MensagensAutomaticas_Gatilho;
GO

ALTER TABLE dbo.MensagensAutomaticas ADD CONSTRAINT CK_MensagensAutomaticas_Gatilho
    CHECK (Gatilho IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14));
GO

/* --- 4. O texto do pedido de nome -----------------------------------------
   ESPELHO OBRIGATORIO: identico ao de
   Infrastructure/Persistence/Seeding/CatalogoDeDemonstracao.cs, mesma disciplina
   de 002/015/016/018/020/024.

   NAO USA {NomeCliente}, e isso e a regra e nao o acaso: esta e a unica mensagem
   do sistema que sai justamente porque nao ha nome usavel. Escrever o vocativo
   aqui pediria de volta o "Ola, .!" que o gatilho existe para evitar.
   ------------------------------------------------------------------------- */

DECLARE @agora DATETIME2(3) = SYSUTCDATETIME();

DECLARE @tenants TABLE (Id UNIQUEIDENTIFIER);
INSERT INTO @tenants (Id) SELECT Id FROM dbo.Tenants;

INSERT INTO dbo.MensagensAutomaticas
    (Id, TenantId, Gatilho, EtapaAlvoId, Conteudo, Ativo, CriadoEm, AtualizadoEm)
SELECT
    CASE WHEN t.Id = '0199B2C0-0000-7000-8000-000000000001'
         THEN CAST('0199B2C0-0006-7000-8000-000000000015' AS UNIQUEIDENTIFIER)
         ELSE NEWID() END,
    t.Id, 14, NULL,
    N'Olá! 👋 Você está falando com o {NomeCartorio}.' + CHAR(10) + CHAR(10) +
    N'Antes de começarmos, como posso te chamar?',
    1, @agora, @agora
FROM @tenants t
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.MensagensAutomaticas m WHERE m.TenantId = t.Id AND m.Gatilho = 14);
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'025-pedido-de-nome.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'025-pedido-de-nome.sql',
                N'Pedido de nome quando o perfil do WhatsApp nao traz nome usavel: flag na Conversa, carimbo no Cliente e o gatilho 14.');
END
GO

COMMIT TRANSACTION;
GO

SELECT COL_LENGTH(N'dbo.Conversas', N'AguardandoNome')   AS ConversaTemFlag,
       COL_LENGTH(N'dbo.Clientes',  N'NomePerguntadoEm') AS ClienteTemCarimbo;
GO

SELECT Gatilho, Ativo, LEFT(Conteudo, 60) AS InicioDoTexto
  FROM dbo.MensagensAutomaticas
 WHERE Gatilho = 14
 ORDER BY TenantId;
GO

PRINT 'Migracao 025 concluida.';
GO
