# Clientes — continuidade do ajuste visual e de exclusão

> **Status:** visual aprovado pelo usuário; preparado junto de Serviços para revisão em teste.
> **Última atualização:** 2026-09-07

## Continuidade e isolamento

O pedido é continuar a investigação interrompida do Claude: corrigir a experiência
de exclusão e atualizar o visual de `/clientes` seguindo o novo Atendimento e a
Visão Geral, preservando o comportamento dos cadastros.

A transcrição fornecida foi confrontada com os arquivos reais. A investigação
anterior estava em `CRM-Cartorio-visao-geral`, limpo no commit `301959a` (PR #73,
base de `teste`). Não foi encontrado um plano final salvo para Clientes nos
históricos e planos locais consultados. O novo Atendimento estava apenas em
alterações locais no worktree `notion-pending-tasks-8b6d64`; foi consultado como
referência, sem copiar seus arquivos ou modificar esse ambiente.

Este ajuste está em `.worktrees/clientes-visual-exclusao`, na branch
`202609-CRM-AJUSTE-clientes-visual-exclusao`, criada a partir da mesma base usada
na investigação anterior. Não muda a branch do diretório principal nem os
outros worktrees. Dependências locais foram copiadas, sem junctions ou symlinks
para instalações em uso por outra sessão.

Em 2026-09-07, o usuário aprovou a grade de Clientes e pediu a mesma evolução
para Serviços. A branch recebeu `origin/teste` por fast-forward até `daefa7e`,
incluindo o Atendimento publicado no PR #74. O pacote inclui as duas telas.

## Investigação e diagnóstico

Fluxo conferido: página → `clientesApi.excluir` → cliente HTTP →
`ClientesController.Excluir` → `ClienteService.ExcluirAsync` → EF Core.

- O backend exige Supervisor/Administrador e busca o cliente pelo tenant.
- Qualquer conversa vinculada, inclusive encerrada, impede exclusão (409,
  `cliente_tem_historico`). A FK de conversa usa `DeleteBehavior.Restrict`.
- Sem histórico, o serviço remove fisicamente e a API devolve 204. O cliente
  HTTP já aceita resposta vazia; não havia evidência de erro nesse contrato.
- A tela antiga oferecia a lixeira a todos os perfis e a clientes com histórico,
  levando o operador até uma confirmação que o backend necessariamente recusava.
- A listagem não distinguia falha de carregamento de resultado vazio.

O sintoma original não veio acompanhado de mensagem HTTP/erro reproduzível.
Foi solicitada essa informação, sem presumir que a regra de preservação do
histórico deveria ser removida. A correção cobre os defeitos de interface
confirmados; não afirma resolver uma falha de servidor não reproduzida.

## Plano executado e comportamento resultante

1. Isolar a mesma base da investigação anterior e mapear regras e referências.
2. Evoluir a listagem com os tokens existentes: superfície `branco` sobre
   `papel`, `rounded-card`, `shadow-card`, filtros em pílula e hierarquia de
   tipografia. A autorização do pedido estende a linguagem aprovada às
   superfícies de Clientes antes descritas como Fase 1 em `DESIGN.md`.
3. Reorganizar a página em três planos: bancada de filtros, resumo operacional
   da busca e grade de fichas de cadastro em todas as larguras. Cada ficha reúne
   contato, documento, completude, qualificação, atendimentos e ações. Filtros
   avançados ficam em diálogo abaixo de `lg`; todos os parâmetros de API
   existentes são mantidos.
4. Separar apresentação da lista e diálogo de exclusão em componentes da
   própria feature, sem alterar componentes globais ou acrescentar dependências.
5. Mostrar exclusão apenas para os perfis autorizados. Para clientes com histórico,
   apresentar opções de remoção com explicação e atalho à aba Observações da ficha.
   O operador ainda precisa desligar “Cliente ativo” e salvar; abrir a ficha não
   modifica dados. Clientes já inativos recebem orientação correspondente.
6. Manter exclusão definitiva dos contatos sem histórico e tratar recusas com
   mensagem persistente no diálogo. Um 409 surgido depois da listagem também muda
   a orientação para histórico protegido. 403/404 impedem repetir a exclusão;
   falhas de rede/servidor permitem tentar novamente. A lista é invalidada para
   obter dados atualizados, sem fabricar contagens no cliente.
7. Impedir confirmação duplicada e fechamento enquanto o DELETE está em andamento.
   Separar carregamento, falha com tentativa novamente, base vazia e busca sem
   resultados. Oferecer limpeza dos filtros sem alterar seus contratos.

## Impactos e limites

- Sem mudanças em banco, endpoints, regras de autorização, recebimento/envio de
  WhatsApp, automações ou histórico. Nenhum dado real foi criado, alterado ou apagado.
- Cadastro, edição, importação em duas fases, consultas de CEP/CNPJ, máscaras,
  validações e busca por URL foram preservados.
