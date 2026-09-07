# IDENTIDADE VISUAL

> **Status:** Fase 1 aprovada. Shell global, navegação e Visão Geral entregues
> na Fase 3 (seção 14). **Fase 3.1 (2026-08-21) redirecionou a linguagem visual
> do painel — ver seção 15 antes de aplicar as seções 1 a 4.**
> **Última atualização:** 2026-08-21
>
> **Este documento é a fonte de verdade da interface.** Cor, fonte, raio e
> espessura de borda saem daqui. Valor arbitrário no código é bug, não estilo.
>
> ⚠️ **LEIA A SEÇÃO 15 PRIMEIRO.** As seções 1 a 4 descrevem o conceito
> "documento oficial" da Fase 1, que a Fase 3.1 **substituiu nas superfícies de
> painel** por decisão explícita do Thiago. Aplicar as seções 1 a 4 na Visão
> Geral, em Relatórios, no shell ou no login reconstrói um visual que já foi
> recusado. A seção 15 diz exatamente o que valeu, onde, e o que continua
> intacto.

---

## 1. Conceito

**Documento oficial / livro de registro. Não dashboard de startup.**

O produto vive num cartório: fé pública, papel timbrado, livro encadernado,
selo. A interface tem que parecer parte desse mundo — não um SaaS genérico com
gradiente roxo.

Consequência prática: a tela é **disciplinada**. Contenção não é falta de
capricho, é o capricho. O que dá personalidade é a tipografia e um único
elemento com licença poética (o selo, seção 6), não efeito visual espalhado.

Isso também é argumento comercial. O concorrente é SaaS em nuvem com cara de
software. Parecer registro oficial reforça exatamente o que vendemos:
seriedade e soberania sobre o dado.

---

## 2. Paleta

Nomes em português; os tokens CSS usam os mesmos nomes.

### Papel e superfície

| Token | Hex | Uso |
| --- | --- | --- |
| `papel` | `#EDEBE6` | Fundo da aplicação. É o papel |
| `superficie` | `#F7F6F3` | Painéis, barras, áreas de apoio |
| `branco` | `#FCFBF9` | Cartões, campos, o que está "em cima" |

### Tinta

| Token | Hex | Uso |
| --- | --- | --- |
| `tinta` | `#1F1D1A` | Texto principal, títulos |
| `tinta-media` | `#5C5852` | Texto secundário, rótulos |
| `tinta-fraca` | `#8E8981` | Metadados, placeholder, texto desativado |

### Linhas

| Token | Hex | Uso |
| --- | --- | --- |
| `linha` | `#DDD9D2` | Bordas padrão, divisores |
| `linha-forte` | `#C5C0B7` | Borda de campo, separação que precisa aparecer |

### Acentos

| Token | Hex | Uso |
| --- | --- | --- |
| `selo` | `#7A2E32` | Bordô. Ação primária e ênfase — **uso contido, ver 2.1** |
| `ambar` | `#8A6516` | Alerta, pendência, prazo apertado |
| `verde` | `#3D5C3A` | Concluído, em dia, sucesso |

### Série de gráfico

| Token | Hex | Uso |
| --- | --- | --- |
| `tendencia` | `#2F5D7C` | Azul-ardósia. **Somente série de gráfico** — ver 15.3 |

`tendencia` entrou na Fase 3.1 e **não é cor semântica**: não vai em botão,
badge, borda de estado nem texto de aviso. Os três acentos acima já carregam
significado (bordô = atraso, âmbar = alerta, verde = concluído), e uma linha de
gráfico pintada com qualquer um deles herdaria um sentido que ela não tem. Por
isso também **não conta para o teto de 3 bordôs** (2.1): não compete com eles.

### 2.1 A regra do bordô

**No máximo 3 ocorrências de bordô por tela.**

É a regra mais importante deste documento. O bordô é o selo do cartório: se
estiver em tudo, não significa nada. Botão primário, item de menu ativo e um
indicador — acabou.

Antes de usar bordô num lugar novo, conte os que já existem na tela. Se já são
três, a resposta é `tinta` ou `linha-forte`.

**Histórico que vale conhecer:** na primeira versão a etapa "Captação do serviço"
usava bordô, e como a etapa aparece em toda linha da inbox, a cor do selo se
espalhava pela tela. A saída na época foi reduzir a marca de etapa a um ponto de
6px. A paleta de etapa (2.2) resolveu o problema na origem: **nenhuma etapa usa
bordô**, então a mini-pílula preenchida voltou a ser segura. O bordô ficou só onde
importa — ação primária, item de menu ativo, atraso.

### 2.2 Cores de etapa

Cada etapa do funil tem uma cor de identificação própria — **trio de fundo, tinta
e borda**. Validada com o cliente.

| Etapa | Nome da cor | Fundo | Tinta | Borda |
| --- | --- | --- | --- | --- |
| Atendimento inicial | azul acinzentado | `#E3EAF0` | `#2E5170` | `#C9D8E3` |
| Captação do serviço | roxo suave | `#EDE6EF` | `#6B3F73` | `#D9C9DC` |
| Em processamento / exigência | âmbar | `#F4EAD5` | `#8A6516` | `#E0CFA6` |
| Pronto / entrega | verde | `#E4EBE1` | `#3D5C3A` | `#C3D2BE` |

**A cor acompanha a etapa, não a conversa nem o cliente.** Mover a conversa de
etapa muda a cor na hora, nos três lugares abaixo. Cliente não tem cor própria.

Onde aparece — sempre o mesmo trio, para que a leitura seja a mesma:

1. **Pílula do funil** no topo do painel
2. **Mini-pílula de etapa** na linha da inbox
3. **Avatar do cliente** na inbox e no cabeçalho da conversa — aqui é fundo
   **sólido na tinta** da etapa, com iniciais em branco

Onde **não** aparece:

- **Controles interativos.** Os `select` de etapa, setor e serviço ficam neutros.
  Cor aqui é status passivo; num controle, viraria promessa de que a cor faz algo.
- **Listagens sem etapa** — cadastro de clientes, cadastro de atendentes. Ali o
  avatar mantém o tratamento neutro de borda.

Contraste das iniciais brancas sobre a tinta, medido: azul 8,3:1 · roxo 8,1:1 ·
verde 7,5:1 · âmbar 5,3:1. Todos passam o mínimo de 4,5:1 — o âmbar é o mais
apertado e não deve escurecer menos que isso.

**O selo de "Pronto / entrega" continua.** A cor verde da etapa e o selo circular
(seção 6) são coisas diferentes e convivem: a cor diz em que etapa está, o selo
diz que o ato ficou pronto. Não substituir um pelo outro.

#### Só três colunas de cor, e uma no banco

`EtapaFunil.Cor` guarda **apenas a tinta**. Fundo e borda saem de um mapa no
frontend (`features/atendimento/cores-de-etapa.ts`), com a tinta como chave.

Guardar os três exigiria três colunas novas — e uma tinta determina as outras duas
sem ambiguidade. Consequência prática: **o seletor de cor do cadastro de etapa é
fechado nessas quatro tintas**. Cor fora do mapa cai no neutro, o que é degradação
aceitável, não quebra.

---

## 3. Tipografia

Três famílias, cada uma com um trabalho. Nenhuma faz o trabalho da outra.

| Família | Onde | Por quê |
| --- | --- | --- |
| **Spectral** | Somente títulos e cabeçalhos | Serifa. É a voz do documento oficial |
| **Inter** | Corpo, interface, botões, rótulos | Lida bem em tamanho pequeno e densidade alta |
| **IBM Plex Mono** | Protocolo, CPF, CNPJ, telefone, datas em tabela | Dado que se confere caractere por caractere pede largura fixa |

**Spectral não desce para o corpo do texto.** Título e cabeçalho de seção, nada
mais. Serifa em texto de interface densa cansa.

**IBM Plex Mono não é decoração.** É para dado conferível. Protocolo
`202608-0042` em mono se lê de relance; em Inter, o `0` e o `O` disputam.

### Fontes empacotadas, não CDN

As três vêm por `@fontsource`, instaladas como dependência e servidas pelo
próprio build. **Não usamos Google Fonts por CDN.**

Motivo: o produto é self-hosted e roda num cartório no norte de Minas, onde
internet instável é hipótese realista. Fonte por CDN significa tela quebrada
quando a rede oscila — e um sistema que se apresenta como soberano sobre o
próprio dado não deveria depender de servidor de terceiro para desenhar texto.

---

## 4. Regras rígidas

> **Parcialmente substituída pela seção 15.** Gradiente, sombra, `blur` e o raio
> de 6px valem como escrito nas telas operacionais herdadas da Fase 1 (inbox,
> fio de mensagens, cadastros, configurações). **Nas superfícies de painel
> listadas em 15.1 a proibição foi levantada.** As linhas de emoji e de "cor
> fora dos tokens" continuam valendo em toda parte, sem exceção.

Não são preferências. São restrições.

