# Módulo: CanalWhatsApp

O canal real de WhatsApp do **piloto de demonstração**. É o único lugar do
sistema que conhece o formato da Evolution API — o Atendimento nunca vê um campo
do gateway.

> **Piloto, não produção.** Conexão não oficial (Evolution API, wrapper de
> Baileys) em números dedicados e isolados, nunca no número do cartório. Produção
> e teste usam instâncias diferentes no mesmo gateway. Ver
> [CLAUDE.md](../../../../../CLAUDE.md), seção "Mudança de direção — 2026-08-16",
> e [docs/DEPLOY-WHATSAPP.md](../../../../../docs/DEPLOY-WHATSAPP.md).

## Por que existe um slice inteiro para isto

Porque a via não oficial é **provisória por decisão**. Todo o formato do
terceiro — payload de webhook, rotas REST, forma do QR — mora aqui dentro. O
Atendimento fala com este módulo por porta (`IEnviadorDeCanal`) e recebe por
porta (`IRecebimentoDeWhatsApp`). Migrar para a Cloud API oficial da Meta é
reescrever este módulo, e nada mais.

## Peças

| Peça | Papel |
| --- | --- |
| `IEvolutionClient` / `EvolutionClient` | REST do gateway: criar instância, registrar webhook, QR, desconectar, ler perfil pareado, enviar texto e mídia, baixar mídia, ler a agenda de contatos (`ObterContatosAsync`, usado pela importação em `Modules/Clientes`) |
| `WebhookContracts` + `TradutorDePayload` | Traduz o payload cru para o nosso vocabulário |
| `IPareamentoDoCanal` / `PareamentoDoCanal` | Conectar, trocar de número, desconectar, testar envio e histórico de conexões — a gestão do canal para a tela |
| `IRegrasDoCanal` / `RegrasDoCanal` | Quem pode falar com o canal (liberado/bloqueado/todos) — única fonte da resposta, para os dois sentidos |
| `ControleDeTentativasDePareamento` | Espaça pedidos de QR no tempo (30s), para não gastar tentativa que a Meta já vai bloquear |
| `OpcoesDoCanalWhatsApp` | BaseUrl, ApiKey, instância, token do webhook e a política de números permitidos/bloqueados do `appsettings` — fallback de `IRegrasDoCanal` até alguém configurar pela tela, e trava de arranque do ambiente `Teste` |
| `CanalWhatsAppServiceException` | Códigos estáveis para a tela reagir |

### Por que `IRegrasDoCanal` é um serviço à parte, e não um método a mais em `PareamentoDoCanal`

Porque a mesma pergunta — "este número pode falar com o canal?" — precisa da
mesma resposta nos dois sentidos: `ProcessadorDeWebhook` consulta antes de deixar
uma mensagem virar `Cliente`/`Conversa`, e `EnviadorPeloWhatsApp` consulta antes
de mandar o que o atendente escreveu. Separar em dois lugares seria a receita
clássica para a regra divergir — um lado atualizado, o outro esquecido.

Enquanto `Tenants.WhatsappModoDeRecebimento` estiver nulo (ninguém configurou
pela tela ainda), a fonte é `CanalWhatsApp:NumerosDeTeste` do `appsettings` — o
comportamento que já estava em produção antes desta peça existir. Salvar um modo
pela tela passa a mandar, e o `appsettings` vira letra morta.

## Decisões que parecem estranhas mas não são

- **Leitura defensiva em todo lugar** (`JsonElement`, campo opcional, `null` em
  vez de exceção). O gateway é software de terceiro que muda de formato entre
  versões; uma mudança de campo não pode derrubar o atendimento do cartório.
- **Três eventos são descartados de propósito**, e cada um por um motivo:
  - `fromMe` — é o eco da nossa própria mensagem enviada. Gravar duplicaria no
    fio cada resposta do atendente.
  - **Grupo** (`@g.us`) — grupo não é atendimento individual. Abrir protocolo
    para cada evento poluiria a inbox e atribuiria uma identidade errada à conversa.
  - **Tipo não suportado** (figurinha, localização, contato) — sem tradução útil
    para o fio.
- **`NumerosDeTeste` vale nos dois sentidos.** O webhook descarta entrada antes
  de criar Cliente/Conversa, e o adaptador de saída recusa qualquer destino fora
  da lista. Isso também protege bases de teste que já tenham conversa WhatsApp
  antiga ou importada. Consultas auxiliares de foto de perfil ao gateway seguem
  a mesma política.
