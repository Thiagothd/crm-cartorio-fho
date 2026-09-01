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

## SEC-03 — MFA/TOTP para contas privilegiadas

Login em duas etapas (RFC 6238) para `Administrador` e para quem tem
`CargoNaServentia.Titular`. Login normal (etapa 1) continua idêntico para
quem não tem MFA — a mudança é aditiva.

**Divisão de responsabilidade:** `AutenticacaoService` é o LOGIN (etapas 1 e
2, sempre lendo estado, nunca gerindo o segredo); `MfaService` é a GESTÃO da
conta já autenticada (ativar, confirmar, desativar, regenerar códigos,
matrícula por ticket, reset administrativo, política). Os dois compartilham
`VerificadorDeFatorMfa` (interpreta um código sem nunca escrever no banco —
cada porteiro único faz sua própria escrita atômica) e `BloqueioDeMfa`
(contador de falhas por conta).

### O porteiro único — por que tudo aqui é uma única instrução condicional

A mesma disciplina de SEC-01/SEC-02: nenhuma decisão de segurança sai de um
valor lido em memória. A conclusão da etapa 2
(`AutenticacaoService.ConcluirLoginMfaAsync`) e a confirmação de ativação
(`MfaService.ConfirmarNucleoAsync`) reconfirmam **no `WHERE` da própria
escrita** — `Ativo`, `TenantId`, `TokenVersion`, `MfaAtivo`, ausência de
bloqueio — e só emitem JWT se essa reconfirmação afetar exatamente 1 linha.

- **Reserva antes de validar.** A etapa 2 incrementa
  `DesafiosDeMfa.TentativasDeCodigo` ANTES de checar o código — o teto de 5
  tentativas por desafio vale mesmo se a validação for interrompida no meio.
  Não existe um passo separado de "inutilizar": ao chegar em 5, o próprio
  `WHERE` (`TentativasDeCodigo < 5`) para de deixar reservar.
- **Consumo só no sucesso.** `DesafiosDeMfa.ConsumidoEm` só é gravado depois
  que TUDO o resto (estado da conta, e — no caminho de recuperação — o
  consumo do código) já commitou na mesma transação.
- **`TokenVersionNoDesafio`** é o snapshot que invalida um desafio pendente
  quando algo relevante muda entre a etapa 1 e a etapa 2 — troca de senha,
  desativação, mudança de `Perfil`, reset de MFA. Todos esses eventos já
  sobem `TokenVersion` (SEC-01); o desafio só herda essa garantia.

### Dois contadores de bloqueio, nunca um só

`FalhasDeLoginConsecutivas` (SEC-02, senha) e `MfaFalhasConsecutivas`
(SEC-03, segundo fator) são colunas e progressões **separadas** — mesma
forma (5 falhas fecham um ciclo, bloqueio de 1/2/4/8/16/30 min, teto no 6º
nível), zero acoplamento. O motivo: quem já tem a senha pode abrir quantos
desafios de segundo fator quiser, então o teto de 5 tentativas *por desafio*
não protege nada sozinho — é `MfaBloqueadoAte`, por CONTA, que impede força
bruta persistente do TOTP. Tentativa durante bloqueio (de qualquer um dos
dois) nunca conta e nunca estende o próprio bloqueio.

### Segredo TOTP — nunca em texto claro

AES-256-GCM (`Infrastructure/Security/CifraDeSegredoMfa.cs`), formato
versionado (`[formatoVersao][tamanhoDaVersaoDaChave][versaoDaChave][nonce:12][tag:16][ciphertext]`).
AAD = `TenantId ‖ AtendenteId` — mover o ciphertext de uma conta para outra,
mesmo com a chave certa, falha ao decifrar. Chave em
`appsettings.<Ambiente>.json` (`Mfa:Chaves:<versão>`), custódia e teste de
restauração obrigatórios em `docs/BACKUP.md` antes de exigir MFA em produção.
TOTP em si via `Otp.NET` — SHA1/6 dígitos/passo 30s, janela ±1 (90s de
tolerância a deriva de relógio), nunca implementação própria da RFC.
Anti-replay: `Atendentes.MfaUltimoPassoUsado`, compare-and-set (`WHERE
MfaUltimoPassoUsado IS NULL OR MfaUltimoPassoUsado < @passo`) na MESMA
transação do consumo do desafio.

### Matrícula privilegiada — nunca só JWT + senha

