import * as Menu from '@radix-ui/react-dropdown-menu'
import { Check, Monitor, Moon, Sun } from 'lucide-react'
import { usarTema, type PreferenciaDeTema } from '@/lib/tema'
import { cn } from '@/lib/utils'
import { DropdownMenu, DropdownMenuContent, DropdownMenuTrigger } from './dropdown-menu'

const temas = [
  { valor: 'claro', rotulo: 'Claro', Icone: Sun },
  { valor: 'escuro', rotulo: 'Escuro', Icone: Moon },
  { valor: 'sistema', rotulo: 'Sistema', Icone: Monitor },
] as const

export function SeletorDeTema({ expandido = false }: { expandido?: boolean }) {
  const { preferencia, definirTema } = usarTema()
  const atual = temas.find((opcao) => opcao.valor === preferencia)!

  if (expandido) {
    return (
      <fieldset className="min-w-0">
        <legend className="mb-3 text-sm font-semibold text-tinta">Tema da interface</legend>
        <div className="grid max-w-xl grid-cols-3 gap-2">
          {temas.map(({ valor, rotulo, Icone }) => (
            <label key={valor} className="relative min-w-0 cursor-pointer">
              <input
                type="radio"
                name="tema-da-interface"
                value={valor}
                checked={preferencia === valor}
                onChange={() => definirTema(valor)}
                className="peer absolute inset-0 z-10 size-full cursor-pointer opacity-0"
              />
              <span className="flex min-h-24 flex-col items-center justify-center gap-2 rounded-lg border border-linha-forte bg-branco p-3 text-sm text-tinta-media transition-colors hover:bg-superficie peer-checked:border-primary peer-checked:bg-primary/10 peer-checked:text-informacao peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-foco">
                <Icone className="size-5" aria-hidden />
                {rotulo}
              </span>
              {preferencia === valor ? <Check className="absolute right-2 top-2 size-3.5 text-informacao" aria-hidden /> : null}
            </label>
          ))}
        </div>
      </fieldset>
    )
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <button
          type="button"
          aria-label={`Tema: ${atual.rotulo}`}
          title={`Tema: ${atual.rotulo}`}
          className="flex size-11 shrink-0 items-center justify-center rounded-lg border border-transparent bg-branco text-tinta-media transition-colors hover:border-linha-forte hover:bg-superficie hover:text-tinta md:size-9"
        >
          <atual.Icone className="size-4" aria-hidden />
        </button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <Menu.RadioGroup value={preferencia} onValueChange={(valor) => definirTema(valor as PreferenciaDeTema)} aria-label="Tema da interface">
          {temas.map(({ valor, rotulo, Icone }) => (
            <Menu.RadioItem
              key={valor}
              value={valor}
              className={cn('flex min-h-11 cursor-pointer items-center gap-2 rounded-md px-3 text-sm outline-none focus:bg-accent focus:text-accent-foreground', preferencia === valor && 'text-informacao')}
            >
              <Icone className="size-4" aria-hidden />
              {rotulo}
              <Menu.ItemIndicator className="ml-auto"><Check className="size-4" aria-hidden /></Menu.ItemIndicator>
            </Menu.RadioItem>
          ))}
        </Menu.RadioGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