- **`NumerosBloqueados` vence a allowlist.** É a barreira entre duas instâncias
  automáticas que compartilham o gateway: produção bloqueia o número de teste,
  impedindo um ciclo de respostas `produção ↔ teste` sem fechar o canal público.
- **Celular brasileiro pode chegar com ou sem o nono dígito no JID.** A política
  considera equivalentes apenas as duas formas estritas do mesmo móvel
  (`55 + DDD + 9[6-9]XXXXXXX` ↔ `55 + DDD + [6-9]XXXXXXX`). Não compara por sufixo,
  não acrescenta DDI e não aplica a regra a fixos ou números internacionais.
  A allowlist continua guardando somente a forma canônica com o nono dígito.
- **Canal desligado ignora webhook antigo.** `Habilitado=false` interrompe
  entrada e saída mesmo se o gateway ainda estiver com a URL registrada.
- **A instância também é conferida.** O token correto não basta: um webhook de
  `cartorio-piloto` nunca pode ser consumido por uma API configurada como
  `cartorio-teste`, mesmo que um banco clonado ainda guarde vínculo antigo.
- **Teste falha antes de subir com configuração ampla.** Canal habilitado nesse
  ambiente exige a instância `cartorio-teste` e exatamente um contato brasileiro
  efetivamente permitido; lista vazia, múltipla, inválida ou totalmente bloqueada
  interrompe a inicialização.
- **`@lid` usa `remoteJidAlt`.** A Evolution 2.3.7 pode entregar um identificador
  opaco em `remoteJid` e o telefone em `remoteJidAlt`; criar Cliente com os
  dígitos do LID faria a allowlist bloquear o contato correto. Um LID sem JID
  alternativo individual é descartado, assim como grupo, status, broadcast e
  newsletter.
- **Sufixo de dispositivo não é telefone.** Em um JID como
  `5519990001488:12@s.whatsapp.net`, `:12` identifica o aparelho vinculado e é
  removido antes de aplicar a política ou criar o Cliente.
- **`PENDING` e `SERVER_ACK` são ignorados** no status de entrega. A mensagem já
  nasce `Enviada` do nosso lado quando o gateway aceita; só `DELIVERY_ACK` e
  `READ` movem para frente.
- **O token do webhook é um terceiro segredo**, diferente da `ApiKey`. O endpoint
  do webhook é anônimo por necessidade — o gateway não carrega JWT —, então esse
  token é a única barreira entre o mundo e um endpoint que cria conversa.
  Separados para que vazar um não entregue o outro.
- **`GarantirInstanciaAsync` nunca chama `instance/create` numa instância que já
  existe.** Incidente ao vivo em 2026-08-22: repetir a criação com a sessão já
  aberta (mesmo com `qrcode=false`) fez o gateway forçar um **logout de
  verdade**, exigindo QR novo — o 403/409 "já existe" não é o idempotente
  inofensivo que parecia ser nesta versão do Evolution. Por isso o método
  primeiro consulta `connectionState`; só cria quando o gateway confirma (404)
  que a instância não existe. Depois disso segue para registrar e confirmar o
  webhook. A criação usa `qrcode=false`; o QR só pode ser pedido pelo POST
  explícito da tela, depois de o webhook estar confirmado e o vínculo com o
  tenant persistido.
- **Consultar estado nunca conecta.** O GET de situação, o polling da tela e os
  indicadores chamam apenas `connectionState`. `instance/connect` pertence
  exclusivamente ao comando de pareamento e não é repetido enquanto a sessão
  estiver `connecting`, evitando `conflict: replaced`.
- **Banco clonado não finge conexão.** Se `Tenants.WhatsappInstancia` ainda
  apontar para outra instância, o GET devolve `nao_vinculado` sem tocar o
  gateway. O POST seguro registra o webhook, corrige o vínculo e reutiliza uma
  sessão já `open` sem gerar outro QR.
- **Uma instância = um tenant.** O webhook descobre o cartório pelo nome da
  instância no payload (`Tenants.WhatsappInstancia`), não por token de sessão.
  Na Fase 1 há um tenant só, mas amarrar agora evita reescrever o recebimento na
  Fase 3.
- **`ObterContatosAsync` (`chat/findContacts`) — confirmado ao vivo em
  2026-08-24.** `POST chat/findContacts/{instancia}` com corpo `{"where":{}}`
  devolve um **array JSON cru**, sem envelope — testado contra o gateway real
  (`cartorio-teste`). Vazio é resultado válido e esperado: a Evolution só
  devolve o que sincronizou no pareamento, não uma leitura ao vivo do telefone,
  e mesmo um número com histórico de conversa real voltou `[]`. Mesma regra de
  `@lid`→`remoteJidAlt` do webhook de mensagem se aplica aqui — confirmado que
  o gateway usa esse campo no mesmo formato em `chat/findChats`.

