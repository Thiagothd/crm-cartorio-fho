/* =============================================================================
   CRM-Cartorio — Protecao contra forca bruta no login (SEC-02)
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-29
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 001-schema.sql, 021-versao-do-schema.sql, 031-token-version-atendente.sql

   O QUE FAZ
       dbo.Atendentes += FalhasDeLoginConsecutivas INT      NOT NULL DEFAULT (0)
       dbo.Atendentes += NivelDeBloqueio           TINYINT  NOT NULL DEFAULT (0)
       dbo.Atendentes += BloqueadoAte              DATETIME2(3) NULL
       + CK_Atendentes_FalhasDeLogin   (FalhasDeLoginConsecutivas >= 0)
       + CK_Atendentes_NivelDeBloqueio (NivelDeBloqueio BETWEEN 0 AND 6)

       Nenhuma linha existente e alterada de valor: as tres colunas nascem no
       estado "conta destravada", que e exatamente o que toda conta e hoje.

   O PROBLEMA QUE ISTO RESOLVE (SEC-02)
       Ate aqui o login nao tinha limite nenhum de tentativas. O unico freio era
       o custo do PBKDF2 (210 000 iteracoes) — real, mas insuficiente sozinho:
       nao impede paralelismo nem um script paciente. Qualquer senha podia ser
       testada indefinidamente contra qualquer e-mail.

       O backoff e por CONTA e persistido aqui porque precisa sobreviver a
       reciclagem do App Pool e valer para o cluster inteiro. O limite por IP
       que acompanha esta entrega (Program.cs, rate limiter nativo) e memoria
       de processo e deliberadamente descartavel — ele so amortece; o que de
       fato para um ataque e o que esta nestas tres colunas.

   COMO OS TRES CAMPOS TRABALHAM JUNTOS
       FalhasDeLoginConsecutivas conta as falhas do ciclo atual. Na 5a, zera e
       abre um bloqueio. NivelDeBloqueio conta quantos bloqueios ja houve SEM
       login bem-sucedido no meio, e escolhe a duracao do proximo na progressao
       1, 2, 4, 8, 16 e 30 minutos (teto 6 = 30 min, para sempre). BloqueadoAte
       e o carimbo de ate quando a conta esta travada.

       Um login BEM-SUCEDIDO zera os tres — e por isso a progressao recomeca em
       1 minuto para quem de fato voltou a entrar, e so escala de verdade contra
       quem nunca acerta.

   TRES REGRAS QUE O CODIGO IMPOE E QUE NAO SAO OBVIAS
       1. Senha correta NAO fura bloqueio vigente. Enquanto BloqueadoAte estiver
          no futuro, o login e recusado mesmo com a senha certa; so a expiracao
          ou uma redefinicao administrativa liberam.
       2. Tentativa feita DURANTE um bloqueio nao conta e nao estende o bloqueio
          — do contrario um atacante manteria a conta travada para sempre so
          continuando a tentar.
       3. Conta INATIVA nao acumula lockout. Ela executa o PBKDF2 e responde
          igual a todas as outras, mas nao incrementa nada — sem isso, uma conta
          desligada podia ser martelada por semanas e voltaria da reativacao ja
          bloqueada, no topo da progressao, sem ninguem entender por que.

   POR QUE NAO PRECISA DE BACKFILL
       Ao contrario de 028 (EmAtendimentoHumano), aqui nao ha estado anterior a
       reconstruir: antes desta migracao ninguem estava bloqueado, porque
       bloqueio nao existia. Os defaults ja descrevem a verdade de toda linha
       existente.

   COMPATIBILIDADE DE DEPLOY — A ORDEM IMPORTA
       A migration 033 pode ser aplicada enquanto o binario ANTIGO esta rodando,
       pois ele ignora as novas colunas.

       Porem o binario SEC-02 NAO deve ser iniciado antes da aplicacao da
       migration 033.

       O motivo e direto: o binario novo MAPEIA e CONSULTA
       FalhasDeLoginConsecutivas, NivelDeBloqueio e BloqueadoAte em toda
       autenticacao. Subir o binario contra um banco sem essas colunas quebra o
       login inteiro — nao um caso de borda, mas a porta de entrada do sistema.

       Nao confundir com "nao precisa de backfill": a ausencia de backfill diz
       que nenhum DADO precisa ser corrigido antes, nao que a ordem seja
       indiferente. O ESQUEMA continua sendo pre-requisito do binario.

       Os dois workflows (deploy-teste.yml / deploy-producao.yml) ja garantem
       essa ordem: aplicam as migrations pendentes, abortam se alguma falhar, e
       so entao param o App Pool, copiam os arquivos e religam. Em deploy MANUAL,
       a mesma ordem tem de ser seguida a mao — ver docs/INSTALACAO.md, secao 2.

   IDEMPOTENCIA
       Cada coluna so entra quando ainda nao existe; cada CHECK so e criado
       quando ainda nao existe. Rodar o script de novo nao encontra nada para
       fazer.
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

IF OBJECT_ID(N'dbo.Atendentes', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode as migracoes anteriores antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

/* --- 1. As colunas ---------------------------------------------------------- */

