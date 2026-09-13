# Módulo: Seguranca

Blindagem LGPD (2026-09) — a resposta, visível na tela ao cartório, para "o
programador consegue ver as conversas do meu cliente sem eu saber?" Ver
[docs/PROTECAO-DE-DADOS.md](../../../../../docs/PROTECAO-DE-DADOS.md).

Este módulo tem duas responsabilidades. A primeira é **só leitura**: agrega e
expõe trilhas que já existiam gravadas em outro lugar do sistema
(`RegistrosDeAcesso`, nova; `EventosDeSeguranca`, do SEC-03 em
`Modules/Usuarios`) para a tela "Acesso aos dados". A gravação dessa trilha
não mora aqui — ver a seção seguinte. A segunda, desde o PR4, é o **ciclo de
vida do acesso técnico autorizado** — o suporte técnico pede, o cartório
aprova pela tela (já autenticado, sem passo extra), e a permissão de leitura
entra na sessão só enquanto durar — ver "Acesso técnico autorizado" mais
abaixo.

## Peças

| Peça | Papel |
| --- | --- |
| `RegistroDeAcesso` (Domain) | Linha append-only: quem leu, o quê, quando, de onde, e (quando aplicável) sob qual autorização técnica |
| `TipoDeAcesso` (Domain) | O que foi lido/ocorreu — leitura de conversa/anexo/ficha, ciclo de vida do acesso técnico, e (faixa reservada) eventos de cifra para o PR3 do mesmo plano |
| `ISegurancaService` / `SegurancaService` | Leitura paginada, por período, das duas trilhas + o resumo "Proteção ativa" |
| `EscopoDeAcesso` (Domain) | `Geral` ou `Conversa` — o que uma autorização técnica libera |
| `AutorizacaoDeAcessoTecnico` (Domain) | Pedido de acesso da conta técnica: motivo, escopo, duração, estado derivado das datas |
| `IAcessoTecnicoService` / `AcessoTecnicoService` | Solicitar, aprovar, negar, revogar, consultar a autorização ativa |
| `SegurancaController` | `GET /api/seguranca/acessos`, `/eventos`, `/protecao`; `POST/GET .../acesso-tecnico/...` — ver tabela de permissões abaixo |

## Onde a trilha é gravada, e por quê não é aqui

`Infrastructure/Auditoria/IRegistradorDeAcesso` (não `Modules/Seguranca/Application`)
é quem grava. Fica em `Infrastructure` porque é chamado de dentro de
`ConversaService`, `MensagemService` e `ClienteService` — três módulos
diferentes — e CLAUDE.md 7.1 proíbe um módulo referenciar o `Application`
interno de outro. Mesmo motivo de `INotificadorDeAtendimento` morar em
`Infrastructure/Realtime`.

**Quem chama `RegistradorDeAcesso.RegistrarAsync`, e o que registra:**

| Chamador | `TipoDeAcesso` | Quando |
| --- | --- | --- |
| `ConversaService.ObterAsync` | `LeituraDeConversa` | Abrir o detalhe de uma conversa (mensagens + histórico) |
| `MensagemService.ListarAsync` | `LeituraDeConversa` | Listar as mensagens de uma conversa — dedup com a linha acima quando é a mesma conversa/atendente dentro da janela |
| `MensagemService.ObterAnexoAsync` | `DownloadDeAnexo` | Baixar um anexo — **nunca deduplica**, cada download conta |
| `ClienteService.ObterAsync` | `LeituraDeFichaDeCliente` | Abrir a ficha de um cliente |

**O que NÃO registra, de propósito:** a prévia da última mensagem na lista de
conversas (`ConversaService.ListarAsync`), o resumo do painel
(`ResumoDoPainelAsync`), a entrega por SignalR. Justificativa: a lista e o
painel são contadores/prévias que o próprio atendimento já expõe a quem tem
`atendimento.ver`; "abrir a conversa" — o ato de ler o fio inteiro — é o que
importa para a trilha, e é isso que fica registrado. O SignalR (`AtendimentoHub`)
não registra porque não é "alguém pediu para ler" — é a atualização automática
de quem já está com a tela aberta; a blindagem aqui é outra: só quem tem
`atendimento.ver` entra no grupo que recebe conteúdo (ver
`AtendimentoHub.OnConnectedAsync`).

## Deduplicação — por quê, e o limite dela

