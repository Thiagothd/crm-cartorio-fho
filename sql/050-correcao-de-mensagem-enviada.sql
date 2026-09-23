/* =============================================================================
   CRM-Cartorio — Correcao e apagamento de mensagem ja enviada ao cliente
   -----------------------------------------------------------------------------
   Gerado em .... 2026-09-22
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 001-schema.sql, 021-versao-do-schema.sql

   O QUE FAZ
       1. Cinco colunas novas em dbo.Mensagens, todas NULL, sem DEFAULT e sem
          backfill:
              ConteudoOriginal               NVARCHAR(MAX) (cifrada em repouso)
              CorrigidaEm                    DATETIME2(3)
              CorrigidaPorAtendenteId        UNIQUEIDENTIFIER
              ApagadaParaTodosEm             DATETIME2(3)
              ApagadaParaTodosPorAtendenteId UNIQUEIDENTIFIER
       2. Duas FOREIGN KEY para dbo.Atendentes (quem corrigiu, quem apagou)
       3. Duas CHECK constraints NOVAS: CK_Mensagens_Correcao e
          CK_Mensagens_ApagadaParaTodos

   O PROBLEMA QUE ISTO RESOLVE
       Mensagem enviada por engano chegava ao WhatsApp do cliente e nao havia
       remedio nenhum: o unico caminho era mandar OUTRA mensagem pedindo
       desculpa, e as duas ficavam la. O atendente agora pode corrigir o texto
       (o WhatsApp marca "editada") ou apagar no aparelho do cliente (o WhatsApp
       mostra "Esta mensagem foi apagada").

   O REGISTRO NAO SE PERDE — e por isso as colunas sao aditivas
       Nenhuma linha e apagada e nenhum texto e sobrescrito sem guarda: a
       primeira correcao copia o texto que saiu para ConteudoOriginal, e o
       apagamento so MARCA a linha, deixando Conteudo intacto. Some do aparelho
       do cliente, nunca do registro da serventia — mesma postura de OcultaEm e
       de conversa encerrada (RNF-008).

   POR QUE ConteudoOriginal E NVARCHAR(MAX) E NAO NVARCHAR(4000)
       Espelha exatamente dbo.Mensagens.Conteudo, que e cifrada em repouso
       (PR3/LGPD, ver Infrastructure/Persistence/Cifra): o valor gravado e
       ciphertext + overhead, e o limite de 4000 caracteres do texto EM CLARO e
       validado na aplicacao (ConversorCifrado), nao pela coluna. Guardar o
       original em claro ao lado de um conteudo cifrado entregaria de graca
       justamente o que a cifra protege.

   POR QUE AS CHECK EXIGEM AutorTipo = 2 (Atendente)
       Corrigir e apagar sao acoes do atendente sobre o que o proprio painel
       mandou. O cliente nao tem como pedir conserto por aqui, e mensagem
       automatica ou da IA se conserta no cadastro que a gerou (tela Automacao >
       Mensagens automaticas), nao no fio, uma a uma. As tres colunas da
       correcao andam juntas pelo mesmo motivo: texto original sem autor, ou
       autor sem instante, seria um registro incapaz de responder "quem mudou o
       que, e quando" — a unica pergunta que ele existe para responder.

   SEGURO COM O BINARIO ANTERIOR RODANDO
       Expand-only: nenhuma coluna, constraint ou indice existente e alterado ou
       removido. As cinco colunas nascem NULL sem DEFAULT, entao todo INSERT ou
       UPDATE do binario N-1 (que nao as conhece) simplesmente as omite, e a
       omissao satisfaz o primeiro ramo das duas CHECK novas. Ver o contrato em
       deploy/migration-compatibility.json.

   IDEMPOTENCIA
       Cada coluna, FK e constraint so entra quando ainda nao existe. Rodar de
       novo nao tem efeito.
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

IF OBJECT_ID(N'dbo.Mensagens', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode as migracoes anteriores antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

/* --- 1. As cinco colunas ----------------------------------------------------
   Todas NULL, sem DEFAULT: nenhuma linha existente e tocada, e o binario
   anterior continua inserindo sem saber que elas existem.
   ------------------------------------------------------------------------- */

IF COL_LENGTH('dbo.Mensagens', 'ConteudoOriginal') IS NULL
    ALTER TABLE dbo.Mensagens ADD ConteudoOriginal NVARCHAR(MAX) NULL;
GO

