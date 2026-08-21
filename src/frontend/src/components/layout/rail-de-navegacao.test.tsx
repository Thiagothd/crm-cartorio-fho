import { act } from 'react'
import { fireEvent, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { RailDeNavegacao } from './rail-de-navegacao'
import { renderizarComProvedores } from '@/test/utils'

vi.mock('@/lib/auth/usar-autenticacao', () => ({
  usarAutenticacao: () => ({
    atendente: {
      nome: 'Atendente de Teste',
      email: 'atendente@cartorio.com.br',
      cargo: 'Escrevente',
      cargoDescrito: 'Escrevente',
    },
    tenant: { nomeFantasia: 'Cartório de Teste', razaoSocial: 'Cartório de Teste Ltda.' },
    sair: vi.fn(),
    podeGerirCadastros: true,
    podeGerirAtendentes: true,
  }),
}))

describe('RailDeNavegacao', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  // Regressão: o painel ficava aberto sobre a página nova até o ponteiro sair
  // da área do rail. Como o ponteiro fica exatamente onde clicou, na prática só
  // um clique em outro lugar da tela fechava.
  it('recolhe o painel ao escolher um destino', () => {
    const { container } = renderizarComProvedores(<RailDeNavegacao />)
    const aside = container.querySelector('aside')!

    fireEvent.pointerEnter(aside)
    act(() => {
      vi.advanceTimersByTime(160)
    })
    expect(aside).toHaveClass('w-64')

    // Sem `pointerLeave`: o ponteiro continua sobre o rail, exatamente como
    // fica depois de um clique de verdade.
    fireEvent.click(screen.getByRole('link', { name: 'Clientes' }))
    act(() => {
      vi.runOnlyPendingTimers()
    })

    expect(aside).toHaveClass('w-16')
  })

  it('não deixa o rail rolar na horizontal quando fechado', () => {
    // Os rótulos seguem ocupando largura mesmo em `opacity-0`; com
    // `overflow-y-auto` sozinho o CSS promove o eixo X para `auto` e desenha uma
    // barra de rolagem horizontal no pé do rail fechado.
    const { container } = renderizarComProvedores(<RailDeNavegacao />)

    expect(container.querySelector('nav')).toHaveClass('overflow-x-hidden')
  })

  it('sempre expõe nome acessível dos itens, mesmo fechado', () => {
    renderizarComProvedores(<RailDeNavegacao />)

    expect(screen.getByRole('link', { name: 'Atendimento' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Clientes' })).toBeInTheDocument()
    // Rótulo do grupo "Estrutura da operação" some do fio visual quando
    // fechado, mas o botão continua com nome acessível.
    expect(screen.getByRole('button', { name: 'Estrutura da operação' })).toBeInTheDocument()
  })

  it('expande ao passar o mouse, com atraso — não instantâneo', () => {
    const { container } = renderizarComProvedores(<RailDeNavegacao />)
    const aside = container.querySelector('aside')!

    fireEvent.pointerEnter(aside)

    // Ainda dentro do atraso de abertura (160ms): não expandiu. É o que
    // separa "abriu porque eu quis" de "abriu porque o mouse passou perto".
    act(() => {
      vi.advanceTimersByTime(120)
    })
    expect(aside).toHaveClass('w-16')

    act(() => {
      vi.advanceTimersByTime(60)
    })
    expect(aside).toHaveClass('w-64')
  })

  it('fecha ao sair do rail, com atraso maior que o de abrir', () => {
    const { container } = renderizarComProvedores(<RailDeNavegacao />)
    const aside = container.querySelector('aside')!

    fireEvent.pointerEnter(aside)
    act(() => {
      vi.advanceTimersByTime(160)
    })
    expect(aside).toHaveClass('w-64')

    fireEvent.pointerLeave(aside)

    act(() => {
      vi.advanceTimersByTime(200)
    })
    // Ainda dentro do atraso de fechar (320ms): continua aberto, para quem
    // move o mouse em direção ao painel não ver ele sumir no caminho.
    expect(aside).toHaveClass('w-64')

    act(() => {
      vi.advanceTimersByTime(200)
    })
    expect(aside).toHaveClass('w-16')
  })

  it('foco de teclado abre e trava o painel', () => {
    renderizarComProvedores(<RailDeNavegacao />)
    const atendimento = screen.getByRole('link', { name: 'Atendimento' })
    const aside = atendimento.closest('aside')!

    fireEvent.focus(atendimento)
    expect(aside).toHaveClass('w-64')
  })

  it('marca o item ativo pela rota atual', () => {
    renderizarComProvedores(<RailDeNavegacao />, { rota: '/clientes' })

    // Rail fechado por padrão (sem hover/foco): item ativo é a lombada
    // bordô sobre fundo transparente, nunca `bg-branco` — se fosse
    // `bg-branco` aqui, o ícone (branco no estado fechado) ficaria
    // branco sobre branco, invisível.
    const clientes = screen.getByRole('link', { name: 'Clientes' })
    expect(clientes).toHaveClass('border-l-selo')
    expect(clientes).toHaveClass('text-branco')
    expect(clientes).not.toHaveClass('bg-branco')
  })

  it('abre o submenu sozinho quando a rota ativa está dentro dele', () => {
    renderizarComProvedores(<RailDeNavegacao />, { rota: '/estrutura/etapas' })

    const grupo = screen.getByRole('button', { name: 'Estrutura da operação' })
    expect(grupo).toHaveAttribute('aria-expanded', 'true')
  })

  it('alternar o submenu no clique', () => {
    renderizarComProvedores(<RailDeNavegacao />, { rota: '/clientes' })

    const grupo = screen.getByRole('button', { name: 'Estrutura da operação' })
    expect(grupo).toHaveAttribute('aria-expanded', 'false')

    fireEvent.click(grupo)
    expect(grupo).toHaveAttribute('aria-expanded', 'true')

    fireEvent.click(grupo)
    expect(grupo).toHaveAttribute('aria-expanded', 'false')
  })
})
