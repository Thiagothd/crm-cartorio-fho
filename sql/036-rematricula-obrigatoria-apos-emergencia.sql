/* =============================================================================
   CRM-Cartorio — rematricula obrigatoria apos break-glass (SEC-03)
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-31
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 001-schema.sql, 021-versao-do-schema.sql, 034-mfa-totp.sql,
                   035-mfa-pendente-token-version.sql

   O QUE FAZ
       dbo.Atendentes += RematriculaObrigatoriaAposEmergencia BIT NOT NULL
       DEFAULT (0)

       Nenhuma linha existente muda de comportamento: a coluna nasce 0 para
       toda conta ja existente, que e exatamente o estado "nunca passou por
       break-glass" em que todas elas estao hoje (o break-glass ainda nem
       tinha sido implementado quando esta coluna foi criada).

   O PROBLEMA QUE ISTO RESOLVE (SEC-03, bloqueador B19 da sexta revisao
   independente do PR #46)
       O reset de emergencia (FerramentaDeResetDeEmergencia) zera MfaAtivo do
       alvo, mas AutenticacaoService.AutenticarAsync so recusa login so-senha
       para conta privilegiada sem MFA quando a POLITICA DO TENANT
       (Tenants.MfaObrigatorioParaAdministradores) exige. Num tenant com essa
       politica desligada, a conta resetada por break-glass voltava a logar
       com senha sozinha, sem nunca passar pelo ticket de rematricula que o
       proprio break-glass emitiu — o reset de emergencia virava, na pratica,
       "tirar o MFA de alguem", nao "forcar rematricula segura".

       Esta coluna e um sinalizador POR CONTA, independente da politica do
       tenant: quando true, o login continua recusado com
       `mfa_obrigatorio_nao_matriculado` ate a conta concluir uma matricula
       nova (com o ticket do break-glass), mesmo que a politica do tenant
       nunca tenha exigido MFA para ninguem.

   COMO O CODIGO USA ISTO
       FerramentaDeResetDeEmergencia.TentarResetarDeEmergenciaAsync grava
       RematriculaObrigatoriaAposEmergencia = 1 na MESMA instrucao atomica que
       zera MfaAtivo e os demais campos de MFA do alvo.

       AutenticacaoService.AutenticarAsync passa a recusar login so-senha
       tambem quando RematriculaObrigatoriaAposEmergencia = 1, alem do caso ja
       existente de politica do tenant ligada — nos dois lugares em que a
       decisao e tomada: a pre-checagem (momento 2) e o WHERE atomico do
       UPDATE final (momento 3B), nunca so um dos dois.

       MfaService.ConfirmarNucleoAsync volta a coluna para 0 no MESMO UPDATE
       atomico que reativa o MFA (SetProperty(a => a.MfaAtivo, true)) — a
       confirmacao bem-sucedida de QUALQUER matricula (voluntaria, troca de
       autenticador, ou por ticket) libera o campo; nao ha logica especial
       para distinguir "matricula por ticket de break-glass" de qualquer
       outra confirmacao, porque o proprio ticket ja e a prova de quem
       autorizou.

   POR QUE E UM ARQUIVO NOVO, NAO UM AJUSTE NA 034/035
       034 e 035 ja foram aplicadas em CRM_Cartorio_Dev. dbo.VersaoDoSchema
       rastreia migration aplicada por NOME DE ARQUIVO — editar um arquivo ja
       rodado no lugar faria o script achar que ja passou e pular o ALTER
       TABLE novo justamente no unico banco onde isso importa agora. Mesma
       disciplina de 001..035: migration aplicada e registro historico, nunca
       edita no lugar.

   POR QUE NAO PRECISA DE BACKFILL
       O break-glass so foi implementado nesta mesma rodada de correcoes —
       nenhuma conta em nenhum ambiente jamais passou por
       FerramentaDeResetDeEmergencia antes desta coluna existir. A coluna
       nasce 0 e so e preenchida daqui para frente, na proxima vez que
       alguem rodar `--reset-de-emergencia`.

   COMPATIBILIDADE DE DEPLOY
       Compativel com o binario ANTIGO (sem o bloqueador B19 corrigido)
       rodando: ele ignora a coluna nova, e break-glass ainda nao existia
       nesse binario. O binario NOVO MAPEIA e CONSULTA
       RematriculaObrigatoriaAposEmergencia em AutenticarAsync,
       ConfirmarNucleoAsync e TentarResetarDeEmergenciaAsync — nao deve ser
       iniciado antes desta migration estar aplicada, mesmo padrao de 033,
       034 e 035 (ver docs/INSTALACAO.md, secao 1.2).

   IDEMPOTENCIA
       A coluna so entra quando ainda nao existe. Rodar o script de novo nao
       encontra nada para fazer.
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

IF COL_LENGTH(N'dbo.Atendentes', N'MfaPendenteTokenVersion') IS NULL
BEGIN
    RAISERROR('035-mfa-pendente-token-version.sql precisa ter rodado antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

IF COL_LENGTH(N'dbo.Atendentes', N'RematriculaObrigatoriaAposEmergencia') IS NULL
    ALTER TABLE dbo.Atendentes
        ADD RematriculaObrigatoriaAposEmergencia BIT NOT NULL
            CONSTRAINT DF_Atendentes_RematriculaObrigatoriaAposEmergencia DEFAULT (0);
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'036-rematricula-obrigatoria-apos-emergencia.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'036-rematricula-obrigatoria-apos-emergencia.sql',
                N'SEC-03 (bloqueador B19, sexta revisao independente): RematriculaObrigatoriaAposEmergencia em Atendentes — bloqueia login so-senha apos reset de emergencia (break-glass) INDEPENDENTE da politica obrigatoria do tenant, ate concluir matricula nova com o ticket do proprio break-glass. Binario corrigido exige esta migration antes de subir (docs/INSTALACAO.md 1.2).');
END
GO

COMMIT TRANSACTION;
GO

SELECT COL_LENGTH(N'dbo.Atendentes', N'RematriculaObrigatoriaAposEmergencia') AS RematriculaObrigatoriaAposEmergencia;
GO

PRINT 'Migracao 036 concluida. Nenhuma conta foi afetada por esta migracao.';
GO
