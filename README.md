# CRM-Cartório

CRM de atendimento via WhatsApp especializado para cartórios (serventias
extrajudiciais). **Repositório privado.**

> **Fase 1 — sistema completo com dados simulados.** O WhatsApp real não está
> conectado: as mensagens ficam numa conversa simulada, prontas para a Fase 2
> plugar a Cloud API da Meta sem reescrever a lógica.

## Por onde começar a leitura

| Arquivo | O que responde |
| --- | --- |
| [CLAUDE.md](CLAUDE.md) | **Comece aqui.** Regras de trabalho, roadmap, convenções, stack |
| [docs/CONTEXTO.md](docs/CONTEXTO.md) | A serventia e o contexto do projeto |
| [docs/REQUISITOS.md](docs/REQUISITOS.md) | Requisitos confirmados e o que ainda depende de validação |
| [docs/MODELO-DADOS.md](docs/MODELO-DADOS.md) | Entidades, índices e as decisões técnicas por trás delas |
| [docs/DESIGN.md](docs/DESIGN.md) | Identidade visual — fonte de verdade da interface |

Cada módulo do backend tem um `README.md` próprio explicando o que ele faz e por
quê (`src/backend/CrmCartorio.Api/Modules/*/README.md`).

## Stack

| Camada | Tecnologia |
| --- | --- |
| Backend | ASP.NET Core 10 + EF Core 10, Vertical Slice Architecture |
| Tempo real | SignalR |
| Frontend | React + Vite + TypeScript + Tailwind + shadcn/ui |
| Banco | SQL Server |

## Rodando localmente

**1. Banco.** Nada é criado automaticamente — o DDL é escrito à mão e revisado.
Crie a base e rode os scripts de [`sql/`](sql/) em ordem numérica.

> Os `.sql` são gravados em **UTF-8 com BOM**. Sem o BOM, o `sqlcmd` lê como ANSI
> e todo acento vira dois caracteres no banco. Ao editar, preserve o BOM.

**2. Configuração.**

```bash
cd src/backend/CrmCartorio.Api
cp appsettings.Development.example.json appsettings.Development.json
```

Ajuste a connection string. O arquivo real está no `.gitignore`.

**3. Subir.**

```bash
cd src/backend && dotnet run --project CrmCartorio.Api   # http://localhost:5210
cd src/frontend && npm install && npm run dev            # http://localhost:5173
```

Com `Seed:Habilitado = true`, a API popula a base de demonstração na primeira
execução. Usuários criados usam a senha `cartorio2026` — **descartável, só para
demonstração**.

## Verificadores

Rodam de `src/backend/`. Chamam os **serviços reais**, não cópias da consulta.

```bash
dotnet run verificar-traducao.cs      # LINQ → SQL, não toca em banco
dotnet run verificar-concorrencia.cs  # protocolo e trava sob corrida (escreve)
dotnet run verificar-automacao.cs     # triagem, avisos e transferência (escreve)
```

Os dois últimos escrevem no banco: **só contra base de desenvolvimento**, e
removem tudo que criam.

## Avisos

- Os dados da serventia (CNPJ, endereço, telefone, nomes do titular e do
  substituto) são **públicos**, vindos do perfil oficial no CNJ, e estão no
  repositório como contexto do projeto. Ver [docs/CONTEXTO.md](docs/CONTEXTO.md).
- "Carla Menezes" e "Paula Ribeiro" são **nomes fictícios** de demonstração.
- Os textos das mensagens automáticas são **suposição documentada**, não redação
  aprovada pelo cliente. Ver [docs/REQUISITOS.md](docs/REQUISITOS.md), seção 5.7.
- Nenhuma biblioteca não oficial de WhatsApp é usada, em nenhuma fase.