| Proibido | Por quê | Situação |
| --- | --- | --- |
| **Gradiente** | Nenhum documento oficial tem gradiente | Levantado em 15.1 |
| **Sombra** (`box-shadow`) | Profundidade falsa. Papel não flutua | Levantado em 15.1 |
| **Glow**, `blur` decorativo | Idem | Levantado em 15.1 |
| **Emoji** no painel (botão, ícone, badge, menu) | Quebra o registro formal na hora — exceção para mensagem ao cliente, ver abaixo | **Continua** |
| **Cor fora dos tokens** | Se não está na seção 2, não entra | **Continua** |

| Obrigatório | Valor | Situação |
| --- | --- | --- |
| Espessura de borda | **1px**. Sempre | Continua |
| Raio de canto | **6px** | Convive com `--raio-card` (15.2) |
| Ícones | Stroke **1.6px**, sem preenchimento | Continua |

Nas telas operacionais herdadas, separação entre áreas é feita com **linha**,
não com sombra, e hierarquia é feita com **tipografia e espaço**, não com
elevação. Nas superfícies de 15.1 a elevação passou a ser permitida — e o motivo
está registrado lá, não é liberdade geral.

Sobre ícones: usamos `lucide-react`, que é stroke por natureza. A espessura vai
no `strokeWidth` global — nunca `fill`.

**Sem emoji, aqui, é sobre o painel — botão, ícone, badge, menu.** Decisão de
2026-08-17: o texto que o CLIENTE lê no WhatsApp
(`MensagemAutomatica.Conteudo`, `RespostaRapida.Conteudo`, resposta da IA) pode
usar emoji pontual e com propósito — que ajuda a escanear a mensagem (📍
endereço, 📄 documento, ⏱️ prazo), nunca emoji solto só para chamar atenção.
Essa mensagem é conteúdo que o cartório parceiro escreve e edita pela tela, na voz do
cartório para o cliente final — registro formal do painel e tom de conversa
por WhatsApp são dois registros diferentes, e forçar o segundo a soar como o
primeiro é o motivo do produto ainda soar "atrás" das mensagens de um cartório
que já usa WhatsApp de verdade. A restrição do parágrafo anterior continua
inteira para o painel em si.

---

## 5. Cores por significado

Mapeamento fixo. Não inventar variação.

### Situação de prazo

| Situação | Cor | Tratamento |
| --- | --- | --- |
| `EmDia` | `verde` | Texto e borda |
| `EmAlerta` | `ambar` | Texto e borda |
| `Atrasada` | `selo` | Texto e borda |
| `SemPrazo` | — | Não exibe nada |

Atrasada usa bordô porque atraso em cartório é falha de prazo legal, e essa é a
única situação de prazo que merece o peso do selo. **Conta para o limite de 3.**

### Etapa do funil

Ver **seção 2.2** — trio de fundo, tinta e borda por etapa.

O cadastro de etapa oferece **apenas** as quatro tintas de lá. Um seletor de cor
livre deixaria o cliente colocar azul-piscina no funil e derrubar a identidade
toda — por isso a paleta do formulário é fechada.

### Estado da conversa

| Estado | Tratamento |
| --- | --- |
| Na fila | Borda `linha-forte`, texto `tinta-media` |
| Minha | Texto `verde` |
| De outro atendente (travada) | Texto `tinta-fraca`, ícone de cadeado |
| Encerrada | Texto `tinta-fraca` |

---

## 6. O selo

**O único elemento da interface com personalidade.** Todo o resto é disciplinado
justamente para que este apareça.

Onde: conversa cuja etapa está marcada como final (`EhFinal`) e que já foi
concluída.

Como:

- Círculo, borda **1.5px** `verde`, fundo transparente
- Rotação **-9deg** — carimbo batido à mão nunca sai reto
- Texto dentro em Spectral, caixa alta, `letter-spacing` aberto
- Sem sombra, sem preenchimento. É tinta em papel

A rotação é o detalhe que faz funcionar. Reto vira ícone de sistema; torto vira
carimbo. Não "corrigir" isso.

Um selo por conversa. Nunca dois na mesma tela por engano.

---

## 7. Densidade

Cartório trabalha com fila. A inbox precisa mostrar muita conversa sem rolagem
infinita.

| Item | Valor |
| --- | --- |
| Altura de linha da inbox | Compacta, ~3 linhas de conteúdo |
| Altura de campo e botão | 36px (`h-9`) |
| Espaçamento interno de cartão | 16px |
| Tamanho de texto base | 14px |
| Metadado | 12px |
| Protocolo e documento | 12px em mono |

Denso não é apertado: a respiração vem do espaço entre grupos, não de padding
grande em cada item.

### 7.1 O fio de mensagens é mais apertado que o resto da tela

Decidido em 2026-08-18, depois de ver a tela num monitor de 1366×768: **duas
mensagens visíveis por vez**. A tabela acima continua valendo para a aplicação
inteira; o fio é a exceção, e tem motivo.

A altura do fio é o que sobra depois de tudo que é fixo — cabeçalho da página,
resumo/funil, cabeçalho da conversa e caixa de envio. Num monitor pequeno isso é
~320px, e cada mensagem custava ~92px. A escala do fio desceu para:

| Peça | Antes | Agora |
| --- | --- | --- |
| Espaço entre mensagens | `gap-3`, `py-4` | `gap-1.5`, `py-2.5` |
| Interior do balão | `px-3 py-2.5` | `px-2.5 py-1.5` |
| Corpo da mensagem | 14px, `leading-relaxed` | 13px, `leading-snug` |
| Citação | `mb-2 px-2 py-1.5` | `mb-1.5 px-2 py-1` |
| Imagem e vídeo | `max-h-72` (288px) | `max-h-52` (208px) |
| **Custo por mensagem curta** | **~92px** | **~68px** |

**Muda espaço, não identidade.** Cor, borda de 1px, raio de 6px, família
tipográfica, lado de cada autor e nome em Spectral continuam idênticos.

**O metadado de 10px e 11px não encolheu.** Ele já é o piso de legibilidade, e
encolher metadado é justamente o que faz uma tela parecer esmagada em vez de
compacta.

**É fixo, não uma preferência.** Houve uma versão com botão de alternar entre duas
densidades; foi recusada pelo cliente e removida. Controle a mais numa tela que
já é densa é custo, não recurso — a tela certa é uma só.

---

## 8. Como aplicar no código

1. **Cor sempre por token.** `var(--cor-selo)`, `text-selo`, `border-linha`.
   Nunca `#7A2E32` solto num componente.
2. **Fonte por classe utilitária.** `font-titulo` (Spectral), `font-corpo`
   (Inter), `font-dado` (IBM Plex Mono).
3. **Componente novo copia a forma do vizinho.** Se `badge.tsx` resolve estado
   com borda 1px e texto colorido, o próximo também.
4. **Ao mexer em cor de etapa**, mexer no cadastro (`CatalogoDeDemonstracao.cs`
   e `sql/002-seed-simulado.sql`), não no componente.

Tokens definidos em [`src/frontend/src/index.css`](../src/frontend/src/index.css).

---

## 9. O que este documento não resolve

- **Tema escuro.** Papel escuro é contradição. Se um dia precisar, é redesenho,
  não inversão de tokens.
- **Impressão.** Nenhuma tela foi pensada para imprimir. Quando houver relatório,
  entra aqui uma seção própria.
- **Variações vetoriais da marca.** A assinatura oficial foi fornecida em imagem
  raster na Fase 2. As aplicações atuais usam recortes fiéis do original; uma
  futura entrega em SVG deve substituí-los sem alterar proporção, cor ou desenho.

---

## 10. Aplicações pontuais

Dois detalhes aprovados depois da primeira rodada. São **aplicações pontuais, não
elementos estruturais** — vivem em duas classes de estilo em
[`index.css`](../src/frontend/src/index.css), sem componente próprio, e podem ser
removidos apagando a classe do lugar onde é usada.

### 10.1 Etiqueta de protocolo — `.etiqueta-protocolo`

O número do protocolo ganha caixa própria:

| Propriedade | Valor |
| --- | --- |
| Fonte | IBM Plex Mono (`--font-dado`) |
| Borda | 1px `--cor-linha-forte` |
| Raio | 3px |
| Fundo | `--cor-superficie` |
| Padding | `0 0.25rem` |

Por que só o protocolo: é o único dado que o cliente repete ao telefone e que o
atendente procura na lista. Telefone e CPF continuam em mono sem caixa — se tudo
tiver borda, nada se destaca.

Aparece na linha da inbox e no cabeçalho da conversa. **A posição não mudou**, só
o tratamento do texto.

> O raio é 3px, e não os 6px da seção 4. É a exceção: 6px numa caixa de 18px de
> altura arredonda demais e o número parece caber mal. Não replicar esse valor em
> outro lugar.

### 10.2 Marca d'água do selo — `.marca-dagua-selo`

