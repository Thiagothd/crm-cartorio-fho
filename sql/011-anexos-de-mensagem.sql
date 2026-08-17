/* =============================================================================
   CRM-Cartorio — Anexo de midia nas mensagens
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-16
   Uso .......... SOMENTE em banco de DESENVOLVIMENTO (CRM_Cartorio_Dev)
   Depende de ... 001-schema.sql

   O QUE FAZ
       Acrescenta a dbo.Mensagens as quatro colunas que descrevem um arquivo
       anexado: nome original, tipo MIME, tamanho e caminho no disco.

   POR QUE O ARQUIVO NAO FICA NO BANCO
       Guardar o binario em VARBINARY(MAX) faria o .bak crescer com cada foto de
       documento que um cliente manda — e sao muitas, num cartorio. O arquivo vai
       para o disco do proprio servidor (nada de storage de terceiro, mesma
       soberania de dado que justifica o self-hosted) e a linha guarda so o
       CAMINHO RELATIVO a raiz configurada.

       Relativo, e nao absoluto: mover a pasta de anexos para outro disco amanha
       nao pode invalidar todas as linhas ja gravadas.

   CONSEQUENCIA PARA BACKUP — LEIA ANTES DE CONFIAR NO .bak
       O backup do banco NAO leva os anexos. Quem faz copia so do SQL Server
       restaura conversas com anexo quebrado. A rotina de backup (Fase 2D) tem de
       cobrir a pasta de anexos junto, e docs/BACKUP.md registra isso.

   NOME DO ARQUIVO NO DISCO
       E sempre {MensagemId}{extensao}, NUNCA o nome que veio do cliente. Nome de
       origem entra so como dado exibido: um "..\\..\\web.config" enviado por um
       cliente mal-intencionado nao pode virar caminho de gravacao.

   IDEMPOTENCIA
       Cada coluna e a constraint so nascem quando ainda nao existem. Rodar de
       novo nao muda nada.
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;

/* Obrigatorio para QUALQUER escrita em tabela que tenha indice filtrado, e
   dbo.Tenants ganhou um na migracao 010. O sqlcmd sobe com OFF por padrao. Esta
   linha fica aqui por habito de arquivo, mesmo que este script nao escreva em
   Tenants: o custo e nenhum e a lembranca vale. */
SET QUOTED_IDENTIFIER ON;
GO

IF DB_NAME() NOT LIKE '%_Dev%' AND DB_NAME() NOT LIKE '%Dev%'
BEGIN
    RAISERROR('Recusado: o nome do banco atual nao parece ser de desenvolvimento. Revise antes de rodar.', 16, 1);
    RETURN;
END
GO

IF OBJECT_ID(N'dbo.Mensagens', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode 001-schema.sql antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

/* Nome com que o arquivo chegou, para exibir no fio e para o download devolver
   algo reconhecivel. 260 = limite historico de caminho no Windows; nome maior
   que isso e truncado na gravacao, nao recusado. */
IF COL_LENGTH('dbo.Mensagens', 'ArquivoNome') IS NULL
    ALTER TABLE dbo.Mensagens ADD ArquivoNome NVARCHAR(260) NULL;
GO

/* O tipo declarado, ja conferido contra a lista de permitidos no servico. E ele
   que decide se o anexo abre na tela (imagem) ou baixa (documento). */
IF COL_LENGTH('dbo.Mensagens', 'ArquivoTipoMime') IS NULL
    ALTER TABLE dbo.Mensagens ADD ArquivoTipoMime VARCHAR(150) NULL;
GO

IF COL_LENGTH('dbo.Mensagens', 'ArquivoTamanhoBytes') IS NULL
    ALTER TABLE dbo.Mensagens ADD ArquivoTamanhoBytes BIGINT NULL;
GO

/* Caminho RELATIVO a raiz configurada, no formato {tenant}/{ano}/{mes}/{id}.ext.
   A subdivisao por ano e mes existe para nao terminar com uma pasta unica de
   centenas de milhares de arquivos, que trava qualquer listagem no servidor. */
IF COL_LENGTH('dbo.Mensagens', 'ArquivoCaminho') IS NULL
    ALTER TABLE dbo.Mensagens ADD ArquivoCaminho NVARCHAR(400) NULL;
GO

/* Ou o anexo esta inteiro, ou nao ha anexo.
   Meia linha gravada — caminho sem tipo, tipo sem caminho — produziria um fio
   que promete um arquivo e um download que devolve 404. E o tipo de defeito que
   so aparece na conversa de um cliente de verdade. */
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Mensagens_Arquivo')
    ALTER TABLE dbo.Mensagens ADD CONSTRAINT CK_Mensagens_Arquivo CHECK
    (
        (ArquivoCaminho IS NULL AND ArquivoNome IS NULL AND ArquivoTipoMime IS NULL AND ArquivoTamanhoBytes IS NULL)
        OR
        (ArquivoCaminho IS NOT NULL AND ArquivoNome IS NOT NULL AND ArquivoTipoMime IS NOT NULL AND ArquivoTamanhoBytes IS NOT NULL)
    );
GO

COMMIT TRANSACTION;
GO

SELECT COUNT(*) AS MensagensComAnexo FROM dbo.Mensagens WHERE ArquivoCaminho IS NOT NULL;
GO

PRINT 'Migracao 011 concluida. Configure CanalWhatsApp:RaizDosAnexos e inclua a pasta no backup.';
GO