Abrir a mesma conversa várias vezes em poucos minutos é uso normal do painel
(recarregar a tela, alternar de aba). Sem deduplicar, a trilha vira ruído e o
cartório para de ler. `RegistradorDeAcesso` usa uma janela de 15 minutos por
`(tenant, atendente, tipo, alvo)` — passada a janela, abrir de novo grava de
novo. `DownloadDeAnexo` é a exceção deliberada: baixar um documento é uma ação
discreta, não uma tela que fica aberta, e cada download conta.

## O gatilho `TR_RegistrosDeAcesso_SomenteInsercao` — o limite honesto

`sql/045-registros-de-acesso.sql` cria um gatilho `INSTEAD OF UPDATE, DELETE`
que recusa qualquer alteração ou remoção de linha. Isso impede o **acidente**
(um `ExecuteUpdate`/`ExecuteDelete` escrito por engano contra esta tabela) e o
**atalho** (um `UPDATE` avulso rodado sem pensar). **Não impede** um
administrador do servidor determinado — `db_owner` (a identidade do App Pool)
ignora `DENY` de permissão convencional, e quem tem acesso direto ao SQL Server
pode rodar `DISABLE TRIGGER ... ON dbo.RegistrosDeAcesso` antes de mexer na
tabela. O que fecharia essa lacuna de verdade é uma cópia da trilha fora do
alcance de quem administra o servidor — nível 2 do plano de proteção de dados,
ainda não construído. Ver
[docs/PROTECAO-DE-DADOS.md](../../../../../docs/PROTECAO-DE-DADOS.md), seção 4.

`Modules/Usuarios/Domain/Entities/EventoDeSeguranca` **não** ganhou o mesmo
gatilho: `AtendenteService.ExcluirAsync` apaga eventos do próprio alvo quando
um atendente sem histórico é excluído fisicamente — exclusão legítima,
existente antes desta entrega. Aplicar o mesmo gatilho ali quebraria esse
fluxo; registrado como pendência, não como esquecimento.

## Login vira evento de segurança

`TipoDeEventoDeSeguranca.LoginRealizado` (13) — `AutenticacaoService` grava
nos três caminhos de login concluído (sem MFA, por dispositivo confiável, e
por segundo fator completo). Antes desta entrega, login nunca virava evento
de segurança nem gravava IP; falha de senha/código continua **não** virando
evento — o backoff por conta (SEC-02) já existe, e uma tabela alimentada por
tentativa errada seria alvo fácil de enchimento por quem só quer forçar o
rodízio dela.

## `seguranca.eventos.ver` — já existia, sem endpoint

A permissão granular já estava no catálogo (`CatalogoDePermissoes.cs`, bloco
"Equipe e segurança") desde as permissões granulares, sem nenhum endpoint que
a usasse. Este módulo é o primeiro a checá-la de verdade.

## Acesso técnico autorizado — o suporte técnico pede, o cartório aprova

Responde à pergunta seguinte de "quem lê a conversa": e quando é o próprio
suporte técnico que precisa olhar, para investigar um problema relatado
pelo cartório? A resposta é a mesma trilha do PR2, com um portão na frente:
sem autorização ativa, nem o suporte técnico enxerga uma linha de conversa.

**Conta técnica.** `Atendente.EhContaTecnica` marca uma conta criada pelo
próprio cartório em Equipe para o suporte técnico do sistema. Ela nasce **sem**
nenhuma permissão de leitura de dado — só `suporte.solicitar_acesso`
(preset "Suporte técnico"). `AtendenteService.DefinirPermissoesAsync` recusa
conceder qualquer `atendimento.*`/`clientes.*`/`relatorios.*` a uma conta
técnica, mesmo que alguém tente pela tela de permissões — a única porta de
entrada para leitura é a autorização abaixo, nunca a matriz de permissões
direta.

**`AutorizacaoDeAcessoTecnico`** — um pedido com motivo (texto livre,
obrigatório), escopo (`Geral` ou `Conversa`, com o protocolo da conversa
quando for o segundo — a conta técnica não tem `atendimento.ver` para
conhecer o `Guid` interno, então resolve por protocolo) e duração (teto de
480 minutos). O estado nunca é uma coluna gravada — `AcessoTecnicoService`
deriva `Pendente`/`Ativa`/`Expirada`/`Negada`/`Revogada` a partir dos
timestamps toda vez que lê, o mesmo padrão de `PoliticaDeMfa`.

