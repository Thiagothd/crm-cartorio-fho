# Módulo: CentralDeIa

Área privada do desenvolvedor da equipe do projeto (Thiago) — não do cartório-cliente.
Consolida saúde, custo e falha da IA **entre tenants**: nome de provedor,
modelo, tokens e custo, exatamente o que `Modules/RespostaInteligente` (painel
`/automacao/ia`, `IaController`) esconde de propósito de qualquer
Supervisor/Administrador do cartório. As duas telas resolvem problemas
diferentes e não se sobrepõem — não mexa em `pagina-ia.tsx` nem em
`IaController`/`SituacaoDaIa` por causa desta entrega.

## Por que uma identidade própria, fora de Atendente/Tenant

`Atendente` é sempre preso a um `TenantId` — é assim que `IContextoDoUsuario`
resolve `TenantId`/`Perfil`/permissão em toda consulta do sistema, e é a razão
de toda tabela de tenant ser filtrada por ele (CLAUDE.md 7.1). Isso é exatamente
o oposto do que a Central precisa: uma identidade que enxergue TODOS os
tenants ao mesmo tempo, mesmo havendo um só hoje — a tela já nasce pensando em
mais.

Encaixar isso num `Atendente` especial (um "super-perfil" sem `TenantId`, ou um
`TenantId` mágico que "vê tudo") quebraria a invariante que todo o resto do
sistema assume — e um dia alguém confiaria nela por engano numa consulta que
deveria estar isolada. Mais simples e mais seguro: a Central não é um usuário
do CRM. É outra coisa.

## Como a identidade funciona

1. **Segredo compartilhado** (`CentralDeIa:Chave`, `Infrastructure/Identity/OpcoesDaCentralDeIa.cs`)
   — configurado só no servidor/appsettings local, nunca versionado com valor
   real (mesmo padrão de `CanalWhatsApp:TokenDoWebhook`). Comparado em tempo
   fixo (`CentralDeIaAutenticador.SegredoConfere`, mesma técnica de
   `ProcessadorDeWebhook.TokenConfere`) — nunca com `==`, que vazaria o
   tamanho do prefixo correto pelo tempo de resposta.

2. **`POST /api/central-ia/entrar`** (anônimo, com rate limit próprio — ver
   `PoliticaDeLimiteDeLogin.NomeCentralDeIa`) recebe o segredo e, se bater,
   emite um JWT reaproveitando emissor/audiência/chave de `OpcoesDeJwt` — a
   MESMA seção `Jwt` do token de Atendente — mas com um conjunto de claims
   deliberadamente diferente: só `central_ia_operador=true`
   (`ClaimsDaCentralDeIa`), nunca `atendente_id`/`tenant_id`/`perfil`/`token_version`.

3. **A exclusividade é estrutural, não uma checagem a mais.**
   `SessaoAtivaHandler` e `ContextoDoUsuario` exigem `atendente_id`/`tenant_id`/
   `token_version` para funcionar — um token da Central nunca carrega essas
   claims, então ele automaticamente não serve NENHUM endpoint comum do
   sistema. Não existe um `if` que alguém possa esquecer de checar: o token é
   *incapaz* de passar por ali, pela própria forma como foi montado.

4. **Policy nomeada `"CentralDeIa"`** (`Program.cs`, dentro do mesmo
   `AddAuthorization`) exige só `RequireAuthenticatedUser()` +
   `RequireClaim(ClaimsDaCentralDeIa.Operador, "true")`. É a única policy
   nomeada do projeto — o comentário em `Program.cs` explica por que o resto
   do sistema usa só a policy padrão sem nome; aqui é o caso certo de fugir
   disso, porque o ator não é um `Atendente` e não existe `IContextoDoUsuario`
   por trás dele.

5. **Sem `SessaoAtivaRequirement`.** Não existe `Atendente`/`TokenVersion` por
   trás deste token para revalidar no banco a cada request — a mitigação é
   só a expiração curta (`CentralDeIa:ExpiracaoEmMinutos`, padrão 60 min).

## Limitação conhecida — sem revogação antes de expirar

Um token da Central vazado continua válido até o `exp`, sem forma de
derrubá-lo antes disso (ao contrário do token de Atendente, que tem
`TokenVersion` — ver SEC-01 em `Modules/Usuarios/README.md`). É risco aceito,
não ignorado: expiração curta limita a janela, e o único usuário legítimo
desta identidade é o próprio desenvolvedor.

Trocar `CentralDeIa:Chave` impede emitir um token NOVO, mas não invalida um já
emitido — a assinatura do token usa `Jwt:Chave`, não `CentralDeIa:Chave`. A
única forma de derrubar um token da Central já em circulação, antes do `exp`,
é trocar `Jwt:Chave` — o que também derruba todas as sessões de Atendente ao
mesmo tempo. Não é um botão fino; é o extremo, e só vale para o caso raro de
segredo comprometido de verdade.

## O que fica de fora desta entrega

- **Log de auditoria de acesso** — quem entrou na Central e quando. Hoje só
  fica o rastro comum de log da aplicação.
- **Refinamento por tenant quando a Fase 3 existir de verdade** — hoje o
  `SituacaoGlobalDaIaService` já lê todos os tenants existentes (só há um), mas
  não há paginação nem filtro por tenant específico. Adequado enquanto o
  número de tenants for pequeno; reavaliar quando a Fase 3 (multi-tenant real)
  sair do papel.
- **Revogação individual de token antes do `exp`** — ver seção acima.

## Peças

| Peça | Papel |
| --- | --- |
| `ICentralDeIaAutenticador` / `CentralDeIaAutenticador` | Confere o segredo, emite o token via `IGeradorDeTokenDaCentralDeIa` |
| `ISituacaoGlobalDaIa` / `SituacaoGlobalDaIaService` | Único serviço do sistema que lê `ChamadasDeIa` sem filtro de `TenantId` — deliberado, ver comentário na classe |
| `CentralDeIaServiceException` | Só para segredo inválido |
| `Infrastructure/Identity/OpcoesDaCentralDeIa` | Segredo + expiração |
| `Infrastructure/Identity/ClaimsDaCentralDeIa` | Nome da claim própria |
| `Infrastructure/Identity/IGeradorDeTokenDaCentralDeIa` / `GeradorDeTokenDaCentralDeIa` | Emite o JWT com a claim própria |
| `Controllers/CentralDeIaController` | `POST /entrar` (anônimo, rate limit) e `GET /situacao` (policy `CentralDeIa`) |

## Fronteiras

Lê `ChamadasDeIa` (entidade de `Modules/RespostaInteligente/Domain/Entities`) e
`Tenants` direto pelo `DbContext` — permitido, é um banco só (CLAUDE.md 7.1).
Reaproveita `MotivosDeFallbackResponse` e `DisjuntorDoGemini`/`FingerprintDeChave`
de `Modules/RespostaInteligente` da mesma forma: leitura de tipo/serviço
transversal de cota, nunca chamando o `Application` interno daquele módulo por
uma porta que ele não publica. Não referencia `Modules/Usuarios` nem
`IContextoDoUsuario` — é exatamente o ponto deste módulo existir separado.
