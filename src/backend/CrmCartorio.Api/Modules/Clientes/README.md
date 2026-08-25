# Módulo: Clientes

O contato final que manda mensagem para o cartório. **Não é usuário do sistema.**

## O telefone é a identidade

`TelefoneWhatsapp` é obrigatório e único por tenant, guardado em E.164 sem
símbolos (`5538999000045`). É por ele que a mensagem chega — sem telefone não há
conversa.

O serviço aceita o número como o balcão digita (`38999000045`) e completa o `55`
sozinho. Ninguém digita "+55" no meio de um atendimento.

## O documento é opcional de propósito

Exigir CPF no primeiro "bom dia" trava o atendimento. Quando informado, é
validado (11 ou 14 dígitos) e guardado só com dígitos.

**O backend não valida dígito verificador, e isso é decisão, não esquecimento.**
No balcão aparece documento antigo e documento de terceiro; recusar por conta
disso travaria atendimento. O que se valida aqui é o tamanho, que já pega erro de
digitação.

A tela confere o DV e mostra **aviso em âmbar** quando não fecha — sem bloquear o
salvamento. As duas camadas dizem a mesma coisa de propósito: uma avisa, nenhuma
recusa. Mudar uma sem a outra faz o cadastro parecer quebrado.

## CNPJ preenche sozinho; CPF não

CNPJ e CEP consultam fonte pública e preenchem o cadastro
([Modules/Consultas](../Consultas/README.md)). **CPF não tem consulta e não vai
ter**: não existe fonte pública e legal de dado pessoal a partir de CPF. Ver
DESIGN.md, seção 11.

## Nome

Nasce como o nome do perfil do WhatsApp e é corrigido pelo atendente quando a
identificação real aparece.

## Sem exclusão

Só `Ativo = false`. Vale para todo o produto.

## Origem do cadastro

`OrigemDoCadastro` (`Webhook`, `Manual`, `ImportacaoPlanilha`, `ImportacaoWhatsapp`)
registra de onde o contato veio. Nulo nos clientes de antes da coluna (migration
026) — "não sabemos" é a verdade para eles, e ninguém faz backfill retroativo.

## Importação de contatos em lote

`IImportacaoDeContatosService` cadastra contatos em massa a partir de três
fontes: texto colado, CSV, ou a agenda do WhatsApp pareado
(`IEvolutionClient.ObterContatosAsync`, no número que estiver conectado agora —
não fixo em nenhum ambiente). **Não dispara mensagem nenhuma** — só cria a
ficha. Disparo em massa foi avaliado e cancelado neste ajuste pelo risco de
banimento do piloto sobre Baileys (CLAUDE.md, seção 2, "Mudança de direção —
2026-08-16"); a coluna acima nasce pronta para o dia em que essa distinção
importar de novo, pela via oficial da Meta.

**A fonte WhatsApp filtra o que a agenda devolve** antes de virar linha de
importação: descarta grupo (`@g.us`), lista de transmissão
(`status@broadcast`) e o próprio número pareado. `@lid` (identificador
anônimo) já chega resolvido ou descartado por `EvolutionClient` — ver
`Modules/CanalWhatsApp/README.md`. Um resultado vazio é válido e esperado
(nem sempre a Evolution sincronizou agenda para o número pareado); a tela
mostra isso como "nenhum contato encontrado", não como falha.

Duas rotas — `POST /importacao/analisar` (classifica, não grava) e
`POST /importacao/confirmar` (grava só o que foi aprovado) — porque a prévia é
**editável**: o operador desmarca linha antes de confirmar, e um único endpoint
com flag não permitiria isso.

**Não usa `ClienteService.CriarAsync` no lote.** Ele faz uma consulta de telefone
livre por linha e fecha com três consultas extras em `ObterAsync` — um custo que
não faz sentido pagar centenas de vezes na mesma chamada. O importador escreve
direto no `DbContext`, em lotes de 500, com dedup em três níveis (dentro do
arquivo, contra o banco por lote, e `try/catch DbUpdateException` no
`SaveChanges` como rede de segurança contra corrida com o webhook) — mesma
decisão que `RecebimentoDeWhatsApp` já toma pelo mesmo motivo.

`TelefoneDeContato.Interpretar` (`Domain/TelefoneDeContato.cs`) é mais desconfiado
que a validação do cadastro manual: valida DDD e, para telefone de 12 dígitos com
DDD válido, decide pelo **primeiro dígito do assinante** (regra da Anatel, válida
em todo o Brasil) em vez de marcar tudo como "revisar" — `2`–`5` é fixo (8 dígitos,
já completo), `6`–`9` é celular na forma legada (insere o nono). Só o dígito fora
dessas duas faixas — o que não deveria acontecer em número brasileiro real — cai
em `Ambiguo`. Corrigido em 2026-08-25 depois de o cartório parceiro testar a importação de
verdade: a versão anterior marcava a maioria dos celulares legados como "revisar"
sem necessidade, porque tratava qualquer 12 dígitos como incerto. O cadastro
manual continua usando `ParaE164Basico`, sem esse crivo extra: um atendente
digitando na tela vê o erro na hora e corrige.
