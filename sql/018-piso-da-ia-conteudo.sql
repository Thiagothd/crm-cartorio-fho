/* =============================================================================
   CRM-Cartorio — Piso da IA: conteudo das 3 mensagens novas
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-17
   Uso .......... SOMENTE em banco de DESENVOLVIMENTO (CRM_Cartorio_Dev)
   Depende de ... 017-piso-da-ia-schema.sql

   O QUE FAZ
       Insere as 3 mensagens automaticas novas da Fase 2E.1:
         8  MenuDeContinuacao — "posso ajudar em mais algo?" apos o checklist
         9  PedidoDeAtendente — encaminha para humano no MEIO do atendimento
         10 Despedida         — cliente escolheu encerrar pelo menu

   ESPELHO OBRIGATORIO
       O mesmo conteudo esta em
       Infrastructure/Persistence/Seeding/CatalogoDeDemonstracao.cs — os dois
       precisam bater caractere a caractere, mesma disciplina de 002/015/016.

   IDEMPOTENCIA
       Guardado por um IF NOT EXISTS no primeiro Id novo.
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF DB_NAME() NOT LIKE '%_Dev%' AND DB_NAME() NOT LIKE '%Dev%' AND DB_NAME() <> N'CRM_Cartorio_Demo'
BEGIN
    RAISERROR('Recusado: o nome do banco atual nao parece ser de desenvolvimento. Revise antes de rodar.', 16, 1);
    RETURN;
END
GO

IF OBJECT_ID(N'dbo.MensagensAutomaticas', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode as migracoes anteriores antes deste script.', 16, 1);
    RETURN;
END
GO

IF EXISTS (SELECT 1 FROM dbo.MensagensAutomaticas WHERE Id = '0199B2C0-0006-7000-8000-000000000009')
BEGIN
    PRINT 'Migracao 018 ja aplicada — nada a fazer.';
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

DECLARE @agora    DATETIME2(3)     = SYSUTCDATETIME();
DECLARE @tenantId UNIQUEIDENTIFIER = '0199B2C0-0000-7000-8000-000000000001';
DECLARE @lf       CHAR(1)          = CHAR(10);

INSERT INTO dbo.MensagensAutomaticas
    (Id, TenantId, Gatilho, EtapaAlvoId, Conteudo, Ativo, CriadoEm, AtualizadoEm)
VALUES
    -- Gatilho 8 = MenuDeContinuacao
    ('0199B2C0-0006-7000-8000-000000000009', @tenantId, 8, NULL,
     N'Posso ajudar em mais alguma coisa, {NomeCliente}?' + @lf + @lf +
     N'1 - Ver outro serviço' + @lf +
     N'2 - Trocar de setor' + @lf +
     N'3 - Falar com um atendente' + @lf +
     N'4 - Encerrar atendimento',
     1, @agora, @agora),

    -- Gatilho 9 = PedidoDeAtendente
    ('0199B2C0-0006-7000-8000-000000000010', @tenantId, 9, NULL,
     N'Certo, {NomeCliente}! Um de nossos atendentes vai continuar seu atendimento (protocolo {Protocolo}) em instantes.',
     1, @agora, @agora),

    -- Gatilho 10 = Despedida
    ('0199B2C0-0006-7000-8000-000000000011', @tenantId, 10, NULL,
     N'Foi um prazer atender você, {NomeCliente}! 👋' + @lf + @lf +
     N'Seu protocolo {Protocolo} fica registrado — qualquer necessidade, é só mandar mensagem por aqui.',
     1, @agora, @agora);
GO

COMMIT TRANSACTION;
GO

SELECT Gatilho, LEFT(Conteudo, 50) AS InicioDoTexto FROM dbo.MensagensAutomaticas WHERE Gatilho IN (8,9,10) ORDER BY Gatilho;
GO

PRINT 'Migracao 018 concluida.';
GO
