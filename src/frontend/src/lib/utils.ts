import { clsx, type ClassValue } from 'clsx'
import { extendTailwindMerge } from 'tailwind-merge'

/**
 * `rounded-card` (14px, Fase 3.1) e `rounded-balao` (12px, Fase 3.2) são
 * valores de raio PRÓPRIOS deste projeto (index.css), fora da escala nativa
 * do Tailwind (sm/md/lg/xl/2xl/3xl/full). O `twMerge` padrão não sabe que
 * eles pertencem ao mesmo grupo de "raio de borda" que `rounded-md` — então,
 * ao sobrescrever o raio de um componente base (ex.: `<Textarea
 * className="rounded-balao">`), as duas classes coexistem no HTML final e
 * quem vence é a ordem do CSS compilado, não a ordem do `cn(...)`. Já
 * aconteceu: a caixa de envio ficou com 6px em vez de 12px porque
 * `rounded-md` (do componente base) venceu por sorte de posição no arquivo
 * gerado. Registrar os dois tokens no grupo `rounded` corrige isso na fonte,
 * para qualquer uso futuro — não só o de hoje.
 */
const twMerge = extendTailwindMerge({
  extend: {
    classGroups: {
      rounded: ['rounded-card', 'rounded-balao'],
    },
  },
})

/** Junta classes condicionais resolvendo conflitos do Tailwind. */
export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}
