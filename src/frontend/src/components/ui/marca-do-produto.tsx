import { usarTema } from '@/lib/tema'
import { cn } from '@/lib/utils'

export function MarcaDoProduto({
  simbolo = false,
  alt = '',
  className,
  prioridade = false,
}: {
  simbolo?: boolean
  alt?: string
  className?: string
  prioridade?: boolean
}) {
  const { tema } = usarTema()
  const escuro = tema === 'escuro'
  const arquivo = simbolo
    ? escuro ? 'crm-cartorio-simbolo-escuro.webp' : 'crm-cartorio-simbolo.png'
    : escuro ? 'crm-cartorio-logo-escuro.webp' : 'crm-cartorio-logo-horizontal.webp'

  return (
    <img
      src={`/brand/${arquivo}`}
      alt={alt}
      width={simbolo ? 304 : 820}
      height={simbolo ? 304 : 200}
      decoding={prioridade ? 'sync' : 'async'}
      fetchPriority={prioridade ? 'high' : undefined}
      className={cn('marca-imagem shrink-0', simbolo ? 'aspect-square object-contain' : 'marca-horizontal', className)}
    />
  )
}
