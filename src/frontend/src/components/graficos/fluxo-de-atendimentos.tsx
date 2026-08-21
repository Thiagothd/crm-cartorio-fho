import { useMemo, useState, type CSSProperties } from 'react'
import {
  formatarDataHora,
  formatarDiaCurto,
  formatarDuracaoMinutos,
  formatarHoraDoDia,
  formatarMesCurto,
  formatarMesExtenso,
  formatarNumero,
} from '@/lib/formatacao'
import { cn } from '@/lib/utils'
import type { PontoDoFluxo } from '@/lib/api/tipos'

/**
 * Escala do eixo de tempo. `mes` foi acrescentada quando a tela de Relatórios
 * passou a mostrar 12 meses — o gráfico é o mesmo, só o rótulo do eixo e o
 * título da dica mudam de formato.
 */
export type Granularidade = 'hora' | 'dia' | 'mes'

/**
 * Escala do eixo com teto "redondo": 8 vira 0-2-4-6-8, 25 vira 0-10-20-30.
 * Sem isto o topo do gráfico é o próprio pico e os rótulos saem quebrados
 * (0, 3.7, 7.3, 11) — número quebrado em contagem de atendimento é ruído, o
 * dado é inteiro por natureza. Passo restrito a 1/2/5/10 × magnitude
 * justamente para nenhuma marca cair em fração.
 */
function escalaDoEixo(maximo: number): { teto: number; marcas: number[] } {
  const cru = Math.max(maximo, 1) / 4
  const magnitude = 10 ** Math.floor(Math.log10(Math.max(cru, 1)))
  const passo = [1, 2, 5, 10].map((m) => m * magnitude).find((p) => p >= cru) ?? 10 * magnitude
  const teto = Math.max(passo, Math.ceil(maximo / passo) * passo)

  const marcas: number[] = []
  for (let valor = 0; valor <= teto + 1e-9; valor += passo) marcas.push(Math.round(valor))

  return { teto, marcas }
}

/**
 * Interpolação cúbica monotônica (Fritsch-Carlson, o `curveMonotoneX` do
 * d3-shape). Spline comum "estoura" acima do pico ou abaixo de zero entre dois
 * pontos vizinhos — numa curva de estoque isso desenharia fila negativa.
 * Monotônica garante que a curva nunca sai do intervalo dos dois pontos que
 * conecta.
 */
function caminhoSuave(pontos: readonly { x: number; y: number }[]): string {
  const n = pontos.length
  if (n === 0) return ''
  if (n === 1) return `M ${pontos[0].x},${pontos[0].y}`

  const secantes: number[] = []
  for (let i = 0; i < n - 1; i++) {
    const dx = pontos[i + 1].x - pontos[i].x
    secantes.push(dx === 0 ? 0 : (pontos[i + 1].y - pontos[i].y) / dx)
  }

  const m = new Array<number>(n).fill(0)
  m[0] = secantes[0]
  m[n - 1] = secantes[n - 2]
  for (let i = 1; i < n - 1; i++) {
    m[i] = secantes[i - 1] * secantes[i] <= 0 ? 0 : (secantes[i - 1] + secantes[i]) / 2
  }

  for (let i = 0; i < n - 1; i++) {
    if (secantes[i] === 0) {
      m[i] = 0
      m[i + 1] = 0
      continue
    }
    const a = m[i] / secantes[i]
    const b = m[i + 1] / secantes[i]
    const h = Math.hypot(a, b)
    if (h > 3) {
      const t = 3 / h
      m[i] = t * a * secantes[i]
      m[i + 1] = t * b * secantes[i]
    }
  }

  let d = `M ${pontos[0].x},${pontos[0].y}`
  for (let i = 0; i < n - 1; i++) {
    const passo = (pontos[i + 1].x - pontos[i].x) / 3
    d += ` C ${pontos[i].x + passo},${pontos[i].y + m[i] * passo} ${pontos[i + 1].x - passo},${
      pontos[i + 1].y - m[i + 1] * passo
    } ${pontos[i + 1].x},${pontos[i + 1].y}`
  }

  return d
}