O anel do selo ao fundo da área de mensagens, canto superior direito.

| Propriedade | Valor |
| --- | --- |
| Forma | Anel do selo, sem texto |
| Cor | `--cor-verde` a 5% (`stroke-opacity: .05`) |
| Tamanho | 11rem |
| Posição | `right 1.25rem top 1.25rem` |

Duas decisões de implementação que importam:

**É `background-image`, não elemento.** Assim não entra no fluxo, não recebe
clique, não precisa de `z-index` e não desloca mensagem nenhuma. `pointer-events`
e ordem de pintura deixam de ser problema porque o problema não existe.

**O anel não tem texto.** Fonte de `@font-face` não carrega dentro de `data:` URI
— o SVG é renderizado isolado. Um selo com "ATO PRONTO" ali cairia numa serifa
qualquer do sistema e brigaria com a Spectral do selo de verdade. Só o anel.

O `background-attachment` fica no padrão (`scroll`), então a marca gruda na caixa
visível e não acompanha a rolagem do fio.

### 10.3 Menu como lombada do livro

> **Substituída pela 14.2 no que é mecânica.** O CONCEITO abaixo — navegação como
> lombada escura, e única superfície escura da aplicação — continua sendo a regra.
> O que mudou: não existe mais botão de três linhas (o rail abre por hover com
> intenção), e o rótulo recolhido usa `opacity-0` em vez de `title`. A marca
> também não mora mais no topo do menu; subiu para o cabeçalho claro (seção 13).

O menu principal usa `tinta` como fundo e `linha` nos itens. É a única grande
superfície escura da aplicação: separa navegação e conteúdo como a lombada escura
separa a capa das folhas de um livro de registro.

- Item ativo continua em `branco` com texto `selo` quando o painel está aberto;
  conta como uma ocorrência de bordô. Recolhido, é só a lombada `selo` de 2px —
  **nunca `bg-branco` ali**, ou o ícone branco fica branco sobre branco.
- Abrir/recolher não muda rota, filtro nem estado da página.
- Não criar uma segunda superfície escura na área de conteúdo: a distinção só
  funciona porque é exclusiva da navegação. A folha "Mais" do celular (14.5) é a
  única outra, e é a mesma navegação em outro formato.

### 10.4 Autoria e ações no fio

O fio segue a leitura de uma troca documental: cliente à esquerda e atendente à
direita, em cartões com largura limitada. O cliente usa `branco` com borda
`linha-forte`; a resposta usa uma diluição do token `verde` no fundo e na borda.
Não há preenchimento bordô nas mensagens.

O nome do autor aparece em Spectral, caixa alta e espaçamento aberto, no topo de
cada mensagem. O cliente usa `tinta-media`; o atendente usa `verde`. Horário e
estado de entrega ficam juntos no canto inferior direito, em IBM Plex Mono.

A mudança de dia é marcada por uma etiqueta central com borda, sem linhas
laterais. Isso mantém a cronologia legível sem transformar cada data num divisor
estrutural da página.

Copiar e responder ficam num grupo discreto ao lado da mensagem, visível por
hover ou foco do teclado em telas amplas e permanentemente em telas pequenas,
que não possuem hover. As ações ficam voltadas para o centro do fio para não
alterar as margens esquerda e direita dos cartões. A citação usa uma faixa com
borda esquerda `linha-forte`. `Lida` usa verde e `Falha` usa selo, sempre
acompanhadas de ícone e rótulo — cor nunca é o único sinal.

### 10.5 Mensagem automática no fio

O fio tem **três** autores, não dois. A mensagem gerada por regra fica do lado da
serventia, como qualquer resposta, mas não é de ninguém.

| | Cliente | Atendente | Automática |
| --- | --- | --- | --- |
| Lado | esquerda | direita | direita |
| Fundo | `branco` | diluição de `verde` | `superficie` |
| Borda | `linha-forte` | diluição de `verde` | `linha-forte` |
| Linha de autoria | nome, `tinta-media` | nome, `verde` | etiqueta "Automática" |

**O fundo é neutro, e não a diluição de verde do atendente.** Verde é a cor de quem
respondeu; a mensagem automática não tem quem. Ela fica no papel da própria
interface — presente, sem reivindicar autoria.

**A etiqueta ocupa a linha do nome**, porque é exatamente a informação que falta ali.
Mesma tipografia da autoria — Spectral, 10px, caixa alta, `letter-spacing` aberto —
com borda 1px `linha-forte` e texto `tinta-media`. Sem bordô: não conta para o
limite de 3, e não deveria — mensagem automática é rotina, não ênfase.

**O rótulo é texto, não cor.** Um ponto colorido não diria "não fui eu que escrevi".

Não confundir com a **anotação de sistema** ("Conversa assumida por Fulano"), que
continua exatamente como estava: texto centralizado, itálico, `tinta-fraca`, sem
caixa. As duas são `Sistema` no banco; só a automática vira balão, porque só ela foi
endereçada ao cliente.

### 10.6 Seletor de resposta rápida

Abre acima do campo de digitação quando o atendente digita `/`. Borda 1px
`linha-forte` sobre `branco`, cabeçalho em `superficie` — **sem sombra**, como todo
painel da aplicação: a separação vem da borda, não de elevação falsa.

- Opção ativa marcada por fundo `superficie`, nunca por cor de acento.
- O atalho aparece em **IBM Plex Mono**: é literal que se digita caractere a
  caractere, mesma justificativa do protocolo (seção 3).
- O foco **nunca sai do campo de texto**. A opção ativa é anunciada por
  `aria-activedescendant` — foco de verdade na lista interromperia a digitação a
  cada seta.
- Escolher **insere no campo**. Nunca envia. A revisão do atendente é o ponto.

### 10.7 Lista de conversas recolhível

Decidido em 2026-08-18, mesma família da 10.3: o que recolhe, recolhe para uma
faixa que ainda serve para alguma coisa.

A coluna da esquerda ocupa 360px fixos o tempo todo, inclusive depois que o
atendente já escolheu a conversa e só quer ler e responder. Um botão ao lado da
busca recolhe a coluna para **56px**; o botão de voltar fica no topo da faixa.

O que sobra na faixa recolhida:

- **Os avatares das conversas, clicáveis.** Recolher não pode custar a troca de
  atendimento. O avatar já carrega a cor da etapa (seção 2.2) e o nome vai no
  `title` — é o mesmo arranjo do menu principal recolhido, que mantém os ícones e
  joga os rótulos para o `title`.
- **A marca da conversa aberta**, idêntica à da lista aberta: barra de 2px em
  `tinta` à esquerda, fundo `superficie`.
- **O ponto de não lidas em `selo`.** Não é uma quarta ocorrência de bordô: é a
  contagem que a linha da lista já mostrava, no lugar dela, já que a lista não
  está na tela.
- **O total de conversas** no rodapé, e um ponto discreto em `tinta-media` quando
  há filtro aplicado — com os filtros escondidos, uma lista curta sem explicação
  parece defeito.

Recolher é **só largura**: não muda filtro, não muda rota, não desmonta a
consulta da lista e não mexe na conversa aberta. Mesma regra da 10.3. A escolha
fica no `localStorage` (`crm-cartorio.lista-recolhida`), padrão **aberta**.

Sem animação de largura, como no menu principal: animar uma coluna que tem fio
rolando dentro rende repaint torto, não sofisticação.

### 10.8 Cabeçalho da conversa em uma linha

Decidido em 2026-08-18, na mesma medição da 7.1. Tudo que é faixa fixa no topo
sai da altura do fio, e o cabeçalho da conversa tinha duas: identificação em
cima, três seletores de 32px logo abaixo. Virou **uma linha**.

**Etapa, setor e serviço viraram pílulas na linha da identificação.** São os
**mesmos** `Select` — mesmas opções, mesma trava de permissão, mesma ação. Só o
tamanho mudou: `text-[11px]`, `px-1.5 py-0.5`, chevron de 12px.

- **Neutras**, nunca na cor da etapa: cor de etapa é status passivo e não entra em
  controle (seção 2.2). A cor da etapa continua onde sempre esteve — no avatar ao
  lado, na mini-pílula da inbox e na pílula do funil.
- Não vira texto sem ação. Setor e serviço quase sempre já chegam preenchidos pela
  triagem automática, mas cliente erra opção de menu, e **etapa não é automática
  em fase nenhuma**: quem move é o atendente. Tirar o controle seria tirar
  trabalho de cima da mesa, não ruído da tela.

**O selo do ato pronto passa a ser o mini** (2,5rem) neste cabeçalho. O de 4,25rem
sozinho devolveria a altura que a linha única economizou. Continua um selo por
conversa (seção 6).

**Linha de metadado não quebra — encolhe.** Faixa fixa que ganha uma segunda
linha tira altura do fio sem avisar, e foi o primeiro defeito que a linha única
mostrou num monitor pequeno. A ordem de sacrifício, tanto aqui quanto na linha da
inbox, é sempre a mesma:

