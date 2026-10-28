import { fireEvent, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { renderizarComProvedores } from '@/test/utils'
import { FiltrosDaInbox, type FiltrosVisiveis } from './filtros-da-inbox'

const BASE: FiltrosVisiveis = { status: 'Ativa', escopo: 'fila', busca: '' }

function renderizar(filtros: FiltrosVisiveis, aoMudar = vi.fn()) {
  renderizarComProvedores(
    <FiltrosDaInbox
      filtros={filtros}
      setores={[]}
      etapas={[]}
      aoMudar={aoMudar}
      aoRecolher={vi.fn()}
      aoCriarConversa={vi.fn()}
    />,
  )
  return aoMudar
}

/**
 * Regressão de 2026-10-28: o Chrome preencheu a busca da inbox com o nome
 * completo do atendente, e a lista ficou em "0 de 0" com conversas ativas.
 */
describe('FiltrosDaInbox — busca', () => {
  it('fica fora do preenchimento automático do navegador', () => {
    renderizar(BASE)

    const campo = screen.getByRole('searchbox', { name: /Buscar atendimento/ })
    expect(campo).toHaveAttribute('type', 'search')
    expect(campo).toHaveAttribute('autocomplete', 'off')
    // Nome neutro: "nome"/"name" no name/id é o que a heurística do Chrome lê.
    expect(campo.getAttribute('name') ?? '').not.toMatch(/nome|name/i)
  })

  it('sem busca, não mostra o botão de limpar', () => {
    renderizar(BASE)

    expect(screen.queryByRole('button', { name: 'Limpar busca' })).not.toBeInTheDocument()
  })

  it('com busca, o botão de limpar zera só a busca', () => {
    const aoMudar = renderizar({ ...BASE, busca: 'RAFAEL CAMPOS' })

    fireEvent.click(screen.getByRole('button', { name: 'Limpar busca' }))

    expect(aoMudar).toHaveBeenCalledWith({ ...BASE, busca: '' })
  })
})
