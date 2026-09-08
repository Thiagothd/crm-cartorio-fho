import { ChevronLeft, ChevronRight } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { formatarNumero } from '@/lib/formatacao'

/** Paginação simples de "Anterior/Próxima" — usada pelas duas tabelas server-side da Central (tenants e histórico). */
export function Paginacao({
  pagina,
  tamanhoDaPagina,
  total,
  aoMudarPagina,
}: {
  pagina: number
  tamanhoDaPagina: number
  total: number
  aoMudarPagina: (pagina: number) => void
}) {
  const totalDePaginas = Math.max(1, Math.ceil(total / tamanhoDaPagina))

  if (totalDePaginas <= 1) {
    return null
  }

  return (
    <div className="flex items-center justify-between gap-3 border-t border-linha pt-3">
      <p className="text-xs text-tinta-fraca">{formatarNumero(total)} no total</p>

      <div className="flex items-center gap-2">
        <Button
          variant="outline"
          size="sm"
          disabled={pagina <= 1}
          onClick={() => aoMudarPagina(pagina - 1)}
        >
          <ChevronLeft />
          Anterior
        </Button>
        <span className="font-dado text-xs text-tinta-media">
          {pagina} / {totalDePaginas}
        </span>
        <Button
          variant="outline"
          size="sm"
          disabled={pagina >= totalDePaginas}
          onClick={() => aoMudarPagina(pagina + 1)}
        >
          Próxima
          <ChevronRight />
        </Button>
      </div>
    </div>
  )
}