1. **Trunca** o que tem o valor inteiro em outro lugar — nome de quem atende
   (fica no `title`), nome de etapa e de setor (ficam na lista e no seletor).
2. **Esconde** o telefone quando a coluna fica menor que 42rem. É
   `@container`, não breakpoint de tela: quem aperta essa linha é o painel de
   histórico abrindo ao lado, com a janela do mesmo tamanho.
3. **Nunca sai o protocolo.** É o identificador que o cliente repete ao telefone
   e que o atendente procura na lista.

"Atendido por Fulano" virou **cadeado + nome**, o mesmo tratamento que a linha da
inbox já usava — mesma informação, ~70px a menos de linha.

Onde o container **não** tem altura fixa — painel de histórico, funil, telas de
configuração — `flex-wrap` continua certo e não foi mexido. A regra é sobre faixa
de altura fixa, não sobre a aplicação inteira.

**A caixa de envio perdeu duas linhas de texto de apoio:**

- O aviso "esta conversa está na fila — ao enviar, você assume o atendimento"
  virou `title` do botão de enviar. A informação continua a um passe de mouse, e
  o botão "Assumir" no cabeçalho segue sendo o caminho de quem só quer reservar.
- A dica do `/` saiu: o `placeholder` do campo já diz a mesma coisa, e dizer duas
  vezes custava uma linha.
- O botão "Simular mensagem do cliente" saiu de vez. Era andaime da Fase 1, quando
  não havia WhatsApp; com o canal real no ar, mensagem de cliente se testa pelo
  WhatsApp.

---

## 11. Preenchimento assistido e validação de campo

Diferente da seção 10, isto **não** é aplicação pontual: é como todo campo que
consulta ou confere se comporta, hoje e no próximo formulário.

### 11.1 A regra que não se negocia

| Campo | Consulta fonte pública | Valida |
| --- | --- | --- |
| **CEP** | **Sim** — preenche logradouro, bairro, cidade e UF | 8 dígitos |
| **CNPJ** | **Sim** — preenche nome, razão social e endereço | Dígito verificador |
| **CPF** | **Não. Nunca.** | Dígito verificador |

**CPF não se consulta porque não existe fonte pública e legal que devolva dado
pessoal a partir de CPF.** O que se encontra por aí é base vazada ou revenda
irregular. Plugar uma dessas trocaria o argumento central do produto — soberania
de dados e LGPD num nicho que lida com documento de fé pública — por conveniência
de digitação. Não vale, e não é assunto reaberto por pedido de tela.

CNPJ é outra coisa: dado de empresa é público por definição e a Receita publica.

A consulta sai do **servidor**, nunca do navegador da estação: o cache serve a
serventia inteira, só uma máquina precisa de internet, e o endereço do terceiro
fica em `appsettings` — dá para trocar de provedor ou desligar tudo. Ver
`Modules/Consultas/README.md`.

### 11.2 Estado do campo

| Estado | Cor | Ícone (`lucide`) | Quando |
| --- | --- | --- | --- |
| Consultando | `tinta-fraca` | `Loader2` girando | Chamada em voo |
| Preenchido | `verde` | `Check` | Achou e preencheu |
| Aviso | `ambar` | `TriangleAlert` | Não achou, serviço fora, ou DV não confere |

Texto de 11px, na mesma linha do texto de ajuda do campo — **substitui** a ajuda
enquanto durar, para o campo não crescer duas linhas.

**Sem bordô.** Falha de consulta não é falha de prazo legal; gastar `selo` aqui
comeria a cota de 3 por tela (seção 2.1) numa tela que já usa o bordô no botão
primário e no asterisco de obrigatório.

**Cor nunca é o único sinal** — mesma regra de 10.4. Todo estado leva ícone e
texto.

### 11.3 Preenchimento não apaga trabalho

- **CEP sobrescreve** logradouro, bairro, cidade e UF. Quem trocou o CEP trocou o
  endereço. Número e complemento nunca são tocados: são do imóvel, não do CEP.
- **CNPJ só preenche o que está vazio**, exceto razão social. O endereço vem de
  brinde na consulta; sobrescrever o que o atendente digitou seria destruir dado
  para entregar comodidade.
- Todo campo preenchido continua **editável à mão**. A consulta sugere; quem
  responde pelo cadastro é o atendente.

### 11.4 Validação avisa, não tranca

Dígito verificador inválido pinta o aviso e **não bloqueia o salvamento**. No
balcão aparece documento antigo e documento de terceiro, e recusar por conta
disso travaria atendimento — mesma razão registrada em
`ClienteService.ValidarDocumento`. O backend segue conferindo só o tamanho.

### 11.5 Recusa do servidor cai no campo, não num toast

Erro de salvamento **aponta o campo que o causou**, no mesmo lugar e com o mesmo
tratamento de 11.2 — âmbar, ícone, texto de 11px sob o campo.

- Se o campo está em outra aba, **a aba troca sozinha** e o campo recebe o foco.
  Formulário de quatro abas com o erro num toast obriga o atendente a caçar.
- O aviso **sai assim que o campo é editado**. Erro que fica na tela depois de
  corrigido ensina a ignorar aviso.
- Erro **sem endereço** — sessão expirada, conflito de versão, rede fora —
  continua em toast. É onde erro sem campo pertence.
- A decisão vem do **`codigo` do ProblemDetails**, nunca do texto da mensagem: o
  código é contrato estável, a redação não é.

Como o texto do backend passa a ficar **fixo sob o campo**, e não mais de
passagem num toast, ele é texto de interface para todos os efeitos: leva acento
e diz o que fazer. Ver `ClientesServiceException`.

### 11.6 Máscara

CPF, CNPJ, CEP e telefone são **IBM Plex Mono** (`font-dado`, seção 3): é dado
conferível caractere a caractere.

A máscara formata enquanto se digita e aceita dígito corrido, colado ou digitado.
Regra de implementação em `lib/mascaras.ts`: **nunca emitir separador à direita**
(`38` vira `(38`, não `(38) `), senão o backspace trava.

---

## 12. Fundação compartilhada de interface

Esta seção transforma as decisões anteriores em contrato para os componentes
reutilizáveis. Ela não cria uma identidade nova e não autoriza cor ou medida
fora das seções 2 a 4.

### 12.1 Tokens semânticos

Os papéis abaixo apontam para a paleta curta já aprovada:

| Papel | Token CSS | Origem |
| --- | --- | --- |
| Foco | `--cor-foco` | `tinta-media` |
| Erro | `--cor-erro` | `selo` |
| Sucesso | `--cor-sucesso` | `verde` |
| Alerta | `--cor-alerta` | `ambar` |
| Informação | `--cor-informacao` | `tinta-media` |
| Desabilitado | `--cor-desabilitado` | `tinta-fraca` |
| Overlay | `--cor-overlay` | `tinta` a 50% |

Componente consome o papel, não repete hexadecimal. Erro, sucesso e alerta
sempre têm texto; cor nunca carrega o significado sozinha.

O alvo móvel padrão é `--alvo-toque: 2.75rem` (44px). Durações funcionais são
`--duracao-rapida` (120ms) e `--duracao-padrao` (180ms). Quem solicita menos
movimento no sistema recebe transições e animações reduzidas globalmente.

### 12.2 Controles e formulário

- Botão, input e select têm 44px no celular e retornam à densidade de balcão a
  partir de `md`.
- **Botão só de ícone precisa de nome acessível** — isto não se negocia, e o nome
  vem de `aria-label` ou de texto no DOM (inclusive `opacity-0`, que leitor de
  tela continua lendo).
  O tooltip é reforço opcional, **não** requisito: no rail ele foi removido em
  2026-08-21 por ser defeituoso ali (14.2), e nenhum nome acessível se perdeu,
  porque tooltip nunca foi o nome — é descrição (`aria-describedby`). Onde a
  legenda não estiver visível e o hover for a única pista, o tooltip continua
  recomendado.
- `aria-invalid="true"` aplica o papel de erro no controle. A mensagem explica
  o problema e é ligada ao campo por `aria-describedby` no formulário.
- Switch mantém trilho compacto, mas a área clicável chega a 44px no celular.
- Foco de teclado nunca é removido. O outline global tem 2px e offset de 2px.
- Desabilitado é estado real (`disabled`), não apenas opacidade visual.

### 12.3 Feedback e estados

`EstadoDaInterface` é a base de carregamento, vazio, sem resultados e erro. Todo
estado tem título textual; carregamento usa `role="status"` e `aria-busy`, erro
usa `role="alert"`, e uma ação de recuperação aparece quando existe.

Toast é reservado a confirmação ou falha sem campo específico. Usa superfície
de papel, borda, sem sombra e duração padrão de 5 segundos. Operação crítica ou
erro que exige decisão não pode existir apenas num toast temporário.

Tooltip complementa um nome acessível; nunca o substitui, nunca contém ação e
nunca guarda informação indispensável. Abre por mouse e foco de teclado.

