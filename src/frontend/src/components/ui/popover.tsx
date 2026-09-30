import * as React from 'react'
import * as PopoverPrimitive from '@radix-ui/react-popover'
import { cn } from '@/lib/utils'

/**
 * Janela leve presa a um botão, para conteúdo interativo (interruptores,
 * escolhas). Diferente de `DropdownMenu`, que é lista de ações: dentro de um
 * menu o Tab fecha a lista e as setas só andam entre itens, e um interruptor
 * ali fica inalcançável pelo teclado.
 *
 * Mesma casca visual do `DropdownMenuContent` — borda, raio, sombra de cartão
 * elevado — para as duas janelas do cabeçalho parecerem da mesma família.
 */
export const Popover = PopoverPrimitive.Root
export const PopoverTrigger = PopoverPrimitive.Trigger
export const PopoverClose = PopoverPrimitive.Close

export const PopoverContent = React.forwardRef<
  React.ComponentRef<typeof PopoverPrimitive.Content>,
  React.ComponentPropsWithoutRef<typeof PopoverPrimitive.Content>
>(({ className, align = 'end', sideOffset = 6, collisionPadding = 12, ...props }, ref) => (
  <PopoverPrimitive.Portal>
    <PopoverPrimitive.Content
      ref={ref}
      align={align}
      sideOffset={sideOffset}
      collisionPadding={collisionPadding}
      className={cn(
        'z-50 w-80 max-w-[calc(100vw-1.5rem)] rounded-lg border border-border bg-popover text-popover-foreground shadow-card-hover outline-none',
        className,
      )}
      {...props}
    />
  </PopoverPrimitive.Portal>
))
PopoverContent.displayName = PopoverPrimitive.Content.displayName
