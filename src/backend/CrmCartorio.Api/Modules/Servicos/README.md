# Módulo: Servicos

Catálogo de atos cartorários (`TipoServico`) e o SLA de cada um (`PrazoServico`).

## Prazo em horas, não em dias

A faixa é larga demais para uma unidade só: reconhecimento de firma sai em
minutos, registro de imóvel leva dias. Hora é a menor unidade que cobre os dois
extremos sem virar dois campos.

`TipoServicoService` traduz isso de volta para linguagem de balcão na resposta
(`PrazoDescrito`) — ninguém pensa em "240 horas".

## Como o prazo vira data

`CalculadoraDePrazo.CalcularLimite` avança as horas do SLA a partir do início.
Com `ApenasDiasUteis`, sábado e domingo não consomem prazo — o cartório está
fechado e descontar esse tempo do cliente seria mentira do contador.

O contador começa quando o **serviço é vinculado à conversa**, não na abertura:
a triagem inicial não deve consumir o SLA do ato.

## Situação de prazo

Calculada na leitura, nunca persistida — persistir exigiria um job periódico e o
valor mentiria entre duas execuções.

O que **é** persistido na conversa são dois instantes fixos: `PrazoLimite` e
`PrazoAlertaEm`. Com eles, filtrar a inbox por situação de prazo vira comparação
de datas em SQL, indexável, em vez de uma subconsulta de percentual por linha.

## Desativar SLA não remove o registro

Tirar o prazo de um serviço marca `PrazoServico.Ativo = false`. As conversas
abertas antes continuam com o prazo que valia quando nasceram.

## Aguarda validação

Os 14 serviços e todos os prazos semeados são sugestão de mercado, não dado real
do cartório (`docs/REQUISITOS.md`, seção 5, pendências 4 e 5).
