import * as React from 'react'
import { cva, type VariantProps } from 'class-variance-authority'
import { cn } from '@/lib/utils'

/**
 * Etiqueta de estado. Borda 1px e texto colorido — sem fundo saturado.
 * docs/DESIGN.md, secao 5: cor tem significado fixo.
 */
const variantesDaEtiqueta = cva(
  'inline-flex items-center gap-1 rounded-md border px-1.5 py-0.5 text-xs font-medium',
  {
    variants: {
      variant: {
        default: 'border-linha-forte bg-superficie text-tinta-media',
        secondary: 'border-linha bg-superficie text-tinta-fraca',
        outline: 'border-linha-forte bg-transparent text-tinta-media',

        /** Concluido, em dia. */
        success: 'border-verde/45 bg-verde/8 text-verde',

        /** Alerta, pendencia, prazo apertado. */
        warning: 'border-ambar/45 bg-ambar/8 text-ambar',

        /** Atraso. Usa bordo — conta para o limite de 3 por tela. */
        destructive: 'border-selo/45 bg-selo/8 text-selo',
      },
    },
    defaultVariants: {
      variant: 'default',
    },
  },
)

export interface EtiquetaProps
  extends React.HTMLAttributes<HTMLSpanElement>,
    VariantProps<typeof variantesDaEtiqueta> {}

export function Badge({ className, variant, ...props }: EtiquetaProps) {
  return <span className={cn(variantesDaEtiqueta({ variant }), className)} {...props} />
}
