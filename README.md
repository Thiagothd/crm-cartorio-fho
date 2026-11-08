# CRM Cartório

**Sistema de atendimento inteligente via WhatsApp para serventias extrajudiciais.**

Projeto Final da disciplina **Aplicações Integradas em Engenharia da Computação**,
curso de Engenharia da Computação da **FHO**.

| | |
| --- | --- |
| **Professor** | Thiago Giroto Milani |
| **Integrantes** | Thiago Antunes de Oliveira (RA 114667) e Matheus Moreira (RA 114882) |
| **Documento de entrega** | [docs/ENTREGA-ACADEMICA.md](docs/ENTREGA-ACADEMICA.md) |

## O problema

Cartórios já atendem pelo WhatsApp, mas de forma manual: 60% das serventias usam
aplicativos de mensagem e só 8,25% têm algum chatbot (ANOREG/BR, Raio-X dos
Cartórios). Um chatbot genérico não resolve, porque prazo legal, valor de
emolumento e documento exigido não podem sair errados.

O sistema automatiza o primeiro contato sem inventar informação: a IA responde
**apenas com base no que está cadastrado** e passa a conversa para uma pessoa
quando não tem segurança para responder.

## O que o sistema faz

- **Canal real de WhatsApp:** a mensagem entra e sai de verdade por um gateway.
- **Triagem automática:** menu em níveis montado a partir do cadastro de setores e serviços.
- **Resposta com LLM e grounding:** o contexto enviado ao modelo contém só o catálogo e a base de conhecimento cadastrados.
- **Contingência entre provedores de IA:** troca automática com padrão *circuit breaker*.
- **Painel do atendente:** caixa de entrada com filtros, trava de conversa em tempo real e histórico por etapa.
- **Mensagens automáticas configuráveis:** gatilhos, marcadores e avisos de prazo editáveis pela tela.
- **Proteção de dados:** trilha de acesso, cifra em repouso e mascaramento de dados pessoais.

## Arquitetura

```text
Cliente (WhatsApp)
      |
Gateway Evolution API  ---- webhook ---->  API ASP.NET Core 10
      ^                                     |   Vertical Slice Architecture
      |                                     |-- Triagem e mensagens automáticas
      +-------- envio ----------------------|-- Resposta Inteligente --> Claude / Gemini
                                            |-- EF Core 10 --> SQL Server
                                            |-- SignalR
                                            v
                                  Painel React + TypeScript
```

| Camada | Tecnologia |
| --- | --- |
| Backend | C#, ASP.NET Core 10, Entity Framework Core 10 |
| Frontend | TypeScript, React, Vite, Tailwind CSS, shadcn/ui |
| Tempo real | SignalR |
| Banco de dados | SQL Server, com esquema versionado em [`sql/`](sql/) |
| LLM | Claude (Anthropic) e Gemini (Google) |
| Canal | Evolution API, em número dedicado |

## Como executar

O passo a passo completo está em [docs/INSTALACAO.md](docs/INSTALACAO.md).

```bash
cd src/backend && dotnet run --project CrmCartorio.Api   # http://localhost:5210
cd src/frontend && npm install && npm run dev            # http://localhost:5173
```

## Testes

Cada módulo crítico tem um verificador que chama o **serviço real**, em
[`src/backend/`](src/backend/). A matriz completa está em
[docs/QUALITY-GATE.md](docs/QUALITY-GATE.md).

```bash
dotnet run verificar-traducao.cs              # LINQ para SQL
dotnet run verificar-resposta-inteligente.cs  # orquestrador de IA, cota e disjuntor
dotnet run verificar-canal-whatsapp.cs        # restrições do canal
dotnet run verificar-automacao.cs             # triagem, avisos e transferência (escreve no banco de desenvolvimento)
dotnet run verificar-concorrencia.cs          # protocolo e trava sob corrida (escreve no banco de desenvolvimento)
```

## Documentação

| Arquivo | Conteúdo |
| --- | --- |
| [docs/ENTREGA-ACADEMICA.md](docs/ENTREGA-ACADEMICA.md) | Requisitos do roteiro, objetivos e linha do tempo |
| [docs/CONTEXTO.md](docs/CONTEXTO.md) | Problema, domínio e serventia de referência |
| [docs/REQUISITOS.md](docs/REQUISITOS.md) | Requisitos funcionais e não funcionais |
| [docs/MODELO-DADOS.md](docs/MODELO-DADOS.md) | Entidades, índices e decisões de modelagem |
| [docs/FLUXO-AUTOMACAO.md](docs/FLUXO-AUTOMACAO.md) | Gatilhos, máquina de estado e escalonamento da IA |
| [docs/PROTECAO-DE-DADOS.md](docs/PROTECAO-DE-DADOS.md) | LGPD e controles de segurança |
| [docs/DESIGN.md](docs/DESIGN.md) | Identidade visual e regras de interface |
| [docs/CONVENCOES.md](docs/CONVENCOES.md) | Regras de engenharia do repositório |

Cada módulo do backend tem um `README.md` próprio em
`src/backend/CrmCartorio.Api/Modules/*/`.

## Origem do código e privacidade

- **Base anterior à disciplina:** o desenvolvimento começou em 15/08/2026, antes do pré-projeto, como declarado na metodologia da N1. A separação entre a base e o trabalho do semestre está em [docs/ENTREGA-ACADEMICA.md](docs/ENTREGA-ACADEMICA.md).
- **Histórico autêntico:** autores, datas e ordem dos commits são os reais.
- **Anonimização:** esta cópia substitui nomes de pessoas e telefones por valores fictícios, em arquivos e mensagens de commit, e omite arquivos operacionais do servidor.
- **Dados de demonstração:** os atendimentos usados nos testes e na apresentação são fictícios.