IF COL_LENGTH(N'dbo.Atendentes', N'FalhasDeLoginConsecutivas') IS NULL
    ALTER TABLE dbo.Atendentes ADD FalhasDeLoginConsecutivas INT NOT NULL
        CONSTRAINT DF_Atendentes_FalhasDeLoginConsecutivas DEFAULT (0);
GO

IF COL_LENGTH(N'dbo.Atendentes', N'NivelDeBloqueio') IS NULL
    ALTER TABLE dbo.Atendentes ADD NivelDeBloqueio TINYINT NOT NULL
        CONSTRAINT DF_Atendentes_NivelDeBloqueio DEFAULT (0);
GO

IF COL_LENGTH(N'dbo.Atendentes', N'BloqueadoAte') IS NULL
    ALTER TABLE dbo.Atendentes ADD BloqueadoAte DATETIME2(3) NULL;
GO

/* --- 2. Os CHECKs ------------------------------------------------------------

   Precisam de GO separado dos ALTER TABLE acima: o SQL Server resolve nomes de
   coluna na compilacao do LOTE inteiro, entao uma coluna criada por ALTER TABLE
   so fica visivel a partir do proximo lote, mesmo dentro da mesma transacao
   (BEGIN TRANSACTION nao fecha lote, so GO fecha).

   O teto 6 do nivel esta no banco e nao so no codigo de proposito: e o
   invariante que garante que a progressao para em 30 minutos. Um bug futuro que
   incrementasse sem limite falharia aqui, alto e claro, em vez de silenciosamente
   trancar uma conta por um tempo que ninguem escolheu. -------------------------- */

IF OBJECT_ID(N'dbo.CK_Atendentes_FalhasDeLogin', N'C') IS NULL
    ALTER TABLE dbo.Atendentes WITH CHECK ADD CONSTRAINT CK_Atendentes_FalhasDeLogin
        CHECK (FalhasDeLoginConsecutivas >= 0);
GO

IF OBJECT_ID(N'dbo.CK_Atendentes_NivelDeBloqueio', N'C') IS NULL
    ALTER TABLE dbo.Atendentes WITH CHECK ADD CONSTRAINT CK_Atendentes_NivelDeBloqueio
        CHECK (NivelDeBloqueio BETWEEN 0 AND 6);
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'033-protecao-contra-forca-bruta-no-login.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'033-protecao-contra-forca-bruta-no-login.sql',
                N'SEC-02: backoff progressivo por conta no login (1, 2, 4, 8, 16 e 30 min a cada 5 falhas seguidas). Senha correta nao fura bloqueio vigente; tentativa durante bloqueio nao o estende; conta inativa nao acumula lockout. Acompanha limite por IP em memoria no Program.cs.');
END
GO

COMMIT TRANSACTION;
GO

SELECT COL_LENGTH(N'dbo.Atendentes', N'FalhasDeLoginConsecutivas') AS FalhasDeLoginConsecutivas,
       COL_LENGTH(N'dbo.Atendentes', N'NivelDeBloqueio') AS NivelDeBloqueio,
       COL_LENGTH(N'dbo.Atendentes', N'BloqueadoAte') AS BloqueadoAte;
GO

SELECT name, definition
  FROM sys.check_constraints
 WHERE parent_object_id = OBJECT_ID(N'dbo.Atendentes')
   AND name IN (N'CK_Atendentes_FalhasDeLogin', N'CK_Atendentes_NivelDeBloqueio');
GO

PRINT 'Migracao 033 concluida. Nenhuma conta foi bloqueada por esta migracao.';
GO
