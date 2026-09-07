import { cn } from '@/lib/utils'
import type { ModeloDePermissaoCatalogado } from '@/lib/api/tipos'

/**
 * Um dos 6 modelos prontos — clicar aplica o conjunto inteiro dele ao estado
 * PENDENTE (nunca salva sozinho; a barra do rodapé é quem confirma). "Ativo"
 * é calculado por comparação de conjunto, não pelo rótulo salvo: assim o
 * cartão acende mesmo quando alguém montou à mão exatamente o que um modelo
 * já descreveria.
 */
export function CartaoDeModelo({
  modelo,
  ativo,
  aoAplicar,
}: {
  modelo: ModeloDePermissaoCatalogado
  ativo: boolean
  aoAplicar: () => void
}) {
  return (
    <button
      type="button"
      aria-pressed={ativo}
      onClick={aoAplicar}
      className={cn(
        'flex flex-col gap-1 rounded-card border p-3.5 text-left transition-all duration-200 hover:-translate-y-0.5',
        ativo
          ? 'border-tinta bg-tinta text-branco shadow-card-hover'
          : 'border-linha bg-branco shadow-card hover:border-linha-forte',
      )}
    >
      <span className="text-sm font-semibold">{modelo.rotulo}</span>
      <span className={cn('text-xs', ativo ? 'text-branco/80' : 'text-tinta-media')}>{modelo.descricao}</span>
      <span className={cn('mt-1 font-dado text-[11px]', ativo ? 'text-branco/70' : 'text-tinta-fraca')}>
        {modelo.chaves.length} permissões
      </span>
    </button>
  )
}
