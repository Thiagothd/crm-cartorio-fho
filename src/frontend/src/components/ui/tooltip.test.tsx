import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@/components/ui/tooltip'

describe('Tooltip', () => {
  it('expõe a dica ao navegar pelo teclado', async () => {
    const usuario = userEvent.setup()

    render(
      <TooltipProvider delayDuration={0}>
        <Tooltip>
          <TooltipTrigger asChild>
            <button type="button" aria-label="Recolher menu">
              M
            </button>
          </TooltipTrigger>
          <TooltipContent>Recolher menu</TooltipContent>
        </Tooltip>
      </TooltipProvider>,
    )

    await usuario.tab()

    expect(await screen.findByRole('tooltip')).toHaveTextContent('Recolher menu')
  })
})
