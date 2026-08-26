# Módulo: Atendimento

O coração do produto. Tudo que envolve a conversa de um cliente com o cartório.

## Entidades

| Entidade | Papel |
| --- | --- |
| `Conversa` | O atendimento. Onde mora a trava contra duplicidade |
| `Mensagem` | O fio da conversa. Nunca editada, nunca apagada |
| `HistoricoEtapa` | Trilha de mudanças de etapa e setor. Só cresce |

## Por que a trava está aqui

Dois atendentes respondendo o mesmo cliente ao mesmo tempo é o erro mais citado
no mercado para este tipo de produto — a serventia parece desorganizada e o
cliente recebe respostas contraditórias. Este módulo trata isso em três camadas:

1. **`UPDATE` condicional** em `ConversaService.AssumirAsync`. Não é
   ler-e-depois-gravar: é um `UPDATE ... WHERE AtendenteId IS NULL` único. Se
   afetar 0 linhas, outro atendente ganhou a corrida e o serviço devolve
   `ConflitoDeAtribuicaoException` com o **nome de quem assumiu**. A atomicidade
   é do banco, então funciona com várias instâncias de IIS.
2. **`rowversion`** em `Conversa`, como token de concorrência do EF Core. Cobre
   as demais escritas — etapa, setor, encerramento, transferência — não só a
   atribuição.
3. **SignalR**, via `INotificadorDeAtendimento`. A interface trava o botão
   "Assumir" assim que o evento chega, então o segundo atendente normalmente nem
   tenta. As camadas 1 e 2 existem para quando ele tenta mesmo assim.

`MensagemService.EnviarAsync` fecha o cerco do outro lado: quem não é o
responsável não escreve.

## Human Takeover — a IA nunca responde por cima de um atendente

A trava acima protege atendente × atendente. Até 2026-08-26 não existia
proteção equivalente para atendente × automação: um cliente que já tinha um
humano cuidando da conversa continuava recebendo respostas da IA (ou do bot
de menu) por cima dele — **BUG-ATD-01**, reproduzido ao vivo em `testecrm`.

A causa raiz: nenhum ponto do despacho de mensagem do cliente
(`RecebimentoDeWhatsApp.ReceberAsync`, `MensagemService.SimularRecebimentoAsync`)
olhava `Conversa.AtendenteId` antes de decidir chamar automação — a decisão
era só pelas flags `Aguardando*`, com um piso (`AplicadorDePosAtendimento`)
desenhado de propósito para nunca deixar mensagem sem resposta. E
`AtendenteId` sozinho não bastaria mesmo que fosse checado:
`ConversaService.LiberarAsync` zera esse campo para devolver a conversa à
fila, mas a conversa continua devendo humano, não devendo bot.

**`Conversa.EmAtendimentoHumano`** é o campo que resolve a ambiguidade — ver
o XML-doc da propriedade para a lista completa de quando liga/desliga. Regra
central: liga assim que o cliente pede atendente OU um atendente assume
(o que vier primeiro), e só desliga no encerramento — nunca em
`LiberarAsync` nem `TransferirAsync`.

Um único gate, no topo de cada cadeia de despacho (antes de QUALQUER flag,
inclusive antes de "menu"/`AguardandoTriagem`): se `EmAtendimentoHumano` é
verdadeiro, nenhum `Aplicador*` é chamado — a mensagem é só persistida
(já aconteceu antes do gate), `UltimaMensagemEm`/`MensagensNaoLidas`
continuam atualizando normalmente, e o SignalR continua dis­parando. A única
exceção é `PerguntaDeEncerramentoEnviadaEm`: se havia uma pendente, a
mensagem do cliente ainda conta como prova de vida e limpa o carimbo — mesmo
princípio já usado em `MensagemService.ConcluirEnvioAsync` quando o
ATENDENTE responde.

**Corrida de IA "em voo":** a chamada à IA pode levar segundos; um atendente
pode assumir nesse meio-tempo.
`AplicadorDeRespostaInteligente.AplicarAsync` relê `EmAtendimentoHumano`
(leitura fresca, fora do change tracker) logo depois que a IA responde e
antes de gravar — se mudou nesse intervalo, descarta a resposta em vez de
entregá-la. Prova sob concorrência real em `verificar-concorrencia.cs`
(Teste 4).

