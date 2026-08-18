# wwwroot — onde o painel React é servido em produção

**Esta pasta fica vazia no repositório de propósito.** O conteúdo dela é gerado,
não escrito à mão.

## Em desenvolvimento

Fica vazia mesmo. Quem serve o painel é o dev server do Vite, na 5173, com o
proxy de `/api` e `/hubs` apontando para a API na 5210 (`vite.config.ts`).
Nada muda no seu dia a dia.

A pasta precisa **existir** — este arquivo é o que garante isso. Sem ela,
`app.UseStaticFiles()` não encontra `WebRootPath` e a API nem sobe.

## Em produção e teste

O deploy copia o resultado de `npm run build` (`src/frontend/dist/`) para cá,
e a API passa a servir o painel e a própria API na **mesma origem**. É o que
faz o CORS deixar de existir, dispensa regra de rewrite no IIS, e mantém o
WebSocket do SignalR fora de cross-origin.

Ver `docs/INFRAESTRUTURA.md`.

> O `.gitignore` da raiz ignora `dist/`, então o build do Vite nunca é
> versionado — nem aqui, nem lá.