Para `Administrador`/`Titular`, a primeira matrícula (e só ela) exige também
um **ticket fora de banda** — token aleatório, hash no banco, TTL 24h, uso
único, emitido por quem já tem MFA (com step-up) ou pela ferramenta de
bootstrap (`--emitir-ticket-de-bootstrap` no binário publicado). Contas não
privilegiadas matriculam com `JWT + senha` normalmente, sem ticket.

**Bootstrap fecha sozinho:** o comando se recusa a rodar se já existir
qualquer conta privilegiada com `MfaAtivo = true` no tenant — depois do
primeiro Titular/Administrador, todo ticket novo sai do fluxo normal, com
step-up. Fecha a corrida "quem sabe a senha primeiro registra o MFA": um
atacante remoto com a senha nunca tem acesso SSH ao servidor, então nunca
alcança o bootstrap.

### Step-up — JWT sozinho nunca mexe em MFA

Iniciar (troca de autenticador com MFA já ativo), desativar e regenerar
códigos exigem senha atual **+** código do fator vigente. Um JWT roubado não
substitui segredo, não desativa MFA e não regenera códigos de recuperação.
1ª ativação (MFA ainda não ativo) exige só a senha — não há fator anterior
para provar.

### Política obrigatória — o flip é transacional

`Tenants.MfaObrigatorioParaAdministradores` só liga por
`MfaService.AtivarObrigatoriedadeAsync`: transação `SERIALIZABLE` que (1)
aborta se houver `Administrador`/`Titular` sem MFA (lista os pendentes), (2)
liga a política, (3) sobe `TokenVersion` de TODOS os afetados — **incluindo
o próprio ator**, que recebe sessão nova na resposta para não ser derrubado
pela própria ação. Administrador criado ou promovido depois do flip nasce
sem MFA e cai em `MfaObrigatorioNaoMatriculado` no próximo login — só entra
pelo ticket.

### Reset administrativo — Titular protegido

Reset remove MFA do alvo, apaga códigos de recuperação, invalida desafios
pendentes, sobe `TokenVersion` — a conta fica sem login até nova matrícula
(se a política exigir MFA para ela). Exige step-up do próprio ator
(`Administrador`). **Alvo com `Cargo.Titular` só pode ser resetado por ator
que também seja Titular** — nunca por Administrador comum. Todo reset e todo
flip de política grava `EventosDeSeguranca` (ator, alvo, IP, motivo) — nunca
o segredo, código, ticket ou desafio em si.

### Break-glass — reset de emergência

**Implementado** (revisão independente do PR #46, gap 17; restrito à sexta
revisão, bloqueadores B18/B19) — `FerramentaDeResetDeEmergencia`
(`Infrastructure/Security/FerramentaDeResetDeEmergencia.cs`), comando de
execução única:

```bash
dotnet CrmCartorio.Api.dll --reset-de-emergencia
```

Existe porque `MfaService.ResetarAsync` e `FerramentaDeBootstrapMfa` sozinhos
criam um **deadlock operacional real**: `ResetarAsync` exige step-up do
próprio ator (inútil quando é exatamente o fator que se perdeu) e, para alvo
`Cargo.Titular`, só um ator que também seja Titular pode resetá-lo — um
Administrador comum não pode, mesmo com MFA ativo. O bootstrap, por sua vez,
só serve para o *primeiro* privilegiado do tenant: se já existe qualquer
Administrador/Titular matriculado (o caso comum — existe um Administrador
não-Titular, só o Titular específico perdeu os fatores), ele se recusa a
rodar. O break-glass é o único caminho de dentro do processo que resolve
esse deadlock, com a MESMA disciplina do bootstrap:

- Só interativo — recusa se stdin OU stdout estiver redirecionado, ou em
  CI/GITHUB_ACTIONS.
- Texto do ticket novo só em `Console.WriteLine`, nunca `ILogger`/disco/banco
  em claro.
- Motivo obrigatório; identidade do operador em `EventosDeSeguranca.AtorExterno`
  (nunca em `Motivo`).
- **Não** toca `Tenants.MfaObrigatorioParaAdministradores` — a política do
  tenant nunca é desligada por isto.
- Remove MFA do alvo (fatores, recovery codes, desafios/tickets pendentes),
  sobe `TokenVersion`, e emite um **ticket novo** de rematrícula já vinculado
  à versão pós-reset — sem isto o alvo ficaria resetado sem nenhum caminho de
  volta. Mutação + ticket + `EventoDeSeguranca(ResetDeEmergencia)` na MESMA
  transação `SERIALIZABLE`.