/**
 * Fluxo de atendimentos — o gráfico principal da Visão Geral.
 *
 * BARRAS + LINHA, e cada forma carrega um tipo de grandeza diferente:
 *
 * - As barras são FLUXO: quanto entrou e quanto saiu naquele dia. Contagem
 *   diária é discreta e esparsa (num cartório a maioria dos dias é zero), então
 *   barra é a forma certa — uma linha ligando essas contagens afirmaria que
 *   existiu meio atendimento na terça.
 * - A linha é ESTOQUE: quantas seguiam abertas ao fim do dia. Estoque é
 *   definido em TODO dia (mesmo dia sem movimento tem um saldo), então a curva é
 *   contínua por natureza, sem um único ponto interpolado.
 *
 * Juntas respondem o que nenhuma responde sozinha: **a serventia está dando
 * conta, ou está acumulando trabalho?** Duas barras altas lado a lado podem
 * significar "movimentou muito e empatou" ou "entrou o dobro do que saiu" — é a
 * linha que desempata, e é ela que mostra a fila subindo dia após dia mesmo
 * quando cada dia isolado parece normal.
 *
 * UM EIXO SÓ, à esquerda: fluxo e estoque estão na MESMA unidade
 * (atendimentos), então dois eixos seriam mentira sobre haver duas escalas. É
 * também o que permite ler a distância vertical entre a linha e as barras como
 * informação real.
 *
 * (Duas tentativas anteriores de linha foram descartadas: média móvel dos
 * próprios abertos — derivável de olho da própria série, não acrescentava nada;
 * e mediana do tempo de primeira resposta — dado valioso, mas indefinido nos
 * dias sem resposta, o que quebrava a linha em pedaços. O tempo de resposta
 * continua no gráfico, na dica de hover, onde não precisa de continuidade.)
 *
 * HTML/flex para barras e pontos, SVG só para a curva. O SVG usa
 * `preserveAspectRatio="none"`, que estica a geometria de forma desigual — bom
 * para a curva (com `vector-effect="non-scaling-stroke"` protegendo a
 * espessura), e inutilizável para qualquer coisa que precise ser redonda: um
 * `<circle>` ali viraria elipse achatada. Por isso os marcadores são `<span>`
 * posicionados em porcentagem.
 */
