# QUALITY GATE DE PULL REQUESTS

> **Status:** ativo para Pull Requests destinados a `teste`.
> **Última atualização:** 2026-08-31

O workflow [`.github/workflows/quality-gate.yml`](../.github/workflows/quality-gate.yml)
valida cada Pull Request destinado a `teste`. Ele roda somente em runners
descartaveis hospedados pelo GitHub, tem permissao apenas de leitura do conteudo
e nao possui job de deploy, push, merge ou acesso ao runner `crm-deploy`.

Nenhum secret do repositorio e consumido. O job com SQL Server gera uma senha
aleatoria durante a propria execucao, mascara o valor no log e a usa somente no
container daquele job.

## Estado de enforcement e configuracao recomendada

O workflow esta **ativo e executa** nos Pull Requests destinados a `teste`, mas
os seus checks **nao devem ser chamados de obrigatorios enquanto a protecao de
branch estiver inativa**. Sem uma Ruleset/Branch Protection ativa, um
colaborador com permissao de escrita ainda pode fazer push direto ou concluir um
merge sem que o GitHub exija os checks. Esta documentacao passa a usar
"checks executados" ate que a configuracao abaixo seja ativada.

No levantamento de 2026-08-31, `teste` estava com `protected=false`.

> **Configuracao remota:** esta recomendacao nao e aplicada por este
> repositorio nem pelo workflow. Um administrador deve ativa-la explicitamente
> nas configuracoes do GitHub. So depois disso os seis checks passam a ser
> "checks obrigatorios".

### Ruleset recomendada (preferencial)

Em **Settings > Rules > Rulesets**, criar uma ruleset de **branch** com os
seguintes valores exatos:

| Campo | Valor |
| --- | --- |
| Nome | `Proteger teste - quality gate` |
| Enforcement status | `Active` |
| Target branches | incluir somente `teste` |
| Bypass list | vazia; nao adicionar administrador, usuario, equipe, GitHub App ou bot |
| Require a pull request before merging | habilitado; 1 aprovacao obrigatoria; `Dismiss stale pull request approvals when new commits are pushed` e `Require approval of the most recent reviewable push` habilitados; `Require review from Code Owners` desabilitado enquanto nao houver `CODEOWNERS` |
| Require conversation resolution before merging | habilitado |
| Require status checks to pass before merging | habilitado, com `Require branches to be up to date before merging` habilitado |
| Required status checks | exatamente os seis contexts da tabela seguinte, selecionando `GitHub Actions` como fonte esperada quando a interface oferecer essa opcao |
| Block force pushes | habilitado |

Nao incluir regras de deploy, migrations, merge queue, assinatura de commits ou
restricao generica de updates nesta ruleset: elas nao fazem parte deste quality
gate e podem impedir o fluxo normal de merge pelo GitHub. A exigencia de Pull
Request, com lista de bypass vazia, bloqueia push direto; `Block force pushes`
impede reescrita de historico. A aprovacao mais recente e a invalidação de
aprovacoes evitam que um commit novo aproveite uma revisao anterior.

Se Rulesets nao estiver disponivel no plano do repositorio, criar a regra
legada em **Settings > Rules > Branch protection rules** para o padrao `teste`,
com os mesmos seis checks e estas opcoes: `Require a pull request before
merging` (1 aprovacao, dismiss stale, approval of the most recent reviewable
push), `Require status checks to pass before merging`, `Require branches to be
up to date before merging`, `Require conversation resolution before merging`,
`Do not allow bypassing the above settings`; manter `Allow force pushes` e
`Allow deletions` desabilitados e nao adicionar atores em `Allow specified
actors to bypass required pull requests` ou em restricoes de push. Essa ultima
opcao e essencial na regra legada porque, por padrao, administradores podem
contornar Branch Protection.

## Checks executados pelo workflow

| Check | O que valida |
| --- | --- |
| `backend-build` | `restore`, build Debug e build Release; warnings do compilador falham o check |
| `backend-verifiers-isolated` | traducao LINQ, limite de login, canal WhatsApp com doubles e resposta inteligente com provedores fake |
| `backend-verifiers-sqlserver` | schema completo e os verificadores que exigem semantica real do SQL Server |
| `frontend-typecheck` | `npm ci` e TypeScript em modo estrito |
| `frontend-tests` | `npm ci` e a suite Vitest |
| `frontend-build` | `npm ci`, typecheck e bundle de producao do Vite |

