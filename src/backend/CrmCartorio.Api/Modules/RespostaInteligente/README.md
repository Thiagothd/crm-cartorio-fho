# Módulo: RespostaInteligente

Camada de resposta grounded (Fase 2B): quando o cliente escreve algo que não bate
com o menu de triagem, tenta responder **só com dado cadastrado** antes de
escalar para atendimento humano.

## Por que existe um slice inteiro para isto

Porque a chamada à API da Anthropic é exatamente o tipo de detalhe de terceiro que
não deve vazar para o Atendimento — mesmo raciocínio do `CanalWhatsApp` com a
Evolution API. O Atendimento fala com este módulo por uma porta só
(`IRespostaInteligenteService`), e quem orquestra o fluxo (escrever a mensagem,
decidir quando escalar) continua sendo o Atendimento — ver
`Atendimento/Application/AplicadorDeRespostaInteligente.cs`.

## Peças

| Peça | Papel |
| --- | --- |
| `IRespostaInteligenteService` / `RespostaInteligenteService` | Chama a API da Anthropic com structured output; nunca lança |
| `MontadorDeContexto` | Monta o texto de grounding: catálogo de `TipoServico` + `RespostaRapida` ativos |
| `PromptDeSistema` | Prompt fixo, versionado em código — regra de segurança, não redação editável |
| `OpcoesDaIA` | Habilitada, ApiKey, Modelo, MaxTrocas, TimeoutSegundos |
| `RespostaInteligenteServiceException` | Só para mau uso da própria porta (ex.: tenant vazio) — a falha de chamar a IA em si nunca chega aqui |

## Decisões que parecem estranhas mas não são

- **Nunca lança.** `ResponderAsync` sempre devolve `RespostaDaIA`, mesmo quando a
  IA está desligada, sem chave, fora do ar, ou devolveu algo que não parseia.
  Quem chama só olha `PodeResponder` e cai para o encaminhamento humano de
  sempre — o mesmo princípio de contingência do resto do sistema (ver
  `CanalWhatsApp`, `IEnviadorDeCanal`). Não existe try/catch em volta da chamada
  em nenhum dos dois lugares que usam este serviço porque não precisa existir.

- **Grounding não usa `Tenant` para horário/endereço**, ao contrário do que o
  plano original previa. O `Tenant` não tem esses campos — o sistema já resolve
  isso via a `RespostaRapida` `/horario` (REQUISITOS.md, item 19), que carrega o
  texto oficial confirmado em CONTEXTO.md. Reusar `RespostaRapida` evita duplicar
  esse dado em outro lugar, e continua "editável pela tela" sem nenhuma tela
  nova — o cartório parceiro já edita isso hoje.

- **`RespostaRapida.Conteudo` aceita marcadores** (`{NomeCliente}`, `{Servico}`,
  etc. — ver `Configuracoes.Domain.PlaceholdersDeMensagem`). Só `{NomeCartorio}`
  é resolvido no grounding; os demais são removidos, porque não fazem sentido
  fora de uma conversa específica e a IA não pode ver um marcador solto e
  reproduzi-lo numa resposta ao cliente.

- **`claude-haiku-4-5` sem `thinking` nem `effort`.** O modelo não aceita esses
  parâmetros (erro 400) — a chamada é simples de propósito, e é o modelo mais
  barato disponível, escolhido especificamente por isso: o grounding é curto e a
  tarefa é Q&A restrita, não raciocínio longo.

- **`output_config.format` (structured output), não texto livre parseado.** O
  sinal de "não sei responder isso" (`podeResponder=false`) é um campo booleano
  que o modelo é obrigado a preencher — nunca inferido de frase livre.

- **Prompt fixo em código, não cadastro.** Ao contrário de `MensagemAutomatica`
  e `RespostaRapida`, este texto não é editável pela tela. É regra de segurança
  (nunca opinião jurídica, nunca inventar dado) — não redação que o cartório parceiro deva
  poder soltar sem revisão de código.

- **O histórico da conversa é reconstruído do banco, não guardado em memória.**
  `AplicadorDeRespostaInteligente.MontarHistoricoAsync` busca as mensagens de
  Cliente e as respostas com `GeradaPorIa=true` desta sequência — exclui a
  primeiríssima mensagem do cliente na conversa (o "bom dia" que abriu o
  atendimento, que nunca passou pela IA). Como a mensagem atual do cliente já
  está no `DbContext` mas ainda não foi salva quando o histórico é montado, a
  consulta `AsNoTracking` nunca a enxerga — não precisa filtrar isso na mão.

## Onde entra no fluxo

`Atendimento/Application/AplicadorDeRespostaInteligente.cs` é chamado de dois
lugares:

1. `AplicadorDeTriagem`, quando o menu não bate com nenhum setor — primeira
   tentativa.
2. Direto de `MensagemService.SimularRecebimentoAsync` e
   `RecebimentoDeWhatsApp.ReceberAsync`, quando `Conversa.AguardandoRespostaInteligente`
   está ligado — trocas seguintes, até `OpcoesDaIA.MaxTrocas` ou até o cliente
   pedir atendente.

## Fronteiras

Não referencia o `Application` de nenhum outro módulo. Fala com o Atendimento
apenas pela porta que publica (`IRespostaInteligenteService`); quem grava
`Mensagem`, decide escalar e chama `IDisparadorDeMensagemAutomatica` é o
Atendimento, não este slice.
