/* =============================================================================
   CRM-Cartorio — Vinculo entre instancia do gateway e serventia
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-16
   Uso .......... SOMENTE em banco de DESENVOLVIMENTO (CRM_Cartorio_Dev)
   Depende de ... 001-schema.sql

   O QUE FAZ
       Acrescenta dbo.Tenants.WhatsappInstancia — o nome da instancia pareada no
       gateway Evolution.

   POR QUE ESTA COLUNA EXISTE
       O webhook do WhatsApp e um endpoint ANONIMO: o gateway nao carrega JWT,
       entao nao ha token de onde ler o TenantId. O payload traz o nome da
       instancia, e e por ele que o recebimento descobre de qual cartorio e a
       mensagem. UMA INSTANCIA = UM TENANT.

       Na Fase 1 existe um tenant so, e daria para fixar o valor no codigo. Nao
       fixamos de proposito: todo dado de tenant ja filtra por TenantId desde o
       inicio (docs/CONVENCOES.md, 7.1), e abrir excecao justamente no ponto de ENTRADA
       das mensagens seria criar, na Fase 3, a reescrita mais cara possivel.

   PILOTO, NAO CANAL OFICIAL
       A instancia usa um numero dedicado ao ambiente de demonstracao/teste. Nao
       e o numero publico do cartorio. Ver docs/INSTALACAO.md.

   IDEMPOTENCIA
       A coluna e o indice so sao criados quando ainda nao existem. Rodar de novo
       nao muda nada.
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;

/* Obrigatorio para o indice FILTRADO la embaixo: o SQL Server recusa criar
   indice com clausula WHERE quando QUOTED_IDENTIFIER esta OFF, e o sqlcmd sobe
   com OFF por padrao. Sem esta linha o script falha em quem roda pelo terminal
   e funciona em quem roda pelo SSMS — o pior tipo de diferenca. */
SET QUOTED_IDENTIFIER ON;
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

IF COL_LENGTH('dbo.Tenants', 'WhatsappInstancia') IS NULL
    ALTER TABLE dbo.Tenants ADD WhatsappInstancia VARCHAR(120) NULL;
GO

/* Duas serventias nao podem apontar para a mesma instancia: a mensagem cairia
   no atendimento da serventia errada. Filtrado para permitir NULL em quem ainda
   nao conectou o canal. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_Tenants_WhatsappInstancia')
    CREATE UNIQUE INDEX UQ_Tenants_WhatsappInstancia
        ON dbo.Tenants (WhatsappInstancia)
        WHERE WhatsappInstancia IS NOT NULL;
GO

COMMIT TRANSACTION;
GO

SELECT RazaoSocial, WhatsappInstancia FROM dbo.Tenants;
GO

PRINT 'Migracao 010 concluida. Vincule a instancia pela tela de Configuracoes -> WhatsApp.';
GO