### 12.4 Tabelas e responsividade

- Cabeçalho identifica a coluna mesmo quando o conteúdo parece óbvio.
- Número, protocolo e documento usam alinhamento tabular e `font-dado` quando a
  conferência caractere a caractere importa.
- Estado não depende só de cor. Texto ou ícone com nome acompanha a célula.
- Em largura insuficiente, a prioridade é preservar ação e dado, nesta ordem:
  quebrar texto seguro, ocultar metadado secundário e, por último, permitir
  rolagem horizontal dentro da região da tabela — nunca na página inteira.
- Estado vazio e erro ficam dentro da mesma região da tabela, para que título,
  filtros e ações permaneçam no lugar.

As larguras de referência para revisão são 360px, 768px, 1024px e 1440px. Não
são quatro layouts separados: são pontos de inspeção do mesmo fluxo responsivo.

---

## 13. Marca do produto

A identidade oficial recebida em 2026-08-20 é preservada em
`src/frontend/src/assets/logo-original.png`. Os ativos de uso são extrações
determinísticas desse original, sem redesenho, vetorização automática ou geração
por IA:

| Variante | Arquivo | Uso |
| --- | --- | --- |
| Assinatura horizontal transparente | `public/brand/crm-cartorio-logo-horizontal.webp` | Login e cabeçalho global, a partir de `sm` — somente em superfície clara |
| Assinatura horizontal (mestre) | `public/brand/crm-cartorio-logo-horizontal.png` | Não é servido. Origem para regerar o WebP |
| Símbolo oficial isolado e transparente | `public/brand/crm-cartorio-simbolo.png` | Cabeçalho global abaixo de `sm` e folha "Mais" do celular |
| Favicons 16, 32 e 48px | `public/brand/favicon-*.png` | Aba do navegador na densidade adequada |
| Ícones 180 e 192px | `public/brand/apple-touch-icon-180.png` e `public/brand/icon-192.png` | Atalho de dispositivo e alta densidade |

**A assinatura é servida em WebP sem perdas** (820×186, recortada na moldura
transparente do mestre): 73KB contra 173KB do PNG, e a mesma imagem serve o login
e o cabeçalho — carregada uma vez, reaproveitada do cache depois de entrar. Sem
perdas, e não lossy: é ativo de marca exibido grande no login, e artefato de
compressão em serifa não se desfaz. 820px de largura cobre o maior uso real (272
CSS px no login, em tela de densidade 3×).

**O símbolo saiu do rail.** Na Fase 3.1 a marca subiu para o cabeçalho claro e o
rail ficou navegação pura — o que, de quebra, resolveu o problema de contraste
descrito abaixo em vez de contorná-lo: a assinatura escura não precisa mais
conviver com o fundo `tinta`.

**CRM Cartório é a marca do produto.** O nome da serventia identifica o ambiente
e aparece como contexto secundário. Não trocar essa hierarquia nem repetir a
marca em todas as regiões da mesma tela.

O fundo externo dos ativos deve ser transparente. Nunca recortar partes do
símbolo, recolorir, deformar nem recriar a tipografia. A assinatura horizontal
escura não tem contraste suficiente sobre `tinta` — por isso ela **nunca** entra
em superfície escura; onde antes isso era um problema (a sidebar), a marca deixou
de morar lá. Uma assinatura reversa só pode entrar se for fornecida ou aprovada
formalmente.

Recorte da moldura transparente **é permitido e desejável**: é o que faz o
desenho preencher a caixa em vez de aparecer pequeno dentro dela. Foi assim que
o favicon subiu de 85–88% para 100% da altura útil. Recortar moldura vazia não é
recortar o desenho.

O favicon deriva do símbolo oficial isolado, sem simplificação silenciosa. Cada
tamanho é rasterizado diretamente do recorte nativo com reamostragem de alta
qualidade; não ampliar um derivado pequeno para gerar outro maior.

O login usa a fotografia editorial sobre atendimento por mensagens como plano
full-screen, com o tablet à direita e o cartão à esquerda. A serventia não
aparece nessa tela, e não há frase institucional, kicker, legenda, texto sobre a
fotografia ou marca de terceiro. A fotografia usa o WebP de 1536px em todas as
larguras, para evitar ampliação dos recortes menores.

**O cartão foi redesenhado na Fase 3.1** e as regras antigas dele não valem mais:

| Antes (Fase 2.1) | Agora (Fase 3.1) |
| --- | --- |
| Bloco sólido em `branco`, sem transparência nem sombra | `branco` a 96% com `backdrop-blur`, sombra funda |
| Cabeçalho próprio em `superficie` com linha superior em `selo` | Sem faixa de cabeçalho; a assinatura abre o corpo |
| Título "Entrar no CRM" só para tecnologia assistiva | Título **visível**, em Inter bold |
| Raio de 6px | `--raio-card` |
| Largura fixa | 27rem, subindo para 30rem em `xl` e 33rem em `2xl` |

A translucidez é o ponto: branco 100% opaco recorta a fotografia como adesivo
colado, e a 96% a textura da madeira atravessa de leve nas bordas. Um véu escuro
em gradiente cobre a faixa do cartão para garantir contraste em qualquer recorte
da imagem — o recorte muda com a largura da janela, e sem o véu o cartão podia
cair sobre a parte clara do aparelho.

O teto de 33rem existe porque campo de formulário largo demais piora a leitura: o
olho percorre a linha inteira para achar um `input` que continua com o mesmo
conteúdo. Quem cresce com a tela é o espaço interno e a tipografia, não só a
largura.

---

## 14. Shell global (Fase 3)

A Fase 3 substituiu o menu de quatro itens e o `CabecalhoDaPagina` por navegação
em rail, cabeçalho global e Visão Geral. Esta seção documenta o que mudou de
estrutural — o vocabulário de cor, tipografia e forma das seções 1 a 4 continua
valendo sem excepção.

### 14.1 Navegação como dado

`src/frontend/src/app/navegacao.ts` é a única lista de itens de menu. Rotas
(`rotas.tsx`), o rail, o cabeçalho, a barra inferior do celular e a folha
"Mais" leem dali — nunca existe uma segunda árvore de menu escrita à mão num
componente. Mudar o menu é editar esse arquivo, não quatro.

### 14.2 Rail de navegação — exceção deliberada ao "sem animação de largura"

O rail (`components/layout/rail-de-navegacao.tsx`) é a mesma "lombada do
livro" da seção 10.3 — única grande superfície escura, item ativo em `branco`
com texto `selo` contando para o limite de 3 ocorrências de bordô. O que muda:

- **Fechado, 64px, só ícones.** Abre por hover com intenção (**160ms** para
  abrir, **320ms** para fechar — cada evento cancela o timer contrário) e por
  foco de teclado, que TRAVA o painel aberto enquanto o foco estiver dentro.
  Os 90ms originais abriam o painel com o ponteiro só atravessando a borda, e o
  resultado era sensação de coisa brusca; 160ms absorve a passagem acidental e
  ainda fica dentro da janela em que o movimento se lê como resposta.
- **Escolher um destino recolhe o painel na hora.** Fica num efeito sobre
  `location.pathname`, não num `onClick` dos itens: a navegação também acontece
  por submenu, por teclado e pelo histórico do navegador, e o `onClick` cobriria
  só um dos caminhos.
- **O painel aberto (256px) é OVERLAY sobre o conteúdo**, não uma coluna em
  fluxo — por isso ele **pode** animar largura, ao contrário das colunas das
  seções 10.3 e 10.7 (lista de conversas, menu antigo), que continuam sem
  animação porque são colunas em fluxo e reflow no meio de uma tela com fio de
  mensagens rolando fica torto. Aqui não há reflow: o rail expandido cobre o
  conteúdo, e os ícones da faixa de 64px não se movem 1 pixel.
- **Fixar aberto** é um botão de 24px no rodapé do painel, sem hambúrguer.
  Preferência em `localStorage` (`crm-cartorio.menu-fixado`), ignorada abaixo
  de 1280px pela mesma razão do menu antigo (a lista de 360px não cabe do
  lado de um rail de 256px fixo).
- **Não existe tooltip no rail**, e a ausência é deliberada — removida em
  2026-08-21 por ser defeituosa por construção. O rail abre no hover em 160ms,
  então a única janela em que o tooltip podia aparecer eram esses 160ms antes de
  o painel mostrar o rótulo de verdade: com o atraso de 500ms do
  `TooltipProvider` ele nunca aparecia, e com o `skipDelayDuration` de 300ms
  piscava. Pior, ele **ficava preso** na tela, porque o componente alternava
  entre controlado (`open={false}`) e não controlado (`open={undefined}`), e
  voltar para não controlado restaura o estado interno do Radix.
