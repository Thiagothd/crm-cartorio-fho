# Módulo: Configuracoes

O que o cartório ajusta pela tela, sem release: **Setor**, **EtapaFunil**,
**MensagemAutomatica** e **RespostaRapida**.

## Os dois eixos do atendimento

## Por que são dois e não um

Erro clássico neste domínio é fundir os dois numa coluna só.

| | `Setor` | `EtapaFunil` |
| --- | --- | --- |
| Responde | *quem* atende | *em que ponto* está |
| Uso | roteamento | progressão (funil) |
| Muda quando | a demanda é de outra área | o atendimento avança |
| Exemplo | Registro de Imóveis | Em processamento / exigência |

Uma conversa pode trocar de setor sem avançar de etapa (foi roteada errado) e
avançar de etapa sem trocar de setor (o caso normal). Duas FKs em `Conversa`,
um histórico só.

## Por que são tabela e não enum

Os nomes que estão semeados vieram de pesquisa de mercado e **ainda não foram
validados pelo cartório parceiro** (`docs/REQUISITOS.md`, seção 5). Como cadastro, ele
renomeia, reordena e desativa tudo pela tela — sem release, sem migration, sem
nos consultar. Como enum, cada palavra dele viraria uma alteração de código.

## Regras que valem a pena conhecer

- **Desativar não apaga.** Conversa que aponta para um setor desativado continua
  legível; o setor só some da lista de opções.
- **O funil não pode ficar vazio.** Desativar a última etapa ativa é recusado —
  senão conversas novas nasceriam sem etapa nenhuma.
- **`EhFinal`** marca a etapa que representa conclusão. É flag de cadastro, não
  posição na lista: o cartório pode ter mais de um final.
- Alterar cadastro exige perfil Supervisor ou Administrador.

## MensagemAutomatica e RespostaRapida

As duas guardam um texto com marcadores e parecem a mesma coisa no banco. São
opostas na responsabilidade:

| | `MensagemAutomatica` | `RespostaRapida` |
| --- | --- | --- |
| Dispara | sozinha, por evento | nunca — o atendente escolhe |
| Quem assina | o sistema | o atendente |
| No fio | rótulo "Automática", sem autor | mensagem normal, com o nome dele |

Por isso a resposta rápida não fere o RF-007: o `AtendenteId` da mensagem
resultante continua sendo o de quem apertou enviar.

### Por que o texto não mora em código

Nenhuma frase que o cliente final lê deveria exigir release para mudar. O cartório parceiro
ainda vai reescrever tudo na voz do cartório
([REQUISITOS.md](../../../../../docs/REQUISITOS.md), seção 5.7) — como constante em
C#, cada ajuste de vocabulário viraria alteração de código.

O **gatilho**, ao contrário, é enum: cada valor corresponde a um ponto de código que
precisa existir para disparar.

### Marcadores

`PlaceholdersDeMensagem` guarda o catálogo e faz a substituição. Marcador fora do
catálogo é **recusado no salvamento** — é a única chance de pegar o erro, porque um
`{NomeClinte}` digitado errado não estouraria em lugar nenhum: sairia literal na
mensagem que o cliente do cartório lê.

`{MenuSetores}` é o que torna o menu de triagem cadastro e não código. O valor vem
de quem dispara (o módulo Atendimento), na mesma ordem em que a resposta do cliente
é interpretada — se as duas listas divergissem, "1" levaria ao setor errado.

O frontend tem um espelho da substituição em
`features/atendimento/placeholders.ts`, usado ao inserir uma resposta rápida no
campo. Mexer aqui exige mexer lá.

### Fronteira com Atendimento

Este módulo é dono do **cadastro**. Transformar cadastro em mensagem no fio é do
módulo Atendimento (`DisparadorDeMensagemAutomatica`), que lê estas tabelas pelo
`DbContext` — é um banco só, e essa é a fronteira que o projeto adotou.