## Anexo: quem baixa, e o que acontece quando falha

A mídia do WhatsApp está **criptografada** nos servidores da Meta, e só o gateway
sabe decifrar. Por isso é este slice que baixa (`BaixarMidiaAsync`) e entrega os
**bytes** ao Atendimento — passar uma URL obrigaria o Atendimento a falar com o
terceiro, que é exatamente o acoplamento que este módulo existe para evitar.

O download acontece **antes** de gravar a mensagem. O fio nunca deve mostrar um
anexo que ainda está chegando: o atendente clicaria e receberia erro.

**Falha no download não perde a mensagem.** Se a mídia não vier, a mensagem entra
com a legenda e sem arquivo. O atendente ver "o cliente mandou uma foto e ela não
chegou" vale muito mais do que a mensagem sumir por causa do anexo — e uma
figurinha recusada silenciaria uma pergunta de verdade.

O mesmo vale na gravação: tipo fora da lista de permitidos, arquivo grande demais
ou disco cheio derrubam o anexo, nunca a mensagem. **No envio pelo painel é o
contrário** — ali a falha sobe, porque quem anexou está na frente da tela e
precisa saber que o arquivo não foi.

> Os rótulos que o tradutor grava quando o anexo vem sem legenda (`[imagem]`,
> `[documento]`, `[áudio]`, `[vídeo]`) são conhecidos em outros dois lugares:
> `EnviadorPeloWhatsApp`, para não mandar "[imagem]" como legenda de uma imagem,
> e o fio no frontend, para não repetir o rótulo embaixo do próprio anexo. Mudar
> aqui sem mudar lá só aparece em conversa real.

## Assinatura de autoria no texto de saída

Desde 2026-09-04, `AssinaturaDeAutoria` decora o texto que sai pelo gateway com
quem falou — atendente ou IA — em `EnviadorPeloWhatsApp.EnviarAsync`, no
momento exato da chamada. Até então o cliente não via nenhuma marca: nem nome
de atendente, nem sinal de automático, mesmo o painel já mostrando os dois.

- **Mensagem de atendente** sai com um cabeçalho em negrito, numa linha antes
  do conteúdo: `*Nome do Atendente*` seguido de quebra de linha.
- **Resposta da IA** sai com o cabeçalho `*Assistente Virtual*` — o rótulo
  descreve QUEM fala, não o mecanismo (por isso não é "mensagem automática").
- **Mensagem automática de regra cadastrada** não leva cabeçalho nenhum —
  decisão deliberada: aquele texto já fala na voz institucional do cartório,
  e marcar toda automática viraria ruído repetido em quase toda mensagem do
  fluxo guiado.
- **Foto sem legenda não ganha cabeçalho** — criar uma legenda que só tem o
  nome, embaixo de uma imagem muda, é mais estranho que não assinar aquela
  mensagem em particular.

**Por que mora aqui e não no `Atendimento`.** `*negrito*` é sintaxe do
WhatsApp — trocar para a Cloud API da Meta mantém a decisão; trocar o canal
por SMS ou e-mail jogaria fora. **Por que não decora dentro de
`PrepararArquivoAsync`.** Aquela função tem uma responsabilidade — decidir se
há texto de gente ou se é rótulo de anexo — e é ela que o comentário de
`RotulosDeAnexo` amarra ao `TradutorDePayload`. **Por que não no
`EvolutionClient`.** `PareamentoDoCanal` chama `gateway.EnviarTextoAsync`
direto para o "testar envio" da tela de gestão do canal; a mensagem de teste
sairia assinada como se fosse de alguém.

**Por que só na saída, nunca gravado no banco.** Se a decoração fosse
persistida em `Mensagem.Conteudo`, o cabeçalho apareceria no painel, na
prévia da citação, no texto copiado por encaminhamento e no histórico que
alimenta o grounding da IA — que passaria a ver "Maria Rezende" como parte da
conversa e a imitar. A projeção do alvo em `EnviadorPeloWhatsApp` carrega
`AutorTipo`, `EhAutomatica`, `GeradaPorIa` e o nome do atendente (subconsulta
correlacionada) só para decidir o cabeçalho no instante do envio; nada disso
sai do método.

## Fronteiras

Não referencia o `Application` de nenhum outro módulo. Fala com o Atendimento
apenas pelas portas que aquele slice publica.
