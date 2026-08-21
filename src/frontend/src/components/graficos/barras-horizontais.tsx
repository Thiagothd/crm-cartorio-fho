import { formatarNumero, formatarPercentual } from '@/lib/formatacao'

export interface ItemDeBarraHorizontal {
  id: string
  rotulo: string
  valor: number
  percentual?: number
}

/**
 * Ranking com a barra COMO FUNDO da linha, não um fio de 2px abaixo do texto.
 * A versão anterior separava rótulo e barra em duas alturas, o que gastava o
 * dobro de espaço vertical e deixava a barra fina demais para comparar dois
 * itens de olho. Com a barra atrás do texto, a linha inteira é o dado: a
 * leitura é imediata e a lista de 8 serviços cabe sem virar parede de traços.
 *
 * Preenchimento em `tinta` translúcido, não em cor de acento — é dado neutro,
 * e cor de acento é orçamento escasso (DESIGN.md 2.1).
 */
export function BarrasHorizontais({ itens }: { itens: ItemDeBarraHorizontal[] }) {
  if (itens.length === 0) {
    return <p className="text-sm text-tinta-fraca">Nenhum serviço vinculado neste período.</p>
  }

  const maximo = Math.max(1, ...itens.map((item) => item.valor))

  return (
    <ul className="flex flex-col gap-1">
      {itens.map((item, i) => (
        <li
          key={item.id}
          className="group relative flex h-9 items-center overflow-hidden rounded-lg px-2.5"
        >
          {/* Trilha preenchida: cresce da esquerda ao montar, escalonada. Só
              transform anima (composto pela GPU), a largura final vem do
              inline style e não é recalculada durante a animação. */}
          <span
            aria-hidden
            className="absolute inset-y-0 left-0 origin-left rounded-lg bg-tinta/[0.07] transition-colors duration-200 group-hover:bg-tinta/[0.14]"
            style={{
              width: `${Math.max(3, (item.valor / maximo) * 100)}%`,
              animation: `crm-crescer-barra 420ms cubic-bezier(0.16, 1, 0.3, 1) ${Math.min(i * 35, 280)}ms both`,
            }}
          />

          <span className="relative z-10 min-w-0 flex-1 truncate pr-3 text-[13px] font-medium text-tinta">
            {item.rotulo}
          </span>
          <span className="relative z-10 shrink-0 font-dado text-xs font-semibold text-tinta">
            {formatarNumero(item.valor)}
            {item.percentual !== undefined ? (
              <span className="ml-1.5 font-sans text-[11px] font-medium text-tinta-fraca">
                {formatarPercentual(item.percentual)}
              </span>
            ) : null}
          </span>
        </li>
      ))}
    </ul>
  )
}
