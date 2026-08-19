import * as React from 'react'
import { cn } from '@/lib/utils'

export const Input = React.forwardRef<HTMLInputElement, React.InputHTMLAttributes<HTMLInputElement>>(
  ({ className, type, ...props }, ref) => (
    <input
      ref={ref}
      type={type}
      className={cn(
        // 16px no celular, 14px a partir de md. Nao e capricho: o Safari do iOS
        // da zoom na PAGINA INTEIRA ao focar campo com fonte menor que 16px, e
        // um layout de 390px viraria um 320px cortado. Acima de md nada muda, e
        // a densidade da secao 7 do DESIGN.md continua valendo.
        'flex h-9 w-full rounded-md border border-input bg-card px-3 py-1 text-base transition-colors md:text-sm',
        'placeholder:text-muted-foreground',
        '',
        'disabled:cursor-not-allowed disabled:opacity-50',
        className,
      )}
      {...props}
    />
  ),
)

Input.displayName = 'Input'
