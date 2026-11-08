/* =============================================================================
   CRM-Cartorio — Encerramento automatico de conversa parada
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-19
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 001-schema.sql, 005-mensagens-automaticas.sql,
                  019-resposta-sem-correspondencia-schema.sql, 021-versao-do-schema.sql

   O QUE FAZ
       1. dbo.Conversas    += PerguntaDeEncerramentoEnviadaEm  (marcador da varredura)
       2. dbo.Tenants      += 4 colunas de configuracao do recurso
       3. CK_MensagensAutomaticas_Gatilho passa a aceitar 12 e 13
       4. IX_Conversas_EncerramentoPendente (filtrado)
       5. Os dois textos novos, ativos, espelhando CatalogoDeDemonstracao.cs
       6. Liga o recurso com 20 / 30 minutos, janela das 8h as 18h

   O PROBLEMA QUE ISTO RESOLVE
       Conversa que ninguem encerra fica Ativa para sempre, e o recebimento do
       WhatsApp reaproveita a conversa Ativa do cliente sem olhar quando foi a
       ultima mensagem. Quem voltava dias depois caia na conversa velha — sem
       boas-vindas, sem triagem, sem menu. Encerrar e o que conserta: conversa
       Encerrada faz o proximo contato abrir atendimento novo, do zero.

   POR QUE UM CAMPO E NAO DOIS
       PerguntaDeEncerramentoEnviadaEm e, sozinho, o marcador idempotente da
       primeira fase, o relogio da segunda e a flag que o despacho consulta. Um
       bool separado andaria sempre junto deste carimbo — e dois campos que nunca
       discordam sao dois campos que um dia vao discordar.

   ATENCAO — ESTE SCRIPT LIGA O RECURSO (passo 6)
       Diferente do 023, que deixou o modo de recebimento NULL de proposito, aqui
       os minutos nascem preenchidos: o recurso foi pedido para operar, nao para
       ficar esperando alguem lembrar de ligar. A partir do primeiro deploy, uma
       conversa parada 20 minutos recebe a pergunta e, sem resposta em 30, fecha.
       O UPDATE so toca em quem esta com os quatro campos nulos, entao rodar de
       novo nunca sobrescreve escolha feita pela tela.

       Para desligar: tela Automacao > Encerramento automatico, limpar os tempos.

   IDEMPOTENCIA
       Coluna, constraint, indice e texto so entram quando ainda nao existem, e o
       UPDATE final e condicionado a configuracao ausente.
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF DB_NAME() NOT IN (N'CRM_Cartorio', N'CRM_Cartorio_Teste', N'CRM_Cartorio_Dev', N'CRM_Cartorio_Demo')
BEGIN
    RAISERROR('Recusado: este script so roda em CRM_Cartorio, CRM_Cartorio_Teste ou CRM_Cartorio_Dev. Confira o banco selecionado.', 16, 1);
    RETURN;
END
GO

IF OBJECT_ID(N'dbo.Conversas', N'U') IS NULL OR OBJECT_ID(N'dbo.MensagensAutomaticas', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode as migracoes anteriores antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

/* --- 1. Marcador na conversa --------------------------------------------- */

IF COL_LENGTH(N'dbo.Conversas', N'PerguntaDeEncerramentoEnviadaEm') IS NULL
    ALTER TABLE dbo.Conversas ADD PerguntaDeEncerramentoEnviadaEm DATETIME2(3) NULL;
GO

/* --- 2. Configuracao por serventia ---------------------------------------
   Nulo nos minutos desliga o recurso inteiro; nulo nas horas significa "qualquer
   hora". Mesma disciplina de Tenants.WhatsappModoDeRecebimento (023): nulo nao e
   zero nem padrao implicito, e "ninguem decidiu isto".

   O piso de 5 minutos e o que permite testar o ciclo inteiro em cerca de dez
   minutos; o teto de 30 dias e o ponto em que "automatico" ja nao significa nada.
   ------------------------------------------------------------------------- */

