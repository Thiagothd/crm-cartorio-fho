import { Inbox } from 'lucide-react'
import { screen, within } from '@testing-library/react'
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
    podeGerirCadastros: true,
    podeGerirAtendentes: true,
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

  it('EstadoVazio continua exportado e funcional', () => {
    renderizarComProvedores(<EstadoVazio icone={Inbox} titulo="Nada por aqui" descricao="Sem registros." />)

    expect(screen.getByText('Nada por aqui')).toBeInTheDocument()
    expect(screen.getByText('Sem registros.')).toBeInTheDocument()
  })
})