- **`className` do `NavLink` nunca é função.** A regra nasceu do `Slot` do Radix
  estringificando a função ao mesclar classes; o tooltip saiu, a forma fica —
  o estado ativo é calculado fora (`pathname` comparado à rota) e passado como
  booleano, mantendo o cálculo num lugar só.
- Rótulo de item recolhido usa **`opacity-0`, nunca `sr-only` condicional nem
  `display:none`**: `opacity: 0` continua legível para leitor de tela, então
  esse `<span>` **é** o nome acessível do link. E opacidade esmaece junto com a
  animação de largura, enquanto `sr-only` some do layout de um quadro para o
  outro e o rótulo popa na tela no fim da abertura.
- **`border-l-2` em todos os estados, variando só a cor.** Com `border` de 1px no
  estado base e 2px só no ativo, cada item deslocava 1px ao ficar ativo ou ao
  painel abrir — numa coluna de dez itens isso lê como tremida.
- **Nunca `transition-all` nos itens.** A largura do item é ditada pela largura
  do `aside`, que está animando; com `all`, o item anima a própria largura
  perseguindo um alvo que muda a cada quadro e fica sempre atrás da borda do
  painel. Só cor e sombra transicionam.
- **`overflow-x-hidden` no `<nav>` é obrigatório.** Com `overflow-y-auto`
  sozinho, o CSS promove o eixo X de `visible` para `auto`, e os rótulos (que
  seguem ocupando largura mesmo em `opacity-0`) transbordam os 64px — o
  navegador desenhava uma barra de rolagem horizontal no pé do rail, que o tema
  de scrollbar do projeto ainda estilizava como pílula clara e parecia elemento
  da interface.
- **A barra de rolagem VERTICAL do `<nav>` é escondida, não desligada**
  (`[scrollbar-width:none]` + `[&::-webkit-scrollbar]:hidden`, 2026-08-24). O
  tema global pinta o polegar com `--cor-linha-forte`, e sobre o `bg-tinta` do
  rail isso vira o maior contraste da interface — uma pílula clara colada nos
  ícones. `overflow-y-auto` **fica**: a partir de ~700px de altura de janela o
  menu transborda, e sem rolagem os últimos itens ficariam inalcançáveis. Some
  o desenho, não o comportamento. Decisão explícita do Thiago de manter a barra
  visível no resto do painel — nas superfícies claras ela é discreta e é a única
  pista de que há mais conteúdo abaixo.

### 14.3 Cabeçalho global

Um cabeçalho só, presente em toda área autenticada
(`components/layout/cabecalho-global.tsx`), lendo título e descrição da rota
ativa em `navegacao.ts`. `CabecalhoDaPagina` (uma instância por página) não
existe mais. Ordem à direita: ações da página → busca → sino → perfil.

- **Ações da página** chegam por portal (`AcoesDaPagina`, em
  `contexto-de-acoes-da-pagina.tsx`): a página manda o botão, o cabeçalho
  desenha o slot. Vazio, o slot some (`empty:hidden`) sem reservar espaço.
- **Perfil e Sair** moram só aqui (desktop) e no rodapé da folha "Mais"
  (celular) — não mais no rodapé do rail. Duas superfícies de logout na
  mesma tela seria redundância.
- Em `/atendimentos/:id`, some no celular (a conversa já tem cabeçalho e
  "voltar" próprios); continua visível no desktop.

### 14.4 Busca global e sino de alertas

Nenhum dos dois criou endpoint novo de escrita nem schema.

- **Busca** (`Ctrl/Cmd+K`) usa `conversasApi.listar`/`clientesApi.listar`, os
  mesmos endpoints já abertos a qualquer autenticado.
- **Sino** chama `GET /api/indicadores/alertas` — atrasados, próximos do
  prazo, fila sem responsável, WhatsApp desconectado (só Administrador) e
  teto diário da IA (só gestão). É honesto sobre não ter "lido" persistido:
  o badge conta o que é mais novo que a última vez que o painel abriu **neste
  navegador** (`localStorage`), e a própria tela diz isso.

### 14.5 Navegação do celular

Barra inferior fixa (`< md`), quatro células: Visão Geral, Atendimento,
Clientes, Mais — sem hambúrguer. Ativo marcado por uma lombada `selo` de 2px
no topo da célula, não pelo preenchimento inteiro. "Mais" abre uma folha que
sobe do rodapé (`components/layout/folha-mais.tsx`) com o menu inteiro,
filtrado pela mesma permissão de `navegacao.ts`, e o rodapé com perfil e Sair.

A folha usa primitivos crus do Radix Dialog, não o `DialogContent`
compartilhado: sobrepor classes utilitárias de posição/tamanho num componente
que já define as suas próprias arrisca uma perder para a outra sem
previsibilidade em Tailwind v4. Mesmo cuidado na busca global.

Barra inferior e cabeçalho global somem juntos em `/atendimentos/:id` — a
conversa não deve dividir altura com nenhum dos dois num aparelho de 360px.

### 14.6 Visão Geral e gráficos

Gráfico é feito em casa, **sem biblioteca**. O motivo mudou de forma na Fase 3.1
mas não de conclusão: não é mais "biblioteca traz gradiente e sombra" (isso
agora é permitido em painel, 15.1) — é que o produto é self-hosted num lugar de
internet instável e já decidiu empacotar até fonte (seção 3), e 100KB+ de
dependência para barras e uma curva não se paga. Os componentes vivem
encapsulados em `components/graficos/`: trocar por biblioteca depois é
substituir o miolo desses arquivos, sem nenhuma tela saber.

**A forma segue a natureza da grandeza, e isso não é estilo:**

- **Barra para fluxo** (abertos, encerrados por dia). Contagem diária é discreta
  e esparsa — num cartório a maioria dos dias é zero, e uma linha ligando essas
  contagens afirmaria que existiu meio atendimento na terça.
- **Linha para estoque** (em aberto ao fim do dia). Estoque tem valor em todo
  dia, mesmo sem movimento, então a curva é contínua por natureza, sem um único
  ponto interpolado.
- Juntas respondem o que nenhuma responde sozinha: **está dando conta ou está
  acumulando?** Duas barras altas lado a lado podem ser "movimentou e empatou"
  ou "entrou o dobro do que saiu"; é a linha que desempata.

**Um eixo só**, à esquerda: fluxo e estoque estão na mesma unidade, então dois
eixos seriam mentira sobre haver duas escalas.

Duas tentativas de linha foram descartadas antes, e o registro evita repeti-las:
**média móvel dos próprios abertos** (derivável de olho da própria série — a
linha só repetia as barras) e **mediana do tempo de primeira resposta** (dado
valioso, mas indefinido nos dias sem resposta, o que quebrava a linha em
pedaços). O tempo de resposta ficou na dica de hover, onde não precisa de
continuidade.

Regras de implementação que custaram bug para descobrir:

- **Marcador de ponto em HTML, nunca `<circle>` SVG.** O `viewBox` usa
  `preserveAspectRatio="none"` e o eixo X é esticado ~11× mais que o Y: um
  círculo SVG ali vira elipse deitada. A curva sobrevive ao esticamento porque
  `vector-effect="non-scaling-stroke"` protege a espessura.
- **A caixa das barras precisa de altura DEFINIDA** (`absolute inset`).
  `height: X%` num filho de pai com altura automática é referência circular: o
  navegador resolve como zero e o gráfico sai só com eixo e grade.
- **Barra de valor zero não desenha nada.** Um "toco" de 2px faria zero parecer
  "quase um".
- Teto do eixo é arredondado para 1/2/5/10 × magnitude, para nenhuma marca cair
  em fração — contagem de atendimento é inteira por natureza.

Outras decisões da tela que continuam:

- Indicador "Atrasados" é a única ocorrência de bordô do número — o cabeçalho
  do grupo "Requer atenção" usa ícone, não cor, para não gastar o orçamento
  duas vezes pela mesma informação na mesma tela.
- Mediana, não média, para "1ª resposta humana": no volume de um cartório, um
  único caso respondido tarde distorce a média sem dizer nada do resto do
  período. Média e amostra continuam disponíveis no tooltip.
- **"Atividade recente" foi removida** e o espaço virou "Carga da equipe agora".
  O painel anterior era um log retrospectivo — vinte linhas de "Etapa alterada
  para X", sem nada a fazer com a informação. A carga responde uma pergunta de
  gestão que a tela não respondia, e é ordenada por NOME, nunca por volume:
  ordenar por quantidade transforma o painel em quadro de comparação entre
  colegas. Só aparece para gestão — expor carga alheia a todo atendente seria
  ampliar permissão.

### 14.7 Rotas preservadas

`/` deixou de ser a inbox — agora é a Visão Geral. Atendimento vive em
`/atendimentos` e `/atendimentos/:id`. `/conversas/:id` (link antigo) e
`/configuracoes` continuam resolvendo por redirect permanente, para nenhum
link salvo ou compartilhado quebrar.

