import { useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react'
import { Lock, Paperclip, Reply, Send, X } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { conversasApi } from '@/lib/api/endpoints'
import { usarInvalidarAtendimento, usarRespostasRapidas } from './consultas'
import { aplicarMarcadores, dadosDaConversa } from './placeholders'
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
  const campo = useRef<HTMLTextAreaElement>(null)
  const seletorDeArquivo = useRef<HTMLInputElement>(null)
  const invalidar = usarInvalidarAtendimento()

  const { data: respostasRapidas } = usarRespostasRapidas()

  const termo = ATALHO_EM_DIGITACAO.exec(texto)?.[1] ?? null

  const filtradas = useMemo(
    () => (termo === null ? [] : filtrarRespostas(respostasRapidas ?? [], termo)),
    [respostasRapidas, termo],
  )

  // Nada cadastrado: nem abre. Um painel vazio a cada "/" seria ruído. Já filtro
  // sem resultado ABRE, com a explicação — senão a tecla pareceria não funcionar.
  const seletorAberto =
    termo !== null && !seletorDispensado && (respostasRapidas?.length ?? 0) > 0

  useEffect(() => {
    // Cada tecla muda o filtro, e a opção ativa tem de voltar para a primeira —
    // senão o índice apontaria para uma linha que não está mais na lista.
    setIndiceAtivo(0)

    // Sair do padrão de atalho rearma o seletor: dispensar com Esc vale para aquela
    // digitação, não para o resto do atendimento.
    if (termo === null) {
      setSeletorDispensado(false)
    }
  }, [termo])

  useEffect(() => {
    if (mensagemRespondida) {
      campo.current?.focus()
    }
  }, [mensagemRespondida])

  if (conversa.status === 'Encerrada') {
    return (
      <div className="border-t border-linha px-3 py-3 md:px-6 md:py-4 text-center text-sm text-tinta-media">
        Atendimento encerrado. O histórico continua disponível — reabra para voltar a responder.
      </div>
    )
  }

  // A trava, do lado de quem escreve: conversa de OUTRO atendente não tem caixa
  // de texto. Conversa na fila tem — quem escreve e envia assume junto, e o
  // backend faz isso com o mesmo UPDATE condicional de sempre.
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

  /**
   * Insere o texto da resposta rápida no campo. NÃO envia.
   *
   * Os marcadores são resolvidos aqui para que o atendente leia a mensagem final
   * antes de mandar — inserir um "{NomeCliente}" cru transferiria para ele a
   * conferência de um detalhe que o sistema já sabe fazer.
   */
  function inserirRespostaRapida(resposta: RespostaRapida) {
    const conteudo = aplicarMarcadores(
      resposta.conteudo,
      dadosDaConversa(conversa, nomeCartorio, setores),
    )

    setTexto(conteudo)
    setSeletorDispensado(false)

    // Depois do re-render, devolve o foco e põe o cursor no fim — o passo seguinte
    // natural é completar ou ajustar o texto.
    requestAnimationFrame(() => {
      const elemento = campo.current
      if (!elemento) return

      elemento.focus()
      elemento.setSelectionRange(elemento.value.length, elemento.value.length)
    })
  }

  /**
   * Recusa o arquivo grande demais AQUI.
   *
   * O backend recusa igual, mas só depois do upload inteiro subir — e um arquivo
   * de 40 MB numa conexão de balcão de cartório leva minutos para ser rejeitado.
   */
  function escolherArquivo(escolhido: File | null) {
    if (!escolhido) {
      return
    }

    if (escolhido.size > TAMANHO_MAXIMO_MB * 1024 * 1024) {
      toast.error(`O arquivo passa do limite de ${TAMANHO_MAXIMO_MB} MB.`)
      return
    }

    setArquivo(escolhido)
    campo.current?.focus()
  }

  async function enviar() {
    const conteudo = texto.trim()

    // Com arquivo, o texto vira legenda e pode ser vazio: um documento sozinho já
    // é uma mensagem completa.
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
      // Corrida perdida ao assumir junto com o envio: a mensagem NÃO foi gravada.
      // O texto fica no campo de propósito — quem escreveu não deve perder o que
      // digitou por causa de um clique de um colega meio segundo antes.
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
    // Com o seletor aberto, as teclas pertencem a ele. Enter precisa INSERIR, não
    // enviar: sem esta ordem, quem digitasse "/prazo" e apertasse Enter mandaria
    // literalmente "/prazo" para o cliente.
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

    // Enter envia, Shift+Enter quebra linha — o hábito de quem usa WhatsApp.
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

      {/* O arquivo escolhido fica à vista ANTES de enviar, e não some sozinho:
          anexar por engano num atendimento de cartório manda o documento de um
          cliente para outro. Sair daqui exige o X, não a sorte. */}
      {arquivo ? (
        <div className="flex items-center gap-2 rounded-md border border-linha-forte bg-superficie px-3 py-2">
          <Paperclip className="size-3.5 shrink-0 text-tinta-media" />
          <div className="min-w-0 flex-1">
            <p className="truncate text-xs text-tinta">{arquivo.name}</p>
            <p className="font-dado text-[10px] text-tinta-fraca">
              {arquivo.size < 1024 * 1024
                ? `${Math.round(arquivo.size / 1024)} KB`
                : `${(arquivo.size / 1024 / 1024).toFixed(1).replace('.', ',')} MB`}
              {' — o texto abaixo vai como legenda'}
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
      ) : null}

      {/* relative: é a âncora do seletor, que sobe a partir do topo do campo. */}
      <div className="relative flex items-end gap-2">
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
          className="max-h-40 min-h-[42px] resize-none"
          /* Padrão combobox sem trocar o papel do campo: ele continua sendo a caixa
             de mensagem para o leitor de tela, e a opção ativa é anunciada por
             activedescendant — o foco nunca sai daqui, senão cada seta interromperia
             a digitação. */
          aria-expanded={seletorAberto}
          /* Só aponta para a lista quando ela existe: sem resultado o seletor mostra
             uma explicação, não um <ul>, e apontar para um id inexistente confunde o
             leitor de tela em vez de ajudar. */
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

            // Zera o input para que escolher O MESMO arquivo de novo, depois de
            // remover, ainda dispare o onChange.
            e.target.value = ''
          }}
        />

        <Button
          variant="outline"
          size="icon"
          onClick={() => seletorDeArquivo.current?.click()}
          disabled={enviando}
          title="Anexar arquivo"
        >
          <Paperclip />
          <span className="sr-only">Anexar arquivo</span>
        </Button>

        {/* O aviso de que enviar TAMBEM assume o atendimento saiu da tela e
            virou title: a informacao continua a um passe de mouse, e as duas
            linhas que ela ocupava voltaram para o fio. O botao "Assumir" no
            cabecalho segue sendo o caminho de quem so quer reservar a conversa.

            A dica do "/" tambem saiu: o placeholder do campo ja diz, e dizer
            duas vezes custava mais uma linha. */}
        <Button
          onClick={() => void enviar()}
          disabled={enviando || (!texto.trim() && !arquivo)}
          size="icon"
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
    </div>
  )
}
