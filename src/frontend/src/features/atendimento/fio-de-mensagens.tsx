import { useEffect, useRef } from 'react'
import {
  Check,
  CheckCheck,
  Clock,
  Copy,
  Eye,
  EyeOff,
  Forward,
  ListChecks,
  Pin,
  PinOff,
  Reply,
  TriangleAlert,
} from 'lucide-react'
import { toast } from 'sonner'
import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuSeparator,
  ContextMenuTrigger,
} from '@/components/ui/context-menu'
import { cn } from '@/lib/utils'
import { formatarDataHora, formatarHora } from '@/lib/formatacao'
import { AnexoDaMensagem } from './anexo-da-mensagem'
import type { AparenciaDaConversa, Mensagem, StatusEntrega } from '@/lib/api/tipos'

/**
 * Rótulos que o servidor grava quando o anexo chega sem legenda.
 *
 * Repetir o rótulo embaixo da própria imagem seria ruído: o anexo já se
 * apresenta. Espelha `TradutorDePayload.LerConteudo` no backend.
 */
const ROTULOS_DE_ANEXO = new Set(['[imagem]', '[documento]', '[áudio]', '[vídeo]'])

/** O texto da mensagem acrescenta algo ao anexo, em vez de repeti-lo. */
function temLegendaPropria(mensagem: Mensagem) {
  if (!mensagem.arquivoNome) {
    return mensagem.conteudo.length > 0
  }

  return (
    mensagem.conteudo !== mensagem.arquivoNome && !ROTULOS_DE_ANEXO.has(mensagem.conteudo)
  )
}

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
  const configuracoes: Record<
    StatusEntrega,
    { rotulo: string; icone: typeof Clock; classe: string }
  > = {
    Pendente: { rotulo: 'Pendente', icone: Clock, classe: '' },
    Enviando: {
      rotulo: 'Envio em andamento ou aguardando confirmação',
      icone: Clock,
      classe: 'text-tinta-fraca',
    },
    Enviada: { rotulo: 'Enviada', icone: Check, classe: '' },
    Entregue: { rotulo: 'Entregue', icone: CheckCheck, classe: '' },
    Lida: { rotulo: 'Lida pelo cliente', icone: CheckCheck, classe: 'text-verde' },
    Falha: { rotulo: 'Falha no envio', icone: TriangleAlert, classe: 'text-selo' },
  }
  const configuracao = configuracoes[status] ?? configuracoes.Pendente
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
  selecionadas,
  modoSelecao,
  aoResponder,
  aoAlternarSelecao,
  aoEntrarEmSelecao,
  aoFixar,
  aoOcultar,
  aoEncaminhar,
  aoAbrirImagem,
  mostrarOcultas,
  aoAlternarOcultas,
}: {
  mensagens: Mensagem[]
  clienteNome: string
  aparencia: AparenciaDaConversa
  selecionadas: Set<string>
  modoSelecao: boolean
  aoResponder: (mensagem: Mensagem) => void
  aoAlternarSelecao: (id: string) => void
  aoEntrarEmSelecao: (id: string) => void
  aoFixar: (mensagem: Mensagem) => void
  aoOcultar: (ids: string[], ocultar: boolean) => void
  aoEncaminhar: (ids: string[]) => void
  /** Clicar na imagem amplia no visualizador da pagina — nunca baixa. */
  aoAbrirImagem: (mensagemId: string) => void
  /**
   * Interruptor de ocultas, compartilhado com a galeria de midias.
   *
   * Vive na PAGINA, e nao aqui, porque a galeria aplica a mesma regra e o
   * visualizador navega por uma lista derivada dela. Com dois interruptores
   * independentes, clicar numa imagem oculta que o fio esta exibindo abriria um
   * visualizador cuja lista nao a contem — e ele fecharia na hora.
   */
  mostrarOcultas: boolean
  aoAlternarOcultas: () => void
}) {
  const fim = useRef<HTMLDivElement>(null)

  const fixada = mensagens.find((m) => m.fixada && !m.oculta)
  const totalOcultas = mensagens.filter((m) => m.oculta).length
  // Ocultas ficam escondidas atrás de um aviso, não somem da tela. O registro
  // continua existindo e o atendente sabe que existe — é o que separa ocultar de
  // apagar, e apagar não é uma operação deste sistema.
  const visiveis = mostrarOcultas ? mensagens : mensagens.filter((m) => !m.oculta)

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
    <div className="flex min-h-0 flex-1 flex-col">
      {/* Faixa da mensagem fixada. Fica FORA da área que rola: fixar serve para
          a mensagem continuar à vista, e uma faixa que some ao rolar não fixa
          nada. Clicar leva até ela no fio. */}
      {fixada ? (
        <div className="flex shrink-0 items-start gap-2 border-b border-linha bg-branco px-3 py-2 sm:px-6">
          <Pin className="mt-0.5 size-3.5 shrink-0 text-tinta-media" />
          <button
            type="button"
            onClick={() =>
              document
                .getElementById(`mensagem-${fixada.id}`)
                ?.scrollIntoView({ block: 'center', behavior: 'smooth' })
            }
            className="min-w-0 flex-1 text-left"
            title="Ir até a mensagem fixada"
          >
            <p className="text-[10px] uppercase tracking-wider text-tinta-fraca">Fixada</p>
            <p className="line-clamp-2 text-xs text-tinta-media">{fixada.conteudo}</p>
          </button>
          <button
            type="button"
            onClick={() => aoFixar(fixada)}
            className="flex size-7 shrink-0 items-center justify-center rounded-md text-tinta-fraca transition-colors hover:bg-superficie hover:text-tinta"
            aria-label="Desafixar mensagem"
            title="Desafixar"
          >
            <PinOff className="size-3.5" />
          </button>
        </div>
      ) : null}

      {totalOcultas > 0 ? (
        <div className="flex shrink-0 items-center justify-between gap-2 border-b border-linha bg-superficie px-3 py-1.5 sm:px-6">
          <p className="flex items-center gap-1.5 text-[11px] text-tinta-media">
            <EyeOff className="size-3.5 shrink-0" />
            {totalOcultas === 1
              ? '1 mensagem ocultada do fio — continua registrada'
              : `${totalOcultas} mensagens ocultadas do fio — continuam registradas`}
          </p>
          <button
            type="button"
            onClick={aoAlternarOcultas}
            aria-pressed={mostrarOcultas}
            className="shrink-0 rounded-md border border-linha-forte bg-branco px-2 py-0.5 text-[11px] text-tinta-media transition-colors hover:text-tinta"
          >
            {mostrarOcultas ? 'Esconder' : 'Mostrar'}
          </button>
        </div>
      ) : null}

      {/* A textura do fundo é background-image, então não afeta rolagem, clique
          nem posição de mensagem alguma (DESIGN.md, seção 10). */}
      <div
        className={cn(
          /* Escala apertada de proposito. Num monitor de 1366x768 sobram ~320px
             de altura para o fio depois das faixas fixas da tela, e no respiro
             antigo cada mensagem custava ~92px: uma ou duas a vista. Aqui muda
             ESPACO — cor, borda, raio e familia tipografica continuam iguais
             (DESIGN.md, secao 7). */
          'flex flex-1 flex-col gap-1.5 overflow-y-auto bg-papel px-3 py-2.5 sm:px-5',
          TEXTURA_DO_FUNDO[aparencia] ?? TEXTURA_DO_FUNDO.PapelLiso,
        )}
      >
      {visiveis.map((mensagem, indice) => {
        const dia = separadorDeDia(visiveis, indice)
        const doAtendente = mensagem.autorTipo === 'Atendente'
        const automatica = mensagem.ehAutomatica
        const geradaPorIa = mensagem.geradaPorIa

        // As duas são 'Sistema' no banco, e a diferença muda tudo na tela: a
        // anotação é registro interno e fica à margem; a automática foi endereçada
        // ao cliente e ocupa o lado da serventia, como qualquer resposta.
        const anotacaoInterna = mensagem.autorTipo === 'Sistema' && !automatica
        const daServentia = doAtendente || automatica

        const selecionada = selecionadas.has(mensagem.id)

        return (
          <div key={mensagem.id} id={`mensagem-${mensagem.id}`} className="flex flex-col gap-1.5">
            {dia ? (
              // Fase 3.2 (secao 16): pilula flutuando sobre o vao — um dos
              // dois unicos lugares do balcao com shadow-card (o outro e' o
              // cartao "Escolha uma conversa"). Nunca no balao em si.
              <div className="my-0.5 flex justify-center">
                <time
                  dateTime={new Date(dia).toISOString()}
                  className="rounded-full border border-linha bg-branco/80 px-2.5 py-0.5 font-dado text-[10px] text-tinta-fraca shadow-card"
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
              <ContextMenu>
                <ContextMenuTrigger asChild>
                  <div
                    className={cn(
                      'group flex items-center gap-2 rounded-md',
                      daServentia ? 'justify-end' : 'justify-start',
                      // Em modo de seleção a linha inteira vira alvo de clique.
                      modoSelecao && 'cursor-pointer',
                      selecionada && 'bg-tinta/5',
                    )}
                    onClick={modoSelecao ? () => aoAlternarSelecao(mensagem.id) : undefined}
                  >
                    {modoSelecao ? (
                      <input
                        type="checkbox"
                        checked={selecionada}
                        onChange={() => aoAlternarSelecao(mensagem.id)}
                        onClick={(e) => e.stopPropagation()}
                        aria-label={`Selecionar mensagem de ${
                          daServentia ? (mensagem.atendenteNome ?? 'atendente') : clienteNome
                        }`}
                        className="size-4 shrink-0 accent-tinta"
                      />
                    ) : null}

                    {daServentia && !modoSelecao ? (
                      <AcoesDaMensagem mensagem={mensagem} aoCopiar={copiar} aoResponder={aoResponder} />
                    ) : null}

                    <article
                      className={cn(
                        // Fase 3.2 (secao 16): rounded-balao (12px) no lugar
                        // do rounded-md (6px) datado, com um canto de cauda
                        // que faz o olho ler "conversa", nao "lista de
                        // caixinhas". Nunca shadow-card aqui — balao nao
                        // pertence a familia elevada (a Central de balões
                        // scrolla e re-renderiza a cada mensagem nova).
                        'max-w-[82%] rounded-balao border px-2.5 py-1.5 sm:max-w-[72%]',
                        daServentia ? 'rounded-br-[4px]' : 'rounded-bl-[4px]',
                        doAtendente ? 'border-verde/25 bg-verde/10' : null,

                        /* Neutra, e não a diluição de verde do atendente: verde é a
                           cor de quem respondeu, e a automática não tem quem. Fica
                           no papel da própria interface — presente, sem reivindicar
                           autoria. */
                        automatica ? 'border-linha-forte bg-superficie' : null,

                        !daServentia ? 'border-linha bg-branco' : null,

                        /* Ocultada e sendo exibida: fica visivelmente rebaixada,
                           para ninguém confundir com o fio normal. */
                        mensagem.oculta && 'opacity-60',
                      )}
                      aria-label={
                        geradaPorIa
                          ? 'Mensagem gerada por inteligência artificial'
                          : automatica
                            ? 'Mensagem automática do sistema'
                            : `Mensagem de ${doAtendente ? (mensagem.atendenteNome ?? 'Atendente') : clienteNome}`
                      }
                    >
                  {/* O nome fica visivel dos dois lados. No atendente, cumpre o
                      RF-007; no cliente, evita ambiguidade ao citar/copiar.
                      Na automática não há nome — o rótulo ocupa essa linha, que é
                      exatamente a informação que falta ao atendente ali. */}
                  {automatica ? (
                    <p className="mb-0.5">
                      <span
                        className="inline-flex items-center rounded-md border border-linha-forte px-1.5 font-titulo text-[10px] font-semibold uppercase tracking-[0.12em] text-tinta-media"
                        title={
                          geradaPorIa
                            ? 'Resposta gerada por inteligência artificial, com base só em dado cadastrado — não escrita por um atendente.'
                            : 'Gerada por uma regra de mensagem automática, não escrita por um atendente.'
                        }
                      >
                        {geradaPorIa ? 'IA' : 'Automática'}
                      </span>
                    </p>
                  ) : (
                    <p
                      className={cn(
                        'mb-0.5 font-titulo text-[10px] font-semibold uppercase tracking-[0.12em]',
                        doAtendente ? 'text-verde' : 'text-tinta-media',
                      )}
                    >
                      {doAtendente ? (mensagem.atendenteNome ?? 'Atendente') : clienteNome}
                    </p>
                  )}

                  {mensagem.respondidaConteudo ? (
                    <div className="mb-1.5 rounded-md rounded-l-none border-l-2 border-verde/40 bg-papel/70 px-2 py-1">
                      <p className="text-[11px] font-medium text-tinta-media">
                        {mensagem.respondidaAutor ?? 'Mensagem'}
                      </p>
                      <p className="line-clamp-2 text-xs text-tinta-fraca">
                        {mensagem.respondidaConteudo}
                      </p>
                    </div>
                  ) : null}

                  {mensagem.arquivoNome ? (
                    <AnexoDaMensagem mensagem={mensagem} aoAbrirImagem={aoAbrirImagem} />
                  ) : null}

                  {temLegendaPropria(mensagem) ? (
                    <p className="whitespace-pre-wrap break-words text-[13px] leading-snug text-tinta">
                      {mensagem.conteudo}
                    </p>
                  ) : null}

                      {/* Procedência do encaminhamento: o texto veio de outro
                          atendimento, e o protocolo de origem é o que permite
                          conferir isso depois. */}
                      {mensagem.encaminhadaDeProtocolo ? (
                        <p className="mb-1.5 flex items-center gap-1 text-[10px] text-tinta-fraca">
                          <Forward className="size-3 shrink-0" />
                          Encaminhada de{' '}
                          <span className="etiqueta-protocolo text-[10px]">
                            {mensagem.encaminhadaDeProtocolo}
                          </span>
                        </p>
                      ) : null}

                      <div className="mt-1 flex flex-wrap items-center justify-end gap-1 font-dado text-[10px] text-tinta-fraca">
                        {mensagem.oculta && mensagem.ocultaPorNome ? (
                          <span className="mr-auto font-corpo italic">
                            oculta por {mensagem.ocultaPorNome}
                          </span>
                        ) : null}
                        {mensagem.fixada ? (
                          <Pin className="size-3 text-tinta-media" aria-label="Fixada" />
                        ) : null}
                        {formatarHora(mensagem.enviadaEm)}
                        {daServentia ? (
                          <IconeDeEntrega status={mensagem.statusEntrega} automatica={automatica} />
                        ) : null}
                      </div>
                    </article>

                    {!daServentia && !modoSelecao ? (
                      <AcoesDaMensagem mensagem={mensagem} aoCopiar={copiar} aoResponder={aoResponder} />
                    ) : null}
                  </div>
                </ContextMenuTrigger>

                <ContextMenuContent>
                  <ContextMenuItem onSelect={() => aoResponder(mensagem)}>
                    <Reply />
                    Responder
                  </ContextMenuItem>
                  <ContextMenuItem onSelect={() => void copiar(mensagem)}>
                    <Copy />
                    Copiar
                  </ContextMenuItem>

                  <ContextMenuSeparator />

                  <ContextMenuItem onSelect={() => aoEncaminhar([mensagem.id])}>
                    <Forward />
                    Encaminhar
                  </ContextMenuItem>
                  <ContextMenuItem onSelect={() => aoFixar(mensagem)}>
                    {mensagem.fixada ? <PinOff /> : <Pin />}
                    {mensagem.fixada ? 'Desafixar' : 'Fixar no topo'}
                  </ContextMenuItem>
                  <ContextMenuItem onSelect={() => aoEntrarEmSelecao(mensagem.id)}>
                    <ListChecks />
                    Selecionar
                  </ContextMenuItem>

                  <ContextMenuSeparator />

                  {/* "Ocultar", nunca "apagar". A mensagem sai da vista de quem
                      atende e continua no registro, com quem ocultou e quando.
                      Não existe exclusão de mensagem neste sistema. */}
                  <ContextMenuItem onSelect={() => aoOcultar([mensagem.id], !mensagem.oculta)}>
                    {mensagem.oculta ? <Eye /> : <EyeOff />}
                    {mensagem.oculta ? 'Reexibir no fio' : 'Ocultar da conversa'}
                  </ContextMenuItem>
                </ContextMenuContent>
              </ContextMenu>
            )}
          </div>
        )
      })}

        <div ref={fim} />
      </div>
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
    /* O critério é TER HOVER, não ser largo. Com `sm:opacity-0` o esconder
       valia para toda tela acima de 640px, mas o `sm:group-hover:` que
       devolvia a visibilidade já vem embrulhado em `@media (hover: hover)` no
       Tailwind v4 — num tablet de 768px sem mouse, Copiar e Responder ficavam
       invisíveis e inalcançáveis. É o que o DESIGN.md sempre descreveu por
       escrito: permanentes onde não há hover. */
    <div className="flex shrink-0 items-center rounded-md border border-linha bg-branco opacity-100 transition-opacity [@media(hover:hover)]:opacity-0 [@media(hover:hover)]:group-focus-within:opacity-100 [@media(hover:hover)]:group-hover:opacity-100">
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