Indicadores e Relatórios (`/indicadores`) tem quatro seções — Dashboard,
Atendimentos, Atendentes, Clientes — e **a seção vive na URL**
(`/indicadores/atendentes`), não em estado local: F5 volta na mesma aba, link
compartilhado abre onde o colega quer mostrar, e o "voltar" do navegador faz o
esperado. Ficam **fora** de `navegacao.ts` de propósito: são subdivisões de uma
tela, e inflariam o menu do rail com detalhe de uma página só. A permissão é
lida da própria entrada `/indicadores` em `navegacao.ts`, para não haver duas
verdades sobre quem entra.

### 14.8 Quem rola: a moldura nunca rola

Três regras, e a terceira foi comprada caro.

**1. Cada página traz o próprio scroller.** O `<main>` do shell é
`flex-col overflow-hidden` e não rola; o padrão de página é
`flex-1 overflow-y-auto p-3 md:p-6`, repetido em cada uma. A inbox é a exceção
(`flex min-h-0 flex-1`) porque delega a rolagem a cada coluna interna.

**2. A moldura é `position: relative`** (`shell.tsx`, 2026-08-24). Não é
decoração — é o que dá **bloco container** aos descendentes posicionados.

**3. A viewport nunca é travada globalmente.** Login e a tela de carregamento
vivem **fora** do `<Shell>` e dependem da rolagem do documento: num 1366×768 a
125% o cartão de login passa da altura da janela e precisa ser alcançável. Um
`html { overflow: hidden }` global cortaria o topo dele justamente quando o
aviso de Caps Lock ou a caixa de erro crescem o cartão.

#### O defeito que ensinou isto

Relatado pelo Thiago e medido em 2026-08-24 na tela de mensagens automáticas: o
documento ganhava **980px de rolagem**, e arrastar essa barra levava rail e
cabeçalho para fora da tela, deixando uma faixa vazia embaixo — `scrollY: 600`
punha o topo da moldura em `-600`, e nessa faixa não havia elemento nenhum.

A causa não era a página, e nem o `overflow` da moldura: era o utilitário
**`.sr-only` do Tailwind, que é `position: absolute`**. Um absoluto **sem
ancestral posicionado** ancora no bloco inicial — a viewport — e ali nenhum
`overflow: hidden` o alcança, porque overflow só recorta descendente cujo bloco
container esteja dentro dele. Cada botão "Editar" da tabela carrega um desses;
o mais baixo ficava em 1700px, e era exatamente o `scrollHeight` do documento.

Duas consequências que valem mais que o conserto:

- **Era latente em toda tela.** A varredura das 14 rotas acusou só uma, porque
  só ali o conteúdo passava da altura da janela. Bastaria o cartório cadastrar
  mais linhas para a próxima começar a fazer o mesmo.
- **Travar a viewport teria mascarado, não corrigido.** Medido: com
  `html { overflow: hidden }` a barra some, mas `docY` continua 980 e a rolagem
  programática (`focus()`, `scrollIntoView()`) ainda desloca a moldura. Só
  `position: relative` (ou `contain`) na moldura zera o `docY` de verdade.

Há uma rede permanente: em desenvolvimento, o `Shell` mede o documento a cada
troca de rota e joga um `console.error` nomeando a rota se ele voltar a rolar
(`useAvisoDeMolduraRolando`). Não roda em produção nem em teste — jsdom não faz
layout, `scrollHeight` seria sempre 0, e o aviso daria falsa segurança.

---

## 15. Fase 3.1 — a linguagem visual do painel

**Decisão do Thiago, 2026-08-21.** O conceito da seção 1 — "documento oficial,
não dashboard de startup" — foi aplicado na Visão Geral da Fase 3 e **recusado
na revisão**: a palavra usada foi "design de site criado em 2010". O pedido foi
explícito, e está registrado aqui porque contraria as seções 1 a 4 deste
documento: *"pode ignorar esse design, talvez seja por isso que ficou tão ruim"*,
e a direção pedida foi algo *"moderno, que chame atenção, bem tecnológico"*,
com **a paleta existente preservada** — sem preto, sem tema escuro.

Isto **não** é liberdade geral. É uma direção com escopo, e o escopo está em 15.1.

### 15.1 Onde vale

| Superfície | Linguagem |
| --- | --- |
| Login | **Fase 3.1** |
| Shell: rail, cabeçalho global, busca, sino, folha "Mais" | **Fase 3.1** |
| Visão Geral (`/`) | **Fase 3.1** |
| Indicadores e Relatórios (`/indicadores/*`) | **Fase 3.1** |
| Componentes compartilhados de `components/ui/` | **Fase 3.1** |
| Inbox, fio de mensagens, conversa | **Fase 3.2 — ver seção 16** (era Fase 1 até 2026-09-06) |
| Equipe e permissões (`/equipe`, `/equipe/:id/permissoes`) | **Fase 3.1 — ver seção 17** (era Fase 1 até 2026-09-08) |
| Clientes, Serviços, telas de configuração | Fase 1 — seções 1 a 12, **intactas** |

A fronteira não é capricho: as telas operacionais são onde o atendente passa o
dia, foram medidas em monitor de 1366×768 (7.1, 10.8) e a densidade delas é
resultado de trabalho de ajuste que elevação e raio maior desfariam. Painel é
tela de leitura; balcão é tela de trabalho.

> **Revisto em 2026-09-06 (seção 16).** O argumento acima continua correto
> quanto ao CUSTO — elevação com respiro e raio de cartão realmente desfariam
> a densidade do balcão. O que ele errou foi tratar "moderno" e "denso" como
> opostos. A seção 16 mantém a densidade medida (7.1 intacto) e moderniza pelas
> alavancas que custam zero pixel vertical: contraste entre planos, raio, forma
> de borda, tipografia e movimento.

**O que continua valendo em toda parte, sem exceção:** a paleta da seção 2, o
teto de 3 bordôs (2.1), as cores de etapa (2.2), o mapeamento de significado
(seção 5), a proibição de emoji no painel e de cor fora de token (seção 4), o
selo (seção 6) e as regras de acessibilidade da seção 12.

### 15.2 Tokens novos

Definidos em `index.css` e expostos ao Tailwind por `@theme inline`:

| Token | Valor | Uso |
| --- | --- | --- |
| `--raio-card` | `14px` | Cartão, painel, diálogo, dica de gráfico |
| `--sombra-card` | dupla, baixa opacidade | Repouso de cartão elevado |
| `--sombra-card-hover` | dupla, mais aberta | Hover de cartão e camada flutuante |
| `--cor-tendencia` | `#2F5D7C` | Série de gráfico — ver 15.3 |

O raio de 6px da seção 4 **não foi substituído**: ele continua em botão, campo,
pílula e badge. `--raio-card` é para a caixa que contém, não para o controle que
está dentro dela.

### 15.3 O que a direção nova autoriza, e o limite de cada coisa

- **Elevação** (`shadow-card`) em cartão de painel. Não em campo, não em botão de
  barra, não em linha de lista. Hierarquia por elevação é para separar o cartão
  do fundo `papel`, não para destacar item dentro do cartão.
- **Gradiente**, e só de duas formas: vertical sutil numa barra de gráfico ou no
  botão primário (`from-selo to-selo/90`), sempre **entre opacidades do mesmo
  token** — nunca entre dois tokens diferentes, que é o que produz o "gradiente
  roxo de SaaS" que a seção 1 recusa com razão.
- **`backdrop-blur`** apenas no cartão do login sobre a fotografia. Não é efeito
  de painel.
- **Tipografia de título em Inter bold** com `tracking` negativo, no lugar da
  Spectral. A Spectral **não saiu do produto**: ela continua no selo (seção 6),
  na linha de autoria do fio (10.4) e dentro do lockup da marca. O que mudou é
  que título de painel deixou de ser serifado.
- **Animação de entrada** escalonada (`grade-com-vida`, `crm-crescer-barra*`,
  `crm-revelar-esquerda-direita`). Teto curto de propósito: escalonar demais
  ATRASA a leitura e a tela passa a *parecer* lenta, que é o oposto do objetivo.
  O último cartão termina de entrar em ~360ms.
- **Micro-interação de hover** que levanta 1px com sombra maior. Em cartão e
  linha clicável; nunca em elemento sem ação, que prometeria clique.

### 15.4 O que continua proibido, e por quê

- **Tema escuro e superfície escura nova.** O rail e a folha do celular seguem
  sendo as únicas superfícies escuras (10.3). Foi pedido explicitamente: *"não
  quero o preto, não quero essa referência"* — as telas de referência
  (Vercel, Linear) são dark-mode, e a inspiração pedida foi de forma e ritmo, não
  de esquema de cor.
- **Cor fora de token.** Vale igual. `tendencia` entrou como token, não como
  hexadecimal solto num componente.
- **Emoji no painel.** Inalterado.
- **Gastar bordô além de 3 por tela.** A direção nova não afrouxa 2.1 — pelo
  contrário, os cartões de indicador pintam só o número, nunca o fundo,
  justamente porque são quatro por linha.

