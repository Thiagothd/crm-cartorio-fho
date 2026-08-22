import { useEffect, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Check } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Switch } from '@/components/ui/switch'
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

  async function salvar() {
    if (erro || salvando) return

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
        <Skeleton className="h-20 w-full max-w-2xl" />
        <Skeleton className="h-40 w-full max-w-2xl" />
      </div>
    )
  }

  const presetEmUso = PRESETS.find(
    (p) => ligado && p.perguntar === perguntarNumero && p.encerrar === encerrarNumero,
  )

  return (
    <div className="flex max-w-2xl flex-col gap-5">
      <p className="text-sm text-tinta-media">
        Conversa que ninguém encerra fica aberta para sempre — e o cliente que volta dias depois
        cai nela, sem passar pelo atendimento automático. Com isto ligado, o sistema{' '}
        <strong className="text-tinta">pergunta antes de encerrar</strong> e só fecha se não houver
        resposta. Quem voltar depois começa um atendimento novo, com protocolo novo.
      </p>

      <div className="flex items-center justify-between rounded-md border border-linha bg-superficie p-3">
        <div className="flex flex-col">
          <Label htmlFor="encerramento-ligado" className="text-sm font-medium text-tinta">
            Encerrar conversas paradas
          </Label>
          <span className="text-xs text-tinta-media">
            {ligado
              ? `Pergunta após ${descreverMinutos(perguntarNumero || 0)} de silêncio e encerra ${descreverMinutos(encerrarNumero || 0)} depois, sem resposta.`
              : 'Desligado. Nenhuma conversa é encerrada automaticamente.'}
          </span>
        </div>

        <Switch id="encerramento-ligado" checked={ligado} onCheckedChange={setLigado} />
      </div>

      {ligado ? (
        <>
          <div className="flex flex-col gap-2">
            <span className="text-sm font-medium text-tinta">Ritmo</span>

            <div className="grid gap-2 sm:grid-cols-3">
              {PRESETS.map((preset) => {
                const emUso = presetEmUso?.rotulo === preset.rotulo

                return (
                  <button
                    key={preset.rotulo}
                    type="button"
                    onClick={() => aplicarPreset(preset)}
                    className={cn(
                      'flex flex-col gap-1 rounded-md border p-3 text-left transition-colors',
                      emUso ? 'border-tinta' : 'border-linha hover:border-linha-forte',
                    )}
                  >
                    <span className="flex items-center gap-1.5">
                      <span className="text-sm font-medium text-tinta">{preset.rotulo}</span>
                      {emUso ? (
                        <Check className="size-3.5 shrink-0 text-verde" aria-label="Em uso" />
                      ) : null}
                    </span>
                    <span className="text-[11px] text-tinta-fraca">
                      {descreverMinutos(preset.perguntar)} · {descreverMinutos(preset.encerrar)}
                    </span>
                    <span className="text-xs leading-snug text-tinta-media">
                      {preset.descricao}
                    </span>
                  </button>
                )
              })}
            </div>
          </div>

          <div className="grid gap-3 sm:grid-cols-2">
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

          <div className="flex flex-col gap-3 rounded-md border border-linha bg-superficie p-3">
            <div className="flex items-center justify-between">
              <div className="flex flex-col">
                <Label htmlFor="com-janela" className="text-sm font-medium text-tinta">
                  Só perguntar em horário comercial
                </Label>
                <span className="text-xs text-tinta-media">
                  Sem isto, uma conversa que parou à noite pode receber a pergunta de madrugada. O
                  encerramento em si acontece a qualquer hora.
                </span>
              </div>

              <Switch id="com-janela" checked={comJanela} onCheckedChange={setComJanela} />
            </div>

            {comJanela ? (
              <div className="grid max-w-xs gap-3 sm:grid-cols-2">
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
          </div>

          <p className="text-xs text-tinta-media">
            O texto das duas mensagens que o cliente recebe fica em{' '}
            <strong className="text-tinta">Automação &gt; Mensagens automáticas</strong>: “Conversa
            parada: posso encerrar?” e “Encerrado por falta de resposta”.
          </p>
        </>
      ) : null}

      {erro ? (
        <p role="alert" className="text-xs text-erro">
          {erro}
        </p>
      ) : null}

      <div>
        <Button type="button" onClick={() => void salvar()} disabled={salvando || erro !== null}>
          {salvando ? 'Salvando…' : 'Salvar'}
        </Button>
      </div>
    </div>
  )
}
