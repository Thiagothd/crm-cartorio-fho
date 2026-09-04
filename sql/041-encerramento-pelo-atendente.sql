/* =============================================================================
   CRM-Cartorio — Aviso ao cliente quando o atendente encerra pelo painel
   -----------------------------------------------------------------------------
   Gerado em .... 2026-09-04
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 001-schema.sql, 005-mensagens-automaticas.sql,
                  021-versao-do-schema.sql

   O QUE FAZ
       1. CK_MensagensAutomaticas_Gatilho passa a aceitar 15
          (EncerramentoPeloAtendente)
       2. O texto do gatilho 15, ATIVO, espelhando CatalogoDeDemonstracao.cs

   O PROBLEMA QUE ISTO RESOLVE
       Existem quatro caminhos que encerram uma Conversa. Tres avisam o cliente
       no WhatsApp (Despedida=10 quando o cliente escolhe encerrar pelo menu,
       EncerramentoPorInatividade=13 na varredura automatica). O quarto — o
       atendente encerrando pelo painel (ConversaService.EncerrarAsync) — nao
       disparava gatilho nenhum: o cliente simplesmente parava de ser respondido,
       sem saber que o atendimento tinha fechado.

   POR QUE NAO REUSA Despedida (10)
       Aquele texto e a voz de quem foi atendido ate o fim e ESCOLHEU fechar
       ("obrigado por escolher o cartorio"). Aqui quem fechou foi a serventia —
       pode ter sido resolvido no balcao, pode ter sido fim de expediente. A
       mesma frase nos dois casos soaria como se o sistema nao soubesse a
       diferenca.

   CORTESIA, NAO CONDICAO — mesma divergencia deliberada do 13
       Sem regra ativa para o gatilho 15 a conversa encerra do mesmo jeito, so
       sem avisar. Prender o encerramento manual do atendente a existencia de um
       texto cadastrado daria a um cadastro poder de veto sobre uma acao humana.

   O TEXTO NAO NOMEIA QUEM ENCERROU NEM REPETE O MOTIVO
       Nao existe {NomeAtendente} no catalogo de marcadores, e o motivo digitado
       no encerramento e registro interno (grava-se so na anotacao de sistema do
       fio, que nunca sai pelo canal). O par natural existe do outro lado: a
       proxima mensagem que o atendente escrever sai assinada com o nome dele
       (CanalWhatsApp.Application.AssinaturaDeAutoria, mudanca no mesmo PR).

   NASCE ATIVO
       Decisao do Thiago em 2026-09-04: o cliente ja passa a ser avisado desde o
       deploy. O texto e rascunho (o cartório parceiro ainda nao viu) e pode ser reescrito
       pela tela Automacao > Mensagens automaticas a qualquer momento, sem
       precisar de codigo novo.

   IDEMPOTENCIA
       Constraint e texto so entram quando ainda nao existem.
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

IF OBJECT_ID(N'dbo.MensagensAutomaticas', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode as migracoes anteriores antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

/* --- 1. Gatilho 15 no CHECK -------------------------------------------------
   Sem isto o INSERT abaixo falha. Mesmo padrao do 019 (10 para 11), 024 (11
   para 13) e 025 (13 para 14) — sempre ampliando, nunca restringindo.
   ------------------------------------------------------------------------- */

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_MensagensAutomaticas_Gatilho')
    ALTER TABLE dbo.MensagensAutomaticas DROP CONSTRAINT CK_MensagensAutomaticas_Gatilho;
GO

ALTER TABLE dbo.MensagensAutomaticas ADD CONSTRAINT CK_MensagensAutomaticas_Gatilho
    CHECK (Gatilho IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15));
GO

/* --- 2. O texto do gatilho 15 -----------------------------------------------
   ESPELHO OBRIGATORIO: identico ao de
   Infrastructure/Persistence/Seeding/CatalogoDeDemonstracao.cs, mesma
   disciplina de 002/015/016/018/020/024/025.
   ------------------------------------------------------------------------- */

DECLARE @agora DATETIME2(3) = SYSUTCDATETIME();

DECLARE @tenants TABLE (Id UNIQUEIDENTIFIER);
INSERT INTO @tenants (Id) SELECT Id FROM dbo.Tenants;

INSERT INTO dbo.MensagensAutomaticas
    (Id, TenantId, Gatilho, EtapaAlvoId, Conteudo, Ativo, CriadoEm, AtualizadoEm)
SELECT
    CASE WHEN t.Id = '0199B2C0-0000-7000-8000-000000000001'
         THEN CAST('0199B2C0-0006-7000-8000-000000000016' AS UNIQUEIDENTIFIER)
         ELSE NEWID() END,
    t.Id, 15, NULL,
    N'Encerramos seu atendimento de protocolo {Protocolo} por aqui, {NomeCliente}. 🙏' + CHAR(10) + CHAR(10) +
    N'Se precisar de qualquer coisa, é só mandar uma mensagem — abrimos um novo atendimento na hora.',
    1, @agora, @agora
FROM @tenants t
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.MensagensAutomaticas m WHERE m.TenantId = t.Id AND m.Gatilho = 15);
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'041-encerramento-pelo-atendente.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'041-encerramento-pelo-atendente.sql',
                N'Gatilho 15 (EncerramentoPeloAtendente): CHECK ampliado e texto ativo, avisando o cliente quando um atendente encerra a conversa pelo painel — ate aqui o unico dos quatro caminhos de encerramento que ficava em silencio.');
END
GO

COMMIT TRANSACTION;
GO

SELECT Gatilho, Ativo, LEFT(Conteudo, 60) AS InicioDoTexto
  FROM dbo.MensagensAutomaticas
 WHERE Gatilho = 15
 ORDER BY TenantId;
GO

PRINT 'Migracao 041 concluida.';
GO