### 15.5 Referências que orientaram, e o que NÃO se copiou

O Thiago trouxe três telas: **Linear** (menu, transições, sensação de
velocidade), **Attio** (estrutura de CRM, organização dos módulos) e **Vercel**
(gráficos, indicadores, leitura operacional). O pedido foi textual: *"não quero
que copie nada, pegue referências"*.

Do que veio: hover-intent no menu com transição de largura, cartão elevado com
raio maior, número de indicador grande e bold, realce de coluna no gráfico,
pílulas segmentadas de filtro.

Do que **não** veio: o fundo escuro, a paleta fria, o glow neon e o tipo de
letra dessas telas. A paleta de papel e tinta da seção 2 é o que diferencia o
produto de "SaaS genérico" — e é argumento comercial (seção 1), não gosto.

## 16. Fase 3.2 — a Bancada (o balcão)

**Decisão do Thiago, 2026-09-06.** A tela de Atendimento foi revista e
recusada: *"esse visual está com aquele visual do arquivo md design ainda, e
não está me agradando"*. A direção pedida foi a mesma da Fase 3.1, agora para a
tela onde o atendente passa o dia: *"queria ver algo que eu abrisse e chamasse
atenção por ser bonita, coisa inovadora, moderno, mas mantendo ainda mais ou
menos os padrões"*. Pedido explícito de seguir a linguagem que já existe, não
inventar uma terceira: *"use o design do visão geral"*.

Junto veio o limite, e ele é a parte mais importante desta seção: *"O tamanho
da tela das conversas foi uma coisa que eu demorei muito para ajustar...
quando vai para algum monitor pequeno ela ficava pequena demais dentro da
conversa"*.

### 16.1 A regra que governa tudo aqui: orçamento vertical

Nenhuma mudança visual no balcão pode reduzir a altura útil do fio. A conta de
7.1 (1366×768) é contrato, não histórico — e desde 2026-09-06 ela **melhorou**,
não só se manteve: a faixa de indicadores saiu da tela com conversa aberta (os
mesmos números foram para dentro do cartão "Escolha uma conversa", que nunca
compete com o fio por espaço) e os filtros avançados saíram da barra para o
diálogo que já existia, devolvendo altura à lista. Medido em 1366×768: de
~520px de fio antes desta entrega para ~539px depois, com o mesmo número de
mensagens à vista ou mais.

Toda alteração é classificada antes de ser feita:

| Custo 0px — autorizado | Custo > 0px — proibido no balcão |
| --- | --- |
| Contraste entre planos (`bg-branco`/`bg-papel`) | Padding e gap dentro do fio ou da linha de lista |
| Raio (`rounded-full`, `rounded-balao`) | Barra, faixa ou linha nova sempre visível |
| Contraste e cor de borda | Fonte de corpo maior que a tabela de 7.1 |
| Cor de estado (badge sólido vs. diluído) | Elevação que exige respiro em volta |
| Peso e tracking de tipografia | |
| Movimento por opacity/transform | |

Medição de referência: altura do scroller do fio e número de mensagens
visíveis em 1366×768, com o documento inteiro em 0px de rolagem.

### 16.2 A ideia: o painel sobe, o balcão afunda

A Fase 3.1 deu profundidade para cima ao painel — cartão elevado sobre `papel`.
O balcão recebe profundidade para dentro: lista, cabeçalho e caixa de envio em
`bg-branco` (o cromo onde se opera, agora em cartões flutuantes com
`rounded-card`/`shadow-card` a partir de `md`) e o fio em `bg-papel` (o vão
onde se lê a conversa, cercado de branco dos dois lados). É o mesmo recurso da
3.1 com o sinal trocado, custa 0px, e é mais fiel ao "papel não flutua" da
seção 4 do que a própria Fase 3.1 chegou a ser — nada no balcão flutua sobre o
conteúdo; o que flutua é a MOLDURA em volta de dois vãos de leitura.

No celular a moldura não existe (a coluna única já ocupa a tela inteira,
como sempre foi) — o custo dos 16px de `md:gap-2 md:p-2` é zero abaixo de
`md`, e o layout do celular é pixel-idêntico ao de antes desta entrega.

### 16.3 Token novo

| Token | Valor | Uso |
| --- | --- | --- |
| `--raio-balao` | `12px` | Balão do fio e campo de escrita da caixa de envio. Não é `--raio-card` (14px): balão não pertence à família elevada e 14px comprime mensagem curta. |

**Armadilha registrada, para não repetir:** `--raio-card` e `--raio-balao` são
valores de raio fora da escala nativa do Tailwind, e o `tailwind-merge` do
projeto (`lib/utils.ts`) não os reconhecia como parte do grupo "raio de
borda" — sobrescrever o raio de um componente base (ex.: `<Textarea
className="rounded-balao">`) deixava as duas classes coexistindo no HTML
final, e quem vencia era a ordem do CSS compilado, não a ordem do `cn(...)`.
Corrigido registrando os dois tokens em `extendTailwindMerge` — sem isso, todo
uso futuro de `rounded-card`/`rounded-balao` por cima de um componente com
raio próprio (`Input`, `Textarea`, `Select`, `Button`) fica sujeito à mesma
sorte de posição no arquivo gerado.

### 16.4 O que continua proibido no balcão

- **`shadow-card` em linha de lista, campo ou botão de barra.** As únicas duas
  elevações do balcão são o cartão do estado vazio "Escolha uma conversa"
  (nunca coexiste com o fio) e a pílula do separador de dia no fio (flutua
  sobre o vão, não é o balão em si).
- **`shadow-card` no balão de mensagem.** Ele recebeu raio novo
  (`rounded-balao` + canto de cauda), nunca elevação — é conteúdo que rola e
  se repete dezenas de vezes por tela, não um cartão.
- **Animar largura de coluna que tenha o fio rolando dentro.** A exceção de
  14.2 é só do rail, que não tem scroller dentro.
- **Animar balão individualmente.** O fio re-renderiza a cada mensagem
  recebida (tempo real); animar por balão faria o histórico inteiro "pular" a
  cada atualização.
- **Alterar qualquer valor da tabela de 7.1** (gap, padding do balão, tamanho
  de corpo, altura de imagem).
- Tudo o que 15.4 já proíbe (tema escuro, cor fora de token, emoji, teto de
  bordô) vale igual aqui.

### 16.5 Orçamento de bordô no balcão

Com a conversa aberta, o bordô fica reservado a **mensagens não lidas** (badge
sólido na lista, antes diluído) e **Enviar** (agora com o gradiente autorizado
em 15.3, `from-selo to-selo/90`). *"+ Novo atendimento"* desceu de `default`
para `outline` em 2026-09-06 — o botão de criação manual de atendimento não
disputa mais atenção com o que já chegou e não foi lido.

## 17. Fase 3.3 — as telas de administração

**Revisto em 2026-09-08.** Equipe (`/equipe`) e a tela nova de permissões
(`/equipe/:id/permissoes`) migraram de Fase 1 para **Fase 3.1** — são telas de
gestão esporádica, lidas em monitor grande pelo dono/administrador, não o
balcão de trabalho contínuo que a seção 16 protege. O orçamento vertical de
16.1 não se aplica aqui: nenhuma delas tem um fio de mensagens rolando por
baixo, e o mesmo tratamento de Visão Geral/Indicadores (`rounded-card`,
`shadow-card`, `grade-com-vida`, `hover:-translate-y-0.5`, `font-dado` nos
números) se aplica sem ressalva.

### 17.1 Tela de detalhe fora do menu

`/equipe/:id/permissoes` segue o mesmo padrão já estabelecido por
`/indicadores/:secao` (seção 14.7): rota própria, fora de `app/navegacao.ts`
(não é item de rail nem de folha "Mais" — chega-se a ela clicando no chip do
modelo de permissão, na linha do atendente), com a permissão de acesso lida
da MESMA entrada que protege a tela-mãe (`equipe.ver`) para nunca haver duas
verdades. F5 preserva a tela, e o link é compartilhável.

### 17.2 Orçamento de bordô

Três usos, e só três: o botão **"Novo atendente"** (`from-selo to-selo/90`,
mesmo gradiente autorizado em 15.3), o número do cartão **"Sem 2 etapas"**
quando maior que zero (a etiqueta correspondente na linha do atendente reusa
o mesmo tom), e o botão **"Resetar MFA"** dentro do diálogo de segurança — que
nunca coexiste na tela com os outros dois, por estar num diálogo modal.

### 17.3 Permissão que esmaece, não desaparece

Na tela de permissões, desmarcar a tela de um bloco (a chave `*.ver`) esmaece
— `opacity-45`, não `hidden` — as demais ações daquele bloco, com a nota "Sem
acesso à tela, esta ação não faz efeito". Esconder o item removeria a
possibilidade de ver (e desfazer) uma combinação que já existia; esmaecer
avisa sem apagar o estado.
