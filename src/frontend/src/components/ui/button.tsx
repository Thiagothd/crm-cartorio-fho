import * as React from 'react'
import { Slot } from '@radix-ui/react-slot'
import { cva, type VariantProps } from 'class-variance-authority'
import { cn } from '@/lib/utils'

// Sem sombra, sem gradiente, borda 1px, raio unico. docs/DESIGN.md, secao 4.
// O foco usa outline (definido em index.css), nao ring — ring e box-shadow.
const variantesDoBotao = cva(
  'inline-flex touch-manipulation items-center justify-center gap-2 whitespace-nowrap rounded-md border text-sm font-medium transition-colors duration-[var(--duracao-rapida)] disabled:pointer-events-none disabled:opacity-50 [&_svg]:pointer-events-none [&_svg]:size-4 [&_svg]:shrink-0',
  {
    variants: {
      variant: {
        // Bordo. Conta para o limite de 3 ocorrencias por tela (DESIGN.md 2.1).
        default: 'border-selo bg-selo text-branco hover:bg-selo/90',

        destructive: 'border-selo bg-selo text-branco hover:bg-selo/90',
        outline: 'border-linha-forte bg-branco text-tinta hover:bg-superficie',
        secondary: 'border-linha bg-superficie text-tinta hover:bg-papel',
        ghost: 'border-transparent text-tinta-media hover:bg-superficie hover:text-tinta',
        link: 'border-transparent text-selo underline-offset-4 hover:underline',
      },
      size: {
        default: 'h-11 px-4 py-2 md:h-9',
        sm: 'h-10 rounded-md px-3 text-xs md:h-8',
        lg: 'h-11 rounded-md px-6 md:h-10',
        icon: 'size-11 md:size-9',
        'icon-sm': 'size-10 md:size-8',
      },
    },
    defaultVariants: {
      variant: 'default',
      size: 'default',
    },
  },
)

export interface BotaoProps
  extends React.ButtonHTMLAttributes<HTMLButtonElement>,
    VariantProps<typeof variantesDoBotao> {
  asChild?: boolean
}

export const Button = React.forwardRef<HTMLButtonElement, BotaoProps>(
  ({ className, variant, size, asChild = false, ...props }, ref) => {
    const Componente = asChild ? Slot : 'button'

    return (
      <Componente
        ref={ref}
        className={cn(variantesDoBotao({ variant, size }), className)}
        {...props}
      />
    )
  },
)

Button.displayName = 'Button'

export { variantesDoBotao }
