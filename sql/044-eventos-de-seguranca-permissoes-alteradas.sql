/* =============================================================================
   CRM-Cartorio — evento de permissoes alteradas (permissoes granulares)
   -----------------------------------------------------------------------------
   Gerado em .... 2026-09-08
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 001-schema.sql, 021-versao-do-schema.sql, 034-mfa-totp.sql,
                   037-reemissao-de-ticket-de-emergencia.sql,
                   043-permissoes-do-atendente.sql

   O QUE FAZ
       Alarga CK_EventosDeSeguranca_Tipo de "BETWEEN 1 AND 11" para
       "BETWEEN 1 AND 12", liberando o valor 12
       (TipoDeEventoDeSeguranca.PermissoesAlteradas). Nenhuma coluna nova,
       nenhuma linha existente e alterada.

   POR QUE ESTA MIGRATION E REALMENTE NECESSARIA (nao so codigo)
       Mesmo motivo da 037: CK_EventosDeSeguranca_Tipo e uma CHECK CONSTRAINT
       gravada no schema. O proprio banco recusa fisicamente um INSERT com
       Tipo = 12 ate este ALTER rodar.

   O PROBLEMA QUE ISTO RESOLVE
       AtendenteService.DefinirPermissoesAsync (Modules/Usuarios/Domain/
       CatalogoDePermissoes.cs — permissoes granulares por atendente) registra
       um EventosDeSeguranca com Tipo = PermissoesAlteradas (12) toda vez que
       alguem com equipe.permissoes ajusta o que outro atendente acessa — o
       FATO, nunca as chaves concedidas (Motivo fica sempre nulo aqui; ver
       Modules/Usuarios/README.md, "Permissoes granulares").

   POR QUE E UM ARQUIVO NOVO, NAO UM AJUSTE NA 037
       037-reemissao-de-ticket-de-emergencia.sql (que alargou a CHECK para
       1..11) ja foi aplicada em CRM_Cartorio_Dev. Editar a definicao original
       no lugar faria o script achar que ja rodou (rastreado por NOME DE
       ARQUIVO em dbo.VersaoDoSchema) e pular o ALTER novo justamente no unico
       banco onde isso importa agora. Mesma disciplina de 001..043: migration
       aplicada e registro historico, nunca edita no lugar.

   POR QUE NAO PRECISA DE BACKFILL
       Nenhuma linha existente tem Tipo = 12 (o valor so passa a existir a
       partir do primeiro uso de DefinirPermissoesAsync, criado nesta mesma
       rodada). Alargar o intervalo permitido nao muda o significado de
       nenhuma linha ja gravada.

   COMPATIBILIDADE DE DEPLOY
       Compativel com o binario ANTIGO (sem permissoes granulares) rodando:
       ele nunca grava Tipo = 12, entao a CHECK mais larga e transparente para
       ele. O binario NOVO (com DefinirPermissoesAsync) nao deve ser iniciado
       antes desta migration estar aplicada — o primeiro INSERT com Tipo = 12
       falharia contra a CHECK antiga, mesmo padrao de 033..037 (ver
       docs/DEPLOY.md, secao 1.2).

   IDEMPOTENCIA
       DROP + ADD da mesma CHECK constraint, sempre para a MESMA definicao
       final (1..12) — rodar o script de novo (ou dez vezes) chega no mesmo
       estado, mesmo que a constraint ja exista. Nao ha "meio-termo" possivel
       para uma CHECK constraint: ela existe com uma definicao ou nao existe.
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

IF OBJECT_ID(N'dbo.EventosDeSeguranca', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode as migracoes anteriores antes deste script.', 16, 1);
    RETURN;
END
GO

IF COL_LENGTH(N'dbo.Atendentes', N'Permissoes') IS NULL
BEGIN
    RAISERROR('043-permissoes-do-atendente.sql precisa ter rodado antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

-- DROP + ADD sempre para a MESMA definicao final (1..12): idempotente no
-- sentido de "chega no mesmo estado", nao no sentido de "pula se ja existe"
-- — CHECK constraint nao tem meio-termo entre existir e nao existir.
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_EventosDeSeguranca_Tipo')
    ALTER TABLE dbo.EventosDeSeguranca DROP CONSTRAINT CK_EventosDeSeguranca_Tipo;
GO

ALTER TABLE dbo.EventosDeSeguranca ADD CONSTRAINT CK_EventosDeSeguranca_Tipo CHECK (Tipo BETWEEN 1 AND 12);
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'044-eventos-de-seguranca-permissoes-alteradas.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'044-eventos-de-seguranca-permissoes-alteradas.sql',
                N'Permissoes granulares: alarga CK_EventosDeSeguranca_Tipo para 1..12, liberando PermissoesAlteradas (12) — gravado por AtendenteService.DefinirPermissoesAsync toda vez que a meta-permissao equipe.permissoes e usada para ajustar o acesso de outro atendente. Binario com permissoes granulares exige esta migration antes de subir (docs/DEPLOY.md 1.2).');
END
GO

COMMIT TRANSACTION;
GO

SELECT OBJECT_DEFINITION(object_id) AS CK_EventosDeSeguranca_Tipo
FROM sys.check_constraints
WHERE name = N'CK_EventosDeSeguranca_Tipo';
GO

PRINT 'Migracao 044 concluida. Nenhuma linha existente foi alterada.';
GO
