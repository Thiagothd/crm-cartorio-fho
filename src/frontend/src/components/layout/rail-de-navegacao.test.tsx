import { act } from 'react'
import type { ReactElement } from 'react'
import { fireEvent, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { RailDeNavegacao } from './rail-de-navegacao'
import { ProvedorDoRail } from './contexto-do-rail'
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
    // Tudo visível — este teste exercita o rail em si (recolher, hover,
    // submenu), não a filtragem por permissão.
    pode: () => true,
  }),
}))

/**
 * `RailDeNavegacao` lê o contexto do rail para saber o que desenhar — fora do
 * `Shell` (que monta o `ProvedorDoRail` de verdade), precisa de um provedor
 * próprio no teste. `localStorage` é limpo em cada teste para a preferência
 * persistida (`crm-cartorio.menu-fixado`) não vazar de um teste para o outro.
 */
function renderizarRail(ui: ReactElement = <RailDeNavegacao />, opcoes?: Parameters<typeof renderizarComProvedores>[1]) {
  localStorage.clear()
  return renderizarComProvedores(<ProvedorDoRail>{ui}</ProvedorDoRail>, opcoes)
}

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
    const { container } = renderizarRail()
    const aside = container.querySelector('aside')!

    fireEvent.pointerEnter(aside)
    act(() => {
      vi.advanceTimersByTime(160)
    })
    expect(aside).toHaveClass('w-[var(--largura-do-rail,4rem)]')
    expect(screen.getByText('Clientes')).toHaveClass('opacity-100')

    // Sem `pointerLeave`: o ponteiro continua sobre o rail, exatamente como
    // fica depois de um clique de verdade.
    fireEvent.click(screen.getByRole('link', { name: 'Clientes' }))
    act(() => {
      vi.runOnlyPendingTimers()
    })

    expect(screen.getByText('Clientes')).toHaveClass('opacity-0')
  })

  it('expande ao passar o mouse, com atraso — não instantâneo', () => {
    const { container } = renderizarRail()
    const aside = container.querySelector('aside')!

    fireEvent.pointerEnter(aside)

    // Ainda dentro do atraso de abertura (160ms): não expandiu. É o que
    // separa "abriu porque eu quis" de "abriu porque o mouse passou perto".
    act(() => {
      vi.advanceTimersByTime(120)
    })
    expect(screen.getByText('Clientes')).toHaveClass('opacity-0')

    act(() => {
      vi.advanceTimersByTime(60)
    })
    expect(screen.getByText('Clientes')).toHaveClass('opacity-100')
  })

  it('fecha ao sair do rail, com atraso maior que o de abrir', () => {
    const { container } = renderizarRail()
    const aside = container.querySelector('aside')!

    fireEvent.pointerEnter(aside)
    act(() => {
      vi.advanceTimersByTime(160)
    })
    expect(screen.getByText('Clientes')).toHaveClass('opacity-100')

    fireEvent.pointerLeave(aside)

    act(() => {
      vi.advanceTimersByTime(200)
    })
    // Ainda dentro do atraso de fechar (320ms): continua aberto, para quem
    // move o mouse em direção ao painel não ver ele sumir no caminho.
    expect(screen.getByText('Clientes')).toHaveClass('opacity-100')

    act(() => {
      vi.advanceTimersByTime(200)
    })
    expect(screen.getByText('Clientes')).toHaveClass('opacity-0')
  })

  it('foco de teclado abre e trava o painel', () => {
    renderizarRail()
    const atendimento = screen.getByRole('link', { name: 'Atendimento' })

    fireEvent.focus(atendimento)
    expect(screen.getByText('Clientes')).toHaveClass('opacity-100')
  })

  // O hover e o foco são transitórios: nenhum dos dois grava a preferência.
  // Só o clique no botão do rodapé fixa o estado.
  it('hover não grava a preferência — só o clique no botão fixa', () => {
    const { container } = renderizarRail()
    const aside = container.querySelector('aside')!

    fireEvent.pointerEnter(aside)
    act(() => {
      vi.advanceTimersByTime(160)
    })
    expect(localStorage.getItem('crm-cartorio.menu-fixado')).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Fixar menu aberto' }))
    expect(localStorage.getItem('crm-cartorio.menu-fixado')).toBe('true')
  })

  // O submenu só desenha os filhos quando expandido — sem isto, clicar num
  // submenu com o rail recolhido não fazia nada visível.
  it('clicar num submenu com o rail recolhido expande o rail', () => {
    renderizarRail()

    fireEvent.click(screen.getByRole('button', { name: 'Estrutura da operação' }))

    expect(screen.getByRole('link', { name: 'Etapas' })).toBeInTheDocument()
  })

  it('o botão de alternar é nomeado pelo texto visível, sem aria-label', () => {
    renderizarRail()

    const botao = screen.getByRole('button', { name: 'Fixar menu aberto' })
    expect(botao).not.toHaveAttribute('aria-label')
    expect(botao).not.toHaveAttribute('aria-pressed')
    expect(botao).not.toHaveAttribute('aria-expanded')
  })

  it('não deixa o rail rolar na horizontal quando fechado', () => {
    // Os rótulos seguem ocupando largura mesmo em `opacity-0`; com
    // `overflow-y-auto` sozinho o CSS promove o eixo X para `auto` e desenha uma
    // barra de rolagem horizontal no pé do rail fechado.
    const { container } = renderizarRail()

    expect(container.querySelector('nav')).toHaveClass('overflow-x-hidden')
  })

  it('esconde a barra de rolagem vertical, mas mantém a rolagem', () => {
    // O tema global pinta o polegar com `--cor-linha-forte`, e sobre o
    // `bg-tinta` do rail isso vira o maior contraste da interface. Some o
    // desenho, fica a rolagem: sem `overflow-y-auto` os itens de baixo ficariam
    // inalcançáveis em janela baixa (o menu transborda a partir de ~700px de
    // altura). São classes arbitrárias, fáceis de "limpar" num refactor sem
    // perceber o que se perde — por isso estão travadas aqui.
    const { container } = renderizarRail()
    const nav = container.querySelector('nav')

    expect(nav).toHaveClass('[scrollbar-width:none]')
    expect(nav).toHaveClass('[&::-webkit-scrollbar]:hidden')
    expect(nav).toHaveClass('overflow-y-auto')
  })

  it('sempre expõe nome acessível dos itens, mesmo fechado', () => {
    renderizarRail()

    expect(screen.getByRole('link', { name: 'Atendimento' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Clientes' })).toBeInTheDocument()
    // Rótulo do grupo "Estrutura da operação" some do fio visual quando
    // fechado, mas o botão continua com nome acessível.
    expect(screen.getByRole('button', { name: 'Estrutura da operação' })).toBeInTheDocument()
  })

  it('marca o item ativo pela rota atual', () => {
    renderizarRail(<RailDeNavegacao />, { rota: '/clientes' })

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
    renderizarRail(<RailDeNavegacao />, { rota: '/estrutura/etapas' })

    const grupo = screen.getByRole('button', { name: 'Estrutura da operação' })
    expect(grupo).toHaveAttribute('aria-expanded', 'true')
  })

  it('alternar o submenu no clique', () => {
    renderizarRail(<RailDeNavegacao />, { rota: '/clientes' })

    const grupo = screen.getByRole('button', { name: 'Estrutura da operação' })
    expect(grupo).toHaveAttribute('aria-expanded', 'false')

    fireEvent.click(grupo)
    expect(grupo).toHaveAttribute('aria-expanded', 'true')

    fireEvent.click(grupo)
    expect(grupo).toHaveAttribute('aria-expanded', 'false')
  })
})
