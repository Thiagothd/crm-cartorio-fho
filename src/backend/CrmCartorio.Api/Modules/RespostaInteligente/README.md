# Módulo: RespostaInteligente

Camada de resposta grounded (Fase 2B): quando o cliente escreve algo que não bate
com o menu de triagem, tenta responder **só com dado cadastrado** antes de
escalar para atendimento humano.

**Desde 2026-08-25 (Fase 2G):** dois provedores por trás da mesma porta —
**Gemini 3.7 Flash é o principal** (gratuito dentro da franquia), **Claude
Haiku 4.5 é a contingência** (paga). Um `OrquestradorDeProvedores` escolhe,
tenta e cai para o outro quando necessário; a troca é invisível para quem
chama e para o cliente.

## Por que existe um slice inteiro para isto

Porque a chamada aos provedores de IA é exatamente o tipo de detalhe de terceiro
que não deve vazar para o Atendimento — mesmo raciocínio do `CanalWhatsApp` com a
Evolution API. O Atendimento fala com este módulo por uma porta só
(`IRespostaInteligenteService`), e quem orquestra o fluxo (escrever a mensagem,
decidir quando escalar) continua sendo o Atendimento — ver
`Atendimento/Application/AplicadorDeRespostaInteligente.cs`. Essa fronteira não
mudou com os dois provedores: o Atendimento nunca soube que existia só um
provedor, e continua sem saber que existem dois.

## Peças

| Peça | Papel |
| --- | --- |
| `IRespostaInteligenteService` / `RespostaInteligenteService` | Porta pública; monta o pedido e delega ao orquestrador. Nunca lança |
| `IOrquestradorDeProvedores` / `OrquestradorDeProvedores` | Escolhe o provedor, tenta, cai para o outro, registra a tentativa. Reserva os dois tetos diários |
| `Provedores/ProvedorGemini` | Interactions API do Gemini (`HttpClient` tipado — não há SDK .NET oficial para esta API) |
| `Provedores/ProvedorAnthropic` | SDK `Anthropic`, mesma chamada estruturada de antes |
| `Cota/PoliticaDeSelecao` | Decide, antes de qualquer HTTP, se tenta o Gemini agora |
| `Cota/ContadorDeCotaDoGemini` | RPM/TPM/RPD do Gemini — **global do projeto Google**, não por tenant |
| `Cota/DisjuntorDoGemini` | Fechado → Aberto → Meio-Aberto; recuperação automática por sonda |
| `Cota/ContadorDeLimiteDiario` | Reserva atômica dos dois tetos diários por tenant (`LimiteDiario`, `LimiteDiarioHaiku`) |
| `Cota/RegistradorDeChamadas` | Grava `ChamadaDeIa` num `DbContext` próprio — sobrevive a falha do fluxo de negócio |
| `MontadorDeContexto` | Monta o texto de grounding: catálogo de `TipoServico` (nome, setor, descrição, **documentos necessários e prazo/SLA**), `RespostaRapida` ativa e `ConhecimentoDeIa` ativo/vigente — igual para os dois provedores |
| `IConhecimentoDeIaService` / `ConhecimentoDeIaService` | Cadastro governado de conhecimento (por serviço/tema, com vigência); toda edição preserva a versão anterior em `HistoricoDeConhecimentoDeIa` antes de sobrescrever |
| `PromptDeSistema` | Prompt fixo, versionado em código. `ParaAnthropic()`/`ParaGemini()` — mesmo texto, ajuste por modelo só se um caso real do corpus de qualidade provar necessidade |
| `OpcoesDaIA` / `OpcoesDoGemini` / `OpcoesDoAnthropic` | Configuração dos dois provedores + tetos |
| `ISituacaoDaIa` / `SituacaoDaIa` | Estado para o painel `/automacao/ia` e resumo leve para Indicadores/sino |
| `RespostaInteligenteServiceException` | Só para mau uso da própria porta (ex.: tenant vazio) |

## Decisões que parecem estranhas mas não são