`VarredorDeInatividade.EncerrarSemRespostaAsync` (fase 2) também respeita o
modo humano — filtra `!EmAtendimentoHumano` no WHERE, para não encerrar
sozinha uma conversa que um atendente está respondendo (ou que está na fila
humana, aguardando outro). A fase 1 (a pergunta "ainda está aí?") **não**
foi alterada — continua saindo mesmo com atendente na conversa; é decisão de
produto separada, registrada em `docs/REQUISITOS.md` §5.8.

As flags `Aguardando*` **não são zeradas** ao entrar em modo humano — ficam
congeladas no valor que tinham no instante do takeover, porque o gate as
torna irrelevantes enquanto `EmAtendimentoHumano` for verdadeiro. Só são
zeradas no encerramento (`ConversaService.EncerrarAsync`, espelhando
`AplicadorDePosAtendimento.EncerrarPeloCliente`), que é fronteira de ciclo de
vida, não de takeover.

Efeito colateral bem-vindo: como o gate intercepta antes de
`AplicadorDeTriagem`, o caso registrado em `docs/REQUISITOS.md` §5.8 original
(triagem reagindo com atendente já na conversa) deixou de acontecer — mesmo
sem nenhuma mudança específica naquele arquivo.

## Decisões que parecem estranhas mas não são

- **Mensagem de sistema não move a conversa na inbox.** `UltimaMensagemEm` só é
  atualizada por mensagem de gente. Se cada mudança de etapa reordenasse a lista,
  a inbox viraria ruído.
- **`Mensagem.AtendenteId` não é inferido do dono da conversa.** É gravado no
  envio. Se a conversa for transferida depois, o autor original permanece — que é
  exatamente o que o RF-007 pede.
- **`AssumirAsync` é idempotente.** Assumir uma conversa que já é sua devolve a
  conversa, não um erro. Dois cliques no mesmo botão não são conflito.
- **Reabrir devolve a conversa a quem reabriu.** Deixar sem dono a faria
  reaparecer na fila sem ninguém responsável.
- **`SimularRecebimentoAsync` é Fase 1 e sai depois.** É o ponto que o webhook da
  Cloud API vai ocupar na Fase 2.

## Automação

| Peça | Papel |
| --- | --- |
| `DisparadorDeMensagemAutomatica` | Transforma regra cadastrada em mensagem no fio |
| `TriagemAutomatica` | Monta o menu e lê a resposta do cliente. Lógica pura |
| `VarredorDePrazos` | Dispara os dois gatilhos de prazo, que ninguém provoca |
| `VarredorDeInatividade` | Pergunta e encerra conversa de WhatsApp real parada, por tenant configurável — ver abaixo |
| `AplicadorDeEncerramentoPorInatividade` | Interpreta a resposta do cliente à pergunta pendente |

- **O disparador enfileira, não grava.** As mensagens entram no `DbContext` e quem
  chamou decide o `SaveChanges`. É o que põe a mensagem de boas-vindas na mesma
  transação que cria a conversa e gera o protocolo — gravando por conta própria, um
  erro depois deixaria uma boas-vindas órfã apontando para conversa inexistente.
- **O disparador não conhece `IContextoDoUsuario`.** O tenant vem da conversa. Sem
  isso, o varredor — que roda em serviço de fundo, sem requisição HTTP nem token —
  não conseguiria usá-lo. Pelo mesmo motivo o varredor projeta com
  `ProjecaoDeConversa`/`ProjecaoDeMensagem` em vez de chamar `IConversaService`.
- **Menu e interpretação moram na mesma classe.** Os dois dependem da mesma lista de
  setores na mesma ordem; separados, uma mudança de ordenação faria "1" levar ao
  setor errado, e o sintoma apareceria semanas depois na conversa de um cliente real.
- **A flag `AguardandoTriagem` sai em todos os caminhos.** Uma tentativa e acabou —
  inclusive quando a resposta é ambígua entre dois setores. Repetir o menu até o
  cliente acertar prende quem escreveu "bom dia" num loop de robô.