IF COL_LENGTH('dbo.Mensagens', 'CorrigidaEm') IS NULL
    ALTER TABLE dbo.Mensagens ADD CorrigidaEm DATETIME2(3) NULL;
GO

IF COL_LENGTH('dbo.Mensagens', 'CorrigidaPorAtendenteId') IS NULL
    ALTER TABLE dbo.Mensagens ADD CorrigidaPorAtendenteId UNIQUEIDENTIFIER NULL;
GO

IF COL_LENGTH('dbo.Mensagens', 'ApagadaParaTodosEm') IS NULL
    ALTER TABLE dbo.Mensagens ADD ApagadaParaTodosEm DATETIME2(3) NULL;
GO

IF COL_LENGTH('dbo.Mensagens', 'ApagadaParaTodosPorAtendenteId') IS NULL
    ALTER TABLE dbo.Mensagens ADD ApagadaParaTodosPorAtendenteId UNIQUEIDENTIFIER NULL;
GO

/* --- 2. Quem corrigiu e quem apagou -----------------------------------------
   Sem ON DELETE: atendente nunca e apagado (desligamento vira Ativo = 0), e a
   assinatura da correcao precisa continuar atribuivel depois que ele sai da
   serventia. Mesmo desenho de FK_Mensagens_FixadaPor e FK_Mensagens_OcultaPor.
   ------------------------------------------------------------------------- */

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Mensagens_CorrigidaPor')
    ALTER TABLE dbo.Mensagens ADD CONSTRAINT FK_Mensagens_CorrigidaPor
        FOREIGN KEY (CorrigidaPorAtendenteId) REFERENCES dbo.Atendentes (Id);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Mensagens_ApagadaParaTodosPor')
    ALTER TABLE dbo.Mensagens ADD CONSTRAINT FK_Mensagens_ApagadaParaTodosPor
        FOREIGN KEY (ApagadaParaTodosPorAtendenteId) REFERENCES dbo.Atendentes (Id);
GO

/* --- 3. As duas CHECK -------------------------------------------------------
   WITH CHECK e deliberado: as colunas acabaram de nascer NULL em toda linha
   existente, entao a verificacao do acervo passa por construcao e a constraint
   ja nasce confiavel para o otimizador. Constraints NOVAS — nenhuma existente e
   derrubada ou recriada por este script.
   ------------------------------------------------------------------------- */

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Mensagens_Correcao')
    ALTER TABLE dbo.Mensagens WITH CHECK ADD CONSTRAINT CK_Mensagens_Correcao
        CHECK (
            (CorrigidaEm IS NULL AND CorrigidaPorAtendenteId IS NULL AND ConteudoOriginal IS NULL)
            OR
            (AutorTipo = 2 AND CorrigidaEm IS NOT NULL AND CorrigidaPorAtendenteId IS NOT NULL AND ConteudoOriginal IS NOT NULL)
        );
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Mensagens_ApagadaParaTodos')
    ALTER TABLE dbo.Mensagens WITH CHECK ADD CONSTRAINT CK_Mensagens_ApagadaParaTodos
        CHECK (
            (ApagadaParaTodosEm IS NULL AND ApagadaParaTodosPorAtendenteId IS NULL)
            OR
            (AutorTipo = 2 AND ApagadaParaTodosEm IS NOT NULL AND ApagadaParaTodosPorAtendenteId IS NOT NULL)
        );
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'050-correcao-de-mensagem-enviada.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'050-correcao-de-mensagem-enviada.sql',
                N'Correcao e apagamento de mensagem ja enviada: cinco colunas aditivas em dbo.Mensagens (ConteudoOriginal cifrada, CorrigidaEm/Por, ApagadaParaTodosEm/Por), duas FK para Atendentes e duas CHECK novas. Nenhuma linha e apagada e nenhum texto se perde — o original fica guardado e o apagamento so marca a linha.');
END
GO

COMMIT TRANSACTION;
GO

SELECT c.name AS Coluna, t.name AS Tipo, c.is_nullable AS AceitaNulo
  FROM sys.columns c
  JOIN sys.types t ON t.user_type_id = c.user_type_id
 WHERE c.object_id = OBJECT_ID(N'dbo.Mensagens')
   AND c.name IN (N'ConteudoOriginal', N'CorrigidaEm', N'CorrigidaPorAtendenteId',
                  N'ApagadaParaTodosEm', N'ApagadaParaTodosPorAtendenteId')
 ORDER BY c.name;
GO

PRINT 'Migracao 050 concluida.';
GO
