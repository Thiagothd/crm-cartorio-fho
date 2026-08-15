/* =============================================================================
   CRM-Cartorio — Aparencia do fio de mensagens
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-15
   Uso .......... SOMENTE em banco de DESENVOLVIMENTO (CRM_Cartorio_Dev)
   Depende de ... 001-schema.sql

   O QUE FAZ
       Acrescenta dbo.Tenants.AparenciaDaConversa — a textura de fundo do fio.

   VALORES
       1 PapelLiso (padrao), 2 SeloRepetido, 3 PautaFina

       Enum, e nao tabela: cada valor corresponde a uma classe de estilo que
       precisa existir no CSS. Textura nova exige release, e isso e proposital —
       um campo livre aqui deixaria alguem colar papel de parede no fundo de um
       livro de registro.

   POR TENANT, NAO POR ATENDENTE
       E a cara do cartorio. A mesma tela vista por duas pessoas da mesma
       serventia nao deveria parecer dois produtos.

   IDEMPOTENCIA
       A coluna so e criada quando ainda nao existe. Rodar de novo nao muda nada.
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF DB_NAME() NOT LIKE '%_Dev%' AND DB_NAME() NOT LIKE '%Dev%'
BEGIN
    RAISERROR('Recusado: o nome do banco atual nao parece ser de desenvolvimento. Revise antes de rodar.', 16, 1);
    RETURN;
END
GO

IF OBJECT_ID(N'dbo.Tenants', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode 001-schema.sql antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

IF COL_LENGTH('dbo.Tenants', 'AparenciaDaConversa') IS NULL
    ALTER TABLE dbo.Tenants ADD AparenciaDaConversa TINYINT NOT NULL
        CONSTRAINT DF_Tenants_AparenciaDaConversa DEFAULT (1) WITH VALUES;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Tenants_AparenciaDaConversa')
    ALTER TABLE dbo.Tenants WITH CHECK ADD CONSTRAINT CK_Tenants_AparenciaDaConversa
        CHECK (AparenciaDaConversa IN (1, 2, 3));
GO

COMMIT TRANSACTION;
GO

SELECT RazaoSocial, AparenciaDaConversa FROM dbo.Tenants;
GO

PRINT 'Migracao 008 concluida. Aparencia padrao: 1 (Papel liso).';
GO
