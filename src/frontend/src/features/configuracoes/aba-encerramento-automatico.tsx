import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, Clock, Hourglass, MessageSquareOff, Sunrise } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Painel } from '@/components/ui/painel'
import { Skeleton } from '@/components/ui/skeleton'
import { Switch } from '@/components/ui/switch'
import { CartaoDeIndicador } from '@/features/indicadores/cartao-de-indicador'
import { serventiaApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { cn } from '@/lib/utils'
import type { EncerramentoAutomatico } from '@/lib/api/tipos'

/**
 * Configura o encerramento automático de conversa parada.
 *
 * O problema que isto resolve: conversa que ninguém encerra fica aberta para
 * sempre, e o cliente que volta dias depois cai nela — sem boas-vindas, sem
 * triagem, sem menu. Conversa encerrada faz o próximo contato abrir atendimento
 * novo, do zero.
 *
 * Presets primeiro, campos depois: um par de campos numéricos sozinho convida a
 * digitar "5" sem perceber o que isso significa para quem recebe a mensagem. Os
 * campos continuam ali para quem sabe o que quer — inclusive para testar o
 * recurso em dez minutos.
 *
 * Reescrita na Fase 3.3 (2026-09-07). O formulário vivia sozinho num
 * `max-w-2xl` e sobrava metade da tela vazia; a largura devolvida virou a
 * LINHA DO TEMPO ao lado, que responde em imagem a pergunta que o par de
 * campos numéricos deixava no ar: "o que o cliente vê, e quando". Ela reage a
 * cada tecla — não é ilustração, é o próprio valor configurado, escrito por
 * extenso.
 */

const MINUTOS_MINIMOS = 5
const MINUTOS_MAXIMOS = 43_200

interface Preset {
  rotulo: string
  descricao: string
  perguntar: number
  encerrar: number
}

const PRESETS: Preset[] = [
  {
    rotulo: 'Rápido',
    descricao: 'Fecha o dia limpo. Quem volta amanhã cai no atendimento automático.',
    perguntar: 20,
    encerrar: 30,
  },
  {
    rotulo: 'Paciente',
    descricao: 'Dá meio turno de folga antes de perguntar qualquer coisa.',
    perguntar: 360,
    encerrar: 180,
  },
  {
    rotulo: 'Teste',
    descricao: 'Só para conferir o funcionamento: o ciclo inteiro roda em ~10 minutos.',
    perguntar: 5,
    encerrar: 5,
  },
]

/** "90" vira "1h30", "1440" vira "1 dia" — minuto cru não diz nada a ninguém. */
function descreverMinutos(minutos: number): string {
  if (minutos < 60) return `${minutos} min`

  const horas = Math.floor(minutos / 60)
  const resto = minutos % 60

  if (horas < 24) return resto === 0 ? `${horas}h` : `${horas}h${String(resto).padStart(2, '0')}`

  const dias = Math.floor(horas / 24)
  const horasRestantes = horas % 24

  return horasRestantes === 0
    ? `${dias} ${dias === 1 ? 'dia' : 'dias'}`
    : `${dias}d ${horasRestantes}h`
}

/** "8" vira "8h" — hora cheia, que é o único formato que o campo aceita. */
function descreverHora(hora: number): string {
  return `${hora}h`
}

export function AbaEncerramentoAutomatico() {
  const clienteDeConsultas = useQueryClient()
  const [salvando, setSalvando] = useState(false)

  const { data, isLoading } = useQuery({
    queryKey: ['encerramento-automatico'],
    queryFn: () => serventiaApi.encerramentoAutomatico(),
  })

  // Campos como texto: um <input type="number"> controlado por número não deixa
  // apagar o conteúdo para digitar outro — a cada tecla o valor volta.
  const [ligado, setLigado] = useState(false)
  const [perguntar, setPerguntar] = useState('')
  const [encerrar, setEncerrar] = useState('')
  const [comJanela, setComJanela] = useState(false)
  const [horaInicial, setHoraInicial] = useState('')
  const [horaFinal, setHoraFinal] = useState('')

  useEffect(() => {
    if (!data) return

    setLigado(data.minutosParaPerguntar !== null)
    setPerguntar(data.minutosParaPerguntar?.toString() ?? '20')
    setEncerrar(data.minutosParaEncerrar?.toString() ?? '30')
    setComJanela(data.horaInicial !== null)
    setHoraInicial(data.horaInicial?.toString() ?? '8')
    setHoraFinal(data.horaFinal?.toString() ?? '18')
  }, [data])

  function aplicarPreset(preset: Preset) {
    setLigado(true)
    setPerguntar(preset.perguntar.toString())
    setEncerrar(preset.encerrar.toString())
  }

  const perguntarNumero = Number(perguntar)
  const encerrarNumero = Number(encerrar)
  const horaInicialNumero = Number(horaInicial)
  const horaFinalNumero = Number(horaFinal)

  function validar(): string | null {
    if (!ligado) return null

    for (const [valor, qual] of [
      [perguntarNumero, 'para perguntar'],
      [encerrarNumero, 'para encerrar'],
    ] as const) {
      if (!Number.isInteger(valor) || valor < MINUTOS_MINIMOS || valor > MINUTOS_MAXIMOS) {
        return `O tempo ${qual} precisa ficar entre ${MINUTOS_MINIMOS} minutos e 30 dias.`
      }
    }

    if (comJanela) {
      for (const valor of [horaInicialNumero, horaFinalNumero]) {
        if (!Number.isInteger(valor) || valor < 0 || valor > 23) {
          return 'A hora precisa ficar entre 0 e 23.'
        }
      }

      if (horaInicialNumero === horaFinalNumero) {
        return 'A hora inicial e a final não podem ser iguais — não sobraria horário nenhum.'
      }
    }

    return null
  }

  const erro = validar()

  /**
   * O botão só acende quando há mudança de verdade. Antes ele ficava sempre
   * ativo, e "Salvar" num formulário intocado é a interação que ensina a
   * ignorar o botão — além de gastar uma requisição para gravar o que já
   * estava gravado.
   */
  const alterado =
    data === undefined
      ? false
      : ligado !== (data.minutosParaPerguntar !== null) ||
        (ligado &&
          (perguntarNumero !== data.minutosParaPerguntar ||
            encerrarNumero !== data.minutosParaEncerrar ||
            comJanela !== (data.horaInicial !== null) ||
            (comJanela &&
              (horaInicialNumero !== data.horaInicial || horaFinalNumero !== data.horaFinal))))

  async function salvar() {
    if (erro || salvando || !alterado) return

    setSalvando(true)

    const corpo: EncerramentoAutomatico = ligado
      ? {
          minutosParaPerguntar: perguntarNumero,
          minutosParaEncerrar: encerrarNumero,
          horaInicial: comJanela ? horaInicialNumero : null,
          horaFinal: comJanela ? horaFinalNumero : null,
        }
      : { minutosParaPerguntar: null, minutosParaEncerrar: null, horaInicial: null, horaFinal: null }

    try {
      await serventiaApi.definirEncerramentoAutomatico(corpo)
      void clienteDeConsultas.invalidateQueries({ queryKey: ['encerramento-automatico'] })
      toast.success(
        ligado ? 'Encerramento automático atualizado.' : 'Encerramento automático desligado.',
      )
    } catch (falha) {
      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível salvar.')
    } finally {
      setSalvando(false)
    }
  }

  if (isLoading) {
    return (
      <div className="flex flex-col gap-4">
        <div className="grid grid-cols-2 gap-3 @3xl:grid-cols-4">
          {[0, 1, 2, 3].map((i) => (
            <Skeleton key={i} className="h-[104px] rounded-card" />
          ))}
        </div>
        <div className="grid gap-4 @4xl:grid-cols-[minmax(0,1fr)_24rem]">
          <Skeleton className="h-80 rounded-card" />
          <Skeleton className="h-80 rounded-card" />
        </div>
      </div>
    )
  }

  const presetEmUso = PRESETS.find(
    (p) => ligado && p.perguntar === perguntarNumero && p.encerrar === encerrarNumero,
  )

  const validoParaPrevia = ligado && erro === null

  return (
    <div className="flex flex-col gap-4">
      <p className="max-w-3xl text-sm text-tinta-media">
        Conversa que ninguém encerra fica aberta para sempre — e o cliente que volta dias depois
        cai nela, sem passar pelo atendimento automático. Com isto ligado, o sistema{' '}
        <strong className="text-tinta">pergunta antes de encerrar</strong> e só fecha se não houver
        resposta. Quem voltar depois começa um atendimento novo, com protocolo novo.
      </p>

      <div className="grade-com-vida grid grid-cols-2 gap-3 @3xl:grid-cols-4">
        <CartaoDeIndicador
          rotulo="Silêncio"
          valor={validoParaPrevia ? descreverMinutos(perguntarNumero) : '—'}
          detalhe={validoParaPrevia ? 'Antes de perguntar' : 'Encerramento desligado'}
          icone={Hourglass}
        />
        <CartaoDeIndicador
          rotulo="Sem resposta"
          valor={validoParaPrevia ? descreverMinutos(encerrarNumero) : '—'}
          detalhe={validoParaPrevia ? 'Antes de encerrar' : 'Encerramento desligado'}
          icone={MessageSquareOff}
        />
        <CartaoDeIndicador
          rotulo="Ciclo total"
          valor={validoParaPrevia ? descreverMinutos(perguntarNumero + encerrarNumero) : '—'}
          detalhe={validoParaPrevia ? 'Do último "oi" ao fim' : 'Conversa fica aberta'}
          icone={Clock}
          cor={validoParaPrevia ? 'verde' : 'tinta'}
        />
        <CartaoDeIndicador
          rotulo="Janela"
          valor={
            validoParaPrevia && comJanela
              ? `${descreverHora(horaInicialNumero)}–${descreverHora(horaFinalNumero)}`
              : validoParaPrevia
                ? '24h'
                : '—'
          }
          detalhe={
            validoParaPrevia && comJanela
              ? 'Fora disso, espera amanhecer'
              : validoParaPrevia
                ? 'Pode perguntar de madrugada'
                : 'Encerramento desligado'
          }
          icone={Sunrise}
          cor={validoParaPrevia && !comJanela ? 'ambar' : 'tinta'}
        />
      </div>

      <div className="grid gap-4 @4xl:grid-cols-[minmax(0,1fr)_24rem]">
        <div className="flex min-w-0 flex-col gap-4">
          <Painel>
            <div className="flex items-center justify-between gap-3">
              <div className="flex min-w-0 flex-col">
                <Label htmlFor="encerramento-ligado" className="text-sm font-semibold text-tinta">
                  Encerrar conversas paradas
                </Label>
                <span className="text-xs text-tinta-media">
                  {ligado
                    ? 'O sistema pergunta antes de fechar, e só fecha sem resposta.'
                    : 'Desligado. Nenhuma conversa é encerrada automaticamente.'}
                </span>
              </div>

              <Switch id="encerramento-ligado" checked={ligado} onCheckedChange={setLigado} />
            </div>
          </Painel>

          {ligado ? (
            <>
              <Painel
                titulo="Ritmo"
                descricao="Escolha um ponto de partida e ajuste nos campos abaixo, se precisar."
              >
                <div className="grid gap-2.5 @xl:grid-cols-3">
                  {PRESETS.map((preset) => {
                    const emUso = presetEmUso?.rotulo === preset.rotulo

                    return (
                      <button
                        key={preset.rotulo}
                        type="button"
                        aria-pressed={emUso}
                        onClick={() => aplicarPreset(preset)}
                        className={cn(
                          'flex flex-col gap-1 rounded-card border bg-branco p-3 text-left shadow-card transition-all duration-200 hover:-translate-y-0.5 hover:shadow-card-hover',
                          emUso ? 'border-tinta' : 'border-linha',
                        )}
                      >
                        <span className="flex items-center gap-1.5">
                          <span className="text-[13px] font-semibold text-tinta">
                            {preset.rotulo}
                          </span>
                          {emUso ? (
                            <Check className="size-3.5 shrink-0 text-verde" aria-label="Em uso" />
                          ) : null}
                        </span>
                        <span className="font-dado text-[11px] text-tinta-fraca">
                          {descreverMinutos(preset.perguntar)} · {descreverMinutos(preset.encerrar)}
                        </span>
                        <span className="text-xs leading-snug text-tinta-media">
                          {preset.descricao}
                        </span>
                      </button>
                    )
                  })}
                </div>

                <div className="mt-4 grid gap-3 @xl:grid-cols-2">
                  <div className="flex flex-col gap-1.5">
                    <Label htmlFor="minutos-perguntar">Perguntar após (minutos de silêncio)</Label>
                    <Input
                      id="minutos-perguntar"
                      inputMode="numeric"
                      value={perguntar}
                      onChange={(evento) => setPerguntar(evento.target.value)}
                    />
                    <span className="text-[11px] text-tinta-fraca">
                      {perguntarNumero >= MINUTOS_MINIMOS
                        ? `Equivale a ${descreverMinutos(perguntarNumero)}.`
                        : 'Mínimo de 5 minutos.'}
                    </span>
                  </div>

                  <div className="flex flex-col gap-1.5">
                    <Label htmlFor="minutos-encerrar">Encerrar após (minutos sem resposta)</Label>
                    <Input
                      id="minutos-encerrar"
                      inputMode="numeric"
                      value={encerrar}
                      onChange={(evento) => setEncerrar(evento.target.value)}
                    />
                    <span className="text-[11px] text-tinta-fraca">
                      {encerrarNumero >= MINUTOS_MINIMOS
                        ? `Equivale a ${descreverMinutos(encerrarNumero)}.`
                        : 'Mínimo de 5 minutos.'}
                    </span>
                  </div>
                </div>
              </Painel>

              <Painel>
                <div className="flex items-center justify-between gap-3">
                  <div className="flex min-w-0 flex-col">
                    <Label htmlFor="com-janela" className="text-sm font-semibold text-tinta">
                      Só perguntar em horário comercial
                    </Label>
                    <span className="text-xs text-tinta-media">
                      Sem isto, uma conversa que parou à noite pode receber a pergunta de
                      madrugada. O encerramento em si acontece a qualquer hora.
                    </span>
                  </div>

                  {/* `tom="tinta"`: sub-interruptor dentro do escopo do de cima.
                      Com o item ativo do rail, o switch mestre e o botão Salvar,
                      um quarto bordô aqui estouraria o teto de 3 (DESIGN.md 2.1,
                      17.4). */}
                  <Switch id="com-janela" tom="tinta" checked={comJanela} onCheckedChange={setComJanela} />
                </div>

                {comJanela ? (
                  <div className="mt-3 grid max-w-xs gap-3 sm:grid-cols-2">
                    <div className="flex flex-col gap-1.5">
                      <Label htmlFor="hora-inicial">Das (hora)</Label>
                      <Input
                        id="hora-inicial"
                        inputMode="numeric"
                        value={horaInicial}
                        onChange={(evento) => setHoraInicial(evento.target.value)}
                      />
                    </div>

                    <div className="flex flex-col gap-1.5">
                      <Label htmlFor="hora-final">Até (hora)</Label>
                      <Input
                        id="hora-final"
                        inputMode="numeric"
                        value={horaFinal}
                        onChange={(evento) => setHoraFinal(evento.target.value)}
                      />
                    </div>
                  </div>
                ) : null}
              </Painel>
            </>
          ) : null}

          {erro ? (
            <p role="alert" className="text-xs text-erro">
              {erro}
            </p>
          ) : null}

          <div className="flex items-center gap-3">
            <Button
              type="button"
              onClick={() => void salvar()}
              disabled={salvando || erro !== null || !alterado}
            >
              {salvando ? 'Salvando…' : 'Salvar'}
            </Button>
            {alterado && !erro ? (
              <span className="text-xs text-tinta-media">Há alterações não salvas.</span>
            ) : null}
          </div>
        </div>

        <LinhaDoTempo
          ligado={validoParaPrevia}
          perguntar={perguntarNumero}
          encerrar={encerrarNumero}
          comJanela={comJanela}
          horaInicial={horaInicialNumero}
          horaFinal={horaFinalNumero}
        />
      </div>
    </div>
  )
}

/**
 * O que o cliente vê, na ordem em que vê. Reage a cada tecla dos campos ao
 * lado: é o valor configurado escrito por extenso, não uma ilustração fixa.
 */
function LinhaDoTempo({
  ligado,
  perguntar,
  encerrar,
  comJanela,
  horaInicial,
  horaFinal,
}: {
  ligado: boolean
  perguntar: number
  encerrar: number
  comJanela: boolean
  horaInicial: number
  horaFinal: number
}) {
  return (
    // O sticky mora no INVÓLUCRO, não no painel: o painel é o item da grade
    // e já vem esticado à altura da linha — grudar nele não teria efeito.
    <div className="@4xl:sticky @4xl:top-0">
      <Painel
        titulo="Como o cliente vê"
        descricao="A partir da última mensagem dele, sem resposta de ninguém."
      >
        {!ligado ? (
          <p className="rounded-md border border-dashed border-linha-forte bg-superficie px-3 py-6 text-center text-xs text-tinta-media">
            Com o encerramento desligado, a conversa fica aberta indefinidamente — e o cliente que
            voltar semanas depois cai nela, sem passar pelo atendimento automático.
          </p>
        ) : (
          <ol className="flex flex-col">
            <PassoDaLinha numero={1} titulo="Cliente para de responder">
              O relógio começa a contar a partir da última mensagem da conversa.
            </PassoDaLinha>

            <PassoDaLinha
              numero={2}
              titulo="“Posso encerrar?”"
              selo={`depois de ${descreverMinutos(perguntar)}`}
            >
              O sistema manda a pergunta com o menu numerado — é por “1” e “2” que ele entende a
              resposta.
              {comJanela ? (
                <>
                  {' '}
                  Se o prazo cair fora do intervalo de {descreverHora(horaInicial)} às{' '}
                  {descreverHora(horaFinal)}, a pergunta espera o horário abrir.
                </>
              ) : null}
            </PassoDaLinha>

            <PassoDaLinha numero={3} titulo="Se o cliente responder" desvio>
              A conversa continua normalmente e a contagem zera. Nada é encerrado.
            </PassoDaLinha>

            <PassoDaLinha
              numero={4}
              titulo="Conversa encerrada"
              selo={`mais ${descreverMinutos(encerrar)} sem resposta`}
              ultimo
            >
              O cliente recebe o aviso de encerramento. O próximo contato dele abre um atendimento
              novo, com protocolo novo.
            </PassoDaLinha>
          </ol>
        )}

        <p className="mt-4 border-t border-linha pt-3 text-[11px] leading-snug text-tinta-fraca">
          O texto das duas mensagens fica em{' '}
          <Link
            to="/automacao/mensagens/automaticas"
            className="font-semibold text-tinta underline-offset-2 hover:underline"
          >
            Automáticas
          </Link>
          , na trilha “Conversa parada”.
        </p>
      </Painel>
    </div>
  )
}

function PassoDaLinha({
  numero,
  titulo,
  selo,
  desvio,
  ultimo,
  children,
}: {
  numero: number
  titulo: string
  /** O tempo decorrido até este passo — em mono, é medida. */
  selo?: string
  /** Ramo que sai da linha principal: acontece, e o resto não acontece. */
  desvio?: boolean
  ultimo?: boolean
  children: ReactNode
}) {
  return (
    <li className={cn('relative flex gap-3', ultimo ? 'pb-0' : 'pb-4')}>
      {/* O fio que liga um passo ao seguinte. `aria-hidden` porque a ordem já
          é dita pelo <ol> e pelo número — o traço é redundância visual. */}
      {ultimo ? null : (
        <span
          aria-hidden
          className={cn(
            'absolute bottom-0 left-[11px] top-6 w-px',
            desvio ? 'border-l border-dashed border-linha-forte' : 'bg-linha',
          )}
        />
      )}

      <span
        aria-hidden
        className={cn(
          'relative flex size-6 shrink-0 items-center justify-center rounded-full border bg-branco font-dado text-[10px] font-semibold',
          desvio ? 'border-dashed border-linha-forte text-tinta-fraca' : 'border-linha text-tinta-media',
        )}
      >
        {numero}
      </span>

      <div className="min-w-0 flex-1 pt-0.5">
        <div className="flex flex-wrap items-baseline gap-x-2">
          <span className="text-[13px] font-semibold leading-tight text-tinta">{titulo}</span>
          {selo ? (
            <span className="font-dado text-[10px] text-tinta-fraca">{selo}</span>
          ) : null}
        </div>
        <p className="mt-0.5 text-[11px] leading-relaxed text-tinta-media">{children}</p>
      </div>
    </li>
  )
}