- Superfície pública da classe: exatamente `{ExecutarAsync, VerificarElegibilidadeAsync}`
  — o núcleo que gera/persiste o ticket é `private`, testado por reflexão.

**Elegibilidade exclusivamente como saída de deadlock (bloqueador B18, sexta
revisão independente).** A versão anterior aceitava qualquer conta ativa e
privilegiada, mesmo sem MFA ativo e mesmo quando já existia um caminho
normal disponível — o break-glass virava, na prática, um segundo emissor de
ticket que contornava step-up/Titular/bootstrap. `VerificarElegibilidadeAsync`
agora exige, além de conta ativa e privilegiada:

- **`MfaAtivo = true` no alvo** — quem ainda não se matriculou tem o
  bootstrap (se for o primeiro do tenant) ou o ticket normal (step-up de
  outro privilegiado). Sem isto: `ContaSemMfaAtivo`.
- **Nenhum outro ator apto a executar o reset NORMAL** — mesmo critério que
  `MfaService.ResetarAsync` realmente exige: outro Administrador ativo com
  MFA ativo no tenant e, se o alvo for `Cargo.Titular`, também `Cargo.Titular`.
  Se existir, o caminho normal resolve sozinho. Sem isto: `ExisteAtorAptoParaResetNormal`.

**Controle de duas pessoas tecnicamente aplicado (bloqueador B18).** Antes,
"duas pessoas devem estar presentes" era só uma mensagem impressa no
console — sem nenhuma verificação técnica, uma pessoa sozinha com acesso SSH
completava o procedimento inteiro. Agora o comando exige, **antes de
conectar ao banco**, o PIN de um segundo custodiante — configurado como hash
SHA-256 em `Emergencia:HashDoPinDeDuasPessoas`, custódia separada de quem
tem acesso SSH ao servidor (mesmo modelo de `docs/BACKUP.md`). Sem essa
configuração, o comando se recusa a rodar. Comparação em tempo constante
(`CryptographicOperations.FixedTimeEquals`) — é, na prática, uma segunda
credencial.

**Rematrícula obrigatória, independente da política do tenant (bloqueador
B19).** `Atendentes.RematriculaObrigatoriaAposEmergencia` (migration 036) é
gravado `true` na MESMA instrução atômica que zera `MfaAtivo`. Antes disto,
num tenant com `MfaObrigatorioParaAdministradores` desligado, a conta
resetada voltava a logar só com senha, nunca passando pelo ticket de
rematrícula que o próprio break-glass emitiu — o reset de emergência virava
"tirar o MFA de alguém", não "forçar rematrícula segura". Agora
`AutenticacaoService.AutenticarAsync` recusa login só-senha enquanto esse
campo for `true`, **em paralelo** à checagem de política do tenant (as duas
condições, tanto na pré-checagem quanto no `WHERE` atômico final — nunca só
uma). Só volta a `false` na confirmação bem-sucedida de uma matrícula nova
(`MfaService.ConfirmarNucleoAsync`, mesmo `UPDATE` que reativa o MFA).

Certificado em `CRM_Cartorio_Dev`, incluindo: o cenário de deadlock (outro
Administrador com MFA ativo, mas não-Titular, já existente no tenant); a
elegibilidade recusando conta sem MFA ativo e conta com ator apto disponível
(alvo Titular e alvo não-Titular); e o fluxo completo com a política do
tenant DESLIGADA, provando que a rematrícula continua obrigatória mesmo
assim.

### O que ainda não existe

- **Roubo de JWT continua fora do alcance do MFA.** Token em `localStorage`,
  8h de validade — MFA protege o *login*, não a sessão já emitida.
- **Redefinir senha de terceiro (SEC-02) continua sem step-up.** Um JWT de
  Administrador roubado ainda troca a senha de um atendente não privilegiado
  e age como ele — MFA não fecha isso; fechar exigiria step-up também ali.

## Pendência da Fase 3

`AutenticacaoService.AutenticarAsync` busca o atendente só pelo e-mail, porque no
login ainda não se sabe o tenant. O e-mail é único **por tenant**, não
globalmente — com mais de um cartório na base essa busca fica ambígua. Antes do
multi-tenant real, o tenant precisa ser resolvido ali (subdomínio, código do
cartório na tela de login, ou índice único global). Está comentado no código.
