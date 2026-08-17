/* =============================================================================
   CRM-Cartorio — Atos candidatos do catalogo (Registro Civil + Notas)
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-17
   Uso .......... SOMENTE em banco de DESENVOLVIMENTO (CRM_Cartorio_Dev)
   Depende de ... 001-schema.sql, 002-seed-simulado.sql

   O QUE FAZ
       Acrescenta ao catalogo os 9 atos candidatos que docs/REQUISITOS.md, secao
       5.6, tinha deixado de fora de proposito ("inventar prazo e nome de ato
       seria repetir o erro dos cinco setores"). Decisao explicita do Thiago
       nesta sessao (2026-08-17): adicionar agora como suposicao defensavel,
       documentada como tal, para o cartório parceiro ver o catalogo completo ja
       funcionando em vez de decidir sobre uma lista pela metade.

   ATRIBUICAO JA CONFIRMADA, PRAZO NAO
       Os 9 atos SAO atribuicao desta serventia (Registro Civil das Pessoas
       Naturais e Tabelionato de Notas — as unicas duas, CONTEXTO.md secao 2.2)
       — diferente da Notificacao Extrajudicial (id 14), cuja propria
       ATRIBUICAO ainda esta em duvida. O que falta aqui e so o cartório parceiro confirmar
       prazo (SLA) e a redacao do nome.

   PRAZOS SAO CHUTE PLAUSIVEL, CALIBRADO CONTRA OS ATOS JA EXISTENTES
       Habilitacao de casamento usa 360h (15 dias) porque o prazo minimo de
       proclamas e LEGAL (Lei 6.015/73, art. 67), nao suposicao solta. Registro
       de nascimento e de obito seguem o padrao dos atos rapidos de balcao ja
       cadastrados (reconhecimento de firma, autenticacao: 2h, fora do calculo
       de dias uteis). Inventario e partilha e o mais longo do catalogo de
       proposito: reune inventario extrajudicial de verdade, com certidoes e
       anuencia de todos os herdeiros. AGUARDA VALIDACAO DO CARTÓRIO PARCEIRO
       (REQUISITOS.md, 5.6).

   IDENTIFICADORES
       Os GUIDs abaixo sao os mesmos de
       Infrastructure/Persistence/Seeding/CatalogoDeDemonstracao.cs — os dois
       precisam continuar batendo, exatamente como 002 ja documenta para o
       resto do seed.

   IDEMPOTENCIA
       Um IF NOT EXISTS cobre o bloco inteiro: roda tudo ou nada, e rodar de
       novo nao duplica.
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF DB_NAME() NOT LIKE '%_Dev%' AND DB_NAME() NOT LIKE '%Dev%'
BEGIN
    RAISERROR('Recusado: o nome do banco atual nao parece ser de desenvolvimento. Revise antes de rodar.', 16, 1);
    RETURN;
END
GO

IF OBJECT_ID(N'dbo.TiposServico', N'U') IS NULL OR OBJECT_ID(N'dbo.PrazosServico', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode 001-schema.sql e 002-seed-simulado.sql antes deste script.', 16, 1);
    RETURN;
END
GO

IF EXISTS (SELECT 1 FROM dbo.TiposServico WHERE Id = '0199B2C0-0004-7000-8000-000000000010')
BEGIN
    PRINT 'Migracao 013 ja aplicada — nada a fazer.';
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

DECLARE @agora     DATETIME2(3)     = SYSUTCDATETIME();
DECLARE @tenantId  UNIQUEIDENTIFIER = '0199B2C0-0000-7000-8000-000000000001';

/* -----------------------------------------------------------------------------
   Registro Civil das Pessoas Naturais — 4 atos candidatos
   Categoria 2 = RegistroCivil (CategoriaServico.cs)
   -------------------------------------------------------------------------- */
