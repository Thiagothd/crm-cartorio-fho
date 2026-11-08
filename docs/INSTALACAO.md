# Instalação e execução

> **Status:** vigente para a entrega acadêmica.
> **Última atualização:** 2026-11-08

## 1. Pré-requisitos

- .NET SDK 10
- Node.js 24
- SQL Server (Express atende) e `sqlcmd`
- Chave de API da Anthropic, para a resposta inteligente
- Chave de API do Google Gemini, para a contingência entre provedores
- Evolution API, para o canal real de WhatsApp

## 2. Banco de dados

O esquema não é criado automaticamente. Crie a base e aplique os scripts de
[`sql/`](../sql/) em ordem numérica.

Os arquivos `.sql` são gravados em UTF-8 com BOM. Ao editar, preserve o BOM para
que o `sqlcmd` não corrompa os acentos.

## 3. Configuração

```bash
cd src/backend/CrmCartorio.Api
cp appsettings.Development.example.json appsettings.Development.json
```

Preencha a connection string e as chaves. O arquivo real é ignorado pelo Git.

Com `Seed:Habilitado = true`, a API popula a base de demonstração na primeira
execução. A senha dos usuários fictícios é `cartorio2026`, de uso exclusivo em
demonstração.

## 4. Execução

```bash
cd src/backend && dotnet run --project CrmCartorio.Api   # http://localhost:5210
cd src/frontend && npm install && npm run dev            # http://localhost:5173
```

## 5. Canal de WhatsApp

O gateway está descrito em [`infra/whatsapp/`](../infra/whatsapp/), e as opções do
canal em `src/backend/CrmCartorio.Api/Modules/CanalWhatsApp/README.md`.

O gateway não é oficial e pode ter o número bloqueado pela Meta. Use somente um
número dedicado ao projeto.

## 6. Verificadores

Execute a partir de `src/backend/`. A relação completa está em
[QUALITY-GATE.md](QUALITY-GATE.md). Verificadores que escrevem no banco devem ser
usados apenas com base de desenvolvimento.
