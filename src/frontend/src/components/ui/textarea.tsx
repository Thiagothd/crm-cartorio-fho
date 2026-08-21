import * as React from 'react'
import { cn } from '@/lib/utils'

export const Textarea = React.forwardRef<
  HTMLTextAreaElement,
  React.TextareaHTMLAttributes<HTMLTextAreaElement>
>(({ className, ...props }, ref) => (
  <textarea
    ref={ref}
    className={cn(
        // Mesma razao do input: 16px no celular evita o zoom automatico do
        // Safari do iOS, que ampliaria a pagina inteira ao focar a caixa de
        // resposta. A partir de md volta aos 14px da secao 7 do DESIGN.md.
        'flex min-h-[70px] w-full min-w-0 rounded-md border border-input bg-card px-3 py-2 text-base transition-colors duration-[var(--duracao-rapida)] md:text-sm',
      'placeholder:text-muted-foreground',
      'aria-invalid:border-erro aria-invalid:text-erro',
      'disabled:cursor-not-allowed disabled:opacity-50',
      className,
    )}
    {...props}
  />
))

Textarea.displayName = 'Textarea'
