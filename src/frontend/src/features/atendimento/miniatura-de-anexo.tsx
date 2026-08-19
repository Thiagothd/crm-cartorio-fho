import { useEffect, useRef, useState } from 'react'
import { Film, ImageOff, RotateCcw } from 'lucide-react'
import { Skeleton } from '@/components/ui/skeleton'
import { formatarDataHora } from '@/lib/formatacao'
import { esquecerAnexo, salvarAnexo, usarAnexo } from './cache-de-anexos'
import type { ItemDeMidia } from './midias-da-conversa'

/**
 * Uma célula da grade de mídias.
 *
 * SÓ BAIXA O QUE ENTRA NA TELA. A conversa de um cartório acumula foto de
 * documento por meses, e não existe miniatura gerada no servidor — o binário da
 * célula é o arquivo inteiro. Sem o `IntersectionObserver`, abrir a aba Mídia
 * de uma conversa antiga puxaria dezenas de megabytes de uma vez.
 *
 * A falha é local por construção: cada célula tem o próprio `usarAnexo`, então
 * um arquivo sumido do disco derruba a própria caixa e mais nada da grade.
 */
export function MiniaturaDeAnexo({
  item,
  aoAbrir,
}: {
  item: ItemDeMidia
  aoAbrir: (mensagemId: string) => void
}) {
  const caixa = useRef<HTMLDivElement>(null)
  const [visivel, setVisivel] = useState(false)

  // Remontar a célula é o que faz "tentar de novo" funcionar: o pedido do
  // binário mora num efeito lá dentro, e sem trocar a chave ele não rodaria de
  // novo — as dependências continuam as mesmas.
  const [tentativa, setTentativa] = useState(0)

  useEffect(() => {
    const alvo = caixa.current

    if (!alvo || visivel) {
      return
    }

    // 200px de folga: a próxima fileira já está pronta quando chega à vista.
    const observador = new IntersectionObserver(
      (entradas) => {
        if (entradas.some((e) => e.isIntersecting)) {
          // Uma vez visível, nunca mais volta a false. Despriorizar ao rolar
          // faria a célula soltar e repedir o mesmo arquivo a cada passada —
          // quem controla memória aqui é o descarte do cache, não o observer.
          setVisivel(true)
          observador.disconnect()
        }
      },
      { rootMargin: '200px' },
    )

    observador.observe(alvo)
    return () => observador.disconnect()
  }, [visivel])

  const legenda = `${item.nome} — ${formatarDataHora(item.enviadaEm)}`

  return (
    <div ref={caixa} className={item.oculta ? 'opacity-60' : undefined}>
      {/* Vídeo não pré-carrega: sem miniatura no servidor, gerar um quadro
          exigiria baixar o arquivo inteiro, e um vídeo sozinho estouraria o
          teto do cache. */}
      {item.ehVideo ? (
        <button
          type="button"
          onClick={() => void salvarAnexo(item.conversaId, item.mensagemId, item.nome)}
          className="flex aspect-square w-full flex-col items-center justify-center gap-1 rounded-md border border-linha-forte bg-papel p-1 transition-colors hover:bg-superficie"
          title={`${legenda}. Clique para baixar.`}
        >
          <Film className="size-5 shrink-0 text-tinta-media" />
          <span className="w-full truncate px-0.5 text-[10px] text-tinta-fraca">{item.nome}</span>
        </button>
      ) : (
        <CelulaDeImagem
          key={tentativa}
          item={item}
          visivel={visivel}
          legenda={legenda}
          aoAbrir={aoAbrir}
          aoTentarDeNovo={() => {
            esquecerAnexo(item.conversaId, item.mensagemId)
            setTentativa((n) => n + 1)
          }}
        />
      )}
    </div>
  )
}

function CelulaDeImagem({
  item,
  visivel,
  legenda,
  aoAbrir,
  aoTentarDeNovo,
}: {
  item: ItemDeMidia
  visivel: boolean
  legenda: string
  aoAbrir: (mensagemId: string) => void
  aoTentarDeNovo: () => void
}) {
  const [quebrada, setQuebrada] = useState(false)
  const { url, erro } = usarAnexo(item.conversaId, item.mensagemId, visivel)

  if (erro || quebrada) {
    return (
      <div className="flex aspect-square flex-col items-center justify-center gap-1 rounded-md border border-linha-forte bg-superficie p-1 text-center">
        <ImageOff className="size-4 shrink-0 text-tinta-fraca" />
        <button
          type="button"
          onClick={aoTentarDeNovo}
          className="flex items-center gap-1 text-[10px] text-tinta-media underline-offset-2 hover:text-tinta hover:underline"
        >
          <RotateCcw className="size-2.5" />
          tentar
        </button>
      </div>
    )
  }

  if (!url) {
    return <Skeleton className="aspect-square w-full" />
  }

  return (
    <button
      type="button"
      onClick={() => aoAbrir(item.mensagemId)}
      className="block aspect-square w-full overflow-hidden rounded-md border border-linha-forte bg-papel transition-opacity hover:opacity-90"
      title={legenda}
      aria-label={legenda}
    >
      <img
        src={url}
        alt={item.nome}
        className="size-full object-cover"
        /* Download que devolve 200 com arquivo corrompido passa pelo cache sem
           erro e mostraria o glifo de imagem quebrada do navegador. */
        onError={() => setQuebrada(true)}
      />
    </button>
  )
}