**Aprovar não pede segundo fator adicional.** A versão original do PR4 fazia
`AprovarAsync` reaproveitar o step-up de TOTP que já existia para reset de
MFA (`IMfaService.ConferirSegundoFatorAsync`). Decisão do Thiago em
2026-09-12: removido — o aprovador já autenticou (senha + MFA, quando a
política do tenant exige) para abrir o painel, e pedir mais um código só
para clicar em "Aprovar" foi considerado atrito sem ganho proporcional. A
trava real é a permissão (`seguranca.acesso_tecnico.aprovar`) + a trilha
registrada, não um segundo fator por ação. Negar e revogar seguem sem pedir
nada além da permissão, como sempre. A aprovação em si continua atômica
(`ExecuteUpdateAsync` guardado por `AprovadaEm == null && NegadaEm == null`)
— duas aprovações simultâneas do mesmo pedido não duplicam o efeito.

**A autorização vira permissão de sessão, não uma checagem nova em cada
guarda.** `ValidadorDeSessao.CarregarSessaoAsync` já filtrava por
`EhContaTecnica`; agora, encontrando uma autorização ativa, **acrescenta**
`AtendimentoVer` (e, só quando o escopo é `Geral`, também `ClientesVer`) ao
conjunto de permissões da sessão, e preenche
`IContextoDoUsuario.AutorizacaoDeAcessoTecnicoId`/`EscopoDeConversaId`. As
guardas do PR2 (`ConversaService`, `MensagemService`, `ClienteService`) não
sabem que acesso técnico existe — continuam checando `atendimento.ver`/
`clientes.ver` do jeito que sempre checaram. **Decisão deliberada de escopo
mínimo em `Conversa`:** libera só `AtendimentoVer`, restrito à conversa
nomeada — nunca `ClientesVer` nem o resto do sistema. Uma investigação sobre
um atendimento específico não precisa (e não deveria) destravar a ficha
completa do cliente, com CPF/RG/filiação, muito menos Equipe ou
Configurações.

**`Geral` é a autorização ampla — decisão do Thiago em 2026-09-13: libera
TODAS as chaves do catálogo** (`CatalogoDePermissoes.TodasAsChaves()`), não
só `AtendimentoVer`/`ClientesVer` como na versão original do PR4
(2026-09-12). Quem aprova um pedido `Geral` está aprovando acesso total,
temporário, equivalente a um administrador — a faixa e a trilha continuam
avisando e registrando do mesmo jeito, e é exatamente por isso que o
seletor de escopo na tela de pedido existe: quem só precisa investigar UM
atendimento deveria pedir `Conversa`, não `Geral`.

**Todo `RegistroDeAcesso` gerado sob uma autorização carrega o
`AutorizacaoDeAcessoTecnicoId`** — a tabela de acessos mostra "via acesso
técnico #…", nunca uma leitura anônima.

**Achado de segurança corrigido neste PR, antes de qualquer coisa ir ao ar:**
`AtendimentoHub` (SignalR) entra num grupo por tenant, não por conversa — um
usuário no grupo recebe push de **todas** as conversas do cartório. Uma
conta técnica com escopo `Conversa` que entrasse nesse grupo receberia
conteúdo de conversas fora do que foi aprovado, por um canal que as guardas
HTTP não cobrem. `OnConnectedAsync` agora só junta ao grupo quando
`EscopoDeConversaId is null` — quem tem acesso escopado por conversa não
entra no grupo de push em tempo real; lê só o que pedir explicitamente pela
API, sempre pela guarda comparando `ctx.EscopoDeConversaId` com o id pedido.

**Simplificação deliberada:** a faixa e a tela de aprovações usam
`GET .../ativa` em polling de 60s pelo frontend, não push de SignalR — um
pedido/aprovação não é tão sensível ao tempo quanto uma mensagem de cliente,
e evita adicionar mais um evento ao hub justo no módulo que acabou de ficar
mais restrito.

## `seguranca.acesso_tecnico.aprovar` e `suporte.solicitar_acesso`

Duas permissões novas no catálogo. A primeira só entra no preset
Administração (quem aprova acesso técnico é quem administra o cartório). A
segunda é exclusiva do preset "Suporte técnico" da conta técnica — os
presets Administração e Auditoria excluem-na explicitamente mesmo cobrindo
"todo o resto", porque um administrador do cartório não deveria *solicitar*
acesso a si mesmo por esse fluxo (ele já tem `atendimento.ver` direto).

## Cifra em repouso (PR3) — o que este módulo tem a ver com isso

