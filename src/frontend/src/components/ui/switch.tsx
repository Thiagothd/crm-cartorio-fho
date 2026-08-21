import * as React from 'react'
import * as SwitchPrimitive from '@radix-ui/react-switch'
import { cn } from '@/lib/utils'

export const Switch = React.forwardRef<
  React.ComponentRef<typeof SwitchPrimitive.Root>,
  React.ComponentPropsWithoutRef<typeof SwitchPrimitive.Root>
>(({ className, ...props }, ref) => (
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
      className="pointer-events-none relative inline-flex h-5 w-9 items-center rounded-full bg-input transition-colors duration-[var(--duracao-rapida)] group-data-[state=checked]:bg-primary"
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