IF COL_LENGTH(N'dbo.Tenants', N'MinutosParaPerguntarEncerramento') IS NULL
    ALTER TABLE dbo.Tenants ADD MinutosParaPerguntarEncerramento INT NULL;
GO

IF COL_LENGTH(N'dbo.Tenants', N'MinutosParaEncerrarSemResposta') IS NULL
    ALTER TABLE dbo.Tenants ADD MinutosParaEncerrarSemResposta INT NULL;
GO

IF COL_LENGTH(N'dbo.Tenants', N'EncerramentoHoraInicial') IS NULL
    ALTER TABLE dbo.Tenants ADD EncerramentoHoraInicial INT NULL;
GO

IF COL_LENGTH(N'dbo.Tenants', N'EncerramentoHoraFinal') IS NULL
    ALTER TABLE dbo.Tenants ADD EncerramentoHoraFinal INT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Tenants_MinutosParaPerguntarEncerramento')
    ALTER TABLE dbo.Tenants ADD CONSTRAINT CK_Tenants_MinutosParaPerguntarEncerramento
        CHECK (MinutosParaPerguntarEncerramento IS NULL
            OR MinutosParaPerguntarEncerramento BETWEEN 5 AND 43200);
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Tenants_MinutosParaEncerrarSemResposta')
    ALTER TABLE dbo.Tenants ADD CONSTRAINT CK_Tenants_MinutosParaEncerrarSemResposta
        CHECK (MinutosParaEncerrarSemResposta IS NULL
            OR MinutosParaEncerrarSemResposta BETWEEN 5 AND 43200);
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Tenants_EncerramentoJanela')
    ALTER TABLE dbo.Tenants ADD CONSTRAINT CK_Tenants_EncerramentoJanela
        CHECK ((EncerramentoHoraInicial IS NULL AND EncerramentoHoraFinal IS NULL)
            OR (EncerramentoHoraInicial BETWEEN 0 AND 23
                AND EncerramentoHoraFinal BETWEEN 0 AND 23
                AND EncerramentoHoraInicial <> EncerramentoHoraFinal));
GO

/* --- 3. Gatilhos 12 e 13 no CHECK -----------------------------------------
   Sem isto o INSERT do passo 5 falha. Mesmo padrao do 019, que ampliou de 10
   para 11.
   ------------------------------------------------------------------------- */

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_MensagensAutomaticas_Gatilho')
    ALTER TABLE dbo.MensagensAutomaticas DROP CONSTRAINT CK_MensagensAutomaticas_Gatilho;
GO

ALTER TABLE dbo.MensagensAutomaticas ADD CONSTRAINT CK_MensagensAutomaticas_Gatilho
    CHECK (Gatilho IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13));
GO

/* --- 4. Indice da segunda fase --------------------------------------------
   Filtrado porque, em regime, "tem pergunta pendente" e um punhado de linhas
   contra uma tabela que cresce para sempre.

   A PRIMEIRA fase nao ganha indice de proposito: ela e servida por
   IX_Conversas_Inbox (TenantId, Status, UltimaMensagemEm), e um indice filtrado
   por "IS NULL" cobriria quase toda conversa ativa — seria a inbox indexada duas
   vezes.
   ------------------------------------------------------------------------- */

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Conversas_EncerramentoPendente')
    CREATE INDEX IX_Conversas_EncerramentoPendente
        ON dbo.Conversas (TenantId, PerguntaDeEncerramentoEnviadaEm)
        WHERE PerguntaDeEncerramentoEnviadaEm IS NOT NULL;
GO

/* --- 5. Os dois textos ----------------------------------------------------
   ESPELHO OBRIGATORIO: identicos aos de
   Infrastructure/Persistence/Seeding/CatalogoDeDemonstracao.cs, mesma disciplina
   de 002/015/016/018/020.

   O menu numerado do gatilho 12 nao e enfeite: "1" e "2" sao exatamente o que
   TriagemAutomatica.LerRespostaDeEncerramento le do outro lado.
   ------------------------------------------------------------------------- */

DECLARE @agora DATETIME2(3) = SYSUTCDATETIME();

