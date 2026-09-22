# Módulo: RespostaInteligente

Camada de resposta grounded (Fase 2B): quando o cliente escreve algo que não bate
com o menu de triagem, tenta responder **só com dado cadastrado** antes de
escalar para atendimento humano.

**Desde 2026-08-25 (Fase 2G):** dois provedores por trás da mesma porta —
arquitetura pensada como **Gemini 3.7 Flash principal** (gratuito dentro da
franquia) com **Claude Haiku 4.5 de contingência** (paga). Um
`OrquestradorDeProvedores` escolhe, tenta e cai para o outro quando necessário;
a troca é invisível para quem chama e para o cliente.

**Desde 2026-09-03: `Gemini.Habilitado=false`, Haiku é o único provedor de
fato.** Decisão temporária do Thiago — o free tier do Gemini se mostrou
instável demais (timeouts de 20-60s, 429/503 mesmo com tráfego espaçado; ver
"Decisões que parecem estranhas mas não são" abaixo) e o tier pago exige
pré-pagamento mínimo (R$200 na conta testada) que ele não quis comprometer
agora. Nada na orquestração precisou mudar para isso: `PoliticaDeSelecao` pula
o Gemini de forma síncrona (sem HTTP, sem espera) quando desligado. Reativar é
só voltar o flag para `true` — mas reveja `Anthropic.LimiteDiarioHaiku` junto
(foi igualado ao `LimiteDiario` geral enquanto o Haiku carrega 100% do
tráfego; com o Gemini de volta absorvendo a maioria, esse número volta a fazer
sentido menor).

**Desde 2026-09-22: ligar/desligar a IA é pela tela, por tenant.**
`RespostaInteligente:Habilitada` saiu de `OpcoesDaIA` — a chave agora é
`Tenant.RespostaInteligenteHabilitada` (migração `sql/049`), com interruptor
em Automação → IA → Situação. `ResponderAsync` a lê a cada chamada e, desligada,
devolve o mesmo `PodeResponder=false` de "IA desligada" — o Atendimento cai no
piso de sempre sem saber o motivo. O appsettings continua mandando só em
infraestrutura: sem provedor com chave (`OpcoesDaIA.EstaConfigurada`), não há
IA. Uma chave `Habilitada` que ainda esteja no appsettings do servidor é
ignorada. Ver `docs/FLUXO-AUTOMACAO.md`, §1.1.

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
| `MascaradorDeDadosPessoais` | Blindagem LGPD (2026-09): oculta CPF, CNPJ, telefone, e-mail e CEP do histórico e da pergunta do cliente antes de montar o `PedidoDeIa` — nunca do contexto/catálogo, que não é dado pessoal de terceiro |
| `IConhecimentoDeIaService` / `ConhecimentoDeIaService` | Cadastro governado de conhecimento (por serviço/tema, com vigência); toda edição preserva a versão anterior em `HistoricoDeConhecimentoDeIa` antes de sobrescrever |
| `Infrastructure/AquecedorDeConexaoDoGemini` | Mantém a conexão HTTP com o Google viva (sondagem sem chave, custo zero de token) para o primeiro cliente não pagar a abertura de conexão |
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

- **O orçamento de latência do Gemini (8s) é menor que o do Anthropic (20s), de
  propósito.** Não é uma medida de quanto cada um demora — é quanto o cliente
  aceita esperar por cada um. O Gemini é a primeira tentativa: se ele tropeça, a
  espera dele é pura perda, porque ainda falta consultar a contingência. O
  Anthropic é o último recurso: desistir dele cedo demais deixaria o cliente sem
  resposta nenhuma. Daí a assimetria.

  **Achado ao vivo, 2026-09-03** (telemetria de `dbo.ChamadasDeIa` no ambiente de
  teste): quando o Gemini responde, responde em **~2,4s** — mais rápido que o
  Haiku. Mas a primeira chamada depois de um período ocioso estourava o limite
  antigo de 20s, e só então o Haiku (1,5–3,7s) era consultado: **22 a 25 segundos
  para uma pergunta trivial**. Como o canal de um cartório fica ocioso a maior
  parte do dia, quase toda conversa começava exatamente nessa condição — o caso
  raro no papel era o caso comum na prática. Descartados na investigação: rede
  (chamada real ao Google respondeu em 2,2s do próprio servidor), IPv6 (falha em
  681ms, sem travar) e nome de modelo inválido.

