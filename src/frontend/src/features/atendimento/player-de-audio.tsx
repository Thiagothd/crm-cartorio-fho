import { useEffect, useRef, useState, type MouseEvent } from 'react'
import { Download, Loader2, Pause, Play } from 'lucide-react'
import { cn } from '@/lib/utils'

function formatarTempo(segundos: number) {
  if (!Number.isFinite(segundos) || segundos < 0) {
    return '0:00'
  }

  const minutos = Math.floor(segundos / 60)
  const resto = Math.floor(segundos % 60)

  return `${minutos}:${String(resto).padStart(2, '0')}`
}

/**
 * Transporte de áudio inteiramente nosso — nunca o `<audio controls>` nativo.
 *
 * O controle nativo do Chromium desenha uma pílula própria, com cores e forma
 * que não respeitam a paleta da aplicação, e ainda quebra visualmente quando
 * espremido num balão estreito do fio — foi o "campo preto" relatado no teste.
 * Aqui o `<audio>` fica oculto (sem `controls`), e o botão, a barra e o tempo
 * são elementos comuns, nos mesmos tokens do resto do painel.
 *
 * O MESMO frame serve dois casos:
 * - No fio (`AnexoDaMensagem`), o binário já chegou por `usarAnexo`: passa só
 *   `url` e `aoBaixar` — o download some para dentro da própria linha porque o
 *   fio já é compacto por contrato (DESIGN.md, seção 7.1), e nome do autor e
 *   data já estão na bolha; repeti-los aqui seria redundante, não fidelidade.
 * - Na galeria (`LinhaDeAudio`), o áudio não é pré-carregado — passa `url` como
 *   `null` até o clique, e `aoIniciar` dispara o download. O baixar já mora na
 *   linha de cima (nome, data, tamanho), então `aoBaixar` fica de fora ali.
 *
 * O frame não muda de forma entre carregando e tocando, e é exatamente o
 * "nasce já certo" pedido: sem um widget diferente aparecendo depois do play.
 */
export function PlayerDeAudio({
  url,
  carregando = false,
  autoPlay = false,
  aoIniciar,
  aoBaixar,
  className,
}: {
  url: string | null
  carregando?: boolean
  autoPlay?: boolean
  aoIniciar?: () => void
  /** Ícone de download no fim da linha. Ausente, a linha não mostra a ação. */
  aoBaixar?: () => void
  className?: string
}) {
  const audioRef = useRef<HTMLAudioElement>(null)
  const medindoDuracaoInfinite = useRef(false)
  const [tocando, setTocando] = useState(false)
  const [atual, setAtual] = useState(0)
  const [duracao, setDuracao] = useState(0)

  // Autoplay só depois que o binário chega: é a continuação do clique que já
  // pediu o carregamento, não um autoplay de página.
  useEffect(() => {
    if (url && autoPlay) {
      void audioRef.current?.play()
    }
  }, [url, autoPlay])

  function registrarDuracaoValida(elemento: HTMLAudioElement) {
    if (Number.isFinite(elemento.duration) && elemento.duration > 0) {
      setDuracao(elemento.duration)
      if (medindoDuracaoInfinite.current) {
        medindoDuracaoInfinite.current = false
        elemento.currentTime = 0
      }
    }
  }

  function alternar() {
    if (!url) {
      if (!carregando) {
        aoIniciar?.()
      }
      return
    }

    const audio = audioRef.current
    if (!audio) {
      return
    }

    if (audio.paused) {
      void audio.play()
    } else {
      audio.pause()
    }
  }

  const duracaoConhecida = Number.isFinite(duracao) && duracao > 0

  function buscar(evento: MouseEvent<HTMLDivElement>) {
    const audio = audioRef.current
    if (!audio || !duracaoConhecida) {
      return
    }

    const caixa = evento.currentTarget.getBoundingClientRect()
    const fracao = Math.min(1, Math.max(0, (evento.clientX - caixa.left) / caixa.width))
    audio.currentTime = fracao * duracao
  }

  const progresso = duracaoConhecida ? Math.min(100, (atual / duracao) * 100) : 0

  return (
    <div className={cn('flex items-center gap-2', className)}>
      {url ? (
        <audio
          ref={audioRef}
          src={url}
          preload="metadata"
          onPlay={() => setTocando(true)}
          onPause={() => setTocando(false)}
          onEnded={() => setTocando(false)}
          onLoadedMetadata={(e) => {
            const el = e.currentTarget
            if (el.duration === Infinity) {
              // Áudios gravados no navegador via MediaRecorder (WebM/Opus) nascem
              // sem duração no cabeçalho; avançar para um instante alto força o
              // Chromium a calcular o fim do stream e devolver a duração exata.
              medindoDuracaoInfinite.current = true
              el.currentTime = 1e101
            } else {
              registrarDuracaoValida(el)
            }
          }}
          onDurationChange={(e) => registrarDuracaoValida(e.currentTarget)}
          onTimeUpdate={(e) => {
            const el = e.currentTarget
            if (medindoDuracaoInfinite.current) {
              registrarDuracaoValida(el)
              return
            }
            setAtual(el.currentTime)
          }}
          className="hidden"
        />
      ) : null}

      <button
        type="button"
        onClick={alternar}
        disabled={carregando}
        className="flex size-8 shrink-0 items-center justify-center rounded-full border border-linha-forte bg-branco text-tinta transition-colors hover:bg-superficie disabled:cursor-wait"
        title={carregando ? 'Carregando…' : tocando ? 'Pausar' : 'Reproduzir'}
      >
        {carregando ? (
          <Loader2 className="size-3.5 animate-spin" />
        ) : tocando ? (
          <Pause className="size-3.5" />
        ) : (
          <Play className="size-3.5" />
        )}
        <span className="sr-only">{tocando ? 'Pausar' : 'Reproduzir'}</span>
      </button>

      {/* Clicar na barra busca a posição — só faz sentido com a duração já
          conhecida, e ela só chega com `onLoadedMetadata`. */}
      <div
        role="slider"
        aria-label="Progresso do áudio"
        aria-valuemin={0}
        aria-valuemax={duracaoConhecida ? Math.round(duracao) : 0}
        aria-valuenow={Math.round(atual)}
        onClick={url && duracaoConhecida ? buscar : undefined}
        className={cn(
          'relative h-1.5 flex-1 rounded-full bg-linha',
          url && duracaoConhecida && 'cursor-pointer',
        )}
      >
        <div
          className="absolute inset-y-0 left-0 rounded-full bg-tinta-media"
          style={{ width: `${progresso}%` }}
        />
      </div>

      <span className="shrink-0 font-dado text-[10px] text-tinta-fraca">
        {formatarTempo(atual)} / {formatarTempo(duracao)}
      </span>

      {aoBaixar ? (
        <button
          type="button"
          onClick={aoBaixar}
          className="flex size-7 shrink-0 items-center justify-center rounded-md text-tinta-fraca transition-colors hover:bg-superficie hover:text-tinta"
          title="Baixar áudio"
        >
          <Download className="size-3.5" />
          <span className="sr-only">Baixar áudio</span>
        </button>
      ) : null}
    </div>
  )
}