DECLARE @tenants TABLE (Id UNIQUEIDENTIFIER);
INSERT INTO @tenants (Id) SELECT Id FROM dbo.Tenants;

INSERT INTO dbo.MensagensAutomaticas
    (Id, TenantId, Gatilho, EtapaAlvoId, Conteudo, Ativo, CriadoEm, AtualizadoEm)
SELECT
    CASE WHEN t.Id = '0199B2C0-0000-7000-8000-000000000001'
         THEN CAST('0199B2C0-0006-7000-8000-000000000013' AS UNIQUEIDENTIFIER)
         ELSE NEWID() END,
    t.Id, 12, NULL,
    N'Percebi que nossa conversa ficou parada, {NomeCliente}. Posso encerrar este atendimento?' + CHAR(10) + CHAR(10) +
    N'1 - Sim, pode encerrar' + CHAR(10) +
    N'2 - Não, ainda preciso de ajuda',
    1, @agora, @agora
FROM @tenants t
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.MensagensAutomaticas m WHERE m.TenantId = t.Id AND m.Gatilho = 12);

INSERT INTO dbo.MensagensAutomaticas
    (Id, TenantId, Gatilho, EtapaAlvoId, Conteudo, Ativo, CriadoEm, AtualizadoEm)
SELECT
    CASE WHEN t.Id = '0199B2C0-0000-7000-8000-000000000001'
         THEN CAST('0199B2C0-0006-7000-8000-000000000014' AS UNIQUEIDENTIFIER)
         ELSE NEWID() END,
    t.Id, 13, NULL,
    N'Como não tivemos retorno, encerrei o atendimento do protocolo {Protocolo} por aqui, {NomeCliente}. 😊' + CHAR(10) + CHAR(10) +
    N'Se precisar de qualquer coisa, é só mandar uma nova mensagem — começamos de novo na hora.',
    1, @agora, @agora
FROM @tenants t
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.MensagensAutomaticas m WHERE m.TenantId = t.Id AND m.Gatilho = 13);
GO

/* --- 6. Liga o recurso ----------------------------------------------------
   20 minutos ate perguntar, 30 ate encerrar, janela das 8h as 18h (hora de
   Brasilia — a conversao do UTC e feita no varredor).

   So mexe em serventia que ainda nao tem NADA configurado. Quem ja escolheu pela
   tela — inclusive quem escolheu DESLIGAR preenchendo so a janela — nao e tocado.
   ------------------------------------------------------------------------- */

UPDATE dbo.Tenants
   SET MinutosParaPerguntarEncerramento = 20,
       MinutosParaEncerrarSemResposta   = 30,
       EncerramentoHoraInicial          = 8,
       EncerramentoHoraFinal            = 18,
       AtualizadoEm                     = SYSUTCDATETIME()
 WHERE MinutosParaPerguntarEncerramento IS NULL
   AND MinutosParaEncerrarSemResposta   IS NULL
   AND EncerramentoHoraInicial          IS NULL
   AND EncerramentoHoraFinal            IS NULL;
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'024-encerramento-por-inatividade.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'024-encerramento-por-inatividade.sql',
                N'Encerramento automatico por inatividade: marcador na Conversa, 4 colunas de configuracao no Tenant, gatilhos 12 e 13, indice filtrado e o recurso ligado em 20/30 min.');
END
GO

COMMIT TRANSACTION;
GO

SELECT RazaoSocial,
       MinutosParaPerguntarEncerramento AS PerguntarApos,
       MinutosParaEncerrarSemResposta   AS EncerrarApos,
       EncerramentoHoraInicial          AS Das,
       EncerramentoHoraFinal            AS Ate
  FROM dbo.Tenants;
GO

SELECT Gatilho, Ativo, LEFT(Conteudo, 60) AS InicioDoTexto
  FROM dbo.MensagensAutomaticas
 WHERE Gatilho IN (12, 13)
 ORDER BY Gatilho;
GO

PRINT 'Migracao 024 concluida.';
GO