- **Nunca lança.** `ResponderAsync` sempre devolve `RespostaDaIA`, mesmo quando a
  IA está desligada, sem chave, os dois provedores fora do ar, ou devolveram algo
  que não parseia. Quem chama só olha `PodeResponder` e cai para o encaminhamento
  humano de sempre — o mesmo princípio de contingência do resto do sistema (ver
  `CanalWhatsApp`, `IEnviadorDeCanal`).

- **`podeResponder=false` não é falha, é resposta válida.** O modelo respondeu,
  só não sabia o que dizer. Isso **não** aciona o outro provedor — gastar Haiku
  para provavelmente ouvir a mesma coisa seria dinheiro jogado fora. Fallback é
  só para erro operacional (rede, cota, indisponibilidade, configuração).

- **A cota do Gemini é global do projeto Google, não por tenant.** A franquia
  gratuita é por projeto — se dois tenants compartilharem a mesma chave, eles
  compartilham RPM/TPM/RPD de verdade. `ContadorDeCotaDoGemini` e
  `DisjuntorDoGemini` são singletons particionados só por um fingerprint não
  secreto da chave (`FingerprintDeChave`), nunca a chave em si.

- **Limites do Gemini em `0` = não cadastrado, nunca um número inventado.** O
  Google parou de publicar RPM/TPM/RPD do free tier na documentação — só
  aparecem em `aistudio.google.com/rate-limit`, por conta. Com os três em `0`,
  o controle preventivo por percentual fica desligado (não existe "85% de uma
  cota desconhecida") e o disjuntor reativo (429 real) vira a única
  autoridade, com um freio local conservador (`LimiteRpmDeDescoberta`) evitando
  martelar a API às cegas.

  **Achado ao vivo em 2026-08-25:** um 429 real citou `limit: 20` para a
  métrica `generate_content_free_tier_requests` do `gemini-3.7-flash` — mas a
  conta de teste ficou repetidamente rate-limitada mesmo espaçando chamadas
  bem abaixo desse número (uma a cada 10s = 6/min). Ou o limite prático real é
  mais baixo que o anunciado, ou a janela de recuperação é mais longa que um
  minuto corrido. **Não trate "20" como garantido** — confirme na conta real
  antes de dimensionar produção, e monitore `dbo.ChamadasDeIa` nas primeiras
  semanas do piloto.

- **`input` do Gemini vai como string única, não array de turnos.** Testado ao
  vivo: `/v1/interactions` devolveu `400 "Unknown parameter 'turns' at
  'input'"` para um array de objetos `{role, parts}` — a forma que a
  documentação (que só detalha a aba `/v1beta`) sugeria por analogia ao
  `generateContent` legado. A forma que funciona (`ProvedorGemini.MontarInput`)
  transcreve o histórico inline num único texto, com marcadores de turno.

- **`store: false` sempre.** O histórico do cartório não fica retido na nuvem
  do Google — coerente com o posicionamento LGPD do produto (CLAUDE.md §4).
  Por isso não se usa `previous_interaction_id` (exigiria `store: true`); o
  histórico é sempre reconstruído do nosso banco e enviado inline.

- **Retry-delay do Gemini vem em texto livre, não campo estruturado.** A
  documentação genérica do Google descreve `error.details[].retryDelay`
  estruturado; o 429 real do free tier manda só uma frase
  ("Please retry in 40.76s.") dentro de `error.message`. `ProvedorGemini` tenta
  o campo estruturado primeiro e cai para um regex no texto — e **nunca**
  confia cegamente no número: qualquer valor fora de `(0, 3600]` segundos é
  descartado (achado ao vivo: um parse retornou um valor absurdo uma vez, e sem
  esse limite o disjuntor teria aberto por "anos").

- **`claude-haiku-4-5` sem `thinking` nem `effort`.** O modelo não aceita esses
  parâmetros (erro 400) — a chamada é simples de propósito, e é o modelo mais
  barato disponível, escolhido especificamente por isso.

- **`gemini-3.7-flash` com `thinking_level: low`.** Latência é prioridade num
  chat de WhatsApp. Mesmo em `low`, o modelo ainda gasta uma quantidade
  pequena de tokens de pensamento (observado: dezenas a poucas centenas por
  chamada) — normal, não é um bug. Ver `verificar-qualidade-ia.cs` para
  comparar `low` × `medium` quando a franquia da conta permitir uma corrida
  completa (o corpus de 2026-08-25 não completou a comparação por causa do
  achado de rate limit acima).

- **`response_format` (structured output), não texto livre parseado**, nos dois
  provedores. O sinal de "não sei responder isso" (`podeResponder=false`) é um
  campo booleano que o modelo é obrigado a preencher — nunca inferido de frase
  livre.

- **Prompt fixo em código, não cadastro.** Ao contrário de `MensagemAutomatica`
  e `RespostaRapida`, este texto não é editável pela tela. É regra de segurança
  (nunca opinião jurídica, nunca inventar dado) — não redação que o cartório parceiro deva
  poder soltar sem revisão de código. `PromptDeSistema.AjusteParaGemini` existe
  para um ajuste específico do Gemini, mas nasce vazio: só cresce se um caso
  real do corpus de qualidade provar desvio, nunca por suposição.

- **`dbo.ChamadasDeIa` é gravada num `DbContext` próprio**
  (`RegistradorDeChamadas`, via `IServiceScopeFactory`), não o da requisição —
  fica registrada mesmo quando os dois provedores falham e nenhuma `Mensagem`
  chega a ser criada, e mesmo que a transação de negócio da requisição sofra
  rollback. Nunca grava conteúdo de mensagem, dado do cliente, chave ou trecho
  de prompt — só metadado técnico (tokens, latência, falha, custo estimado).

- **Os dois tetos diários nunca dobram numa única resposta.** `LimiteDiario`
  (geral) é reservado uma vez por mensagem, confirmado só no sucesso de algum
  provedor; `LimiteDiarioHaiku` só é debitado no sucesso do Anthropic. Gemini
  falhar e o Haiku responder soma 1 no geral e 1 no do Haiku — nunca 2 no
  geral. Ver `verificar-resposta-inteligente.cs`, cenário 11.

- **O histórico da conversa é reconstruído do banco, não guardado em memória.**
  `AplicadorDeRespostaInteligente.MontarHistoricoAsync` busca as mensagens de
  Cliente e as respostas com `GeradaPorIa=true` desta sequência — exclui a
  primeiríssima mensagem do cliente na conversa (o "bom dia" que abriu o
  atendimento, que nunca passou pela IA).

## Onde entra no fluxo

`Atendimento/Application/AplicadorDeRespostaInteligente.cs` — **não mudou**
com os dois provedores, é chamado de dois lugares:

1. `AplicadorDeTriagem`, quando o menu não bate com nenhum setor — primeira
   tentativa.
2. Direto de `MensagemService.SimularRecebimentoAsync` e
   `RecebimentoDeWhatsApp.ReceberAsync`, quando `Conversa.AguardandoRespostaInteligente`
   está ligado — trocas seguintes, sem limite de contagem: só sai por pedido
   explícito de atendente ou quando a própria IA não souber responder
   (`podeResponder=false`).

## Verificação

- `verificar-resposta-inteligente.cs` — orquestrador real, provedores fake,
  banco em memória. 13 cenários, 56 verificações: seleção normal, margem de
  cota, os cinco tipos de falha, recuperação automática do disjuntor, os dois
  provedores falhando, concorrência, `podeResponder=false` não aciona
  fallback, contagem correta dos dois tetos, persistência independente do
  fluxo de negócio, franquia não cadastrada, cota global entre tenants.
- `verificar-qualidade-ia.cs` — rede real, opt-in (as duas chaves precisam
  estar configuradas). Compara Gemini(`low`)/Gemini(`medium`)/Haiku contra um
  corpus de 30 casos fora do roteiro, medindo invenção de conteúdo e acerto de
  `podeResponder`. Não faz parte do build de rotina.

## Fronteiras

Não referencia o `Application` de nenhum outro módulo. Fala com o Atendimento
apenas pela porta que publica (`IRespostaInteligenteService`); quem grava
`Mensagem`, decide escalar e chama `IDisparadorDeMensagemAutomatica` é o
Atendimento, não este slice.