export function FluxoDeAtendimentos({
  pontos,
  granularidade,
}: {
  pontos: PontoDoFluxo[]
  granularidade: Granularidade
}) {
  const [indiceAtivo, setIndiceAtivo] = useState<number | null>(null)

  const semDados = pontos.every((p) => p.abertos === 0 && p.encerrados === 0 && p.emAberto === 0)

  // Teto compartilhado: o maior entre o pico de fluxo e o pico de estoque. Sem
  // isso a curva de estoque (que costuma ser maior que o movimento de um dia)
  // sairia por cima do topo do gráfico.
  const { teto, marcas } = useMemo(
    () =>
      escalaDoEixo(
        Math.max(0, ...pontos.map((p) => Math.max(p.abertos, p.encerrados, p.emAberto))),
      ),
    [pontos],
  )

  const n = pontos.length

  /** Centro da coluna `i`, em porcentagem — alinha curva e pontos com as barras. */
  const xDaColuna = (i: number) => (n <= 1 ? 50 : ((i + 0.5) / n) * 100)

  /** Altura do valor, em porcentagem a partir da base. */
  const yDoValor = (valor: number) => (valor / teto) * 100

  const caminho = useMemo(
    () => caminhoSuave(pontos.map((p, i) => ({ x: xDaColuna(i), y: 100 - yDoValor(p.emAberto) }))),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [pontos, teto, n],
  )

  // Com muitas colunas, um marcador por dia vira miçanga. Acima de 20 pontos só
  // os dias rotulados no eixo recebem marcador — a curva continua inteira.
  const passoDoRotulo = Math.max(1, Math.ceil(n / 7))
  const passoDoMarcador = n > 20 ? passoDoRotulo : 1

  /**
   * O último ponto é SEMPRE rotulado (é o dia/mês corrente, o mais consultado),
   * e por isso o rótulo do passo que cai vizinho a ele é suprimido: com n=12 o
   * passo é 2, o que colocava rótulo em 10 e em 11 a uma coluna de distância
   * (~21px) para textos de ~33px — os dois se sobrepunham exatamente na cauda
   * que mais importa. Exigir 2 colunas de folga resolve sem mexer no resto.
   */
  function ehRotulado(i: number) {
    if (i === n - 1) return true
    return i % passoDoRotulo === 0 && n - 1 - i >= 2
  }

  function rotuloDoPonto(ponto: PontoDoFluxo) {
    const data = new Date(ponto.inicio)

    if (granularidade === 'hora') return formatarHoraDoDia(data.getHours())
    if (granularidade === 'mes') return formatarMesCurto(data)
    return formatarDiaCurto(data)
  }

  function moverAtivo(delta: number) {
    setIndiceAtivo((atual) => {
      const base = atual ?? (delta > 0 ? -1 : n)
      return Math.min(n - 1, Math.max(0, base + delta))
    })
  }

  if (semDados) {
    return (
      <div className="flex flex-col gap-4">
        <Legenda granularidade={granularidade} />
        <div className="flex h-64 items-center justify-center text-sm text-tinta-fraca">
          Sem atendimentos neste período.
        </div>
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-4">
      <Legenda granularidade={granularidade} />

      <div
        className="flex gap-2.5 outline-none"
        role="group"
        aria-label="Fluxo de atendimentos: abertos, encerrados e total em aberto ao longo do período"
        tabIndex={0}
        onKeyDown={(evento) => {
          if (evento.key === 'ArrowRight') {
            evento.preventDefault()
            moverAtivo(1)
          } else if (evento.key === 'ArrowLeft') {
            evento.preventDefault()
            moverAtivo(-1)
          } else if (evento.key === 'Escape') {
            setIndiceAtivo(null)
          }
        }}
        onPointerLeave={() => setIndiceAtivo(null)}
      >
        <div className="flex h-64 w-7 shrink-0 flex-col-reverse justify-between pb-6 text-right">
          {marcas.map((marca) => (
            <span key={marca} className="font-dado text-[10px] leading-none text-tinta-fraca">
              {formatarNumero(marca)}
            </span>
          ))}
        </div>

        <div className="relative min-w-0 flex-1">
          {/* Grade atrás de tudo. `pb-6` reserva a faixa dos rótulos do eixo X,
              para a linha do zero coincidir com a base das barras. */}
          <div aria-hidden className="absolute inset-x-0 top-0 flex h-64 flex-col-reverse justify-between pb-6">
            {marcas.map((marca, i) => (
              <div key={marca} className={cn('h-px w-full', i === 0 ? 'bg-linha-forte' : 'bg-linha')} />
            ))}
          </div>

          <div className="relative flex h-64 items-stretch gap-px">
            {pontos.map((ponto, i) => {
              const ativo = indiceAtivo === i
              const rotulado = ehRotulado(i)

              return (
                <div
                  key={ponto.inicio}
                  className="group relative min-w-0 flex-1"
                  onPointerEnter={() => setIndiceAtivo(i)}
                  // Toque também seleciona a coluna: sem isto, num celular a
                  // dica só existia para quem tem mouse.
                  onPointerDown={() => setIndiceAtivo(i)}
                >
                  {/* Realce da coluna inteira, atrás das barras — o alvo de
                      hover é a coluna toda, inclusive o vazio acima da barra,
                      então dia zerado também responde ao mouse. */}
                  <div
                    aria-hidden
                    className={cn(
                      'pointer-events-none absolute inset-x-0 bottom-6 top-0 rounded-sm transition-colors duration-150',
                      ativo && 'bg-superficie',
                    )}
                  />

                  {/* `absolute inset` dá a esta caixa uma altura DEFINIDA (a da
                      coluna, menos a faixa de rótulos). Sem isso as barras não
                      apareciam: `height: X%` num filho de pai com altura
                      automática é referência circular, o navegador resolve como
                      zero e o gráfico saía só com eixo e grade. */}
                  <div className="absolute inset-x-0 bottom-6 top-0 flex items-end justify-center gap-px sm:gap-[3px]">
                    <Barra
                      valor={ponto.abertos}
                      teto={teto}
                      indice={i}
                      classeDeCor="bg-gradient-to-t from-tinta to-tinta/70"
                      ativo={ativo}
                    />
                    <Barra
                      valor={ponto.encerrados}
                      teto={teto}
                      indice={i}
                      classeDeCor="bg-gradient-to-t from-verde to-verde/70"
                      ativo={ativo}
                    />
                  </div>

                  {rotulado ? (
                    <span className="absolute bottom-0 left-1/2 -translate-x-1/2 whitespace-nowrap text-[10px] font-medium leading-6 text-tinta-fraca">
                      {rotuloDoPonto(ponto)}
                    </span>
                  ) : null}
                </div>
              )
            })}

            {/* A curva vive num wrapper com as bordas fixadas, e é o WRAPPER que
                define a altura — não o `<svg>`. Pôr `inset-x-0 bottom-6 top-0`
                no próprio SVG sobre-restringe o layout (ele já tem `width` e
                `height` padrão de 100%), o navegador ignora `bottom`/`right` e a
                geometria escapa da área de plotagem — foi por isso que a linha
                não apareceu na primeira tentativa. */}
            <div
              aria-hidden
              className="anima-revelar-linha pointer-events-none absolute inset-x-0 bottom-6 top-0"
            >
              <svg viewBox="0 0 100 100" preserveAspectRatio="none" className="h-full w-full overflow-visible">
                {/* Área sob a curva, bem discreta: dá corpo à linha e reforça
                    que ela é um acumulado, não uma medição pontual. */}
                <path
                  d={`${caminho} L 100,100 L 0,100 Z`}
                  fill="var(--cor-tendencia)"
                  fillOpacity={0.07}
                  stroke="none"
                />
                <path
                  d={caminho}
                  fill="none"
                  stroke="var(--cor-tendencia)"
                  strokeWidth={2.5}
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  // Sem isto o `preserveAspectRatio="none"` esticaria a
                  // espessura junto com a geometria e o traco sairia achatado.
                  vectorEffect="non-scaling-stroke"
                />
              </svg>

              {/* Marcadores em HTML, NÃO em <circle>: dentro do viewBox
                  esticado, um círculo SVG vira elipse larga (o eixo X é
                  comprimido ~11x mais que o Y). `span` redondo posicionado em
                  porcentagem é redondo em qualquer largura de tela. */}
              {pontos.map((ponto, i) =>
                i % passoDoMarcador === 0 || i === n - 1 ? (
                  <span
                    key={ponto.inicio}
                    className={cn(
                      'absolute size-[7px] -translate-x-1/2 translate-y-1/2 rounded-full border-2 border-tendencia bg-branco transition-transform duration-150',
                      indiceAtivo === i && 'scale-150',
                    )}
                    style={{ left: `${xDaColuna(i)}%`, bottom: `${yDoValor(ponto.emAberto)}%` }}
                  />
                ) : null,
              )}
            </div>

            {/* A dica é irmã das colunas, não filha: dentro da coluna ela era
                recortada pelo `overflow` do cartão nas pontas. Aqui posiciona
                por porcentagem e ancora pela borda nos extremos. */}
            {indiceAtivo !== null ? (
              <Dica
                ponto={pontos[indiceAtivo]}
                granularidade={granularidade}
                indice={indiceAtivo}
                total={n}
              />
            ) : null}
          </div>
        </div>
      </div>

      <table className="sr-only">
        <caption>Fluxo de atendimentos por período</caption>
        <thead>
          <tr>
            <th scope="col">Período</th>
            <th scope="col">Abertos</th>
            <th scope="col">Encerrados</th>
            <th scope="col">Em aberto no fim</th>
            <th scope="col">Mediana da 1ª resposta humana</th>
          </tr>
        </thead>
        <tbody>
          {pontos.map((ponto) => (
            <tr key={ponto.inicio}>
              <td>{formatarDataHora(ponto.inicio)}</td>
              <td>{ponto.abertos}</td>
              <td>{ponto.encerrados}</td>
              <td>{ponto.emAberto}</td>
              <td>{formatarDuracaoMinutos(ponto.primeiraRespostaMedianaMinutos) ?? 'sem resposta medida'}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function Legenda({ granularidade }: { granularidade: Granularidade }) {
  return (
    <div className="flex flex-wrap items-center gap-x-4 gap-y-1.5 text-[13px] font-semibold text-tinta-media">
      <span className="inline-flex items-center gap-1.5">
        <span aria-hidden className="inline-block h-3 w-2 rounded-sm bg-gradient-to-t from-tinta to-tinta/70" />
        Abertos
      </span>
      <span className="inline-flex items-center gap-1.5">
        <span aria-hidden className="inline-block h-3 w-2 rounded-sm bg-gradient-to-t from-verde to-verde/70" />
        Encerrados
      </span>
      <span className="inline-flex items-center gap-1.5 text-tendencia">
        {/* Traço com ponto no meio: a legenda diz qual é a FORMA da série no
            gráfico, e confundir linha com barra é o erro que uma legenda existe
            para evitar. */}
        <span aria-hidden className="relative inline-block h-[3px] w-5 rounded-full bg-tendencia">
          <span className="absolute left-1/2 top-1/2 size-[7px] -translate-x-1/2 -translate-y-1/2 rounded-full border-2 border-tendencia bg-branco" />
        </span>
        Em aberto no fim do {granularidade === 'mes' ? 'mês' : 'dia'}
      </span>
    </div>
  )
}

/**
 * Barra de uma série num ponto. Valor zero não desenha nada — um "toco" de 2px
 * no dia vazio faria zero parecer "quase um", que é justamente o defeito de
 * representar contagem esparsa por linha.
 */
function Barra({
  valor,
  teto,
  indice,
  classeDeCor,
  ativo,
}: {
  valor: number
  teto: number
  indice: number
  classeDeCor: string
  ativo: boolean
}) {
  if (valor === 0) return <span className="w-full max-w-4 flex-1" />

  return (
    <span
      className={cn(
        'w-full max-w-4 flex-1 origin-bottom rounded-t-[2px] transition-[filter,opacity] duration-150 sm:rounded-t-[5px]',
        classeDeCor,
        // A coluna sob o ponteiro fica em plena saturacao e as vizinhas recuam:
        // realce por contraste, sem trocar a cor de ninguem (trocar a cor faria
        // parecer que o dado mudou de categoria).
        ativo ? 'brightness-110' : 'opacity-90',
      )}
      style={{
        height: `${(valor / teto) * 100}%`,
        // Cresce da base com um leve exagero no fim (`cubic-bezier` com
        // overshoot suave) — e' o que da' a sensacao de "vivo" ao entrar, e e'
        // so transform, composto pela GPU, sem relayout. Teto de 420ms de
        // atraso para um periodo de 31 dias nao demorar a assentar.
        animation: `crm-crescer-barra-vertical 520ms cubic-bezier(0.34, 1.4, 0.64, 1) ${Math.min(indice * 16, 420)}ms both`,
      }}
    />
  )
}

function Dica({
  ponto,
  granularidade,
  indice,
  total,
}: {
  ponto: PontoDoFluxo
  granularidade: Granularidade
  indice: number
  total: number
}) {
  // Nas duas pontas a dica ancora pela borda em vez do centro, senão vaza
  // para fora do cartão e o navegador corta.
  const noInicio = indice <= 1
  const noFim = indice >= total - 2

  const saldo = ponto.abertos - ponto.encerrados
  const tempo = formatarDuracaoMinutos(ponto.primeiraRespostaMedianaMinutos)

  return (
    <div
      className={cn(
        'pointer-events-none absolute bottom-full z-20 mb-2 rounded-card border border-linha bg-branco px-3.5 py-2.5 text-xs shadow-card-hover',
        // CELULAR: a dica ocupa a largura inteira da área de plotagem, ancorada
        // nas duas bordas. Seguir a coluna aqui era bug de verdade: a dica tem
        // 192px de largura mínima e a coluna tem ~8px num período de 30 dias, e
        // a guarda de borda era por ÍNDICE — só funciona quando a coluna é mais
        // larga que meia dica. Tocar numa coluna do meio jogava a dica ~43px
        // fora da viewport, e como o container tem `overflow-y-auto` (o
        // navegador promove o eixo X junto), aparecia barra de rolagem
        // horizontal na página.
        'left-0 right-0',
        // A partir de `sm` há largura para ela seguir a coluna. A posição vai por
        // VARIÁVEL CSS, não por `style` inline: inline não aceita media query, e
        // o `left` precisa valer só a partir de `sm`.
        'sm:w-max sm:min-w-48',
        noInicio
          ? 'sm:left-0 sm:right-auto'
          : noFim
            ? 'sm:left-auto sm:right-0'
            : 'sm:left-[var(--centro-da-dica)] sm:right-auto sm:-translate-x-1/2',
      )}
      style={{ '--centro-da-dica': `${((indice + 0.5) / total) * 100}%` } as CSSProperties}
    >
      <p className="font-bold capitalize text-tinta">
        {granularidade === 'mes' ? formatarMesExtenso(ponto.inicio) : formatarDataHora(ponto.inicio)}
      </p>

      <LinhaDaDica cor="bg-tinta" rotulo="Abertos" valor={formatarNumero(ponto.abertos)} />
      <LinhaDaDica cor="bg-verde" rotulo="Encerrados" valor={formatarNumero(ponto.encerrados)} corDoValor="text-verde" />

      {/* Saldo do período: é a leitura que ninguém faz de cabeça olhando duas
          barras, e é ela que explica a direção da linha. */}
      <p className="mt-1.5 flex items-center justify-between gap-4 text-tinta-media">
        <span className="pl-3">{granularidade === 'mes' ? 'Saldo do mês' : 'Saldo do dia'}</span>
        <span className="font-dado font-bold text-tinta">
          {saldo > 0 ? '+' : ''}
          {formatarNumero(saldo)}
        </span>
      </p>

      <LinhaDaDica
        cor="bg-tendencia"
        rotulo="Em aberto no fim"
        valor={formatarNumero(ponto.emAberto)}
        corDoValor="text-tendencia"
        comSeparador
      />

      {/* Só na escala diária/horária: a série mensal não carrega esta medida, e
          uma linha fixa dizendo "sem medição" em todos os doze meses seria
          ruído — pareceria defeito, não ausência de escopo. */}
      {granularidade !== 'mes' ? (
        <p className="mt-1.5 flex items-center justify-between gap-4 text-tinta-media">
          <span className="pl-3">1ª resposta</span>
          {/* "sem medição" e não "0 min": não houve resposta para medir, e zero
              diria que respondemos instantaneamente. */}
          <span className={cn('font-dado font-bold', tempo ? 'text-tinta' : 'text-tinta-fraca')}>
            {tempo ?? 'sem medição'}
          </span>
        </p>
      ) : null}
    </div>
  )
}

function LinhaDaDica({
  cor,
  rotulo,
  valor,
  corDoValor = 'text-tinta',
  comSeparador = false,
}: {
  cor: string
  rotulo: string
  valor: string
  corDoValor?: string
  comSeparador?: boolean
}) {
  return (
    <p
      className={cn(
        'mt-1.5 flex items-center justify-between gap-4 text-tinta-media',
        comSeparador && 'border-t border-linha pt-1.5',
      )}
    >
      <span className="inline-flex items-center gap-1.5">
        <span aria-hidden className={cn('inline-block size-1.5 rounded-full', cor)} />
        {rotulo}
      </span>
      <span className={cn('font-dado font-bold', corDoValor)}>{valor}</span>
    </p>
  )
}
