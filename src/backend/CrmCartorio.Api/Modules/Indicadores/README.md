# Módulo: Indicadores

Alimenta a Visão Geral (`/`), a página Indicadores e Relatórios
(`/indicadores`, gestão) e o sino de alertas operacionais do cabeçalho global.
**Só consulta.** Nenhuma migração, nenhum `.sql`, nenhuma tabela nova — tudo sai
de colunas que já existem em `Conversas`, `Mensagens` e `HistoricoEtapas`.

## Por que não é um Dashboard genérico

Cada campo de resposta responde a uma pergunta real de quem atende num
cartório: quantos estão atrasados agora, quanto tempo até a primeira resposta
de gente, onde está a demanda do período. O que não é calculável com o modelo
atual — tempo até alguém assumir a conversa, custo em dinheiro da IA — **não
aparece**, em vez de fingido. Ver `docs/DESIGN.md` e o plano da Fase 3 para o
detalhe de cada decisão.

## Primeira resposta humana, sem ambiguidade

Três CHECK constraints em `Mensagens` (`MensagemConfiguration`) garantem que
`AutorTipo = Atendente` sempre tem `AtendenteId` preenchido e nunca coincide com
`EhAutomatica`. Por isso `MIN(EnviadaEm) WHERE AutorTipo = Atendente` nunca
confunde resposta de gente com regra de cadastro ou resposta da IA — as duas
últimas são sempre `AutorTipo = Sistema`.

A métrica usa **mediana**, não média: no volume de um cartório, um único caso
respondido no dia seguinte distorce a média sem dizer nada sobre o resto do
período. A média e o tamanho da amostra continuam disponíveis para quem quiser
conferir.

## "Hoje" é o dia do cartório

Todo bucket de série (fluxo de atendimentos, evolução da 1ª resposta, horários
de maior demanda) agrupa os timestamps **em memória**, no fuso local
(`Domain/Common/DiaLocal.cs`), nunca em `GROUP BY` de SQL — agrupar por data no
banco daria o balde em UTC, e "hoje" ficaria errado entre 21h e meia-noite no
horário de Brasília. O volume de um cartório (centenas de linhas por mês) torna
essa leitura em memória barata.

## Escopo "minhas" vs "toda a operação"

Qualquer perfil pode pedir os dois — não é gate de permissão, é preferência de
tela. A fila (`AtendenteId IS NULL`) nunca é filtrada por escopo: não existe
"minha fila", fila é por definição o que ainda não tem responsável.
`AguardandoResposta` segue o escopo como `Ativas`/`EmAlerta`/`Atrasadas` — só
`NaFila` é global.

## "Aguardando resposta" não é "não lida"

O bloco AGORA tem uma quinta contagem: conversas ativas em que a **última
mensagem real do fio** é do cliente — o cartório está devendo resposta agora.
A regra vive em `Modules/Atendimento/Domain/AguardandoRespostaDoCartorio.cs` e
é a mesma usada pelo filtro da inbox (`ApenasAguardandoResposta`) e pelo painel
de resumo, para que o card da Visão Geral e a lista que ele abre nunca
discordem.

Deliberadamente **não** usa `Conversa.MensagensNaoLidas` — aquele contador mede
"nenhum humano abriu no CRM" e nunca zera quando só a IA responde (bug de
produção confirmado em 2026-09-03, ver `Modules/Atendimento/README.md`).
Também não é gateada por `EmAtendimentoHumano` nem pelas flags `Aguardando*`
do bot: o estado é transitório por construção, e se ele persiste é porque a
automação decidiu não responder ou falhou — os dois casos são exatamente o
que este contador existe para mostrar.

## Saúde da operação nunca amplia permissão

WhatsApp exige `PodeGerirAtendentes` (Administrador); IA e Automações exigem
`PodeGerirCadastros` (Supervisor/Administrador) — os mesmos gates já usados em
`WhatsAppController` e nas telas de configuração. Quem não tem permissão recebe
o campo `null`, e o frontend simplesmente não renderiza a peça.

## O sino não inventa "lido"

`ObterAlertasAsync` devolve alertas operacionais **ativos agora** — atrasado,
perto do prazo, na fila sem responsável, WhatsApp desconectado, teto diário da
IA atingido. Não existe leitura persistida por atendente: implementar isso
exigiria schema novo. O que o frontend faz é comparar o campo `Quando` de cada
alerta com a última vez que o painel foi aberto **neste navegador**
(`localStorage`), e é honesto sobre essa diferença na própria tela.
