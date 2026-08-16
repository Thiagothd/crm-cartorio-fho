# Módulo: CanalWhatsApp

O canal real de WhatsApp do **piloto de demonstração**. É o único lugar do
sistema que conhece o formato da Evolution API — o Atendimento nunca vê um campo
do gateway.

> **Piloto, não produção.** Conexão não oficial (Evolution API, wrapper de
> Baileys) no **número pessoal do Thiago**, não no do cartório. Ver
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
| `IEvolutionClient` / `EvolutionClient` | REST do gateway: criar instância, registrar webhook, QR, enviar, baixar mídia |
| `WebhookContracts` + `TradutorDePayload` | Traduz o payload cru para o nosso vocabulário |
| `OpcoesDoCanalWhatsApp` | BaseUrl, ApiKey, instância, token do webhook |
| `CanalWhatsAppServiceException` | Códigos estáveis para a tela reagir |

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

## Fronteiras

Não referencia o `Application` de nenhum outro módulo. Fala com o Atendimento
apenas pelas portas que aquele slice publica.
