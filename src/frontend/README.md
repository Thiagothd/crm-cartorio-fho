# Frontend — painel de atendimento

React + Vite + TypeScript (`strict`) + Tailwind CSS 4 + componentes no padrão shadcn/ui.

## Como rodar

```bash
cd src/frontend
npm install
npm run dev
```

Abre em `http://localhost:5173`. O Vite encaminha `/api` e `/hubs` para
`http://localhost:5210` — o app usa caminhos relativos e não precisa saber o
endereço da API em desenvolvimento.

Suba o backend antes: veja [`src/backend/README.md`](../backend/README.md).

| Comando | O que faz |
| --- | --- |
| `npm run dev` | Servidor de desenvolvimento |
| `npm run build` | Typecheck + build de produção |
| `npm run typecheck` | Só o typecheck |
| `npm run preview` | Serve o build local |

Usuários de demonstração e senha estão no README do backend.

## Organização

```
src/
├── app/              ← bootstrap, rotas, providers
├── components/
│   ├── layout/       ← shell, cabeçalho de página, estado vazio
│   └── ui/           ← componentes no padrão shadcn/ui
├── features/         ← slices; cada um com seus componentes e hooks
│   ├── atendimento/  ← inbox, fio de mensagens, ações de conversa
│   ├── clientes/
│   ├── configuracoes/
│   └── servicos/
├── lib/
│   ├── api/          ← cliente HTTP, endpoints e tipos espelhando o backend
│   ├── auth/         ← contexto de sessão
│   └── realtime/     ← hook do hub SignalR
└── pages/            ← componentes de rota
```

Componente nunca chama `fetch` direto: tudo passa por `lib/api/`. Vale inclusive
para a consulta de CEP e CNPJ, que sai pelo backend (`consultasApi`) e não pelo
navegador — ver `Modules/Consultas/README.md`.

## Formatar entrada e formatar saída são coisas diferentes

Duas famílias em `lib/`, e trocar uma pela outra dá defeito sutil:

| Arquivo | Para quê | Comportamento com valor pela metade |
| --- | --- | --- |
| `formatacao.ts` | **Saída** — tabela, lista, cabeçalho | Devolve lixo (`formatarTelefone('389')` → `+389`) |
| `mascaras.ts` | **Entrada** — campo de formulário | Formata o que já tem e espera o resto |

Regra das máscaras: **nunca emitir separador à direita**, senão o backspace
trava. E `cpfEhValido`/`cnpjEhValido` são **aviso, nunca bloqueio** — a razão está
em DESIGN.md, seção 11.

## Como a trava aparece na tela

O backend garante a trava com `UPDATE` condicional e `rowversion`. O frontend faz
a parte que evita o conflito acontecer:

- O hook `usarHubDeAtendimento` escuta o hub e invalida as consultas afetadas, de
  modo que a conversa aparece travada nos outros painéis assim que alguém assume.
- A caixa de envio só existe para o responsável. Quem não é vê o cadeado e o nome
  de quem está atendendo.
- Quando o conflito acontece mesmo assim (aba antiga, dois cliques), a API devolve
  409 com `codigo: conversa_ja_assumida` e o nome de quem ganhou a corrida — o app
  mostra isso num aviso e recarrega a lista, em vez de um erro genérico.

## Fase 1

O botão **"Simular mensagem do cliente"**, na caixa de envio, encena a chegada de
uma mensagem para demonstrar o fluxo sem WhatsApp real. Ele sai na Fase 2, quando
o webhook da Cloud API passar a alimentar esse caminho.

## Tema

Tokens no padrão shadcn/ui em `src/index.css`, em `oklch`. O tema escuro está
definido sob `.dark` e ainda não tem alternador na interface — falta só o botão.
