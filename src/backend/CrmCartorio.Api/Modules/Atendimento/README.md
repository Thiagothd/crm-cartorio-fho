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

## Fronteiras

Lê entidades de `Clientes`, `Usuarios`, `Configuracoes` e `Servicos` pelo
`DbContext` — é um banco só. Não chama o `Application` de nenhum deles. As regras de
mensagem automática são cadastro de `Configuracoes`; lê-las aqui segue essa mesma
fronteira.

`INotificadorDeAtendimento` é porta deste slice; o adaptador SignalR vive em
`Infrastructure/Realtime`. `IVarredorDePrazos` é chamado por um `BackgroundService`
em `Infrastructure/Automacao`, que é só relógio e escopo — a regra fica aqui.
