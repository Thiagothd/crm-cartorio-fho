# Módulo: Usuarios

Atendentes (funcionários da serventia) e autenticação.

`Atendente` **não** é `Cliente`. Cliente é o contato do WhatsApp e nunca acessa o
sistema.

## Perfis

| Perfil | Pode |
| --- | --- |
| `Atendente` | Assumir conversa livre, responder a sua, liberar a sua, mover etapa/setor da sua, encerrar a sua |
| `Supervisor` | Tudo acima + transferir conversa de terceiro + reabrir + gerir cadastros |
| `Administrador` | Tudo acima + gerir atendentes |

Aguarda validação do cartório parceiro (`docs/REQUISITOS.md`, seção 5, pendência 7). É o
único item da lista de pendências que exigiria mexer em código se a resposta dele
pedir permissão mais fina.

## Senha

PBKDF2-HMAC-SHA256, 210.000 iterações, salt por usuário. O número de iterações
vai dentro do próprio hash (`pbkdf2.sha256$<iter>$<salt>$<hash>`), então
aumentá-lo no futuro não invalida as senhas já cadastradas.

## Detalhes do login que não são acidentais

- **Mensagem única** para e-mail inexistente, senha errada e usuário desativado.
  Diferenciar os três entregaria a um atacante a lista de e-mails válidos.
- **A senha é conferida mesmo quando o e-mail não existe**, contra um hash
  descartável. Sair antes faria o tempo de resposta revelar quais e-mails existem.
- **Ninguém desativa o próprio acesso.** Um administrador se trancando para fora
  deixaria o cartório sem quem gerencie acessos, e o desbloqueio sairia por SQL
  manual.
- **`ObterSessaoAsync` não emite token novo.** O cliente já tem o dele e a
  validade não muda por consultar a sessão.

## SEC-01 — revogação de sessão (TokenVersion)

Até 2026-08-26, um JWT emitido no login continuava válido até expirar (até
8h) mesmo depois de troca de senha, desativação ou mudança de `Perfil` — o
backend nunca revalidava nada além de assinatura e expiração do próprio
token. `Atendente.TokenVersion` é o mecanismo de revogação:

- Todo JWT carrega a `TokenVersion` vigente no instante da emissão (claim
  `token_version`, `Infrastructure/Identity/ClaimsDoAtendente.cs`).
- Todo request autenticado confere, no banco, se essa versão ainda bate com a
  atual — `Infrastructure/Security/ValidadorDeSessao.cs`, acoplado à policy
  padrão de autorização via `SessaoAtivaHandler` (`Program.cs`). Cobre
  controllers e o handshake do Hub do SignalR com o mesmo componente. Falhar
  aqui responde `401`, nunca `403` — para quem chama a API, sessão revogada
  deve parecer "não autenticado", igual a token expirado.
- **Sobe** (atomicamente, via `ExecuteUpdateAsync`, nunca ler-e-somar em
  memória) quando: o próprio atendente troca a senha
  (`AutenticacaoService.AlterarSenhaAsync`); um administrador redefine a senha
  de outro (`AtendenteService.RedefinirSenhaAsync`); `Ativo` vira `false`; ou
  `Perfil` muda (as duas últimas em `AtendenteService.AtualizarAsync`, na
  mesma instrução SQL que grava os campos, comparando com o valor que está no
  banco no instante do `UPDATE` — não com uma leitura anterior).
- **Não desce.** Reativar (`Ativo` volta a `true`) não reduz nem zera a
  versão — só evita que ela suba de novo por causa da reativação em si. Um
  token emitido antes da desativação continua inválido depois da reativação;
  só um login novo (que sempre lê a versão atual) devolve acesso.
- **Logout normal não sobe a versão.** Ver o comentário em
  `contexto-de-autenticacao.tsx` (`sair`): com só `TokenVersion`, subir a
  versão no logout derrubaria todos os dispositivos do atendente, não só a
  aba que fez logout. **Logout local não é revogação individual de token** —
  continua sendo só client-side (`localStorage`) nesta fase. Revogar um único
  dispositivo/aba exigiria `SessionId`/`jti` persistido por sessão, fora do
  escopo deste PR.
- **Resumo por canal, para não confundir revogação com expiração:**

  | Evento | REST | Handshake SignalR novo | Conexão SignalR já aberta |
  | --- | --- | --- | --- |
  | TokenVersion revogado (senha/desativação/perfil) | `401` no próximo request | Recusado | **Continua aberta — SEC-01B** |
  | JWT chega ao `exp` | `401` (já era assim) | Recusado | Encerrada — `CloseOnAuthenticationExpiration = true` no `MapHub` (`Program.cs`) |

