# IDENTIDADE VISUAL

> **Status:** direção aprovada pelo cliente. Implementada na Fase 1.
> **Última atualização:** 2026-08-15
>
> **Este documento é a fonte de verdade da interface.** Cor, fonte, raio e
> espessura de borda saem daqui. Valor arbitrário no código é bug, não estilo.

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

Não são preferências. São restrições.

| Proibido | Por quê |
| --- | --- |
| **Gradiente** | Nenhum documento oficial tem gradiente |
| **Sombra** (`box-shadow`) | Profundidade falsa. Papel não flutua |
| **Glow**, `blur` decorativo | Idem |
| **Emoji** na interface | Quebra o registro formal na hora |
| **Cor fora dos tokens** | Se não está na seção 2, não entra |

| Obrigatório | Valor |
| --- | --- |
| Espessura de borda | **1px**. Sempre |
| Raio de canto | **6px** |
| Ícones | Stroke **1.6px**, sem preenchimento |

Separação entre áreas é feita com **linha**, não com sombra. Hierarquia é feita
com **tipografia e espaço**, não com elevação.

Sobre ícones: usamos `lucide-react`, que é stroke por natureza. A espessura vai
no `strokeWidth` global — nunca `fill`.

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
- **Logo.** A serventia não passou identidade gráfica. Hoje o cabeçalho usa o
  nome em Spectral. Se houver logo, substitui.

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

O menu principal usa `tinta` como fundo, `branco` no título e `linha` nos itens.
É a única grande superfície escura da aplicação: separa navegação e conteúdo como
a lombada escura separa a capa das folhas de um livro de registro.

- Item ativo continua em `branco` com texto `selo`; conta como uma ocorrência de
  bordô.
- O botão de três linhas recolhe o menu para 64px. Recolhido, ficam somente os
  ícones; rótulos continuam disponíveis por `title` e texto para leitor de tela.
- Abrir/recolher não muda rota, filtro nem estado da página.
- Não criar uma segunda superfície escura na área de conteúdo: a distinção só
  funciona porque é exclusiva da navegação.

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

### 11.5 Máscara

CPF, CNPJ, CEP e telefone são **IBM Plex Mono** (`font-dado`, seção 3): é dado
conferível caractere a caractere.

A máscara formata enquanto se digita e aceita dígito corrido, colado ou digitado.
Regra de implementação em `lib/mascaras.ts`: **nunca emitir separador à direita**
(`38` vira `(38`, não `(38) `), senão o backspace trava.

---