- **Mensagem automática não move a conversa na inbox**, pela mesma regra acima: um
  aviso de prazo disparado de madrugada jogaria dez conversas para o topo da fila
  sem que nada tivesse acontecido com o cliente.
- **O aviso de prazo é reivindicado por `UPDATE` condicional**, na mesma disciplina
  da trava (seção acima). Zero linhas afetadas = outra instância chegou primeiro. É
  o que impede o cliente de receber o mesmo aviso duas vezes.
- **`VarredorDeInatividade` não usa `IConversaService`**, pelo mesmo motivo do
  varredor de prazos: roda em `BackgroundService`, sem `IContextoDoUsuario` nem
  requisição HTTP, e projeta direto do `DbContext`. Two-phase: fase 1 pergunta
  (gatilho `ConfirmacaoDeEncerramento`) quando a conversa está parada há
  `Tenant.MinutosParaPerguntarEncerramento`; fase 2 encerra
  (`EncerramentoPorInatividade`) quando não veio resposta em
  `Tenant.MinutosParaEncerrarSemResposta`. Roda em host próprio
  (`HospedeDaVarreduraDeInatividade`), separado do de prazos, porque uma
  passada com erro não pode derrubar a outra.
- **`PerguntaDeEncerramentoEnviadaEm` é marcador, relógio e flag de despacho no
  mesmo campo.** Um bool separado sempre andaria junto com a data — e dois
  campos que sempre andam juntos são dois campos que um dia discordam. Zerado
  em todo ponto onde o cliente prova estar vivo: qualquer resposta dele,
  atendente respondendo, reabertura, ou o recurso sendo desligado pela tela.
- **Fase 2 encerra mesmo sem `MensagemAutomatica` cadastrada para o gatilho
  13 — diferente do varredor de prazos.** Lá a mensagem é o efeito (regra
  desativada = nada acontece, nem o marcador é gravado). Aqui fechar é o
  efeito e a mensagem é cortesia: deixar a conversa presa em "já perguntei,
  nunca fechei" é exatamente o bug que este varredor existe para consertar.
- **A resposta à pergunta pendente é comparada por palavra inteira, nunca
  substring.** `TriagemAutomatica.LerRespostaDeEncerramento` usa
  `HashSet<string>.Contains` por palavra — diferente de `PediuHumano`/
  `PediuMenu` (que usam `Contains` porque errar ali custa uma mensagem a
  mais). Errar aqui fecharia o atendimento: "assim" contém "sim".
- **Guardas na fase 1** (perguntar): `Canal == WhatsApp` (protege a base de
  demonstração), `MensagensNaoLidas == 0` (não pergunta "quer encerrar?"
  quando o cartório ainda deve resposta) e `UltimaMensagemEm` capturado no
  `WHERE` do `ExecuteUpdateAsync` (corrida com o webhook — 0 linhas afetadas
  = o varredor perde a corrida em silêncio, em vez de colidir com o
  `SaveChanges` do webhook). **Não** olha `PrazoLimite`/SLA — guarda removida
  em 2026-08-23: como a maioria dos serviços tem SLA cadastrado, a versão
  original ficava presa pelos dias inteiros do prazo sempre que o cliente
  escolhia um serviço, e nunca fechava se ele sumisse nesse meio tempo.
  Encerrar é soft-close (o protocolo não desaparece); só o canal de WhatsApp
  fecha, não o atendimento offline do ato.

## Fronteiras

Lê entidades de `Clientes`, `Usuarios`, `Configuracoes` e `Servicos` pelo
`DbContext` — é um banco só. Não chama o `Application` de nenhum deles. As regras de
mensagem automática são cadastro de `Configuracoes`; lê-las aqui segue essa mesma
fronteira.

`INotificadorDeAtendimento` é porta deste slice; o adaptador SignalR vive em
`Infrastructure/Realtime`. `IVarredorDePrazos` e `IVarredorDeInatividade` são
chamados por um `BackgroundService` cada, em `Infrastructure/Automacao`, que é só
relógio e escopo — a regra fica aqui. Os quatro campos de configuração do
encerramento automático moram em `Tenant` (módulo `Tenants`); ler esse cadastro
segue a mesma fronteira de ler `Configuracoes`.
