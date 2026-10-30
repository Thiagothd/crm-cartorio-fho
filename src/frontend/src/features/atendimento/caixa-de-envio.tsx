import { useCallback, useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react'
import { Lock, Mic, Paperclip, Pause, Reply, Send, Square, Trash2, X } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { conversasApi } from '@/lib/api/endpoints'
import { cn } from '@/lib/utils'
import { usarInvalidarAtendimento, usarRespostasRapidas } from './consultas'
import { aplicarMarcadores, dadosDaConversa } from './placeholders'
import { PlayerDeAudio } from './player-de-audio'
import {
  ID_DA_LISTA,
  SeletorDeRespostaRapida,
  filtrarRespostas,
  idDaOpcao,
} from './seletor-de-resposta-rapida'
import type { Conversa, Mensagem, RespostaRapida, Setor } from '@/lib/api/tipos'

/**
 * O seletor só abre quando o campo INTEIRO é o atalho sendo digitado — "/", "/pra",
 * "/prazo".
 *
 * Não é limitação: é o que torna o gatilho previsível. Abrir a cada "/" no meio de
 * um texto faria a lista pular na cara de quem escreveu "segunda/sexta" ou uma data,
 * e o Enter passaria a inserir resposta em vez de enviar a mensagem.
 */
const ATALHO_EM_DIGITACAO = /^\/(\S*)$/

/**
 * Teto do lado do cliente. Espelha `Anexos:TamanhoMaximoMb` no backend, que
 * continua sendo quem decide — aqui é só para o atendente não esperar o upload
 * de um arquivo que já se sabe que será recusado.
 */
const TAMANHO_MAXIMO_MB = 16

/** O mesmo conjunto que o backend aceita, para o seletor já filtrar. */
const TIPOS_ACEITOS =
  'image/jpeg,image/png,image/webp,image/gif,application/pdf,.doc,.docx,.xls,.xlsx,text/plain,audio/*,video/mp4'

const FORMATOS_DE_GRAVACAO = [
  { mime: 'audio/ogg;codecs=opus', ext: '.ogg', tipoBase: 'audio/ogg' },
  { mime: 'audio/webm;codecs=opus', ext: '.webm', tipoBase: 'audio/webm' },
  { mime: 'audio/webm', ext: '.webm', tipoBase: 'audio/webm' },
  { mime: 'audio/mp4', ext: '.m4a', tipoBase: 'audio/mp4' },
]

const QUANTIDADE_BARRAS_ONDA = 20
const NIVEIS_INICIAIS = Array.from({ length: QUANTIDADE_BARRAS_ONDA }, () => 18)

function escolherFormatoDeGravacao() {
  if (typeof window === 'undefined' || typeof window.MediaRecorder === 'undefined') {
    return { mime: '', ext: '.webm', tipoBase: 'audio/webm' }
  }

  for (const formato of FORMATOS_DE_GRAVACAO) {
    if (window.MediaRecorder.isTypeSupported(formato.mime)) {
      return formato
    }
  }

  return { mime: '', ext: '.webm', tipoBase: 'audio/webm' }
}

/**
 * Confere o começo do arquivo gravado: WebM abre com o cabeçalho EBML e Ogg com
 * "OggS". Um arquivo sem isso chega ao cliente (o gateway reconverte o que
 * achar) mas não toca no painel — melhor pedir nova gravação do que enviar um
 * áudio que o atendente não consegue conferir.
 */
async function gravacaoTemCabecalhoValido(blob: Blob, tipoBase: string) {
  const assinatura: Record<string, number[]> = {
    'audio/webm': [0x1a, 0x45, 0xdf, 0xa3],
    'audio/ogg': [0x4f, 0x67, 0x67, 0x53],
  }

  const esperado = assinatura[tipoBase]
  if (!esperado) {
    return true
  }

  try {
    const inicio = new Uint8Array(await blob.slice(0, esperado.length).arrayBuffer())
    return esperado.every((byte, i) => inicio[i] === byte)
  } catch {
    // Sem como ler (ambiente sem Blob.arrayBuffer): não bloqueia o envio.
    return true
  }
}

function formatarCronometro(segundos: number) {
  const min = Math.floor(segundos / 60)
  const seg = Math.floor(segundos % 60)
  return `${min}:${String(seg).padStart(2, '0')}`
}

export function CaixaDeEnvio({
  conversa,
  souOResponsavel,
  mensagemRespondida,
  aoCancelarResposta,
  setores,
  nomeCartorio,
}: {
  conversa: Conversa
  souOResponsavel: boolean
  mensagemRespondida: Mensagem | null
  aoCancelarResposta: () => void
  setores: Setor[]
  nomeCartorio: string
}) {
  const [texto, setTexto] = useState('')
  const [arquivo, setArquivo] = useState<File | null>(null)
  const [enviando, setEnviando] = useState(false)
  const [indiceAtivo, setIndiceAtivo] = useState(0)
  const [seletorDispensado, setSeletorDispensado] = useState(false)

  // Estado de gravação de voz
  const [gravandoAudio, setGravandoAudio] = useState(false)
  const [gravacaoPausada, setGravacaoPausada] = useState(false)
  const [segundosGravados, setSegundosGravados] = useState(0)
  const [niveisDeVoz, setNiveisDeVoz] = useState<number[]>(NIVEIS_INICIAIS)
  const [audioGravado, setAudioGravado] = useState<File | null>(null)

  const campo = useRef<HTMLTextAreaElement>(null)
  const seletorDeArquivo = useRef<HTMLInputElement>(null)
  const mediaRecorderRef = useRef<MediaRecorder | null>(null)
  const iniciandoGravacaoRef = useRef(false)
  const streamDeAudioRef = useRef<MediaStream | null>(null)
  const audioContextRef = useRef<AudioContext | null>(null)
  const rafOndaRef = useRef<number | null>(null)
  const acaoAoFinalizarRef = useRef<'cancelar' | 'previa' | 'enviar'>('previa')
  const formatoEscolhidoRef = useRef(escolherFormatoDeGravacao())

  const invalidar = usarInvalidarAtendimento()

  const { data: respostasRapidas } = usarRespostasRapidas()

  const termo = ATALHO_EM_DIGITACAO.exec(texto)?.[1] ?? null

  const filtradas = useMemo(
    () => (termo === null ? [] : filtrarRespostas(respostasRapidas ?? [], termo)),
    [respostasRapidas, termo],
  )

  const seletorAberto =
    termo !== null && !seletorDispensado && (respostasRapidas?.length ?? 0) > 0

  // Object URL para ouvir a prévia de áudio gravado ou arquivo de áudio anexado
  const urlPreviaAudio = useMemo(() => {
    const alvo = audioGravado ?? (arquivo?.type.startsWith('audio/') ? arquivo : null)
    if (!alvo) return null
    return URL.createObjectURL(alvo)
  }, [audioGravado, arquivo])

  useEffect(() => {
    return () => {
      if (urlPreviaAudio) {
        URL.revokeObjectURL(urlPreviaAudio)
      }
    }
  }, [urlPreviaAudio])

  const liberarHardwareDeAudio = useCallback(() => {
    if (rafOndaRef.current !== null) {
      cancelAnimationFrame(rafOndaRef.current)
      rafOndaRef.current = null
    }
    if (audioContextRef.current) {
      void audioContextRef.current.close().catch(() => {})
      audioContextRef.current = null
    }
    if (streamDeAudioRef.current) {
      for (const trilha of streamDeAudioRef.current.getTracks()) {
        trilha.stop()
      }
      streamDeAudioRef.current = null
    }
    mediaRecorderRef.current = null
  }, [])

  // Se trocar de conversa ou desmontar, encerra imediatamente qualquer gravação aberta
  useEffect(() => {
    return () => {
      acaoAoFinalizarRef.current = 'cancelar'
      if (mediaRecorderRef.current && mediaRecorderRef.current.state !== 'inactive') {
        try {
          mediaRecorderRef.current.stop()
        } catch {
          // ignora
        }
      }
      liberarHardwareDeAudio()
    }
  }, [conversa.id, liberarHardwareDeAudio])

  useEffect(() => {
    setAudioGravado(null)
    setGravandoAudio(false)
    setGravacaoPausada(false)
    setSegundosGravados(0)
  }, [conversa.id])

  // Cronômetro da gravação ativa
  useEffect(() => {
    if (!gravandoAudio || gravacaoPausada) {
      return
    }

    const intervalo = window.setInterval(() => {
      setSegundosGravados((atual) => atual + 1)
    }, 1000)

    return () => window.clearInterval(intervalo)
  }, [gravandoAudio, gravacaoPausada])

  useEffect(() => {
    setIndiceAtivo(0)

    if (termo === null) {
      setSeletorDispensado(false)
    }
  }, [termo])

  useEffect(() => {
    if (mensagemRespondida && !gravandoAudio && !audioGravado) {
      campo.current?.focus()
    }
  }, [mensagemRespondida, gravandoAudio, audioGravado])

  if (conversa.status === 'Encerrada') {
    return (
      <div className="border-t border-linha px-3 py-3 md:px-6 md:py-4 text-center text-sm text-tinta-media">
        Atendimento encerrado. O histórico continua disponível — reabra para voltar a responder.
      </div>
    )
  }

  if (!souOResponsavel && conversa.atendenteId !== null) {
    return (
      <div className="flex items-center justify-between gap-3 border-t border-linha bg-superficie px-3 py-3 md:px-6 md:py-4">
        <p className="flex items-center gap-2 text-sm text-tinta-media">
          <Lock className="size-4 shrink-0" />
          Em atendimento por {conversa.atendenteNome}.
        </p>
      </div>
    )
  }

  function inserirRespostaRapida(resposta: RespostaRapida) {
    const conteudo = aplicarMarcadores(
      resposta.conteudo,
      dadosDaConversa(conversa, nomeCartorio, setores),
    )

    setTexto(conteudo)
    setSeletorDispensado(false)

    requestAnimationFrame(() => {
      const elemento = campo.current
      if (!elemento) return

      elemento.focus()
      elemento.setSelectionRange(elemento.value.length, elemento.value.length)
    })
  }

  function escolherArquivo(escolhido: File | null) {
    if (!escolhido) {
      return
    }

    if (escolhido.size > TAMANHO_MAXIMO_MB * 1024 * 1024) {
      toast.error(`O arquivo passa do limite de ${TAMANHO_MAXIMO_MB} MB.`)
      return
    }

    setAudioGravado(null)
    setArquivo(escolhido)
    campo.current?.focus()
  }

  async function enviarArquivoDeAudioDireto(arquivoDeVoz: File) {
    if (enviando) return
    setEnviando(true)

    try {
      await conversasApi.enviarAnexo(conversa.id, arquivoDeVoz, '', mensagemRespondida?.id)
      setAudioGravado(null)
      aoCancelarResposta()
      invalidar(conversa.id)
    } catch (falha) {
      // Se falhar, preserva o áudio gravado na prévia para o atendente não perder a fala
      setAudioGravado(arquivoDeVoz)

      if (falha instanceof ErroDaApi && falha.ehConflitoDeAtribuicao) {
        toast.warning('Conversa já assumida', {
          description: `${falha.atendenteQueAssumiu ?? 'Outro atendente'} chegou primeiro. Seu áudio não foi enviado.`,
        })
        invalidar(conversa.id)
        return
      }

      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível enviar o áudio.')
    } finally {
      setEnviando(false)
    }
  }

  function iniciarMonitorDeNivelDeVoz(stream: MediaStream) {
    try {
      const Ctx =
        window.AudioContext ||
        (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext
      if (!Ctx) return

      const ctx = new Ctx()
      audioContextRef.current = ctx
      const fonte = ctx.createMediaStreamSource(stream)
      const analisador = ctx.createAnalyser()
      analisador.fftSize = 64
      analisador.smoothingTimeConstant = 0.75
      fonte.connect(analisador)

      const buffer = new Uint8Array(analisador.frequencyBinCount)

      const atualizar = () => {
        if (!mediaRecorderRef.current || mediaRecorderRef.current.state === 'inactive') {
          return
        }

        if (mediaRecorderRef.current.state === 'recording') {
          analisador.getByteFrequencyData(buffer)
          let soma = 0
          for (let i = 0; i < buffer.length; i++) {
            soma += buffer[i]
          }
          const media = soma / Math.max(1, buffer.length)
          const altura = Math.max(16, Math.min(100, Math.round((media / 140) * 100)))

          setNiveisDeVoz((anterior) => [...anterior.slice(1), altura])
        }

        rafOndaRef.current = requestAnimationFrame(atualizar)
      }

      rafOndaRef.current = requestAnimationFrame(atualizar)
    } catch {
      // Visualizador de nível é opcional; se o navegador restringir AudioContext, a gravação segue normal
    }
  }

  async function iniciarGravacaoDeAudio() {
    // `gravandoAudio` so vira true depois do `await getUserMedia`; um segundo
    // clique no meio dessa espera abria um segundo gravador, e os dois escreviam
    // no mesmo conjunto de pedacos. A trava e sincrona, por ref, de proposito.
    if (enviando || gravandoAudio || iniciandoGravacaoRef.current) return

    if (
      typeof navigator === 'undefined' ||
      !navigator.mediaDevices ||
      typeof navigator.mediaDevices.getUserMedia !== 'function' ||
      typeof window.MediaRecorder === 'undefined'
    ) {
      toast.error('Este navegador não suporta gravação de áudio.')
      return
    }

    iniciandoGravacaoRef.current = true

    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: {
          echoCancellation: true,
          noiseSuppression: true,
          autoGainControl: true,
        },
      })

      streamDeAudioRef.current = stream
      const formato = escolherFormatoDeGravacao()
      formatoEscolhidoRef.current = formato

      const opcoes = formato.mime ? { mimeType: formato.mime } : undefined
      const gravador = new MediaRecorder(stream, opcoes)
      mediaRecorderRef.current = gravador

      // Pedaços DESTE gravador, e não de um ref compartilhado: o último pedaço de
      // um gravador já descartado (troca de conversa no meio da gravação) chega
      // depois, como evento enfileirado. Num array compartilhado ele caía na
      // frente da gravação seguinte e o arquivo saía sem o cabeçalho WebM no
      // começo — chegava ao cliente, mas não tocava no painel (achado em
      // produção, 2026-10-30).
      const pedacos: Blob[] = []

      gravador.ondataavailable = (evento) => {
        if (evento.data && evento.data.size > 0) {
          pedacos.push(evento.data)
        }
      }

      gravador.onstop = () => {
        // Gravador descartado (a limpeza já soltou o microfone e zerou a ref): o
        // `onstop` dele não pode finalizar nem derrubar a gravação que começou
        // depois.
        if (mediaRecorderRef.current !== gravador) {
          return
        }

        const acao = acaoAoFinalizarRef.current
        liberarHardwareDeAudio()
        setGravandoAudio(false)
        setGravacaoPausada(false)
        setNiveisDeVoz(NIVEIS_INICIAIS)

        if (acao === 'cancelar' || pedacos.length === 0) {
          setSegundosGravados(0)
          return
        }

        const fmt = formatoEscolhidoRef.current
        const blob = new Blob(pedacos, { type: fmt.tipoBase })
        if (blob.size === 0) {
          toast.error('Nenhum som foi captado pelo microfone.')
          setSegundosGravados(0)
          return
        }

        if (blob.size > TAMANHO_MAXIMO_MB * 1024 * 1024) {
          toast.error(`O áudio gravado passa do limite de ${TAMANHO_MAXIMO_MB} MB.`)
          setSegundosGravados(0)
          return
        }

        const carimbo = new Date().toISOString().replace(/[:.]/g, '-')
        const arquivoFinal = new File([blob], `audio-${carimbo}${fmt.ext}`, {
          type: fmt.tipoBase,
        })

        void gravacaoTemCabecalhoValido(blob, fmt.tipoBase).then((valida) => {
          if (!valida) {
            toast.error('A gravação saiu corrompida', {
              description: 'Grave o áudio de novo. Nada foi enviado ao cliente.',
            })
            setSegundosGravados(0)
            return
          }

          if (acao === 'enviar') {
            setSegundosGravados(0)
            void enviarArquivoDeAudioDireto(arquivoFinal)
          } else {
            setAudioGravado(arquivoFinal)
          }
        })
      }

      gravador.start(200)
      setArquivo(null)
      setAudioGravado(null)
      setSegundosGravados(0)
      setGravacaoPausada(false)
      setGravandoAudio(true)
      iniciarMonitorDeNivelDeVoz(stream)
    } catch (erro) {
      liberarHardwareDeAudio()
      const nomeErro = erro instanceof DOMException ? erro.name : ''
      if (nomeErro === 'NotAllowedError' || nomeErro === 'PermissionDeniedError') {
        toast.error('Permissão de microfone negada', {
          description:
            'Clique no ícone de cadeado ou configurações ao lado do endereço do site no navegador e permita o uso do microfone.',
        })
      } else if (nomeErro === 'NotFoundError' || nomeErro === 'DevicesNotFoundError') {
        toast.error('Nenhum microfone encontrado', {
          description: 'Conecte um microfone ou fone de ouvido para gravar áudios.',
        })
      } else {
        toast.error('Não foi possível acessar o microfone.')
      }
    } finally {
      iniciandoGravacaoRef.current = false
    }
  }

  function alternarPausaDaGravacao() {
    const gravador = mediaRecorderRef.current
    if (!gravador) return

    if (gravador.state === 'recording') {
      gravador.pause()
      setGravacaoPausada(true)
    } else if (gravador.state === 'paused') {
      gravador.resume()
      setGravacaoPausada(false)
    }
  }

  function finalizarGravacao(acao: 'cancelar' | 'previa' | 'enviar') {
    acaoAoFinalizarRef.current = acao
    const gravador = mediaRecorderRef.current
    if (gravador && gravador.state !== 'inactive') {
      gravador.stop()
    } else {
      liberarHardwareDeAudio()
      setGravandoAudio(false)
      setGravacaoPausada(false)
      setSegundosGravados(0)
    }
  }

  async function enviar() {
    if (audioGravado) {
      await enviarArquivoDeAudioDireto(audioGravado)
      return
    }

    const conteudo = texto.trim()

    if ((!conteudo && !arquivo) || enviando) {
      return
    }

    setEnviando(true)

    try {
      if (arquivo) {
        await conversasApi.enviarAnexo(conversa.id, arquivo, conteudo, mensagemRespondida?.id)
      } else {
        await conversasApi.enviarMensagem(conversa.id, conteudo, mensagemRespondida?.id)
      }

      setTexto('')
      setArquivo(null)
      aoCancelarResposta()
      invalidar(conversa.id)
    } catch (falha) {
      if (falha instanceof ErroDaApi && falha.ehConflitoDeAtribuicao) {
        toast.warning('Conversa já assumida', {
          description: `${falha.atendenteQueAssumiu ?? 'Outro atendente'} chegou primeiro. Sua mensagem não foi enviada.`,
        })
        invalidar(conversa.id)
        return
      }

      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível enviar.')
    } finally {
      setEnviando(false)
    }
  }

  function aoTeclar(evento: KeyboardEvent<HTMLTextAreaElement>) {
    if (seletorAberto) {
      if (evento.key === 'ArrowDown' || evento.key === 'ArrowUp') {
        evento.preventDefault()

        if (filtradas.length === 0) return

        const passo = evento.key === 'ArrowDown' ? 1 : -1
        setIndiceAtivo((atual) => (atual + passo + filtradas.length) % filtradas.length)
        return
      }

      if (evento.key === 'Enter' || evento.key === 'Tab') {
        const escolhida = filtradas[indiceAtivo]
        if (escolhida) {
          evento.preventDefault()
          inserirRespostaRapida(escolhida)
        }
        return
      }

      if (evento.key === 'Escape') {
        evento.preventDefault()
        setSeletorDispensado(true)
        return
      }
    }

    if (evento.key === 'Enter' && !evento.shiftKey) {
      evento.preventDefault()
      void enviar()
    }
  }

  const naFila = conversa.atendenteId === null

  return (
    <div className="flex flex-col gap-2 border-t border-linha px-3 py-2 pb-[max(0.5rem,env(safe-area-inset-bottom))] md:px-6 md:py-2.5 md:pb-2.5">
      {mensagemRespondida ? (
        <div className="flex items-start gap-2 rounded-md border border-linha-forte bg-superficie px-3 py-2">
          <Reply className="mt-0.5 size-3.5 shrink-0 text-tinta-media" />
          <div className="min-w-0 flex-1">
            <p className="text-[11px] font-medium text-tinta-media">
              Respondendo a{' '}
              {mensagemRespondida.autorTipo === 'Cliente'
                ? conversa.clienteNome
                : (mensagemRespondida.atendenteNome ?? 'Atendente')}
            </p>
            <p className="truncate text-xs text-tinta-fraca">{mensagemRespondida.conteudo}</p>
          </div>
          <button
            type="button"
            onClick={aoCancelarResposta}
            className="flex size-8 shrink-0 items-center justify-center rounded-md text-tinta-fraca transition-colors hover:bg-branco hover:text-tinta"
            aria-label="Cancelar resposta"
            title="Cancelar resposta"
          >
            <X className="size-3.5" />
          </button>
        </div>
      ) : null}

      {arquivo ? (
        <div className="flex flex-col gap-2 rounded-md border border-linha-forte bg-superficie px-3 py-2">
          <div className="flex items-center gap-2">
            <Paperclip className="size-3.5 shrink-0 text-tinta-media" />
            <div className="min-w-0 flex-1">
              <p className="truncate text-xs text-tinta">{arquivo.name}</p>
              <p className="font-dado text-[10px] text-tinta-fraca">
                {arquivo.size < 1024 * 1024
                  ? `${Math.round(arquivo.size / 1024)} KB`
                  : `${(arquivo.size / 1024 / 1024).toFixed(1).replace('.', ',')} MB`}
                {arquivo.type.startsWith('audio/')
                  ? ' — áudio pronto para envio'
                  : ' — o texto abaixo vai como legenda'}
              </p>
            </div>
            <button
              type="button"
              onClick={() => setArquivo(null)}
              className="flex size-8 shrink-0 items-center justify-center rounded-md text-tinta-fraca transition-colors hover:bg-branco hover:text-tinta"
              aria-label="Remover arquivo"
              title="Remover arquivo"
            >
              <X className="size-3.5" />
            </button>
          </div>
          {arquivo.type.startsWith('audio/') && urlPreviaAudio ? (
            <PlayerDeAudio url={urlPreviaAudio} className="w-full max-w-md pt-0.5" />
          ) : null}
        </div>
      ) : null}

      {gravandoAudio ? (
        <div
          role="region"
          aria-label="Gravação de áudio em andamento"
          onKeyDown={(e) => {
            if (e.key === 'Escape') {
              e.preventDefault()
              finalizarGravacao('cancelar')
            }
          }}
          className="flex min-h-[42px] items-center gap-2 rounded-balao border border-linha-forte bg-superficie px-2.5 py-1.5"
        >
          <Button
            type="button"
            variant="ghost"
            size="icon"
            onClick={() => finalizarGravacao('cancelar')}
            className="size-8 shrink-0 rounded-full text-tinta-media hover:bg-selo/10 hover:text-selo"
            title="Cancelar e descartar áudio (Esc)"
          >
            <Trash2 className="size-4" />
            <span className="sr-only">Cancelar gravação</span>
          </Button>

          <div className="flex min-w-0 flex-1 items-center gap-2.5 px-1">
            <span className="inline-flex shrink-0 items-center gap-1.5">
              <span
                className={cn(
                  'size-2.5 rounded-full',
                  gravacaoPausada ? 'bg-ambar' : 'animate-pulse bg-selo',
                )}
              />
              <span className="font-dado text-xs font-medium text-tinta">
                {formatarCronometro(segundosGravados)}
              </span>
            </span>

            <div
              aria-hidden
              className="flex h-6 min-w-0 flex-1 items-center gap-1 overflow-hidden px-1"
            >
              {niveisDeVoz.map((nivel, idx) => (
                <span
                  key={idx}
                  className={cn(
                    'w-1 shrink-0 rounded-full transition-all duration-75',
                    gravacaoPausada ? 'bg-tinta-fraca/50' : 'bg-envio',
                  )}
                  style={{ height: `${gravacaoPausada ? 22 : nivel}%` }}
                />
              ))}
            </div>

            <span className="hidden text-[11px] text-tinta-media sm:inline">
              {gravacaoPausada ? 'Gravação pausada' : 'Gravando áudio…'}
            </span>
          </div>

          <Button
            type="button"
            variant="ghost"
            size="icon"
            onClick={alternarPausaDaGravacao}
            className="size-8 shrink-0 rounded-full text-tinta-media hover:text-tinta"
            title={gravacaoPausada ? 'Retomar gravação' : 'Pausar gravação'}
          >
            {gravacaoPausada ? <Mic className="size-4 text-envio" /> : <Pause className="size-4" />}
            <span className="sr-only">
              {gravacaoPausada ? 'Retomar gravação' : 'Pausar gravação'}
            </span>
          </Button>

          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => finalizarGravacao('previa')}
            className="h-8 shrink-0 gap-1.5 rounded-full px-2.5 text-xs"
            title="Parar gravação e ouvir antes de enviar"
          >
            <Square className="size-3 fill-current" />
            <span className="hidden sm:inline">Ouvir</span>
          </Button>

          <Button
            type="button"
            size="icon"
            onClick={() => finalizarGravacao('enviar')}
            disabled={enviando}
            className="rounded-full bg-gradient-to-b from-primary to-primary/90"
            title={
              naFila
                ? 'Enviar áudio agora — ao enviar, você assume o atendimento'
                : 'Enviar áudio agora'
            }
          >
            <Send className="size-4" />
            <span className="sr-only">Enviar áudio</span>
          </Button>
        </div>
      ) : audioGravado ? (
        <div
          role="region"
          aria-label="Prévia do áudio gravado"
          className="flex min-h-[42px] items-center gap-2 rounded-balao border border-linha-forte bg-superficie px-2.5 py-1.5"
        >
          <Button
            type="button"
            variant="ghost"
            size="icon"
            onClick={() => {
              setAudioGravado(null)
              setSegundosGravados(0)
            }}
            disabled={enviando}
            className="size-8 shrink-0 rounded-full text-tinta-media hover:bg-selo/10 hover:text-selo"
            title="Descartar áudio gravado"
          >
            <Trash2 className="size-4" />
            <span className="sr-only">Descartar áudio</span>
          </Button>

          <div className="min-w-0 flex-1 px-1">
            <PlayerDeAudio url={urlPreviaAudio} className="w-full" />
          </div>

          <Button
            type="button"
            variant="ghost"
            size="icon"
            onClick={() => void iniciarGravacaoDeAudio()}
            disabled={enviando}
            className="size-8 shrink-0 rounded-full text-tinta-media hover:text-tinta"
            title="Gravar novamente"
          >
            <Mic className="size-4" />
            <span className="sr-only">Gravar novamente</span>
          </Button>

          <Button
            type="button"
            size="icon"
            onClick={() => void enviar()}
            disabled={enviando}
            className="rounded-full bg-gradient-to-b from-primary to-primary/90"
            title={
              naFila
                ? 'Enviar áudio — ao enviar, você assume o atendimento'
                : 'Enviar áudio'
            }
          >
            <Send className="size-4" />
            <span className="sr-only">Enviar áudio</span>
          </Button>
        </div>
      ) : (
        <div className="relative flex items-end gap-1.5 sm:gap-2">
          {seletorAberto ? (
            <SeletorDeRespostaRapida
              respostas={filtradas}
              termo={termo ?? ''}
              indiceAtivo={indiceAtivo}
              aoEscolher={inserirRespostaRapida}
            />
          ) : null}

          <Textarea
            ref={campo}
            value={texto}
            onChange={(e) => setTexto(e.target.value)}
            onKeyDown={aoTeclar}
            placeholder="Escreva a resposta… (Enter envia, / abre as respostas rápidas)"
            rows={2}
            maxLength={4000}
            className="max-h-40 min-h-[42px] resize-none rounded-balao bg-superficie focus-visible:bg-branco"
            aria-expanded={seletorAberto}
            aria-controls={seletorAberto && filtradas.length > 0 ? ID_DA_LISTA : undefined}
            aria-activedescendant={
              seletorAberto && filtradas.length > 0 ? idDaOpcao(indiceAtivo) : undefined
            }
            aria-autocomplete="list"
          />

          <input
            ref={seletorDeArquivo}
            type="file"
            accept={TIPOS_ACEITOS}
            className="hidden"
            onChange={(e) => {
              escolherArquivo(e.target.files?.[0] ?? null)
              e.target.value = ''
            }}
          />

          <Button
            type="button"
            variant="ghost"
            size="icon"
            className="rounded-full"
            onClick={() => seletorDeArquivo.current?.click()}
            disabled={enviando}
            title="Anexar arquivo"
          >
            <Paperclip />
            <span className="sr-only">Anexar arquivo</span>
          </Button>

          <Button
            type="button"
            variant="ghost"
            size="icon"
            className="rounded-full"
            onClick={() => void iniciarGravacaoDeAudio()}
            disabled={enviando}
            title="Gravar áudio de voz"
          >
            <Mic />
            <span className="sr-only">Gravar áudio de voz</span>
          </Button>

          <Button
            onClick={() => void enviar()}
            disabled={enviando || (!texto.trim() && !arquivo)}
            size="icon"
            className="rounded-full bg-gradient-to-b from-primary to-primary/90"
            title={
              naFila
                ? 'Enviar — ao enviar, você assume o atendimento'
                : 'Enviar'
            }
          >
            <Send />
            <span className="sr-only">Enviar</span>
          </Button>
        </div>
      )}
    </div>
  )
}
