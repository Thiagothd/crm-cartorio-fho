# Verificacao do tema

Execute em `src/frontend/e2e`:

```powershell
npx playwright test --config=playwright.tema.config.ts
```

A configuracao sobe somente o frontend em `127.0.0.1:5187`, com porta exclusiva
e sem reutilizar servidores. `VITE_API_URL` aponta para a porta inativa 1;
as chamadas de API sao interceptadas pelo Playwright e o SignalR e bloqueado.
Os dados sao ficticios. Nenhuma API, banco ou integracao real e iniciada.

As capturas ficam em `test-results/tema/` (ignorado pelo Git). A suite cobre:

- Todas as paginas operacionais, configuracoes, permissoes e paginas publicas.
- Central de IA autenticada com dados simulados e estados vazios.
- Celular (390px), tablet (768px) e desktop (1440px).
- Cabecalho e menu em 360px e paisagem (844px).
- Portais de dialogo e seletor, retorno de foco do seletor por Escape.
- Persistencia, preferencia do sistema e sincronizacao entre abas.
- Aplicacao do tema antes do carregamento do aplicativo.
- Contraste minimo de 4,5:1 nos pares de tokens de texto do tema escuro.

O teste de contraste verifica a paleta, nao substitui a inspecao visual de
opacidades compostas, imagens, graficos e estados interativos.

O tema permanece uma preferencia local (`crm-cartorio.tema`), independente
das permissoes para alterar a textura das conversas da serventia. O padrao
continua claro; o modo escuro e opcional. As cores das etapas no servidor
nao mudam: somente sua apresentacao depende do tema.