A cifra em si não mora aqui (vive em `Infrastructure/Security/ICifraDeDados`
e `Infrastructure/Persistence/Cifra`, ver
[docs/PROTECAO-DE-DADOS.md](../../../../../docs/PROTECAO-DE-DADOS.md)) —
mas `SegurancaService.ObterProtecaoAtivaAsync` é quem **relata** o estado
dela para a tela "Acesso aos dados": `CifraAtiva`, `VersaoDaChave` e
`CifraDesde` (o instante do primeiro `RegistroDeAcesso{Tipo=CifraDeLegadoConcluida}`,
gravado por `--cifrar-dados-legados` — nulo enquanto a cifra está ativa mas
ninguém ainda rodou o comando contra dado legado). Enquanto nenhum ambiente
tiver a chave configurada (hoje, em todo ambiente), a tela mostra
honestamente "cifra ainda não ativa" em vez de inventar um valor.

**Achado ao construir isto, guardado para quem mexer em `CrmDbContext`
depois:** o EF Core cacheia o *model* compilado por TIPO de `DbContext`, não
por instância de opções — um segundo `new CrmDbContext(mesmasOpcoes,
outraCifra)` reaproveitaria caladamente o model (e os `ValueConverter` de
cifra) montado pelo PRIMEIRO. `ConfiguracaoDoBanco.Configurar` registra
`ChaveDeCacheDeModeloDeCrm` (`IModelCacheKeyFactory`) justamente para isto
não acontecer — produção nunca sentiria (uma única instância de
`ICifraDeDados`, singleton pela vida do processo), mas
`verificar-protecao-de-dados.cs` testando cifra dormente e ativa no MESMO
processo sentiria, e sentiu: sem essa correção, a cifra "ativa" nunca
cifrava de verdade, e `--cifrar-dados-legados` entrava em loop infinito
(a condição de "ainda em claro" nunca deixava de ser verdadeira).

## Verificação

`src/backend/verificar-protecao-de-dados.cs` — classe B (SQL Server real,
escreve e limpa em `CRM_Cartorio_Dev`), 60+ checagens em 41 cenários.
Cenários 1–18 (PR2): guardas de `atendimento.ver`/`clientes.ver` nas quatro
leituras, deduplicação (inclusive entre pontos de entrada diferentes que
registram o mesmo `TipoDeAcesso`+alvo), `DownloadDeAnexo` nunca
deduplicando, o gatilho recusando `UPDATE`/`DELETE`, falha ao gravar o
registro derrubando a leitura (fail-closed), o login gravando
`LoginRealizado`, e as três leituras de `SegurancaService` (guarda +
conteúdo). Cenários 19–33 (PR4): guarda de permissão em Solicitar/Aprovar,
resolução de protocolo (ausente e inexistente), pedido duplicado pendente
rejeitado, aprovação bem-sucedida **sem nenhum MFA matriculado pelo
aprovador** (prova de que a decisão de 2026-09-12 de tirar o TOTP da
aprovação está de pé), `ValidadorDeSessao` injetando `AtendimentoVer` sem
`ClientesVer` para escopo `Conversa`, a lista de permissões que
`MapeadorDeSessao` devolve ao FRONTEND (login/`GET sessao`) incluindo
`atendimento.ver` com a autorização ativa (cenário 30c — regressão do achado
"conta técnica aprovada sem nenhuma tela para ler a conversa", ver
`Modules/Usuarios/README.md`), leitura permitida dentro do escopo e negada
para outra conversa, revogação com efeito imediato na sessão seguinte, e
negação impedindo aprovação posterior do mesmo pedido. Cenários 34–41 (PR3,
com
chave de desenvolvimento fixa — todo o resto do arquivo continua com a cifra
dormente): round-trip de `Cifrar`/`Decifrar` e identidade quando dormente;
`ClienteService.CriarAsync` grava `Documento`/`DataNascimentoCifrada`
começando com `$c1$` no banco cru, e `ObterAsync` devolve em claro; busca
por CPF completo acha via `DocumentoHash`, busca parcial não acha mais;
`ExigirCifrado` lançando `DadoNaoCifradoException` sobre linha legada;
`--cifrar-dados-legados` rodado de ponta a ponta contra dado real (cria
cliente+mensagem em claro, roda o comando, confirma ciphertext no banco,
roda de novo e confirma "0 pendentes" com o mesmo ciphertext, provando
idempotência); anexo cifrado em disco (marca `CRMA`, round-trip, AAD errado
falhando); `ObterProtecaoAtivaAsync` com a cifra ativa.