Os nomes acima sao os contexts exatos a selecionar na Ruleset/Branch Protection.
Depois que a configuracao de enforcement estiver `Active`, todos os seis devem
ser promovidos nesta documentacao e na interface do GitHub a **checks
obrigatorios**.

Nao existe hoje projeto de testes backend xUnit/NUnit/MSTest na solucao. Os
programas `verificar-*.cs` sao a suite automatizada existente e devolvem codigo
diferente de zero quando encontram falha. Por isso nao ha um check vazio chamado
`backend-tests`: um `dotnet test` verde sem descobrir teste algum daria uma
garantia enganosa.

## Matriz completa dos verificadores backend

Classificacao:

- **A** — pode rodar automaticamente em CI puro.
- **B** — pode rodar em CI com dependencia local descartavel.
- **C** — exige secret ou infraestrutura externa e nao deve rodar automaticamente.
- **D** — certificacao manual ou no ambiente TESTE.

| Verificador | Classe | Dependencias e motivo | Execucao automatica |
| --- | --- | --- | --- |
| `verificar-traducao.cs` | A | Usa `ToQueryString()` com servidor propositalmente inexistente; nao abre conexao | `backend-verifiers-isolated` |
| `verificar-limite-de-login.cs` | A | Sobe Kestrel somente em loopback e usa `HttpClient` local; sem banco | `backend-verifiers-isolated` |
| `verificar-canal-whatsapp.cs` | A | EF InMemory, handlers HTTP e gateway fake; nao chama Evolution nem envia mensagem | `backend-verifiers-isolated` |
| `verificar-resposta-inteligente.cs` | A | EF InMemory e provedores fake; nao usa Gemini, Anthropic, chave ou rede | `backend-verifiers-isolated` |
| `verificar-autenticacao.cs` | B | Precisa de `ExecuteUpdateAsync`, concorrencia e persistencia reais do SQL Server | `backend-verifiers-sqlserver` |
| `verificar-concorrencia.cs` | B | Protocolo usa lock/transacao SQL e a trava disputa conexoes independentes | `backend-verifiers-sqlserver` |
| `verificar-concorrencia-saida-whatsapp.cs` | B | Prova o claim atomico de envio com `ExecuteUpdate`; gateway continua fake | `backend-verifiers-sqlserver` |
| `verificar-concorrencia-entrada-whatsapp.cs` | B | Prova idempotencia, ordem, recovery e concorrencia entre conexoes SQL reais; canal e IA continuam fake | `backend-verifiers-sqlserver` |
| `verificar-automacao.cs` | B | Exercita servicos reais, seed e persistencia; nao envia WhatsApp nem chama IA externa | `backend-verifiers-sqlserver` |
| `verificar-qualidade-ia.cs` | C | Exige chaves reais de Gemini e Anthropic, usa rede, pode consumir cota/credito e mede saida nao deterministica; alem disso, divergencias sao apresentadas para avaliacao e nao tornam o processo vermelho | Nao roda no CI |

## SQL Server descartavel

O uso em CI e seguro e razoavel porque cinco verificadores dependem justamente
de recursos que EF InMemory nao reproduz: locks, transacoes, `ExecuteUpdate`,
indices e concorrencia entre conexoes.

O job SQL segue estas barreiras:

1. roda em `ubuntu-latest`, nunca no runner self-hosted de deploy;
2. gera uma senha nova com `openssl rand` e a mascara antes de usa-la;
3. publica a porta SQL somente em `127.0.0.1` do runner;
4. cria apenas `CRM_Cartorio_Dev`, nome exigido pelos verificadores que escrevem;
5. monta o checkout como somente leitura dentro do container;
6. aplica os arquivos `sql/NNN-*.sql` somente nessa base nova;
7. passa connection string efemera diretamente aos verificadores;
8. remove o container em um passo `always()`, inclusive se um verificador falhar;
9. ao final do job, o proprio runner hospedado pelo GitHub tambem e destruido.