- **SEC-01B — limitação aberta.** Uma conexão SignalR **já estabelecida**
  antes do evento de revogação **não é encerrada por TokenVersion** — o
  handshake só é reavaliado ao abrir uma conexão nova, nunca a cada mensagem
  de uma conexão já aberta. `CloseOnAuthenticationExpiration = true` **não
  resolve isto**: ele fecha a conexão quando o `exp` do próprio token chega,
  o que é um evento diferente e independente de TokenVersion — um token
  revogado mas ainda dentro do prazo de validade continua servindo a conexão
  já aberta até ela cair por outro motivo (rede, fechar a aba, reciclagem do
  App Pool, ou o `exp` do token de fato chegar). Encerrar a conexão ativa no
  instante da revogação exigiria rastrear `ConnectionId` por `AtendenteId` e
  abortar a conexão explicitamente — não implementado aqui.
- Custo por request: uma consulta `AsNoTracking` por `Id` + `TenantId`
  (índice de chave primária), sem `IMemoryCache` — a revogação vale a partir
  do próximo request, sem janela de tolerância.

## SEC-02 — proteção contra força bruta no login

Até 2026-08-29 o login não tinha limite de tentativas. O único freio era o custo
do PBKDF2 — real, mas insuficiente sozinho: não impede paralelismo nem um script
paciente. São duas camadas, e elas não têm o mesmo peso:

| Camada | Onde vive | Papel |
| --- | --- | --- |
| Limite por IP — 10/min | Memória do processo (`Infrastructure/Security/PoliticaDeLimiteDeLogin.cs`) | Amortecedor. Faz um script óbvio parar antes de gastar PBKDF2 e uma ida ao banco. Vale por processo — hoje há um por ambiente (`Maximum Worker Processes = 1`). |
| Backoff por conta | `Atendentes` (3 colunas, migration 033) | **A proteção de verdade.** Sobrevive a reciclagem do App Pool e vale para o cluster inteiro. |

**A progressão:** 5 falhas seguidas fecham um ciclo e abrem um bloqueio. Cada
bloqueio novo, sem login bem-sucedido no meio, dura mais que o anterior — 1, 2,
4, 8, 16 e 30 minutos, e para em 30. Um login bem-sucedido zera contador, nível e
bloqueio, então a progressão **recomeça em 1 minuto** para quem de fato voltou a
entrar; ela só escala contra quem nunca acerta.

**Quatro decisões que não são acidentais:**

- **Senha correta não fura bloqueio vigente.** Enquanto `BloqueadoAte` estiver no
  futuro, o login é recusado mesmo com a senha certa. Só a expiração ou uma
  redefinição administrativa liberam.
- **Tentativa durante bloqueio não estende o bloqueio** — nem a errada, nem a
  certa. Sem isso, um atacante manteria a conta travada para sempre só
  continuando a tentar.
- **Conta inativa não acumula lockout.** Ela executa o PBKDF2 e responde igual a
  todas as outras, mas não incrementa nada. Sem isso, uma conta desligada podia
  ser martelada por semanas e voltaria da reativação já bloqueada, no topo da
  progressão, sem ninguém entender por quê.
- **Redefinir a senha (administrador) desbloqueia**, na mesma instrução que grava
  o hash e sobe `TokenVersion`. É a saída para quem foi travado por engano ou por
  ataque contra o próprio e-mail — sem ela, restaria esperar até 30 minutos.

**Como a corrida é fechada.** A decisão de aceitar o login nunca sai de um
`BloqueadoAte` lido em memória — sai do número de linhas que um
`ExecuteUpdateAsync` condicional afetou. O `WHERE` dele *é* a checagem de
bloqueio, reavaliada pelo SQL Server no instante da **escrita**. É isso, e só
isso, que impede o cenário em que uma 5ª falha e um acerto chegam juntos: se a
falha commitar primeiro, o `UPDATE` de sucesso encontra `BloqueadoAte` no futuro,
afeta zero linhas e nenhum JWT é emitido — em vez de apagar o bloqueio recém-criado.
Coberto por `verificar-autenticacao.cs`, cenário 20, que aceita só os dois
estados finais possíveis e falha em qualquer outro.

**Risco residual de timing — não resolvido, registrado.** O PBKDF2 roda sempre,
antes de qualquer decisão pública, nos quatro casos de recusa (inexistente, senha
errada, inativo, bloqueado) — isso é testado por contagem de invocação, não por
relógio. Mas para uma conta que **existe** há uma escrita SQL a mais depois do
hash (o `UPDATE` de reset ou o de incremento), que não existe para um e-mail
inexistente. É uma assimetria real, pequena, e **não** neutralizada aqui. Reduz o
sinal de enumeração, não o elimina.

## Pendência da Fase 3

`AutenticacaoService.AutenticarAsync` busca o atendente só pelo e-mail, porque no
login ainda não se sabe o tenant. O e-mail é único **por tenant**, não
globalmente — com mais de um cartório na base essa busca fica ambígua. Antes do
multi-tenant real, o tenant precisa ser resolvido ali (subdomínio, código do
cartório na tela de login, ou índice único global). Está comentado no código.