INSERT INTO dbo.TiposServico (Id, TenantId, Nome, Codigo, Categoria, Descricao, Ativo, CriadoEm, AtualizadoEm)
VALUES
    ('0199B2C0-0004-7000-8000-000000000010', @tenantId, N'Habilitação de casamento', 'RC-HABIL', 2, NULL, 1, @agora, @agora),
    ('0199B2C0-0004-7000-8000-000000000011', @tenantId, N'Averbação em registro civil', 'RC-AVERB', 2, NULL, 1, @agora, @agora),
    ('0199B2C0-0004-7000-8000-000000000012', @tenantId, N'Registro de nascimento', 'RC-REGNASC', 2, NULL, 1, @agora, @agora),
    ('0199B2C0-0004-7000-8000-000000000013', @tenantId, N'Registro de óbito', 'RC-REGOBI', 2, NULL, 1, @agora, @agora);

/* -----------------------------------------------------------------------------
   Tabelionato de Notas — 5 atos candidatos
   Categoria 1 = Notas (CategoriaServico.cs)
   -------------------------------------------------------------------------- */
INSERT INTO dbo.TiposServico (Id, TenantId, Nome, Codigo, Categoria, Descricao, Ativo, CriadoEm, AtualizadoEm)
VALUES
    ('0199B2C0-0004-7000-8000-000000000015', @tenantId, N'Ata notarial', 'NOT-ATA', 1, NULL, 1, @agora, @agora),
    ('0199B2C0-0004-7000-8000-000000000016', @tenantId, N'Testamento público', 'NOT-TESTAM', 1, NULL, 1, @agora, @agora),
    ('0199B2C0-0004-7000-8000-000000000017', @tenantId, N'Escritura de inventário e partilha', 'NOT-EINV', 1, NULL, 1, @agora, @agora),
    ('0199B2C0-0004-7000-8000-000000000018', @tenantId, N'Escritura de divórcio consensual', 'NOT-EDIV', 1, NULL, 1, @agora, @agora),
    ('0199B2C0-0004-7000-8000-000000000019', @tenantId, N'Escritura declaratória de união estável', 'NOT-EUNI', 1, NULL, 1, @agora, @agora);

/* -----------------------------------------------------------------------------
   SLAs — prazo em HORAS. CHUTE PLAUSIVEL, NAO DADO REAL (ver cabecalho).
   -------------------------------------------------------------------------- */
INSERT INTO dbo.PrazosServico
    (Id, TenantId, TipoServicoId, PrazoHoras, PercentualAlerta, ApenasDiasUteis, Ativo, CriadoEm, AtualizadoEm)
VALUES
    ('0199B2C0-0005-7000-8000-000000000010', @tenantId, '0199B2C0-0004-7000-8000-000000000010', 360, 80, 1, 1, @agora, @agora),
    ('0199B2C0-0005-7000-8000-000000000011', @tenantId, '0199B2C0-0004-7000-8000-000000000011',  48, 75, 1, 1, @agora, @agora),
    ('0199B2C0-0005-7000-8000-000000000012', @tenantId, '0199B2C0-0004-7000-8000-000000000012',   2, 70, 0, 1, @agora, @agora),
    ('0199B2C0-0005-7000-8000-000000000013', @tenantId, '0199B2C0-0004-7000-8000-000000000013',   2, 70, 0, 1, @agora, @agora),
    ('0199B2C0-0005-7000-8000-000000000015', @tenantId, '0199B2C0-0004-7000-8000-000000000015',  72, 75, 1, 1, @agora, @agora),
    ('0199B2C0-0005-7000-8000-000000000016', @tenantId, '0199B2C0-0004-7000-8000-000000000016', 240, 80, 1, 1, @agora, @agora),
    ('0199B2C0-0005-7000-8000-000000000017', @tenantId, '0199B2C0-0004-7000-8000-000000000017', 480, 80, 1, 1, @agora, @agora),
    ('0199B2C0-0005-7000-8000-000000000018', @tenantId, '0199B2C0-0004-7000-8000-000000000018', 240, 80, 1, 1, @agora, @agora),
    ('0199B2C0-0005-7000-8000-000000000019', @tenantId, '0199B2C0-0004-7000-8000-000000000019', 240, 80, 1, 1, @agora, @agora);

COMMIT TRANSACTION;
GO

SELECT COUNT(*) AS TotalDeServicos FROM dbo.TiposServico;
GO

PRINT 'Migracao 013 concluida — 9 atos candidatos adicionados.';
GO
