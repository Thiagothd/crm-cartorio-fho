# CRM-Cartório

CRM de atendimento via WhatsApp especializado para cartórios (serventias
extrajudiciais). **Repositório privado.**

> **Fase 1 ✅ concluída em 2026-08-15. Fase 2 em piloto desde 2026-08-16.** O
> sistema fala com WhatsApp real por um gateway não oficial, em **número
> dedicado ao piloto** — nunca o número do cartório. A via oficial (Cloud API da
> Meta) segue bloqueada por pré-requisitos do cliente. Ver
> [CLAUDE.md](CLAUDE.md), seção 2, e [docs/DEPLOY-WHATSAPP.md](docs/DEPLOY-WHATSAPP.md).

## Por onde começar a leitura

| Arquivo | O que responde |
| --- | --- |
| [CLAUDE.md](CLAUDE.md) | **Comece aqui.** Regras de trabalho, roadmap, convenções, stack |
| [docs/FASE-1-PRONTIDAO.md](docs/FASE-1-PRONTIDAO.md) | O que é fato confirmado, o que é suposição nossa e o que depende de terceiro |
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
dotnet run verificar-concorrencia-saida-whatsapp.cs # claim de envio com SQL Server real (escreve)
dotnet run verificar-concorrencia-entrada-whatsapp.cs # entrada concorrente, ordem e recovery com SQL Server real (escreve)
dotnet run verificar-automacao.cs     # triagem, avisos e transferência (escreve)
dotnet run verificar-canal-whatsapp.cs # restrições e adaptadores com banco/gateway falsos locais
```

`verificar-concorrencia.cs`, `verificar-concorrencia-saida-whatsapp.cs`,
`verificar-concorrencia-entrada-whatsapp.cs` e `verificar-automacao.cs` escrevem no banco:
**só contra base de desenvolvimento**, e removem tudo que criam. Os outros dois
não abrem conexão; o verificador do canal usa apenas um banco descartável em memória.

## Avisos

- Os dados da serventia (CNPJ, endereço, telefone, nomes do titular e do
  substituto) são **públicos**, vindos do perfil oficial no CNJ, e estão no
  repositório como contexto do projeto. Ver [docs/CONTEXTO.md](docs/CONTEXTO.md).
- "Carla Menezes" e "Paula Ribeiro" são **nomes fictícios** de demonstração.
- Os textos das mensagens automáticas são **suposição documentada**, não redação
  aprovada pelo cliente. Ver [docs/REQUISITOS.md](docs/REQUISITOS.md), seção 5.7.
- Nenhuma biblioteca não oficial de WhatsApp é usada, em nenhuma fase.
