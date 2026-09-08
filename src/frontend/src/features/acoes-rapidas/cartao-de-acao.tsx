import { Link } from 'react-router-dom'
import { Skeleton } from '@/components/ui/skeleton'
import { formatarNumero } from '@/lib/formatacao'
import { cn } from '@/lib/utils'
import type { AcaoRapida } from './catalogo-de-acoes'

/** `undefined` = carregando (só na primeira carga) · `null` = sem contador ou indisponível. */
type Contador = number | null | undefined

export function CartaoDeAcao({
  acao,
  contador,
  aoAcionar,
  aoPrefetch,
}: {
  acao: AcaoRapida
  contador: Contador
  aoAcionar?: () => void
  aoPrefetch?: () => void
}) {
  const Icone = acao.icone
  const temContador = acao.tipo === 'rota' && Boolean(acao.contador)
  const carregando = temContador && contador === undefined
  const numero = typeof contador === 'number' ? contador : null

  const classe = cn(
    'group flex min-h-11 flex-col justify-between gap-3 rounded-card border border-linha bg-branco p-3 text-left shadow-card',
    'transition-all duration-200 hover:-translate-y-0.5 hover:border-linha-forte hover:shadow-card-hover sm:p-4',
  )

  const nomeAcessivel = numero !== null ? `${acao.rotulo}: ${formatarNumero(numero)}` : acao.rotulo

  const miolo = (
    <>
      <span className="flex items-start justify-between gap-2">
        <span className="min-w-0 text-[13px] font-semibold leading-tight tracking-tight text-tinta">
          {acao.rotulo}
        </span>
        <span
          aria-hidden
          className="flex size-7 shrink-0 items-center justify-center rounded-lg bg-superficie text-tinta-media transition-colors duration-200 group-hover:bg-papel group-hover:text-tinta"
        >
          <Icone className="size-3.5" />
        </span>
      </span>

      <span className="flex items-end justify-between gap-2">
        {carregando ? (
          <Skeleton aria-hidden className="h-[26px] w-9" />
        ) : numero !== null ? (
          <span
            className={cn(
              'font-dado text-[26px] font-bold leading-none tracking-tight',
              acao.tipo === 'rota' && acao.tom === 'selo' && numero > 0 && 'text-selo',
              acao.tipo === 'rota' && acao.tom === 'ambar' && numero > 0 && 'text-ambar',
              (numero === 0 || (acao.tipo === 'rota' && !acao.tom)) && 'text-tinta',
            )}
          >
            {formatarNumero(numero)}
          </span>
        ) : temContador ? (
          <span className="font-dado text-[26px] font-bold leading-none text-tinta-fraca">—</span>
        ) : (
          <span className="text-[11px] font-medium leading-snug text-tinta-media">{acao.descricao}</span>
        )}

        {acao.tipo === 'comando' && acao.atalho ? (
          <kbd className="hidden shrink-0 whitespace-nowrap rounded border border-linha-forte px-1.5 py-0.5 font-dado text-[10px] leading-none text-tinta-fraca md:inline-flex md:items-center">
            {acao.atalho}
          </kbd>
        ) : null}
      </span>

      {temContador ? <span className="sr-only">{acao.descricao}</span> : null}
    </>
  )

  if (acao.tipo === 'rota') {
    return (
      <Link
        to={acao.rota}
        aria-label={nomeAcessivel}
        aria-busy={carregando || undefined}
        onPointerEnter={aoPrefetch}
        onFocus={aoPrefetch}
        className={classe}
      >
        {miolo}
      </Link>
    )
  }

  return (
    <button type="button" onClick={aoAcionar} aria-label={acao.rotulo} className={classe}>
      {miolo}
    </button>
  )
}
