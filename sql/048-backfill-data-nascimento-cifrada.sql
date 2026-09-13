/* =============================================================================
   CRM-Cartorio — Backfill de Clientes.DataNascimentoCifrada (blindagem LGPD, PR3)
   -----------------------------------------------------------------------------
   Gerado em .... 2026-09-12
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 047-cifra-em-repouso.sql

   O QUE FAZ
       Copia, uma unica vez, o valor da coluna antiga Clientes.DataNascimento
       (tipo date, orfa desde a 047) para a coluna nova
       Clientes.DataNascimentoCifrada, no formato ISO yyyy-MM-dd (CONVERT
       estilo 23) — exatamente o texto que ConversorDeDataCifrada grava e le
       quando a cifra esta dormente. So toca linhas com a antiga preenchida e
       a nova ainda vazia.

   O PROBLEMA QUE ISTO RESOLVE
       A 047 criou DataNascimentoCifrada VAZIA e o codigo passou a ler so
       ela: todo cliente que ja tinha data de nascimento cadastrada passaria
       a aparecer SEM data na tela — perda de visibilidade de dado real,
       sem nenhum dado ter sido apagado. Este script devolve o valor pela
       coluna nova. A coluna antiga continua orfa e intocada (limpeza numa
       migration futura, so depois de confirmado que nada mais a le).

   POR QUE EM CLARO, E NAO JA CIFRADO
       Um .sql versionado nunca pode ter a chave de cifra. O valor entra em
       claro — o mesmo estado de toda coluna cifrada num ambiente onde a
       cifra ainda esta dormente — e --cifrar-dados-legados (rodado a parte,
       depois da chave provisionada) o cifra junto com o resto, porque ele
       nao comeca com $c1$.

   COMPATIBILIDADE DE DEPLOY
       Expand-only: UPDATE que so preenche uma coluna que o binario N-1 nao
       conhece nem le. Nenhuma coluna removida ou alterada; a antiga
       DataNascimento continua identica, entao N-1 (se ainda rodar) continua
       lendo a data de onde sempre leu. Rollback automatico do binario e
       seguro. Direcao forward-only: o binario NOVO le a coluna nova, e sem
       este backfill mostraria nulo onde N-1 mostrava data.

   IDEMPOTENCIA
       O WHERE exige DataNascimentoCifrada IS NULL — rodar de novo nao toca
       linha ja preenchida (em claro ou ja cifrada).
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

IF COL_LENGTH(N'dbo.Clientes', N'DataNascimentoCifrada') IS NULL
   OR COL_LENGTH(N'dbo.Clientes', N'DataNascimento') IS NULL
BEGIN
    RAISERROR('Colunas DataNascimento/DataNascimentoCifrada ausentes. Rode 047-cifra-em-repouso.sql antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

UPDATE dbo.Clientes
   SET DataNascimentoCifrada = CONVERT(VARCHAR(10), DataNascimento, 23)
 WHERE DataNascimento IS NOT NULL
   AND DataNascimentoCifrada IS NULL;
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'048-backfill-data-nascimento-cifrada.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'048-backfill-data-nascimento-cifrada.sql',
                N'Blindagem LGPD: copia Clientes.DataNascimento (date, orfa) para DataNascimentoCifrada (ISO yyyy-MM-dd, em claro) onde a nova ainda estava vazia — sem isso, clientes com data cadastrada apareceriam sem data apos a 047. --cifrar-dados-legados cifra o valor depois.');
END
GO

COMMIT TRANSACTION;
GO

PRINT 'Migracao 048 concluida. DataNascimentoCifrada preenchida a partir de DataNascimento onde estava vazia.';
GO
