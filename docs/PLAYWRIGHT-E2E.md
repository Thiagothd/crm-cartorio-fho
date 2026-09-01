# Playwright E2E — CRM Cartório

## Objetivo

Esta suíte exercita o painel React pelo navegador, como um usuário real, sem depender de DEV compartilhado, TESTE, produção, SQL Server, WhatsApp, Gemini, Anthropic ou qualquer segredo real.

A base desta frente é a branch `teste` no SHA `e41069a7cf909025f3ba4a18effff01bf32f75c6`. O SEC-03/MFA do PR #46 e o quality gate do PR #47 não foram incorporados nem alterados.

## Arquitetura

A suíte usa:

- Chromium via Playwright;
- Vite local para servir exatamente o frontend do repositório;
- `page.route()` para interceptar a API do CRM;
- exceção explícita para os módulos-fonte do Vite em `/src/lib/api/**`, evitando que o roteamento fake capture o próprio cliente HTTP do frontend;
- estado em memória novo por teste;
- respostas com os mesmos formatos consumidos por `src/lib/api/endpoints.ts`;
- `ProblemDetails` com códigos estáveis nos cenários de erro;
- bloqueio de `/hubs/**`, portanto nenhum SignalR real é aberto;
- bloqueio explícito de chamadas HTTPS externas durante os testes.

O objetivo não é substituir os verificadores de backend. Esta camada prova rota → cliente HTTP → React Query → componentes → interação no navegador. Regras de SQL, concorrência real e persistência continuam pertencendo aos verificadores backend.

## Instalação e execução

Primeira preparação:

```bash
cd src/frontend
npm ci
cd e2e
npm ci
npm run install:chromium
```

Executar a suíte a partir de `src/frontend`:

```bash
npm run e2e
```

Modos de desenvolvimento:

```bash
npm run e2e:ui
npm run e2e:headed
```

A configuração sobe o Vite automaticamente em `127.0.0.1:5173` usando `--configLoader runner`. Não é necessário iniciar API ou banco.

## Cobertura inicial

A suíte possui **25 testes em 6 specs**:

- `autenticacao.e2e.ts` — formulário, validação obrigatória, credenciais inválidas, login válido, proteção de rota e revalidação de token;
- `navegacao.e2e.ts` — visibilidade por perfil, proteção de rota administrativa e redirecionamento legado;
- `inbox.e2e.ts` — listagem, abertura de conversa, assumir, conflito de atribuição, busca e respostas rápidas sem envio automático;
- `clientes.e2e.ts` — listagem/busca, filtro de documento ausente, cadastro fictício e proteção contra exclusão de cliente com atendimento;
- `administracao.e2e.ts` — equipe e criação de setor;
- `mobile.e2e.ts` — fluxo lista ↔ conversa e cadastro de clientes em viewport 390×844.

## Responsividade

O projeto principal do Playwright usa Chromium desktop em 1366×768. `mobile.e2e.ts` reduz o mesmo Chromium para 390×844 e habilita touch. Não usamos o preset WebKit/iPhone: a finalidade desta primeira camada é validar comportamento responsivo, não multiplicar a suíte por engines.

Os testes mobile verificam ações funcionais e navegação. Não são testes pixel-perfect nem snapshots visuais.

Nos fluxos desktop autenticados, o fixture grava a preferência real `crm-cartorio.menu-fixado=true` antes da aplicação iniciar. Isso mantém o rail em sua própria coluna durante os cenários de produto e evita que a animação de hover criada pela posição sintética do ponteiro do Chromium intercepte cliques no conteúdo. O comportamento de hover/recolhimento do rail permanece coberto pelos testes unitários próprios do componente; em viewport mobile a própria aplicação ignora essa preferência.

## Isolamento dos dados

`fixtures/api-mock.ts` cria estado novo para cada teste. Nenhum cenário depende do anterior. Clientes, usuários, tenant, conversas, mensagens, setores, etapas e serviços são todos fictícios.

`fixtures/api-mock-seguro.ts` envolve o mock base para preservar o carregamento dos módulos do Vite e estabelecer preferências determinísticas de interface sem `force click`, sleeps arbitrários ou chamadas a ambiente real.

Mutações relevantes alteram o estado em memória para que uma invalidação do React Query leia o novo resultado em seguida; isso evita mocks estáticos que ficariam verdes sem provar a transição da interface.

## Segurança e efeitos externos

A suíte não contém connection string, API key, token de ambiente, número de WhatsApp real ou credencial real. O login utilizado é fictício (`*.local`) e só existe no mock do navegador.

Não executar os E2E apontando `VITE_API_URL` para um ambiente real. O desenho desta suíte pressupõe que as chamadas da API do CRM sejam interceptadas pelo fixture.

## MFA / SEC-03

MFA não faz parte desta frente porque o PR #46 está em desenvolvimento paralelo e não pertence à base `teste` usada aqui. Não houve cherry-pick nem cópia de código do SEC-03.

Depois que o SEC-03 for mergeado e estabilizado, adicionar specs próprias para matrícula, segundo fator, recovery codes, step-up e futuros dispositivos confiáveis, preservando a mesma disciplina de não usar segredos reais.

## Evidências em falha

O Playwright guarda em falha:

- screenshot;
- trace;
- vídeo.

Os diretórios `e2e/test-results`, `e2e/playwright-report` e `e2e/blob-report` são ignorados pelo Git. Em CI, o workflow E2E publica essas evidências somente quando houver falha.

## CI e PR #47

Esta branch não modifica o workflow `quality-gate.yml` do PR #47. Para certificar esta própria frente existe um check independente `e2e-playwright`, somente em Pull Requests destinados a `teste`.

A execução real do GitHub Actions no head `98fa507ba56e0e07700ca10b33145816141f9356` validou, no mesmo job:

- `typecheck` do frontend: PASS;
- Vitest existente: **8 arquivos / 37 testes / 37 PASS**;
- build Vite: PASS;
- Playwright: **25/25 PASS**, sem falhas, em Chromium headless.

Os avisos já conhecidos do build sobre anotações do pacote SignalR e chunk principal acima de 500 kB continuam não bloqueantes e não foram mascarados pela suíte.

Quando PR #47 e esta suíte já estiverem convergidos em `teste`, o check pode continuar independente ou ser incorporado ao quality gate em uma mudança posterior. Não é necessário reabrir o PR #47 para isso.

## Como adicionar um cenário

1. crie estado novo com `criarEstadoE2E()`;
2. implemente no mock somente os contratos que a tela realmente usa;
3. prefira `getByRole`, `getByLabel` e `getByPlaceholder`;
4. evite `waitForTimeout` e sleeps arbitrários;
5. valide a mudança de estado quando a ação mutar dados;
6. nunca esconda falha real com `test.skip`, `try/catch` ou retry excessivo;
7. se um endpoint não estiver mockado, o fixture devolve HTTP 501 em vez de sucesso silencioso.

## Limitações conhecidas

- não prova SQL Server/locks/transações — isso é responsabilidade dos verificadores backend;
- não prova SignalR real;
- não prova WhatsApp real;
- não cobre MFA enquanto o PR #46 não estiver em `teste`;
- primeira matriz de navegador é Chromium, com desktop e viewport mobile;
- filtros/cadastros não listados acima ainda podem receber specs incrementais em PRs futuros.
