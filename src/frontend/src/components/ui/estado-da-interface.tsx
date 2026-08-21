import type { LucideIcon } from 'lucide-react'
import { CircleAlert, FileText, LoaderCircle, SearchX } from 'lucide-react'
import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

type TipoDeEstado = 'carregando' | 'vazio' | 'sem-resultados' | 'erro'

const padroes: Record<
  TipoDeEstado,
  { icone: LucideIcon; titulo: string; descricao?: string }
> = {
  carregando: {
    icone: LoaderCircle,
    titulo: 'Carregando',
    descricao: 'Aguarde um instante.',
  },
  vazio: {
    icone: FileText,
    titulo: 'Nenhum registro ainda',
  },
  'sem-resultados': {
    icone: SearchX,
    titulo: 'Nenhum resultado encontrado',
    descricao: 'Revise os filtros ou tente outro termo de busca.',
  },
  erro: {
    icone: CircleAlert,
    titulo: 'Não foi possível carregar',
    descricao: 'Tente novamente. Se o problema continuar, informe o suporte.',
  },
}

interface EstadoDaInterfaceProps {
  tipo: TipoDeEstado
  titulo?: string
  descricao?: string
  icone?: LucideIcon
  acao?: ReactNode
  preencher?: boolean
  className?: string
}

/**
 * Estado compartilhado para regiões que ainda não têm conteúdo utilizável.
 * O texto continua explícito: cor e ícone nunca são a única explicação.
 */
export function EstadoDaInterface({
  tipo,
  titulo,
  descricao,
  icone,
  acao,
  preencher = false,
  className,
}: EstadoDaInterfaceProps) {
  const padrao = padroes[tipo]
  const Icone = icone ?? padrao.icone
  const carregando = tipo === 'carregando'
  const erro = tipo === 'erro'

  return (
    <div
      role={erro ? 'alert' : 'status'}
      aria-live={erro ? 'assertive' : 'polite'}
      aria-busy={carregando || undefined}
      className={cn(
        'flex flex-col items-center justify-center gap-3 p-6 text-center md:p-10',
        preencher && 'min-h-0 flex-1',
        className,
      )}
    >
      <div
        className={cn(
          'flex size-11 items-center justify-center rounded-md border border-linha-forte text-tinta-fraca',
          erro && 'border-erro text-erro',
        )}
      >
        <Icone aria-hidden="true" className={cn('size-5', carregando && 'animate-spin')} />
      </div>
      <div className="w-full min-w-0 max-w-sm">
        <p className="font-titulo text-[15px] text-tinta">{titulo ?? padrao.titulo}</p>
        {descricao ?? padrao.descricao ? (
          <p className="mt-1 text-sm text-tinta-media [overflow-wrap:anywhere]">
            {descricao ?? padrao.descricao}
          </p>
        ) : null}
      </div>
      {acao ? <div className="mt-1">{acao}</div> : null}
    </div>
  )
}
