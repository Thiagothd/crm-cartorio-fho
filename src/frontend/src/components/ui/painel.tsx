import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

/**
 * O cartão elevado que embala cada peça de indicador.
 *
 * Extraído em 2026-08-21 de `pagina-visao-geral.tsx` e `pagina-indicadores.tsx`,
 * onde vivia duplicado — com a tela de Relatórios virando quatro abas, a terceira
 * cópia era garantia de as três divergirem no primeiro ajuste de sombra.
 */
export function Painel({
  titulo,
  descricao,
  acao,
  className,
  children,
}: {
  titulo?: string
  descricao?: string
  /** Canto superior direito: filtro local, indicador de atualização, link. */
  acao?: ReactNode
  className?: string
  children: ReactNode
}) {
  return (
    <section
      className={cn(
        // `p-3` no celular, `p-5` a partir de `sm`. Os 40px de padding fixo
        // custavam 12% da largura de uma tela de 360px, e esse cartão embala
        // gráfico: 16px devolvidos é ~1 caractere a mais em cada rótulo de eixo.
        'rounded-card border border-linha bg-branco p-3 shadow-card transition-shadow duration-300 hover:shadow-card-hover sm:p-5',
        className,
      )}
    >
      {titulo || acao ? (
        <div className={cn('flex items-start justify-between gap-3', descricao ? 'mb-4' : 'mb-4')}>
          <div className="min-w-0">
            {titulo ? <h2 className="text-[15px] font-bold tracking-tight text-tinta">{titulo}</h2> : null}
            {descricao ? <p className="mt-1 text-xs text-tinta-media">{descricao}</p> : null}
          </div>
          {acao ? <div className="shrink-0">{acao}</div> : null}
        </div>
      ) : null}
      {children}
    </section>
  )
}
