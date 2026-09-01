/* =============================================================================
   CRM-Cartorio — TokenVersion vinculada ao segredo MFA pendente (SEC-03)
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-31
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 001-schema.sql, 021-versao-do-schema.sql, 034-mfa-totp.sql

   O QUE FAZ
       dbo.Atendentes += MfaPendenteTokenVersion INT NULL

       Nenhuma linha existente e alterada de valor: a coluna nasce NULL, que e
       exatamente o estado de "nenhum segredo pendente" que toda conta esta
       hoje (mesmo quem ja tem MFA ativo).

   POR QUE E UM ARQUIVO NOVO, NAO UM AJUSTE NA 034
       034-mfa-totp.sql ja foi aplicada em CRM_Cartorio_Dev. dbo.VersaoDoSchema
       rastreia migration aplicada por NOME DE ARQUIVO — editar 034 no lugar
       faria o script achar que ja rodou (o nome nao mudou) e pular o ALTER
       TABLE novo justamente no unico banco onde ela ja passou, exigindo
       aplicacao manual fora do fluxo normal. TESTE e producao nunca rodaram
       a 034 ainda, mas o mesmo raciocinio vale para lá quando isso acontecer:
       migration aplicada e registro historico, nunca edita no lugar (mesma
       disciplina de 001..034 ate aqui).

   O PROBLEMA QUE ISTO RESOLVE (SEC-03, bloqueador 8 da revisao independente)
       IniciarAsync grava MfaSegredoPendenteCifrado sem registrar em que
       TokenVersion aquele segredo nasceu. Se um evento de seguranca (troca de
       senha, mudanca de Perfil/Cargo que cruza a fronteira de privilegio,
       reset administrativo, politica obrigatoria ligada) sobe a TokenVersion
       ENTRE o inicio e a confirmacao, o segredo pendente continua utilizavel
       — a cerimonia de matricula/troca de autenticador que comecou ANTES do
       evento pode terminar DEPOIS dele, como se nada tivesse mudado.

       O fluxo de ticket ja tem protecao equivalente (DesafiosDeMfa.
       TokenVersionNoDesafio, migration 034) — esta coluna estende a MESMA
       garantia para as outras duas cerimonias que nunca tiveram: primeira
       ativacao voluntaria de conta comum, e troca de autenticador de conta
       que ja tem MFA ativo.

   COMO O CODIGO USA ISTO
       Ao gerar o segredo pendente (GerarSegredoPendenteAsync), a TokenVersion
       ATUAL da propria linha e gravada em MfaPendenteTokenVersion na MESMA
       instrucao UPDATE (auto-referenciada: `MfaPendenteTokenVersion =
       TokenVersion`, nunca um valor lido antes em memoria).

       Na confirmacao (ConfirmarNucleoAsync), o WHERE atomico que ativa o MFA
       exige `TokenVersion = MfaPendenteTokenVersion` no INSTANTE do commit —
       se algo subiu a TokenVersion nesse meio-tempo, a condicao nao bate e a
       confirmacao falha fechada, mesmo com o codigo TOTP correto.

       Ao confirmar com sucesso, resetar ou desativar o MFA, a coluna volta a
       NULL junto de MfaSegredoPendenteCifrado/MfaPendenteExpiraEm — nunca
       fica orfa apontando para um segredo que ja nao existe mais.

   POR QUE NAO PRECISA DE BACKFILL
       Nenhuma conta tem cerimonia de MFA em andamento entre uma migration e
       outra (o TTL do segredo pendente e de 10 minutos) — a coluna nasce NULL
       e e preenchida so na proxima vez que alguem chamar IniciarAsync.

   COMPATIBILIDADE DE DEPLOY
       Compativel com o binario ANTIGO (SEC-03 sem esta correcao) rodando: ele
       ignora a coluna nova. O binario NOVO (com o bloqueador 8 corrigido)
       MAPEIA e CONSULTA MfaPendenteTokenVersion em IniciarAsync e em
       ConfirmarNucleoAsync — nao deve ser iniciado antes desta migration
       estar aplicada, mesmo padrao de 033/034 (ver docs/DEPLOY.md, secao 1.2).

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

IF COL_LENGTH(N'dbo.Atendentes', N'MfaSegredoPendenteCifrado') IS NULL
BEGIN
    RAISERROR('034-mfa-totp.sql precisa ter rodado antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

IF COL_LENGTH(N'dbo.Atendentes', N'MfaPendenteTokenVersion') IS NULL
    ALTER TABLE dbo.Atendentes ADD MfaPendenteTokenVersion INT NULL;
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'035-mfa-pendente-token-version.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'035-mfa-pendente-token-version.sql',
                N'SEC-03 (bloqueador 8): MfaPendenteTokenVersion em Atendentes — vincula o segredo MFA pendente (primeira ativacao voluntaria e troca de autenticador) a TokenVersion do instante em que foi gerado. Confirmacao exige que a TokenVersion atual ainda bata, fechando a promocao/revogacao no meio da cerimonia. Binario corrigido exige esta migration antes de subir (docs/DEPLOY.md 1.2).');
END
GO

COMMIT TRANSACTION;
GO

SELECT COL_LENGTH(N'dbo.Atendentes', N'MfaPendenteTokenVersion') AS MfaPendenteTokenVersion;
GO

PRINT 'Migracao 035 concluida. Nenhuma conta foi afetada por esta migracao.';
GO
