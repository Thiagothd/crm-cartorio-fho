import * as React from 'react'
import * as SwitchPrimitive from '@radix-ui/react-switch'
import { cn } from '@/lib/utils'

/**
 * `tom` existe por causa do teto de 3 bordôs por tela (DESIGN.md 2.1), não por
 * gosto: ligado, o trilho é `selo`, e uma tela com item de rail ativo, dois
 * switches e um botão primário passa de três sem que ninguém tenha decidido
 * isso. A saída que a própria 2.1 manda é `tinta` — e é exatamente o que
 * `tom="tinta"` faz, sem cor nova e sem exceção fora de token.
 *
 * Regra de uso: o switch que comanda a tela fica em `selo`; sub-interruptor
 * dentro do escopo de outro fica em `tinta`. Nunca o contrário — o mais escuro
 * lendo como principal inverteria a hierarquia.
 */
export const Switch = React.forwardRef<
  React.ComponentRef<typeof SwitchPrimitive.Root>,
  React.ComponentPropsWithoutRef<typeof SwitchPrimitive.Root> & { tom?: 'selo' | 'tinta' }
>(({ className, tom = 'selo', ...props }, ref) => (
  <SwitchPrimitive.Root
    ref={ref}
    className={cn(
      'group peer inline-flex size-11 shrink-0 touch-manipulation cursor-pointer items-center justify-center rounded-md border border-transparent transition-colors duration-[var(--duracao-rapida)] md:size-9',
      'disabled:cursor-not-allowed disabled:opacity-50',
      className,
    )}
    {...props}
  >
    <span
      aria-hidden="true"
      className={cn(
        'pointer-events-none relative inline-flex h-5 w-9 items-center rounded-full bg-input transition-colors duration-[var(--duracao-rapida)]',
        tom === 'tinta' ? 'group-data-[state=checked]:bg-selecao' : 'group-data-[state=checked]:bg-primary',
      )}
    >
      <SwitchPrimitive.Thumb
        className={cn(
          'pointer-events-none block size-4 rounded-full bg-white transition-transform duration-[var(--duracao-rapida)]',
          'data-[state=checked]:translate-x-4 data-[state=unchecked]:translate-x-0',
        )}
      />
    </span>
  </SwitchPrimitive.Root>
))

Switch.displayName = SwitchPrimitive.Root.displayName