Nao ha rota, hostname, usuario, senha ou connection string de DEV compartilhado,
TESTE ou producao no workflow. As migrations executadas pelo quality gate existem
somente no container descartavel; os workflows de deploy continuam separados e
nao sao chamados por Pull Requests.

## O que permanece fora do CI

| Certificacao | Classe | Razao |
| --- | --- | --- |
| Qualidade comparativa Gemini x Haiku | C | Chaves, rede, custo/cota e resultado nao deterministico; executar manualmente com `verificar-qualidade-ia.cs` quando houver mudanca de provedor/prompt |
| Handshake REST/SignalR e revogacao de uma conexao ja aberta | D | O verificador de autenticacao cobre o componente compartilhado, mas o handshake end-to-end continua sendo certificacao no ambiente TESTE |
| Pareamento, webhook, entrada/saida e anexos com WhatsApp real | D | Envolve numero dedicado, Evolution API, sessao pareada e efeitos externos reais |
| Validacao funcional da mudanca no ambiente TESTE | D | Acontece somente depois de revisao e merge; o quality gate de PR nao acessa esse ambiente |
| `/health/pronto` apos deploy | D | Pertence ao workflow de deploy depois do merge, nao a um PR sem efeitos colaterais |

## Reproducao local

### Backend sem banco

Na raiz do repositorio:

```bash
dotnet restore src/backend/CrmCartorio.slnx
dotnet build src/backend/CrmCartorio.slnx -c Debug --no-restore -warnaserror
dotnet build src/backend/CrmCartorio.slnx -c Release --no-restore -warnaserror

cd src/backend
dotnet run verificar-traducao.cs
dotnet run verificar-limite-de-login.cs
dotnet run verificar-canal-whatsapp.cs
dotnet run verificar-resposta-inteligente.cs
```

### Frontend

```bash
cd src/frontend
npm ci
npm run typecheck
npm run test
npm run build
```

### Verificadores com SQL Server

Use somente uma instancia local descartavel com banco chamado
`CRM_Cartorio_Dev`. Aplique os scripts numerados em ordem, começando por
`001-schema.sql` e `002-seed-simulado.sql`, e passe a connection string como
argumento:

```bash
cd src/backend
dotnet run verificar-autenticacao.cs -- "<connection string descartavel>"
dotnet run verificar-concorrencia.cs -- "<connection string descartavel>"
dotnet run verificar-concorrencia-saida-whatsapp.cs -- "<connection string descartavel>"
dotnet run verificar-concorrencia-entrada-whatsapp.cs -- "<connection string descartavel>"
dotnet run verificar-automacao.cs -- "<connection string descartavel>"
```

O workflow contem a receita executavel de criacao, espera, aplicacao do schema e
destruicao do container. Nao substitua a connection string descartavel por uma de
TESTE ou producao.

## Como adicionar um novo verificador

1. Faça o programa devolver `0` somente quando todas as verificacoes passarem e
   codigo diferente de zero para falha real ou dependencia ausente.
2. Documente no cabecalho se ele usa banco, rede, secret, arquivo ou servico externo.
3. Se for classe A, use doubles locais e adicione um passo explicito em
   `backend-verifiers-isolated`.
4. Se for classe B, aceite a connection string por argumento, recuse qualquer
   banco diferente de `CRM_Cartorio_Dev`, limpe os dados criados em `finally` e
   adicione um passo em `backend-verifiers-sqlserver`.
5. Se for classe C ou D, nao o adicione ao workflow: registre aqui como manual e
   explique o risco ou dependencia.
6. Nunca use `continue-on-error` para um gate obrigatorio e nunca transforme
   dependencia ausente em sucesso silencioso.

## Avisos conhecidos do baseline

O build Vite atual termina com sucesso, mas informa duas observacoes que nao sao
falhas funcionais: anotacoes `PURE` posicionadas pelo pacote `@microsoft/signalr`
e o chunk principal acima de 500 kB antes de gzip. Elas ficam registradas para
trabalho futuro de bundle/performance; o quality gate nao altera codigo de produto
apenas para silencia-las.
