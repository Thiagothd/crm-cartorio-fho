# Serviços — catálogo visual e revisão de Clientes

> **Status:** implementado; validação e preparação do pacote para teste.
> **Última atualização:** 2026-09-07

## Solicitação e direção

Após aprovar Clientes, Thiago solicitou a mesma linguagem de Visão Geral,
Atendimento e Relatórios em Serviços. Esta autorização estende às duas telas
os cartões, raios e superfícies das seções 15 e 16 de DESIGN.md. A paleta,
tipografia, acessibilidade e contenção do bordô continuam sendo as existentes.

Tudo foi desenvolvido no worktree `clientes-visual-exclusao`, branch
`202609-CRM-AJUSTE-clientes-visual-exclusao`, atualizado por fast-forward com
`origin/teste` em `daefa7e`. Nenhum arquivo de outro worktree foi editado.

## Investigação

Foram lidos página, diálogo, navegação/permissões, endpoints, tipos do frontend,
README de Serviços e TipoServicoService. A rota exige gestão; cadastro e edição
exigem Supervisor/Administrador também no backend. A API lista o catálogo
completo, com categoria, setor, documentos, prazo e contagem de conversas ativas.

O SLA é configurado em horas; o alerta é um percentual entre 1 e 99. Dias úteis
desconsideram sábados e domingos. O prazo começa ao vincular o serviço à conversa.
Alterações posteriores no cadastro não recalculam os instantes da conversa.
Retirar SLA o desativa sem apagar seu registro. Serviços são desativados pela
edição; não existe exclusão física nessa tela.

## Mudança

- Catálogo agrupado por categoria com fichas responsivas: código, nome,
  disponibilidade, setor, prazo, alerta e atendimentos abertos.
- Documentos necessários expansíveis com teclado, sem abrir edição.
- Busca por nome/código, insensível a acentos, categoria, setor (incluindo sem
  setor), serviços sem prazo, sem documentos e com atendimento aberto.
- Filtros locais sobre a listagem integral existente. Uma consulta inicial,
  sem requisição a cada tecla. Inclusão de inativos continua ligada por padrão.
- Resumo calculado apenas sobre os serviços exibidos: quantidade, atendimentos
  abertos, ausência de prazo e de documentos. Nenhuma estimativa de demanda ou
  indicador inventado. No celular, dois indicadores para priorizar as fichas;
  os atalhos de pendências permanecem disponíveis e categoria/setor recolhem.
- Estados de carregamento, erro com recuperação, catálogo vazio e busca vazia.
- Formulário dividido em cadastro/documentos e prazo/disponibilidade. Preserva
  campos e payload; a descrição existente deixa de ser enviada como `null`.
- Validação nativa dos obrigatórios e limites numéricos; formulário e fechamento
  indisponíveis enquanto salva, com feedback de andamento.

## Impacto e limites

Sem alteração de backend, schema, regras de prazo, triagem, IA, WhatsApp ou
permissões. A descrição preservada evita perda silenciosa ao editar um campo
que o formulário não expõe. Aviso de validação do cartório parceiro permanece na página.
Nenhum cadastro real foi criado, alterado ou excluído nos testes desta entrega.

Os testes de navegador usam API simulada e estado em memória; demonstram o
fluxo frontend → payload → estado retornado → atualização da tela, sem afirmar
validação de persistência contra SQL Server ou envio de mensagens reais.

## Validação

Na pasta `src/frontend`:

```powershell
npm.cmd run build
npm.cmd test
```

Na pasta `src/frontend/e2e`:

```powershell
npm.cmd test -- --config playwright.clientes.config.ts servicos.e2e.ts clientes.e2e.ts mobile.e2e.ts --reporter=list
```

A rodada conjunta passou com 36 testes. Inclui exclusão de Clientes e proteção
do histórico, perfis, recuperação de falhas, importação, busca e responsividade.
Serviços cobre criação por Supervisor, edição preservando descrição e SLA,
desativação de serviço/prazo, documentos pelo teclado, filtros combinados,
limites dos campos, erros de API e acesso por Atendente. Capturas em
360/768/1024/1366/1920px verificam página e formulário sem transbordamento.

A primeira rodada teve uma interrupção de diálogo durante a atualização da
base/Vite; a rodada completa com a base estabilizada passou. Após compactar os
filtros móveis, a suíte de Serviços é repetida e exige o nome do primeiro serviço
visível na viewport de 360px antes da rolagem. Capturas ficam em
`src/frontend/e2e/test-results/`, ignorado pelo Git.

Build mantém os avisos anteriores de PURE no SignalR e chunk global maior que
500 kB. Nenhuma dependência foi adicionada. Publicação segue PR para `teste`
e revisão manual antes do merge, conforme PROCESSO-DE-AJUSTE.md.

## Arquivos da entrega de Serviços

- `src/frontend/src/pages/pagina-servicos.tsx`
- `src/frontend/src/features/servicos/ficha-de-servico.tsx` (novo)
- `src/frontend/src/features/servicos/dialogo-de-servico.tsx`
- `src/frontend/e2e/fixtures/api-mock.ts` (payload e atualização de Serviços)
- `src/frontend/e2e/specs/servicos.e2e.ts` (novo)
- `docs/AJUSTE-SERVICOS-VISUAL.md` (este documento)

Os arquivos de Clientes e a configuração local de Playwright estão listados em
[AJUSTE-CLIENTES-VISUAL-EXCLUSAO.md](AJUSTE-CLIENTES-VISUAL-EXCLUSAO.md).
Configurações locais da API, logs, capturas, dependências e arquivos de build
não compõem o pacote para publicação.
