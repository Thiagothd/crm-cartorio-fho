import { useEffect, useRef } from 'react'
import { Check, CheckCheck, Clock, Copy, Reply, TriangleAlert } from 'lucide-react'
import { toast } from 'sonner'
import { cn } from '@/lib/utils'
import { formatarDataHora, formatarHora } from '@/lib/formatacao'
import type { AparenciaDaConversa, Mensagem, StatusEntrega } from '@/lib/api/tipos'

const formatadorDeDia = new Intl.DateTimeFormat('pt-BR', {
  day: '2-digit',
  month: 'long',
  year: 'numeric',
})

function IconeDeEntrega({
  status,
  automatica = false,
}: {
  status: StatusEntrega
  automatica?: boolean
}) {
  const configuracao = {
    Pendente: { rotulo: 'Pendente', icone: Clock, classe: '' },
    Enviada: { rotulo: 'Enviada', icone: Check, classe: '' },
    Entregue: { rotulo: 'Entregue', icone: CheckCheck, classe: '' },
    Lida: { rotulo: 'Lida pelo cliente', icone: CheckCheck, classe: 'text-verde' },
    Falha: { rotulo: 'Falha no envio', icone: TriangleAlert, classe: 'text-selo' },
  }[status]
  const Icone = configuracao.icone

  // Toda mensagem automática nasce Pendente na Fase 1 — não há WhatsApp conectado
  // para entregar nada. Sem esta ressalva, o relógio ao lado do horário pareceria
  // falha de envio em vez do estado correto de "gerada, aguardando a Fase 2".
  const rotulo =
    automatica && status === 'Pendente'
      ? 'Gerada pelo sistema. O envio real ao WhatsApp entra na Fase 2.'
      : configuracao.rotulo

  return (
    <span title={rotulo} aria-label={rotulo}>
      <Icone className={cn('size-3', configuracao.classe)} />
    </span>
  )
}

function separadorDeDia(mensagens: Mensagem[], indice: number) {
  const atual = new Date(mensagens[indice].enviadaEm).toDateString()

  if (indice === 0) {
    return atual
  }

  const anterior = new Date(mensagens[indice - 1].enviadaEm).toDateString()
  return atual === anterior ? null : atual
}

/**
 * O fio da conversa.
 *
 * A mensagem do atendente NAO usa bordo de fundo. Bordo em cada resposta
 * colocaria a cor do selo em toda a tela e quebraria a regra de contencao
 * (docs/DESIGN.md, secao 2.1). A distincao entre quem falou vem de tres coisas
 * mais discretas e mais baratas: alinhamento, peso de borda e o nome do
 * atendente em Spectral — que ainda cumpre o RF-007 melhor que cor.
 */
/**
 * Classe de textura por aparência configurada.
 *
 * Uma classe só por vez: todas declaram a pilha inteira de camadas de fundo, e
 * duas disputando `background-image` dariam resultado dependente da ordem do CSS.
 * A marca d'água do selo está em todas — é assinatura fixa, não alternativa.
 */
const TEXTURA_DO_FUNDO: Record<AparenciaDaConversa, string> = {
  PapelLiso: 'marca-dagua-selo',
  SeloRepetido: 'fundo-conversa-selo',
  PautaFina: 'fundo-conversa-pauta',
}

