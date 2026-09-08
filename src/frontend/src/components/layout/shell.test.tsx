import { Inbox } from 'lucide-react'
import { fireEvent, screen, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { Shell, EstadoVazio } from '@/components/layout/shell'
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
    pode: () => true,
  }),
}))

describe('Shell', () => {
  it('mostra o símbolo oficial e a navegação principal com o conteúdo da rota', () => {
    const { container } = renderizarComProvedores(
      <Shell>
        <p>Conteúdo</p>
      </Shell>,
    )

    const simbolos = container.querySelectorAll('img[src="/brand/crm-cartorio-simbolo.png"]')
    expect(simbolos.length).toBeGreaterThan(0)
    // "Atendimento" existe duas vezes (rail + barra inferior do celular, as
    // duas presentes no DOM ao mesmo tempo — CSS decide qual é exibida);
    // escopar ao menu principal evita a ambiguidade.
    const menu = screen.getByRole('navigation', { name: 'Menu principal' })
    expect(within(menu).getByRole('link', { name: 'Atendimento' })).toBeInTheDocument()
    expect(screen.getByText('Conteúdo')).toBeInTheDocument()
  })

  it('em /atendimentos/:id no celular, some cabeçalho global e barra inferior', () => {
    const { container } = renderizarComProvedores(
      <Shell>
        <p>Conversa</p>
      </Shell>,
      { rota: '/atendimentos/abc-123' },
    )

    const cabecalho = container.querySelector('header')
    expect(cabecalho).toHaveClass('hidden')
    expect(screen.queryByRole('navigation', { name: 'Navegação principal' })).not.toBeInTheDocument()
  })

  it('em outras rotas, cabeçalho e barra inferior continuam presentes', () => {
    const { container } = renderizarComProvedores(
      <Shell>
        <p>Lista</p>
      </Shell>,
      { rota: '/atendimentos' },
    )

    const cabecalho = container.querySelector('header')
    expect(cabecalho).not.toHaveClass('hidden')
    expect(screen.getByRole('navigation', { name: 'Navegação principal' })).toBeInTheDocument()
  })

  it('a moldura cria bloco container, para nada escapar do recorte', () => {
    // Medido em 2026-08-24: sem `relative` aqui, o `.sr-only` do Tailwind —
    // que é `position: absolute` — ancorava na viewport em vez da moldura,
    // escapava dos `overflow: hidden` e dava 980px de rolagem ao documento.
    // Arrastar essa barra levava rail e cabeçalho para fora da tela.
    //
    // Era latente em TODA tela: só aparecia onde o conteúdo passava da altura
    // da janela. jsdom não faz layout, então o efeito não é testável aqui —
    // o que dá para travar é a causa, que é esta classe.
    const { container } = renderizarComProvedores(
      <Shell>
        <p>Conteúdo</p>
      </Shell>,
    )

    expect(container.firstElementChild).toHaveClass('relative')
  })

  it('EstadoVazio continua exportado e funcional', () => {
    renderizarComProvedores(<EstadoVazio icone={Inbox} titulo="Nada por aqui" descricao="Sem registros." />)

    expect(screen.getByText('Nada por aqui')).toBeInTheDocument()
    expect(screen.getByText('Sem registros.')).toBeInTheDocument()
  })

  // A partir daqui: cobertura do push. `--largura-do-rail` é a ÚNICA fonte de
  // largura para o espaçador em fluxo do rail (`rail-de-navegacao.tsx`) e para
  // o `<aside>` — provar que a var muda ao clicar prova que a coluna
  // reservada muda, sem precisar medir geometria (jsdom não faz layout).

  it('a moldura publica a largura do rail recolhida por padrão', () => {
    localStorage.clear()
    const { container } = renderizarComProvedores(
      <Shell>
        <p>Conteúdo</p>
      </Shell>,
    )

    expect(container.firstElementChild).toHaveClass(
      'md:[--largura-do-rail:var(--largura-do-rail-recolhido)]',
    )
  })

  it('fixar o menu troca a largura publicada pela moldura', () => {
    localStorage.clear()
    const { container } = renderizarComProvedores(
      <Shell>
        <p>Conteúdo</p>
      </Shell>,
    )

    fireEvent.click(screen.getByRole('button', { name: 'Fixar menu aberto' }))

    expect(container.firstElementChild).toHaveClass(
      'md:[--largura-do-rail:var(--largura-do-rail-expandido)]',
    )
    expect(localStorage.getItem('crm-cartorio.menu-fixado')).toBe('true')
  })

  it('a var vale 0 abaixo de md, sem prefixo', () => {
    const { container } = renderizarComProvedores(
      <Shell>
        <p>Conteúdo</p>
      </Shell>,
    )

    expect(container.firstElementChild).toHaveClass('[--largura-do-rail:0px]')
  })

  it('Ctrl+B fixa/desafixa o menu', () => {
    localStorage.clear()
    renderizarComProvedores(
      <Shell>
        <p>Conteúdo</p>
      </Shell>,
    )

    fireEvent.keyDown(window, { key: 'b', ctrlKey: true })

    expect(localStorage.getItem('crm-cartorio.menu-fixado')).toBe('true')
    expect(screen.getByRole('button', { name: 'Recolher menu' })).toBeInTheDocument()
  })
})
