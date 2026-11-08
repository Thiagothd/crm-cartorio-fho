# Backend — CrmCartorio.Api

ASP.NET Core 10 + EF Core 10 + SignalR, arquitetura vertical slice.

## Como subir

### 1. Banco de dados

**Nada é executado automaticamente.** Crie a base e rode os scripts à mão:

```sql
CREATE DATABASE CrmCartorio;
GO
USE CrmCartorio;
GO
```

Depois, na ordem:

| Arquivo | O que faz |
| --- | --- |
| [`sql/001-schema.sql`](../../sql/001-schema.sql) | Tabelas, FKs, índices, `CHECK` |
| [`sql/002-seed-simulado.sql`](../../sql/002-seed-simulado.sql) | Tenant, atendentes, setores, etapas, serviços, SLAs, mensagens automáticas, respostas rápidas |

O `001` é mantido **atualizado**: já contém tudo que as migrações `003` a `005`
acrescentaram. Base nova roda só `001` + `002`. Base que **já existe** roda as
migrações que ainda faltarem — a última é
[`sql/005-mensagens-automaticas.sql`](../../sql/005-mensagens-automaticas.sql), que
cria `MensagensAutomaticas` e `RespostasRapidas`.

### 2. Configuração

`appsettings.Development.json` já traz valores locais descartáveis. Para qualquer
outro ambiente, defina fora do controle de versão:

```
ConnectionStrings__SqlServer=...
Jwt__Chave=...            # mínimo 32 bytes; a API se recusa a subir com menos
```

Automação de mensagens (opcional, valores padrão abaixo):

```
Automacao__VarrerPrazos=true       # desligado, os gatilhos de prazo nunca disparam
Automacao__IntervaloEmMinutos=5    # entre passadas da varredura
```

Os gatilhos de **evento** (novo atendimento, etapa alterada) não dependem disso —
nascem da ação do atendente. Só os de prazo precisam da varredura, porque ninguém
provoca a passagem do tempo.

### 3. Rodar

```bash
cd src/backend
dotnet run --project CrmCartorio.Api
```

API em `http://localhost:5210`. OpenAPI em `/openapi/v1.json` (só em Development).

### 4. Dados de demonstração

Com `Seed:Habilitado = true` (padrão em Development), a API popula clientes,
conversas e mensagens fictícias **na primeira execução com a base vazia**.

O semeador **não cria schema**. Se as tabelas não existirem, ele avisa no log e
desiste — rode o `001-schema.sql` antes.

Mensagens automáticas e respostas rápidas são semeadas **em separado**, mesmo com a
base já configurada: chegaram depois, e o bloco "se não há tenant" nunca mais roda
numa base montada. Se as tabelas ainda não existirem, o semeador avisa apontando
para o `005` e segue.

**Os textos semeados são rascunho** e aguardam a redação do cartório parceiro
([REQUISITOS.md](../../docs/REQUISITOS.md), seção 5.7). Os dois avisos de prazo
nascem **desativados** — contar ao cliente que o cartório estourou o próprio prazo é
decisão dele, não nossa.

Usuários criados (senha `cartorio2026` para todos):

| E-mail | Perfil de acesso | Cargo na serventia |
| --- | --- | --- |
| `helena@cartoriogameleiras.com.br` | Administrador | Titular |
| `rafael@cartoriogameleiras.com.br` | Administrador | Substituto |
| `carla@cartoriogameleiras.com.br` | Supervisor | Escrevente autorizado |
| `paula@cartoriogameleiras.com.br` | Atendente | Escrevente |

Perfil e cargo são eixos independentes desde a migração `007` — ver
[Modules/Usuarios](CrmCartorio.Api/Modules/Usuarios/README.md). Carla e Paula
são fictícias: existem para demonstrar a trava de conversa, que precisa de mais
de um atendente na mesma fila.

## Organização

