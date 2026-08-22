# Módulo: CanalWhatsApp

O canal real de WhatsApp do **piloto de demonstração**. É o único lugar do
sistema que conhece o formato da Evolution API — o Atendimento nunca vê um campo
do gateway.

> **Piloto, não produção.** Conexão não oficial (Evolution API, wrapper de
> Baileys) num **número dedicado ao piloto**, não no do cartório. Ver
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
| `IEvolutionClient` / `EvolutionClient` | REST do gateway: criar instância, registrar webhook, QR, desconectar, ler perfil pareado, enviar texto e mídia, baixar mídia |
| `WebhookContracts` + `TradutorDePayload` | Traduz o payload cru para o nosso vocabulário |
| `IPareamentoDoCanal` / `PareamentoDoCanal` | Conectar, trocar de número, desconectar, testar envio e histórico de conexões — a gestão do canal para a tela |
| `IRegrasDoCanal` / `RegrasDoCanal` | Quem pode falar com o canal (liberado/bloqueado/todos) — única fonte da resposta, para os dois sentidos |
| `ControleDeTentativasDePareamento` | Espaça pedidos de QR no tempo (30s), para não gastar tentativa que a Meta já vai bloquear |
| `OpcoesDoCanalWhatsApp` | BaseUrl, ApiKey, instância, token do webhook, `NumerosDeTeste` (fallback de `IRegrasDoCanal`) |
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
  - **Grupo** (`@g.us`) — o número do piloto é pessoal e está em grupos de
    família. Abrir protocolo para cada um poluiria a inbox com a vida do Thiago.
  - **Tipo não suportado** (figurinha, localização, contato) — sem tradução útil
    para o fio.
- **`PENDING` e `SERVER_ACK` são ignorados** no status de entrega. A mensagem já
  nasce `Enviada` do nosso lado quando o gateway aceita; só `DELIVERY_ACK` e
  `READ` movem para frente.
- **O token do webhook é um terceiro segredo**, diferente da `ApiKey`. O endpoint
  do webhook é anônimo por necessidade — o gateway não carrega JWT —, então esse
  token é a única barreira entre o mundo e um endpoint que cria conversa.
  Separados para que vazar um não entregue o outro.
- **`GarantirInstanciaAsync` é idempotente.** Instância já existente não é erro:
  segue para registrar o webhook. É o que permite a tela de pareamento ser um
  botão só.
- **Uma instância = um tenant.** O webhook descobre o cartório pelo nome da
  instância no payload (`Tenants.WhatsappInstancia`), não por token de sessão.
  Na Fase 1 há um tenant só, mas amarrar agora evita reescrever o recebimento na
  Fase 3.

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

## Fronteiras

Não referencia o `Application` de nenhum outro módulo. Fala com o Atendimento
apenas pelas portas que aquele slice publica.
