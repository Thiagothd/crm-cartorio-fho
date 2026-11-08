/* =============================================================================
   CRM-Cartorio — Cargo na serventia, separado do perfil de acesso
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-14
   Uso .......... SOMENTE em banco de DESENVOLVIMENTO (CRM_Cartorio_Dev)
   Depende de ... 001-schema.sql

   O QUE FAZ
       Acrescenta dbo.Atendentes.Cargo e preenche os quatro atendentes semeados.

   POR QUE
       Cargo e perfil estavam fundidos numa coluna so, e nao sao a mesma pergunta:

           Cargo  ... o que a pessoa E no cartorio. Informacao funcional, existe
                      fora do software, nao concede nada.
           Perfil ... o que a pessoa PODE FAZER no sistema. Permissao, so existe
                      aqui dentro.

       Fundidos, um escrevente nao podia receber permissao de supervisao sem
       aparecer como "Supervisor" — cargo que nao existe em cartorio —, e o titular
       nao podia ter acesso restrito sem parecer rebaixado na tela.

       Resolvido agora porque o custo so cresce: cada tela e cada relatorio que
       passasse a ler o campo fundido aumentaria o preco da separacao depois
       (docs/REQUISITOS.md, pendencia 14).

   VALORES
       0 NaoInformado, 1 Titular, 2 Substituto, 3 EscreventeAutorizado,
       4 Escrevente, 5 Auxiliar

       Enum, e nao tabela: os cargos vem da estrutura da Lei 8.935/94, nao da
       preferencia do cartorio. O que e configuravel por tenant e quem ocupa cada um
       — isso se faz pela tela de Atendentes.

   IDEMPOTENCIA
       A coluna so e criada se nao existir. Os UPDATE gravam valor final e sao por
       Id: rodar de novo nao muda nada.
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF DB_NAME() NOT LIKE '%_Dev%' AND DB_NAME() NOT LIKE '%Dev%' AND DB_NAME() <> N'CRM_Cartorio_Demo'
BEGIN
    RAISERROR('Recusado: o nome do banco atual nao parece ser de desenvolvimento. Revise antes de rodar.', 16, 1);
    RETURN;
END
GO

IF OBJECT_ID(N'dbo.Atendentes', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode 001-schema.sql antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

/* Nasce NaoInformado para todo mundo: cargo e informacao que o cartorio preenche
   quando quiser, e nunca deve bloquear um cadastro de atendente. */
IF COL_LENGTH('dbo.Atendentes', 'Cargo') IS NULL
    ALTER TABLE dbo.Atendentes ADD Cargo TINYINT NOT NULL
        CONSTRAINT DF_Atendentes_Cargo DEFAULT (0) WITH VALUES;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Atendentes_Cargo')
    ALTER TABLE dbo.Atendentes WITH CHECK ADD CONSTRAINT CK_Atendentes_Cargo
        CHECK (Cargo IN (0, 1, 2, 3, 4, 5));
GO

DECLARE @agora DATETIME2(3) = SYSUTCDATETIME();

/* Titular da delegacao (nome ficticio). */
UPDATE dbo.Atendentes SET Cargo = 1, AtualizadoEm = @agora
WHERE Id = '0199B2C0-0003-7000-8000-000000000005';

/* Substituto (nome ficticio). */
UPDATE dbo.Atendentes SET Cargo = 2, AtualizadoEm = @agora
WHERE Id = '0199B2C0-0003-7000-8000-000000000001';

/* Os dois eixos separados, na pratica: cargo de escrevente autorizado com perfil
   de supervisao no painel. Antes isso era impossivel de representar.
   NOME FICTICIO (REQUISITOS.md, pendencia 5). */
UPDATE dbo.Atendentes SET Cargo = 3, AtualizadoEm = @agora
WHERE Id = '0199B2C0-0003-7000-8000-000000000002';

/* NOME FICTICIO (REQUISITOS.md, pendencia 5). */
UPDATE dbo.Atendentes SET Cargo = 4, AtualizadoEm = @agora
WHERE Id = '0199B2C0-0003-7000-8000-000000000003';
GO

COMMIT TRANSACTION;
GO

SELECT Nome, Perfil, Cargo FROM dbo.Atendentes ORDER BY Cargo, Nome;
GO

PRINT 'Migracao 007 concluida. Cargo e perfil agora sao colunas independentes.';
GO
