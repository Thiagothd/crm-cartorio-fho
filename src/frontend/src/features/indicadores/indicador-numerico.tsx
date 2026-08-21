import { useEffect, useState } from 'react'
import { cn } from '@/lib/utils'
import { formatarNumero } from '@/lib/formatacao'

/**
 * Conta de 0 até o valor final em ~280ms, só na montagem — quem quiser
 * recontar ao trocar de período remonta este componente com uma `key`
 * diferente. Uma vez terminada a animação, o valor passa a refletir a prop
 * direto: um refetch de fundo que troque o número não deve fazê-lo "tremer"
 * de novo (regra do plano — nunca animar em atualização silenciosa).
 */
function useContagemAnimada(valorFinal: number) {
  const [exibido, setExibido] = useState(0)
  const [concluida, setConcluida] = useState(false)

  useEffect(() => {
    const reduzido =
      typeof window.matchMedia === 'function' && window.matchMedia('(prefers-reduced-motion: reduce)').matches

    if (reduzido) {
      setExibido(valorFinal)
      setConcluida(true)
      return
    }

    let vivo = true
    const inicio = performance.now()
    const duracao = 280

    function passo(agora: number) {
      if (!vivo) return

      const progresso = Math.min(1, (agora - inicio) / duracao)
      setExibido(Math.round(valorFinal * progresso))

      if (progresso < 1) {
        requestAnimationFrame(passo)
      } else {
        setConcluida(true)
      }
    }

    requestAnimationFrame(passo)

    return () => {
      vivo = false
    }
    // Só na montagem — de propósito (ver comentário acima).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  return concluida ? valorFinal : exibido
}

export function IndicadorNumerico({
  rotulo,
  valor,
  cor,
  ativo = false,
  aoSelecionar,
}: {
  rotulo: string
  valor: number
  cor?: 'ambar' | 'selo'
  ativo?: boolean
  aoSelecionar?: () => void
}) {
  const exibido = useContagemAnimada(valor)

  const conteudo = (
    <>
      <span
        className={cn(
          'text-[28px] font-bold leading-none tracking-tight tabular-nums',
          cor === 'selo' && valor > 0 && 'text-selo',
          cor === 'ambar' && valor > 0 && 'text-ambar',
          (!cor || valor === 0) && 'text-tinta',
        )}
      >
        {formatarNumero(exibido)}
      </span>
      <span className="text-[11px] font-medium uppercase tracking-wider text-tinta-fraca">{rotulo}</span>
    </>
  )

  if (!aoSelecionar) {
    return <div className="flex flex-1 flex-col gap-1 rounded-lg px-3 py-2.5 md:px-3.5">{conteudo}</div>
  }

  return (
    <button
      type="button"
      onClick={aoSelecionar}
      aria-pressed={ativo}
      title={`Ver ${rotulo.toLocaleLowerCase('pt-BR')} na inbox`}
      className={cn(
        'flex min-h-11 flex-1 flex-col gap-1 rounded-lg border border-transparent px-3 py-2.5 text-left transition-all duration-200 hover:-translate-y-0.5 hover:border-linha hover:bg-superficie hover:shadow-card md:px-3.5',
        ativo && 'border-linha bg-superficie shadow-card',
      )}
    >
      {conteudo}
    </button>
  )
}