- **A causa real da lentidão do Gemini não era conexão fria, era o free tier
  sendo estrangulado.** Investigação ao vivo, 2026-09-03, depois do timeout de
  8s (achado acima) já estar em produção: medi chamada real, direta, do
  próprio servidor, contra `/v1/interactions` e contra `/v1beta/generateContent`
  (5 rodadas cada). Resultado: **~40-50% de falha em qualquer um dos dois
  endpoints**, alternando entre timeout (20-60s), `503 Service Unavailable` e
  `429 Too Many Requests` — às vezes rápido (200-700ms), às vezes pendurando a
  conexão inteira. Testei também os 5 modelos Flash disponíveis na conta
  (3.5/3.6/3.7/3.8/`flash-latest`): todos com o mesmo padrão de falha
  errática, nenhum consistente. Aquecer conexão (achado abaixo) e cortar o
  timeout ajudam a limitar o prejuízo, mas **não eliminam a causa**: é a
  franquia gratuita do Google, que estrangula por taxa e tem prioridade baixa
  na fila. Corrigir de verdade exige tier pago — decisão comercial, não
  técnica, registrada no topo deste README (`Gemini.Habilitado=false`).

- **`AquecedorDeConexaoDoGemini` sonda o Google sem chave de API, e isso é
  intencional.** O objetivo da sondagem é abrir DNS + TCP + TLS e deixar a conexão
  no pool — não gerar texto. Sem chave, o Google responde 401/403 na hora, que
  serve perfeitamente e **não consome nenhum token nem cota da franquia gratuita**.
  Junto com `SetHandlerLifetime(Infinite)` + `PooledConnectionLifetime` em
  `Program.cs`, evita que o `IHttpClientFactory` descarte a cada 2 minutos
  justamente a conexão recém-aquecida.

  Se medições futuras mostrarem que a lentidão da primeira chamada vem do modelo
  frio do lado do Google (e não da conexão fria do nosso lado), aquecer conexão
  reduz mas não elimina o problema — nesse caso a próxima alavanca é disparar
  Gemini e Haiku **em paralelo** após alguns segundos e ficar com quem responder
  primeiro, trocando um pouco de custo por latência previsível. Decisão comercial,
  não técnica: medir `ChamadasDeIa` antes de decidir.

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

## Blindagem LGPD — mascaramento antes de sair

`RespostaInteligenteService.ResponderAsync` é o ÚNICO ponto do sistema que
monta o `PedidoDeIa` — por isso é ali, e só ali, que
`MascaradorDeDadosPessoais.Mascarar` roda sobre cada `TrocaAnterior.Conteudo`
do histórico e sobre a pergunta atual, antes de qualquer HTTP para Gemini ou
Anthropic. **Nunca** aplicado ao `Contexto` (catálogo/conhecimento), que é
conteúdo nosso, não dado pessoal digitado pelo cliente.

Limite honesto, documentado na própria classe: é reconhecimento por padrão de
texto, não de entidade — não cobre nome próprio, e um CPF de 11 dígitos sem
pontuação é indistinguível de um telefone de 11 dígitos sem pontuação (cai na
regra de telefone). Reduz o que sai; não elimina o risco por completo. Ver
[docs/PROTECAO-DE-DADOS.md](../../../../../docs/PROTECAO-DE-DADOS.md), seção 5.

## Verificação

- `verificar-resposta-inteligente.cs` — orquestrador real, provedores fake,
  banco em memória. Cenários 1-30: seleção normal, margem de cota, os cinco
  tipos de falha, recuperação automática do disjuntor, os dois provedores
  falhando, concorrência, `podeResponder=false` não aciona fallback, contagem
  correta dos dois tetos, persistência independente do fluxo de negócio,
  franquia não cadastrada, cota global entre tenants. Cenário 31 (blindagem
  LGPD): captura o `PedidoDeIa` que `RespostaInteligenteService` monta e prova
  que CPF, CNPJ, telefone, e-mail e CEP saem mascarados, sem exercitar
  Gemini/Haiku/cota.
- `verificar-qualidade-ia.cs` — rede real, opt-in (as duas chaves precisam
  estar configuradas). Compara Gemini(`low`)/Gemini(`medium`)/Haiku contra um
  corpus de 30 casos fora do roteiro, medindo invenção de conteúdo e acerto de
  `podeResponder`. Não faz parte do build de rotina.

## Fronteiras

Não referencia o `Application` de nenhum outro módulo. Fala com o Atendimento
apenas pela porta que publica (`IRespostaInteligenteService`); quem grava
`Mensagem`, decide escalar e chama `IDisparadorDeMensagemAutomatica` é o
Atendimento, não este slice.
