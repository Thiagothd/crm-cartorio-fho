# Módulo Devedores (`Modules/Devedores`)

Gestão de clientes com valores em aberto junto ao cartório (emolumentos,
certidões, escrituras, procurações, custas complementares ou atos liberados em
confiança), histórico de baixas parciais/totais e cobrança assistida pelo
WhatsApp.

## Responsabilidades

1. **Lançamento de pendências financeiras (`PendenciaFinanceira`):**
   - Permite vincular um `Cliente` já existente ou cadastrar um contato novo na
     mesma operação (`Nome` + `TelefoneWhatsapp` + documento opcional).
   - Suporta `ValorOriginal` opcional (`NULL` quando o responsável quer apenas
     marcar o contato como devedor antes de apurar o valor exato) e
     `DataPromessaPagamento` opcional.
   - Guarda `ConversaOrigemId` e `TipoServicoId` opcionais para rastreabilidade
     direta com o atendimento no balcão.
2. **Baixas parciais e quitação (`PagamentoDePendencia`):**
   - Registra pagamentos parciais ou totais por meio (`Pix`, `Dinheiro`,
     `Cartao`, `Transferencia`, `Outro`), recalculando `ValorPago` e quitando
     automaticamente quando `ValorPago >= ValorOriginal` ou quando solicitado
     explicitamente (`QuitarTotalmente`).
   - Permite disparar recibo/agradecimento opcional no WhatsApp no momento da
     baixa.
3. **Cobrança pelo WhatsApp e abertura de conversa em Atendimento Humano:**
   - Envia lembrete/cobrança pelo canal da serventia, abrindo ou reaproveitando
     a conversa ativa do cliente já com `EmAtendimentoHumano = true` e
     `AguardandoTriagem = false` (para que a resposta do devedor não acione o
     robô de triagem nem a IA).
   - Incrementa `TotalCobrancas` e carimba `UltimaCobrancaEm`.
4. **Importação 3-em-1 em lote (`ImportarEmLoteAsync`):**
   - Recebe itens vindos de colagem de texto/planilha, agenda do WhatsApp ou
     seleção de clientes existentes, deduplicando por telefone E.164.
5. **Integração com a Inbox (`ProjecaoDeConversa`):**
   - Expõe `ClientePendenciasAbertas` e `ClienteSaldoDevedor` no resumo da
     conversa, mascarados com `0` quando o atendente não possui a permissão
     `devedores.ver`.

## Proteção de Dados (LGPD — PR3)

- `PendenciaFinanceira.Titulo` permanece em texto claro (`NVARCHAR(200)`) para
  sustentar busca parcial em SQL (`Contains`).
- `PendenciaFinanceira.Observacoes` (`.Cifrada(1000)`) e
  `PagamentoDePendencia.Observacao` (`.Cifrada(500)`) são cifradas em repouso.
- Contas de suporte técnico (`EhContaTecnica`) não podem receber permissões do
  bloco `devedores.*` por concessão direta.