```
CrmCartorio.Api/
├── Controllers/          ← finos: recebem, delegam ao slice, devolvem
├── Domain/Common/        ← EntidadeBase, IEntidadeDeTenant, ExcecaoDeAplicacao
├── Infrastructure/       ← transversal; regra de negócio nunca mora aqui
│   ├── Automacao/        ← BackgroundService da varredura: só relógio e escopo
│   ├── Identity/         ← JWT (geração e claims)
│   ├── Persistence/      ← CrmDbContext, semeador de demonstração
│   ├── Realtime/         ← hub SignalR e adaptador do notificador
│   ├── Security/         ← hash de senha, contexto do usuário
│   └── Web/              ← middleware de tratamento de erro
└── Modules/              ← os vertical slices, cada um com seu README
    ├── Atendimento/      ← Conversa, Mensagem, HistoricoEtapa — a trava mora aqui
    ├── Clientes/
    ├── Configuracoes/    ← Setor, EtapaFunil, MensagemAutomatica, RespostaRapida
    ├── Consultas/        ← CEP e CNPJ em fonte pública; sem entidade e sem tabela
    ├── Servicos/         ← TipoServico e PrazoServico (SLA)
    ├── Tenants/
    └── Usuarios/         ← Atendente e autenticação
```

## Tempo real

Hub em `/hubs/atendimento`, um grupo por tenant. O token vai pela query string
(`access_token`) porque o WebSocket não carrega cabeçalho `Authorization` — isso
está restrito ao caminho `/hubs`.

Eventos: `ConversaAtribuida`, `ConversaLiberada`, `ConversaTransferida`,
`ConversaAtualizada`, `ConversaEncerrada`, `ConversaReaberta`, `MensagemRecebida`.

## Erros

Erro de negócio sai como `ProblemDetails` com um campo `codigo` estável — o
frontend reage ao código, nunca ao texto. Qualquer outra exceção vira 500
genérico, sem vazar stack trace.

Conflito de concorrência (`DbUpdateConcurrencyException`) é traduzido para 409;
conflito de atribuição de conversa sai como 409 com `codigo: conversa_ja_assumida`
e o nome de quem assumiu.

## Migrations

Não há migrations do EF Core. O DDL é escrito à mão em `sql/` e revisado
manualmente, conforme a regra 1 do [docs/CONVENCOES.md](../../docs/CONVENCOES.md). As
`IEntityTypeConfiguration` mapeiam para esse schema — se um for alterado, o outro
precisa acompanhar.

Os relacionamentos são declarados **sem propriedade de navegação**
(`HasOne<Tenant>().WithMany().HasForeignKey(...)`). O EF precisa conhecer a
dependência para ordenar os `INSERT` e não violar as FKs do schema; navegar de
`Conversa` para `Cliente` ou `Setor` acoplaria os domínios de módulos diferentes.

## Verificadores

Os scripts rodam de `src/backend/`. O catálogo completo, incluindo os
verificadores de autenticação, concorrência da entrada/saída WhatsApp, resposta
inteligente, dependências e classificação para CI, está em
[`docs/QUALITY-GATE.md`](../../docs/QUALITY-GATE.md).

### `verificar-traducao.cs` — não precisa de banco

```bash
dotnet run verificar-traducao.cs
```

Uma consulta LINQ que não traduz compila normalmente e só estoura em execução.
Este verificador gera o SQL de todas as consultas do sistema com `ToQueryString()`
— que exercita o tradutor sem abrir conexão — e falha se alguma não traduzir.

Não valida resultado, só tradução. **Rode ao mexer em qualquer consulta.**

### `verificar-concorrencia.cs` — escreve no banco

```bash
dotnet run verificar-concorrencia.cs ["<connection string>"]
```

Dispara 12 chamadas realmente simultâneas contra os **serviços reais** em cada um
dos dois pontos sensíveis a corrida:

| Teste | O que verifica |
| --- | --- |
| `ConversaService.CriarAsync` | 12 atendimentos abertos no mesmo instante geram 12 protocolos distintos |
| `ConversaService.AssumirAsync` | 12 atendentes disputando a mesma conversa → exatamente 1 vence, o resto vira conflito tratado |