- A contagem de histórico da lista é uma indicação para orientar a interface;
  a decisão final continua no backend, inclusive sob mudanças concorrentes.
- A listagem mantém o limite anterior de 100 registros por consulta. Paginação
  adicional não foi incluída silenciosamente neste ajuste; o rodapé informa a
  quantidade efetivamente exibida.
- Alterações compartilhadas do novo Atendimento permanecem independentes. Este
  ajuste utiliza tokens já existentes na base e não depende de código não commitado
  de outra sessão. A documentação visual principal não foi sobrescrita.

## Validação reproduzível

Em `src/frontend`:

```powershell
npm.cmd run typecheck
npm.cmd test
npm.cmd run build
```

Em `src/frontend/e2e`:

```powershell
npm.cmd test -- --config playwright.clientes.config.ts clientes.e2e.ts mobile.e2e.ts --reporter=list
```

A configuração específica reserva `127.0.0.1:5197` com `strictPort`; pode
reaproveitar somente o Vite aberto nessa porta para a revisão deste worktree.
API e persistência são simuladas em memória; SQL Server, SignalR e WhatsApp
reais não são usados. Capturas de 360/768/1024/1366/1920px ficam no diretório
ignorado `src/frontend/e2e/test-results/`.

Os cenários verificam sucesso 204, histórico ativo/encerrado, desativação e
releitura, cliente já inativo, perfis Atendente/Supervisor/Administrador, 409 com
listagem desatualizada, 403/404/500, falha de rede, operação pendente, erro da
listagem, busca por URL, filtros, ordenação, importação, teclado, redução de
movimento, resize e ausência de transbordamento do documento.

A primeira execução teve dois timeouts de carregamento com o Vite frio e
encontrou seletores novos de teste ambíguos (“Nome *” e “Cidade”), corrigidos
para usar papel/nome do campo no diálogo. Os cenários funcionais de exclusão
passaram. A execução restrita também travou no encerramento do servidor;
a validação final foi autorizada fora da sandbox.

Resultado final em 2026-09-06:

| Verificação | Resultado |
| --- | --- |
| TypeScript (`typecheck`, também executado no build final) | PASS |
| Vitest existente | 14 arquivos, 70 testes PASS |
| Clientes + regressão mobile no Chromium | 25 testes PASS, 48 segundos, saída 0 |
| Conferência final das cinco larguras após ajuste de filtros | 5 testes PASS, saída 0 |
| Build final de produção | PASS |
| `git diff --check` | PASS |

Capturas revisadas visualmente. Os avisos preexistentes do build sobre
anotações PURE do SignalR e chunk principal acima de 500 kB permanecem;
não foi acrescentada dependência. O módulo da página de Clientes compilou
em 25,02 kB (8,04 kB gzip). O ambiente original `CRM-Cartorio-visao-geral`
permaneceu limpo ao final da conferência.

Limite da evidência: os testes de navegador provam frontend → cliente HTTP →
API simulada → estado em memória → atualização da interface. Não houve teste
de exclusão contra banco real, nem alteração de backend que exigisse os
verificadores de LINQ/SQL. A aprovação visual de Clientes foi recebida em
2026-09-07; a publicação em `teste` depende da revisão final do pacote.

## Revisão conjunta em 2026-09-07

- A grade usa fichas em todas as larguras; ações de exclusão têm texto visível.
- O botão de exclusão é neutro em repouso para preservar o limite de bordô.
- O indicador de endereço completo exige logradouro, número, cidade e UF.
- Os indicadores de atendimento e pendências se referem aos clientes desta lista,
  não a registros além do limite de 100 devolvidos pela consulta.
- A regressão conjunta e a evidência atual estão em
  [AJUSTE-SERVICOS-VISUAL.md](AJUSTE-SERVICOS-VISUAL.md).

## Revisão do produto

- **Produto:** a ação de remoção agora oferece um caminho realizável conforme o cadastro.
- **UX:** recusas têm explicação e recuperação; desativar exige confirmação pelo salvamento.
- **UI:** hierarquia e superfícies seguem as referências existentes, com dados legíveis no celular.
- **Arquitetura:** componentes locais, contrato e backend preservados, sem nova biblioteca.
- **QA:** foco em autorização, preservação de histórico, erros e efeitos observáveis na listagem.

## Arquivos criados ou alterados

- `src/frontend/src/pages/pagina-clientes.tsx`
- `src/frontend/src/features/clientes/lista-de-clientes.tsx` (novo)
- `src/frontend/src/features/clientes/dialogo-de-exclusao.tsx` (novo)
- `src/frontend/src/features/clientes/dialogo-de-cliente.tsx`
- `src/frontend/e2e/fixtures/api-mock.ts` (contratos de Clientes alinhados ao backend)
- `src/frontend/e2e/specs/clientes.e2e.ts`
- `src/frontend/e2e/playwright.clientes.config.ts` (novo)
- `docs/AJUSTE-CLIENTES-VISUAL-EXCLUSAO.md` (este documento)
