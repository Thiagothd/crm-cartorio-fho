import { Badge } from '@/components/ui/badge'
import { descreverDistanciaDePrazo, formatarDataHora } from '@/lib/formatacao'
import type { SituacaoPrazo } from '@/lib/api/tipos'

/**
 * Mapeamento fixo de docs/DESIGN.md, secao 5. Sem icone: a cor e a palavra
 * bastam, e icone em cada linha da inbox viraria ruido.
 *
 * Atrasada usa bordo porque atraso em cartorio e falha de prazo legal — e a
 * unica situacao de prazo que merece o peso do selo.
 */
const aparencia = {
  EmDia: { variante: 'success', rotulo: 'No prazo' },
  EmAlerta: { variante: 'warning', rotulo: 'Em alerta' },
  Atrasada: { variante: 'destructive', rotulo: 'Atrasada' },
} as const

export function EtiquetaDePrazo({
  situacao,
  prazoLimite,
  compacta = false,
}: {
  situacao: SituacaoPrazo
  prazoLimite: string | null
  compacta?: boolean
}) {
  if (situacao === 'SemPrazo') {
    return null
  }

  const { variante, rotulo } = aparencia[situacao]
  const distancia = descreverDistanciaDePrazo(prazoLimite)

  return (
    <Badge
      variant={variante}
      title={prazoLimite ? `Prazo até ${formatarDataHora(prazoLimite)}` : undefined}
    >
      <span className="font-dado">{compacta ? distancia : `${rotulo} · ${distancia}`}</span>
    </Badge>
  )
}
