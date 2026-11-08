/* =============================================================================
   CRM-Cartorio — evento de reemissao de ticket de emergencia (SEC-03)
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-31
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 001-schema.sql, 021-versao-do-schema.sql, 034-mfa-totp.sql,
                   035-mfa-pendente-token-version.sql,
                   036-rematricula-obrigatoria-apos-emergencia.sql

   O QUE FAZ
       Alarga CK_EventosDeSeguranca_Tipo de "BETWEEN 1 AND 10" para
       "BETWEEN 1 AND 11", liberando o valor 11
       (TipoDeEventoDeSeguranca.TicketDeRematriculaReemitido). Nenhuma coluna
       nova, nenhuma linha existente e alterada.

   POR QUE ESTA MIGRATION E REALMENTE NECESSARIA (nao so codigo)
       CK_EventosDeSeguranca_Tipo e uma CHECK CONSTRAINT gravada no schema —
       diferente de TentativasDeCodigo/UltimoAcessoEm/RematriculaObrigatoria-
       AposEmergencia (bloqueadores 13-19), que reusavam coluna ja existente,
       aqui o proprio BANCO recusaria fisicamente um INSERT com Tipo = 11 ate
       este ALTER rodar — nao e uma questao de codigo aplicativo, e uma
       restricao de dados que so o SQL Server pode relaxar.

   O PROBLEMA QUE ISTO RESOLVE (SEC-03, bloqueador B20 da setima revisao
   independente do PR #46)
       O reset de emergencia (break-glass) emite um ticket de rematricula com
       TTL de 24h. Se esse ticket for perdido, expirar ou ficar inutilizavel
       antes da rematricula, o alvo fica preso de novo: bootstrap continua
       fechado (outro privilegiado com MFA ja existe), o Administrador
       nao-Titular continua proibido de resetar/emitir para o Titular, e o
       login continua bloqueado por RematriculaObrigatoriaAposEmergencia —
       um deadlock igual ao que o break-glass original resolveu.

       FerramentaDeReemissaoDeTicketDeEmergencia (novo,
       Infrastructure/Security/FerramentaDeReemissaoDeTicketDeEmergencia.cs)
       resolve isto emitindo um ticket NOVO sem tocar MfaAtivo nem
       RematriculaObrigatoriaAposEmergencia — SO quando a conta ja esta
       numa recuperacao de emergencia aberta (MfaAtivo=false E
       RematriculaObrigatoriaAposEmergencia=true) e nenhum ator normal esta
       apto a resolver pelo caminho comum. Precisa do PROPRIO tipo de
       evento (TicketDeRematriculaReemitido, nao ResetDeEmergencia de novo)
       para um auditor conseguir distinguir "uma emergencia foi aberta" de
       "um ticket foi reemitido dentro de uma emergencia ja aberta" — sinal
       util para saber se o TTL de 24h esta curto demais ou o processo
       operacional esta lento.

   COMO O CODIGO USA ISTO
       FerramentaDeReemissaoDeTicketDeEmergencia.TentarReemitirTicketAsync
       grava EventosDeSeguranca com Tipo = TicketDeRematriculaReemitido
       (11) na MESMA transacao SERIALIZABLE que invalida o(s) desafio(s) de
       rematricula anteriores (nao consumidos) e grava o novo.

   POR QUE E UM ARQUIVO NOVO, NAO UM AJUSTE NA 034
       034-mfa-totp.sql (que criou CK_EventosDeSeguranca_Tipo) ja foi
       aplicada em CRM_Cartorio_Dev. Editar a definicao original no lugar
       faria o script achar que ja rodou (rastreado por NOME DE ARQUIVO em
       dbo.VersaoDoSchema) e pular o ALTER novo justamente no unico banco
       onde isso importa agora. Mesma disciplina de 001..036: migration
       aplicada e registro historico, nunca edita no lugar.

   POR QUE NAO PRECISA DE BACKFILL
       Nenhuma linha existente tem Tipo = 11 (o valor so passa a existir a
       partir do primeiro uso do comando novo, criado nesta mesma rodada).
       Alargar o intervalo permitido nao muda o significado de nenhuma linha
       ja gravada.

   COMPATIBILIDADE DE DEPLOY
       Compativel com o binario ANTIGO (sem o bloqueador B20) rodando: ele
       nunca grava Tipo = 11, entao a CHECK mais larga e transparente para
       ele. O binario NOVO (com FerramentaDeReemissaoDeTicketDeEmergencia)
       nao deve ser iniciado antes desta migration estar aplicada — o
       primeiro INSERT com Tipo = 11 falharia contra a CHECK antiga, mesmo
       padrao de 033/034/035/036 (ver docs/INSTALACAO.md, secao 1.2).

   IDEMPOTENCIA
       DROP + ADD da mesma CHECK constraint, sempre para a MESMA definicao
       final (1..11) — rodar o script de novo (ou dez vezes) chega no mesmo
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

IF COL_LENGTH(N'dbo.Atendentes', N'RematriculaObrigatoriaAposEmergencia') IS NULL
BEGIN
    RAISERROR('036-rematricula-obrigatoria-apos-emergencia.sql precisa ter rodado antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

-- DROP + ADD sempre para a MESMA definicao final (1..11): idempotente no
-- sentido de "chega no mesmo estado", nao no sentido de "pula se ja existe"
-- — CHECK constraint nao tem meio-termo entre existir e nao existir.
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_EventosDeSeguranca_Tipo')
    ALTER TABLE dbo.EventosDeSeguranca DROP CONSTRAINT CK_EventosDeSeguranca_Tipo;
GO

ALTER TABLE dbo.EventosDeSeguranca ADD CONSTRAINT CK_EventosDeSeguranca_Tipo CHECK (Tipo BETWEEN 1 AND 11);
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'037-reemissao-de-ticket-de-emergencia.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'037-reemissao-de-ticket-de-emergencia.sql',
                N'SEC-03 (bloqueador B20, setima revisao independente): alarga CK_EventosDeSeguranca_Tipo para 1..11, liberando TicketDeRematriculaReemitido (11) — reemissao de ticket de rematricula durante uma recuperacao de emergencia ja aberta, sem tocar MfaAtivo/RematriculaObrigatoriaAposEmergencia. Binario corrigido exige esta migration antes de subir (docs/INSTALACAO.md 1.2).');
END
GO

COMMIT TRANSACTION;
GO

SELECT OBJECT_DEFINITION(object_id) AS CK_EventosDeSeguranca_Tipo
FROM sys.check_constraints
WHERE name = N'CK_EventosDeSeguranca_Tipo';
GO

PRINT 'Migracao 037 concluida. Nenhuma linha existente foi alterada.';
GO