Ele chama o serviço, não uma cópia da consulta — e isso não é detalhe. A primeira
versão copiava só o `UPDATE` condicional e passava, enquanto a API devolvia 500:
a transação explícita do serviço era incompatível com a estratégia de retry então
configurada. Copiar a consulta testa a cópia, não o sistema.

**Escreve no banco.** Só contra base de desenvolvimento. Remove tudo que criou ao
final, inclusive sobras de execuções anteriores.

### `verificar-automacao.cs` — escreve no banco

```bash
dotnet run verificar-automacao.cs ["<connection string>"]
```

33 verificações sobre o fluxo inteiro da automação, chamando os serviços reais:

| Teste | O que verifica |
| --- | --- |
| 1 | Boas-vindas sai, menu cita os setores ativos, resposta "2" roteia para o **segundo** setor, histórico grava autoria automática |
| 2 | Resposta fora do menu dispara o fallback e a segunda mensagem **não** repete o menu |
| 3 | Conversa aberta já com setor não recebe menu nenhum |
| 4 | Etapa sem regra não dispara; etapa final dispara 1 aviso com `{Protocolo}` e `{Servico}` resolvidos |
| 5 | Varredura avisa uma vez, grava o marcador, **não reavisa** na segunda passada e não reordena a inbox |
| 6 | Transferência entre atendentes (RF-013) e a anotação interna que ela deixa |

Testa a segunda opção do menu, e não a primeira, de propósito: é o que separa "leu
a resposta do cliente" de "sempre cai no primeiro setor".

Não altera a configuração existente — os gatilhos de prazo nascem desativados por
decisão do cartório parceiro e continuam assim; o teste cria regra própria e a remove no final.
Limpa também na **entrada**, senão uma execução que aborta no meio travaria todas as
seguintes num erro sem relação com o que se quer testar.

### `verificar-canal-whatsapp.cs` — isolado de banco e gateway reais

```bash
dotnet run verificar-canal-whatsapp.cs
```

Confere a política usada na entrada e na saída do canal: lista vazia mantém o
canal público, allowlist aceita somente E.164 configurado, bloqueio pontual tem
precedência e o payload `@lid` da Evolution 2.3.7 usa `remoteJidAlt` como telefone
real. Também chama os adaptadores reais de entrada e saída com portas falsas e
banco descartável em memória; não abre conexão nem chama a Evolution. Rode ao
mexer em `OpcoesDoCanalWhatsApp`, tradução do webhook ou envio pelo gateway.

## Reset do ambiente de demonstração

Depois de mexer manualmente nas telas, para voltar ao estado inicial:

```bash
sqlcmd -S .\SQLEXPRESS -E -C -d CRM_Cartorio_Dev -i ../../sql/dev-resetar-atendimentos.sql
```

Apaga clientes, conversas, mensagens e histórico; preserva a configuração. Na
próxima subida da API o semeador recria os 10 atendimentos com datas relativas a
agora. É o **único** script do repositório que apaga dados, e ele se recusa a
rodar se o nome do banco não parecer de desenvolvimento.

## Por que não há `EnableRetryOnFailure`

A estratégia de retry do SQL Server recusa transação iniciada pela aplicação, e o
sistema depende de transação explícita em dois pontos: geração de protocolo
(`UPDLOCK, HOLDLOCK`) e atribuição de conversa. Usar as duas coisas juntas exige
envolver cada operação em `CreateExecutionStrategy()` com um `DbContext` novo por
tentativa — sem isso, uma retentativa reaproveita entidades já rastreadas e
insere duplicado. Para IIS e SQL Server na mesma rede, o custo da falha
transitória é o atendente clicar de novo. Detalhe em
[`ConfiguracaoDoBanco.cs`](CrmCartorio.Api/Infrastructure/Persistence/ConfiguracaoDoBanco.cs).
