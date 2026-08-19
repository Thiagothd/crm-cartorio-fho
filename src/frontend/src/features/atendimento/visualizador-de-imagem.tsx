import { useEffect } from 'react'
import { ChevronLeft, ChevronRight, Download, ImageOff, Loader2, X } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogTitle,
} from '@/components/ui/dialog'
import { formatarDataHora, formatarTamanhoDeArquivo } from '@/lib/formatacao'
import { salvarAnexo, usarAnexo } from './cache-de-anexos'
import type { ItemDeMidia } from './midias-da-conversa'

/**
 * A foto ampliada, dentro do painel.
 *
 * Substitui o comportamento antigo, em que clicar na miniatura do fio baixava o
 * arquivo na hora. Ver a foto do documento é o ato mais comum do atendimento —
 * baixar é a exceção, e agora exige pedir.
 *
 * A imagem aberta é identificada pelo ID DA MENSAGEM, nunca por índice. O
 * SignalR reinvalida a conversa a cada mensagem nova, e um índice guardado
 * passaria a apontar para outra foto assim que alguém mandasse uma imagem.
 */
export function VisualizadorDeImagem({
  imagens,
  mensagemAberta,
  aoFechar,
  aoNavegar,
}: {
  imagens: ItemDeMidia[]
  mensagemAberta: string | null
  aoFechar: () => void
  aoNavegar: (mensagemId: string) => void
}) {
  const indice = imagens.findIndex((i) => i.mensagemId === mensagemAberta)
  const item = indice >= 0 ? imagens[indice] : null

  // `imagens` vem mais recente primeiro (midias-da-conversa.ts). "Anterior" é
  // sobre TEMPO, não sobre posição no array: voltar (seta ←) é ir para o que
  // chegou ANTES, ou seja, índice MAIOR nesta lista. Foi a troca que corrigiu
  // as setas invertidas relatadas no teste.
  const anterior = indice >= 0 && indice < imagens.length - 1 ? imagens[indice + 1] : null
  const proxima = indice > 0 ? imagens[indice - 1] : null

  // A imagem sumiu do fio enquanto estava aberta — outro atendente ocultou a
  // mensagem, ou ela foi encerrada e recarregada. Fechar é mais honesto do que
  // deixar um modal vazio ou pular para uma foto que ninguém pediu.
  useEffect(() => {
    if (mensagemAberta !== null && indice < 0) {
      toast.info('Este arquivo não está mais no fio.')
      aoFechar()
    }
  }, [mensagemAberta, indice, aoFechar])

  // Setas do teclado. O preventDefault existe porque, sem ele, a seta rolaria o
  // fio atrás do overlay ao mesmo tempo que troca a foto.
  useEffect(() => {
    if (!item) {
      return
    }

    function aoTeclar(evento: KeyboardEvent) {
      if (evento.key === 'ArrowLeft' && anterior) {
        evento.preventDefault()
        aoNavegar(anterior.mensagemId)
      }

      if (evento.key === 'ArrowRight' && proxima) {
        evento.preventDefault()
        aoNavegar(proxima.mensagemId)
      }
    }

    window.addEventListener('keydown', aoTeclar)
    return () => window.removeEventListener('keydown', aoTeclar)
  }, [item, anterior, proxima, aoNavegar])

  return (
    <Dialog
      open={item !== null}
      onOpenChange={(aberto) => {
        if (!aberto) {
          aoFechar()
        }
      }}
    >
      {item ? (
        <DialogContent
          mostrarFechar={false}
          className="max-w-[min(94vw,68rem)] gap-0 overflow-hidden border-linha-forte bg-branco p-0"
        >
          <div className="flex shrink-0 items-start justify-between gap-3 border-b border-linha px-3 py-2.5 sm:px-4">
            <div className="min-w-0">
              <DialogTitle className="truncate text-sm font-medium leading-tight text-tinta">
                {item.nome}
              </DialogTitle>
              <DialogDescription className="mt-0.5 truncate font-dado text-[11px] text-tinta-media">
                {item.autor}
                {' · '}
                {formatarDataHora(item.enviadaEm)}
                {item.tamanhoBytes ? ` · ${formatarTamanhoDeArquivo(item.tamanhoBytes)}` : ''}
              </DialogDescription>
            </div>

            <div className="flex shrink-0 items-center gap-1.5">
              {imagens.length > 1 ? (
                <span className="hidden font-dado text-[11px] text-tinta-fraca sm:inline" aria-live="polite">
                  {indice + 1} de {imagens.length}
                </span>
              ) : null}

              {/* Contido, e não bordô: a tela já gasta o selo no item de menu
                  ativo e no botão primário. DESIGN.md, seção 2.1. */}
              <Button
                variant="outline"
                size="icon-sm"
                title={`Baixar ${item.nome}`}
                onClick={() => void salvarAnexo(item.conversaId, item.mensagemId, item.nome)}
              >
                <Download />
                <span className="sr-only">Baixar</span>
              </Button>

              <Button variant="ghost" size="icon-sm" onClick={aoFechar} title="Fechar">
                <X />
                <span className="sr-only">Fechar</span>
              </Button>
            </div>
          </div>

          <div className="relative flex min-h-0 items-center justify-center bg-papel p-3 sm:p-4">
            <Palco item={item} />

            {/* Pré-carga das vizinhas: montar o hook já adquire a referência e
                dispara o download, então a seta troca a foto sem esperar a
                rede. Não precisa de API nova no cache — a contagem de
                referências e a deduplicação fazem todo o trabalho. */}
            {anterior ? <Precarga item={anterior} /> : null}
            {proxima ? <Precarga item={proxima} /> : null}

            {anterior ? (
              <Button
                variant="outline"
                size="icon"
                className="absolute left-2 top-1/2 -translate-y-1/2 sm:left-3"
                onClick={() => aoNavegar(anterior.mensagemId)}
                title="Imagem anterior"
              >
                <ChevronLeft />
                <span className="sr-only">Imagem anterior</span>
              </Button>
            ) : null}

            {proxima ? (
              <Button
                variant="outline"
                size="icon"
                className="absolute right-2 top-1/2 -translate-y-1/2 sm:right-3"
                onClick={() => aoNavegar(proxima.mensagemId)}
                title="Próxima imagem"
              >
                <ChevronRight />
                <span className="sr-only">Próxima imagem</span>
              </Button>
            ) : null}
          </div>

          {imagens.length > 1 ? (
            <p className="shrink-0 border-t border-linha px-3 py-1.5 text-center font-dado text-[11px] text-tinta-fraca sm:hidden">
              {indice + 1} de {imagens.length}
            </p>
          ) : null}
        </DialogContent>
      ) : null}
    </Dialog>
  )
}

/** Só segura a referência para o binário chegar antes de o atendente pedir. */
function Precarga({ item }: { item: ItemDeMidia }) {
  usarAnexo(item.conversaId, item.mensagemId)
  return null
}

/** A imagem ampliada, com os estados de carga e de arquivo indisponível. */
function Palco({ item }: { item: ItemDeMidia }) {
  const { url, erro } = usarAnexo(item.conversaId, item.mensagemId)

  if (erro) {
    return (
      <p className="flex h-40 items-center justify-center gap-2 text-xs text-tinta-media">
        <ImageOff className="size-4 shrink-0" />
        {erro}
      </p>
    )
  }

  if (!url) {
    return (
      <p className="flex h-40 items-center justify-center gap-2 text-xs text-tinta-fraca">
        <Loader2 className="size-4 shrink-0 animate-spin" />
        Carregando…
      </p>
    )
  }

  return (
    <img
      src={url}
      alt={item.nome}
      className="max-h-[70vh] max-w-full rounded-md border border-linha-forte object-contain"
    />
  )
}