export function FioDeMensagens({
  mensagens,
  clienteNome,
  aparencia,
  aoResponder,
}: {
  mensagens: Mensagem[]
  clienteNome: string
  aparencia: AparenciaDaConversa
  aoResponder: (mensagem: Mensagem) => void
}) {
  const fim = useRef<HTMLDivElement>(null)

  // Rola para a última mensagem sempre que o fio cresce — comportamento
  // esperado de qualquer conversa de WhatsApp.
  useEffect(() => {
    fim.current?.scrollIntoView({ block: 'end' })
  }, [mensagens.length])

  async function copiar(mensagem: Mensagem) {
    try {
      await navigator.clipboard.writeText(mensagem.conteudo)
      toast.success('Mensagem copiada.')
    } catch {
      toast.error('Não foi possível copiar a mensagem.')
    }
  }

  return (
    /* marca-dagua-selo: o anel do selo ao fundo, no canto superior direito.
       E background-image, entao nao afeta rolagem, clique nem posicao de
       mensagem alguma (DESIGN.md, secao 10). */
    <div
      className={cn(
        'flex flex-1 flex-col gap-3 overflow-y-auto bg-papel px-3 py-4 sm:px-6',
        TEXTURA_DO_FUNDO[aparencia] ?? TEXTURA_DO_FUNDO.PapelLiso,
      )}
    >
      {mensagens.map((mensagem, indice) => {
        const dia = separadorDeDia(mensagens, indice)
        const doAtendente = mensagem.autorTipo === 'Atendente'
        const automatica = mensagem.ehAutomatica

        // As duas são 'Sistema' no banco, e a diferença muda tudo na tela: a
        // anotação é registro interno e fica à margem; a automática foi endereçada
        // ao cliente e ocupa o lado da serventia, como qualquer resposta.
        const anotacaoInterna = mensagem.autorTipo === 'Sistema' && !automatica
        const daServentia = doAtendente || automatica

        return (
          <div key={mensagem.id} className="flex flex-col gap-2.5">
            {dia ? (
              <div className="my-0.5 flex justify-center">
                <time
                  dateTime={new Date(dia).toISOString()}
                  className="rounded-md border border-linha bg-branco px-3 py-1 font-dado text-[10px] text-tinta-fraca"
                >
                  {formatadorDeDia.format(new Date(dia))}
                </time>
              </div>
            ) : null}

            {anotacaoInterna ? (
              /* Evento no fio: sem caixa, sem ícone. É uma anotação à margem. */
              <div className="flex items-center justify-center">
                <span
                  className="max-w-[85%] text-center text-[11px] italic text-tinta-fraca"
                  title={formatarDataHora(mensagem.enviadaEm)}
                >
                  {mensagem.conteudo}
                </span>
              </div>
            ) : (
              <div className={cn('group flex items-center gap-2', daServentia ? 'justify-end' : 'justify-start')}>
                {daServentia ? (
                  <AcoesDaMensagem mensagem={mensagem} aoCopiar={copiar} aoResponder={aoResponder} />
                ) : null}

                <article
                  className={cn(
                    'max-w-[82%] rounded-md border px-3 py-2.5 sm:max-w-[72%]',
                    doAtendente ? 'border-verde/20 bg-verde/10' : null,

                    /* Neutra, e não a diluição de verde do atendente: verde é a cor
                       de quem respondeu, e a automática não tem quem. Fica no papel
                       da própria interface — presente, sem reivindicar autoria. */
                    automatica ? 'border-linha-forte bg-superficie' : null,

                    !daServentia ? 'border-linha-forte bg-branco' : null,
                  )}
                  aria-label={
                    automatica
                      ? 'Mensagem automática do sistema'
                      : `Mensagem de ${doAtendente ? (mensagem.atendenteNome ?? 'Atendente') : clienteNome}`
                  }
                >
                  {/* O nome fica visivel dos dois lados. No atendente, cumpre o
                      RF-007; no cliente, evita ambiguidade ao citar/copiar.
                      Na automática não há nome — o rótulo ocupa essa linha, que é
                      exatamente a informação que falta ao atendente ali. */}
                  {automatica ? (
                    <p className="mb-1">
                      <span
                        className="inline-flex items-center rounded-md border border-linha-forte px-1.5 font-titulo text-[10px] font-semibold uppercase tracking-[0.12em] text-tinta-media"
                        title="Gerada por uma regra de mensagem automática, não escrita por um atendente."
                      >
                        Automática
                      </span>
                    </p>
                  ) : (
                    <p
                      className={cn(
                        'mb-1 font-titulo text-[10px] font-semibold uppercase tracking-[0.12em]',
                        doAtendente ? 'text-verde' : 'text-tinta-media',
                      )}
                    >
                      {doAtendente ? (mensagem.atendenteNome ?? 'Atendente') : clienteNome}
                    </p>
                  )}

                  {mensagem.respondidaConteudo ? (
                    <div className="mb-2 border-l-2 border-linha-forte bg-papel/70 px-2 py-1.5">
                      <p className="text-[11px] font-medium text-tinta-media">
                        {mensagem.respondidaAutor ?? 'Mensagem'}
                      </p>
                      <p className="line-clamp-2 text-xs text-tinta-fraca">
                        {mensagem.respondidaConteudo}
                      </p>
                    </div>
                  ) : null}

                  <p className="whitespace-pre-wrap break-words text-sm leading-relaxed text-tinta">
                    {mensagem.conteudo}
                  </p>

                  <div className="mt-1.5 flex items-center justify-end gap-1 font-dado text-[10px] text-tinta-fraca">
                    {formatarHora(mensagem.enviadaEm)}
                    {daServentia ? (
                      <IconeDeEntrega status={mensagem.statusEntrega} automatica={automatica} />
                    ) : null}
                  </div>
                </article>

                {!daServentia ? (
                  <AcoesDaMensagem mensagem={mensagem} aoCopiar={copiar} aoResponder={aoResponder} />
                ) : null}
              </div>
            )}
          </div>
        )
      })}

      <div ref={fim} />
    </div>
  )
}

function AcoesDaMensagem({
  mensagem,
  aoCopiar,
  aoResponder,
}: {
  mensagem: Mensagem
  aoCopiar: (mensagem: Mensagem) => void
  aoResponder: (mensagem: Mensagem) => void
}) {
  return (
    <div className="flex shrink-0 items-center rounded-md border border-linha bg-branco opacity-100 transition-opacity sm:opacity-0 sm:group-focus-within:opacity-100 sm:group-hover:opacity-100">
      <button
        type="button"
        onClick={() => void aoCopiar(mensagem)}
        className="flex size-8 items-center justify-center text-tinta-fraca transition-colors hover:bg-superficie hover:text-tinta"
        aria-label="Copiar mensagem"
        title="Copiar"
      >
        <Copy className="size-3.5" />
      </button>
      <button
        type="button"
        onClick={() => aoResponder(mensagem)}
        className="flex size-8 items-center justify-center border-l border-linha text-tinta-fraca transition-colors hover:bg-superficie hover:text-tinta"
        aria-label="Responder mensagem"
        title="Responder"
      >
        <Reply className="size-3.5" />
      </button>
    </div>
  )
}
