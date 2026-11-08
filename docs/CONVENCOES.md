# Convenções de engenharia

> **Status:** vigente.
> **Última atualização:** 2026-11-08

## 1. Visão geral

CRM de atendimento via WhatsApp para serventias extrajudiciais. Objetivos e
requisitos da disciplina estão em [ENTREGA-ACADEMICA.md](ENTREGA-ACADEMICA.md).

## 2. Fases e decisão do canal

| Fase | Conteúdo |
| --- | --- |
| Fase 1 | Painel, cadastros, trava de conversa, protocolo e automações, com envio simulado |
| Fase 2 | Canal real de WhatsApp, resposta inteligente, mídia, piso da IA e dois provedores |
| Fase 3 | Multi-tenant real, tratado como trabalho futuro |

**Decisão do canal (2026-08-16):** para ter mensagem real sem depender da aprovação
comercial da Meta, o projeto usa a Evolution API em número dedicado. O risco de
bloqueio fica isolado nesse número. Uso em produção definitiva exige a API oficial.

## 3. Regras do repositório

1. Nenhum DDL ou DML é aplicado direto no banco; o SQL fica em arquivo em `sql/` para revisão.
2. Banco, instância e contêiner só são criados por decisão explícita.
3. Commits são decididos pelo desenvolvedor.
4. Segredos, senhas, tokens e connection strings reais não entram no controle de versão.
5. Multi-tenant real é trabalho futuro e está fora do escopo da disciplina.
6. O gateway não oficial fica restrito ao número dedicado.
7. Antes de alterar um documento de `docs/`, leia-o por inteiro.
8. A interface segue [DESIGN.md](DESIGN.md).
9. Consulta LINQ alterada exige `verificar-traducao.cs`. Protocolo e trava exigem `verificar-concorrencia.cs`. Automação exige `verificar-automacao.cs`. Canal exige `verificar-canal-whatsapp.cs`. IA exige `verificar-resposta-inteligente.cs`. Permissões exigem `verificar-autenticacao.cs`.
10. Todo arquivo `.sql` é gravado em UTF-8 com BOM.
11. Toda entrega informa os arquivos criados ou alterados.
12. A comunicação e a documentação são em português do Brasil.
13. O que depende de validação externa recebe a melhor suposição defensável, documentada e editável pela tela.
14. Suporte técnico só lê conversa de cliente com autorização ativa, aprovada na tela e com prazo limitado.

## 4. Diferenciais técnicos

- Hospedagem própria com IIS e SQL Server.
- Trilha de acesso e acesso técnico autorizado.
- Resposta de IA fundamentada no cadastro.

## 5. Stack

ASP.NET Core 10, EF Core 10, SignalR, React, Vite, TypeScript, Tailwind CSS,
shadcn/ui e SQL Server.

## 6. Estrutura

| Pasta | Conteúdo |
| --- | --- |
| `sql/` | Esquema e migrações |
| `src/backend/` | API e verificadores |
| `src/backend/CrmCartorio.Api/Modules/` | Vertical slices |
| `src/frontend/` | Painel web |
| `infra/whatsapp/` | Gateway de WhatsApp |
| `docs/` | Documentação |

## 7. Convenções de código

### 7.1 Backend

- Cada módulo fica em `Modules/<Modulo>/`, com `Application`, `Domain`, `Infrastructure` e `README.md`.
- Um módulo não referencia o `Application` interno de outro.
- Controllers são finos e não têm regra de negócio.
- Contratos de entrada e saída são `record`.
- Entidades são configuradas com `IEntityTypeConfiguration<T>`.
- Toda consulta de dado de tenant filtra por `TenantId`.

### 7.2 Idioma no código

O domínio é nomeado em português, sem acento nos identificadores. Termos técnicos
ficam em inglês.

### 7.3 Frontend

Organização por feature em `src/features/`, chamadas de API em `lib/api/` e
TypeScript em modo `strict`.

### 7.4 Documentação

Documentos abrem com **Status** e **Última atualização**, com datas em formato ISO.
Suposições são marcadas como tal.

## 8. Limites do produto

Não substitui o sistema oficial da serventia, não emite selo digital e não integra
com TJMG, CENSEC, ONR ou CRC. O funil representa progressão de atendimento, não
pipeline de vendas.
