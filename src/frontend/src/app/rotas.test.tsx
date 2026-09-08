import type { ReactNode } from 'react'
import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { Rotas } from '@/app/rotas'

// Qualquer conta real sempre tem PELO MENOS o preset Balcão (nunca um
// conjunto vazio — Permissoes=NULL sempre deriva da Classe). O mock reflete
// isso: "autenticado, sem nada especial" já enxerga Atendimento e Clientes.
const PERMISSOES_BALCAO = ['atendimento.ver', 'atendimento.assumir', 'atendimento.responder', 'clientes.ver']
const PERMISSOES_GESTAO = [
  ...PERMISSOES_BALCAO,
  'servicos.ver',
  'relatorios.ver',
  'etapas.ver',
  'setores.ver',
  'automacao.mensagens.ver',
  'ia.ver',
]
const PERMISSOES_ADMINISTRADOR = [...PERMISSOES_GESTAO, 'equipe.ver', 'whatsapp.ver']

let autenticacao = {
  atendente: null as null | { id: string },
  carregando: false,
  pode: (chave: string) => PERMISSOES_BALCAO.includes(chave),
}

vi.mock('@/lib/auth/usar-autenticacao', () => ({
  usarAutenticacao: () => autenticacao,
}))

vi.mock('@/components/layout/shell', () => ({
  Shell: ({ children }: { children: ReactNode }) => <div data-testid="shell">{children}</div>,
}))

// Uma entrada por página real de app/navegacao.ts — sem isso, Rotas tentaria
// carregar o componente de verdade (que chama a API real) para cada rota.
vi.mock('@/pages/pagina-login', () => ({ PaginaLogin: () => <h1>Login simulado</h1> }))
vi.mock('@/pages/pagina-visao-geral', () => ({ PaginaVisaoGeral: () => <h1>Visão Geral simulada</h1> }))
vi.mock('@/pages/pagina-inbox', () => ({ PaginaInbox: () => <h1>Atendimento simulado</h1> }))
vi.mock('@/pages/pagina-clientes', () => ({ PaginaClientes: () => <h1>Clientes simulados</h1> }))
vi.mock('@/pages/pagina-servicos', () => ({ PaginaServicos: () => <h1>Serviços simulados</h1> }))
vi.mock('@/pages/pagina-indicadores', () => ({ PaginaIndicadores: () => <h1>Indicadores simulados</h1> }))
vi.mock('@/pages/pagina-mensagens', () => ({
  PaginaMensagens: () => <h1>Mensagens simuladas</h1>,
}))
vi.mock('@/pages/pagina-ia', () => ({ PaginaIa: () => <h1>IA simulada</h1> }))
vi.mock('@/pages/pagina-equipe', () => ({ PaginaEquipe: () => <h1>Equipe simulada</h1> }))
vi.mock('@/pages/pagina-etapas', () => ({ PaginaEtapas: () => <h1>Etapas simuladas</h1> }))
vi.mock('@/pages/pagina-setores', () => ({ PaginaSetores: () => <h1>Setores simulados</h1> }))
vi.mock('@/pages/pagina-aparencia', () => ({ PaginaAparencia: () => <h1>Aparência simulada</h1> }))
vi.mock('@/pages/pagina-whatsapp', () => ({ PaginaWhatsApp: () => <h1>WhatsApp simulado</h1> }))
vi.mock('@/pages/pagina-seguranca', () => ({ PaginaSeguranca: () => <h1>Segurança simulada</h1> }))

describe('Rotas', () => {
  beforeEach(() => {
    autenticacao = {
      atendente: null,
      carregando: false,
      pode: (chave: string) => PERMISSOES_BALCAO.includes(chave),
    }
  })

  it('redireciona visitante para o login', async () => {
    window.history.pushState({}, '', '/clientes')
    render(<Rotas />)

    expect(await screen.findByRole('heading', { name: 'Login simulado' })).toBeInTheDocument()
    expect(window.location.pathname).toBe('/login')
  })

  it('/ é a Visão Geral, acessível a qualquer perfil autenticado', async () => {
    autenticacao.atendente = { id: 'atendente-1' }
    window.history.pushState({}, '', '/')
    render(<Rotas />)

    expect(await screen.findByRole('heading', { name: 'Visão Geral simulada' })).toBeInTheDocument()
    expect(screen.getByTestId('shell')).toBeInTheDocument()
  })

  it('redireciona atendente sem gestão para a Visão Geral ao tentar Serviços', async () => {
    autenticacao.atendente = { id: 'atendente-1' }
    window.history.pushState({}, '', '/servicos')
    render(<Rotas />)

    expect(await screen.findByRole('heading', { name: 'Visão Geral simulada' })).toBeInTheDocument()
    expect(window.location.pathname).toBe('/')
  })

  it('permite que gestão acesse Serviços', async () => {
    autenticacao = {
      atendente: { id: 'gestor-1' },
      carregando: false,
      pode: (chave: string) => PERMISSOES_GESTAO.includes(chave),
    }
    window.history.pushState({}, '', '/servicos')
    render(<Rotas />)

    expect(await screen.findByRole('heading', { name: 'Serviços simulados' })).toBeInTheDocument()
    expect(screen.getByTestId('shell')).toBeInTheDocument()
  })

  it('bloqueia rota de administrador para quem só tem gestão', async () => {
    autenticacao = {
      atendente: { id: 'gestor-1' },
      carregando: false,
      pode: (chave: string) => PERMISSOES_GESTAO.includes(chave),
    }
    window.history.pushState({}, '', '/equipe')
    render(<Rotas />)

    expect(await screen.findByRole('heading', { name: 'Visão Geral simulada' })).toBeInTheDocument()
  })

  it('permite administrador acessar Equipe e Integrações', async () => {
    autenticacao = {
      atendente: { id: 'admin-1' },
      carregando: false,
      pode: (chave: string) => PERMISSOES_ADMINISTRADOR.includes(chave),
    }
    window.history.pushState({}, '', '/equipe')
    render(<Rotas />)

    expect(await screen.findByRole('heading', { name: 'Equipe simulada' })).toBeInTheDocument()
  })

  it('atendimento vive em /atendimentos, e /atendimentos/:id abre a mesma página', async () => {
    autenticacao.atendente = { id: 'atendente-1' }
    window.history.pushState({}, '', '/atendimentos/abc-123')
    render(<Rotas />)

    expect(await screen.findByRole('heading', { name: 'Atendimento simulado' })).toBeInTheDocument()
  })

  it('redireciona link antigo /conversas/:id para /atendimentos/:id', async () => {
    autenticacao.atendente = { id: 'atendente-1' }
    window.history.pushState({}, '', '/conversas/abc-123')
    render(<Rotas />)

    expect(await screen.findByRole('heading', { name: 'Atendimento simulado' })).toBeInTheDocument()
    expect(window.location.pathname).toBe('/atendimentos/abc-123')
  })

  it('redireciona /configuracoes para a nova página de Etapas', async () => {
    autenticacao = {
      atendente: { id: 'gestor-1' },
      carregando: false,
      pode: (chave: string) => PERMISSOES_GESTAO.includes(chave),
    }
    window.history.pushState({}, '', '/configuracoes')
    render(<Rotas />)

    expect(await screen.findByRole('heading', { name: 'Etapas simuladas' })).toBeInTheDocument()
    expect(window.location.pathname).toBe('/estrutura/etapas')
  })

  // As três telas que viraram seção. Quem tem o endereço antigo favoritado, ou
  // colado num chamado, precisa cair na ABA certa — não na Visão Geral.
  it.each([
    ['/automacao/respostas', '/automacao/mensagens/respostas', 'Mensagens simuladas'],
    ['/automacao/encerramento', '/automacao/mensagens/encerramento', 'Mensagens simuladas'],
    ['/automacao/conhecimento-ia', '/automacao/ia/conhecimento', 'IA simulada'],
  ])('redireciona o link antigo %s para %s', async (antiga, nova, titulo) => {
    autenticacao = {
      atendente: { id: 'gestor-1' },
      carregando: false,
      pode: (chave: string) => PERMISSOES_GESTAO.includes(chave),
    }
    window.history.pushState({}, '', antiga)
    render(<Rotas />)

    expect(await screen.findByRole('heading', { name: titulo })).toBeInTheDocument()
    expect(window.location.pathname).toBe(nova)
  })

  // Segurança saiu do rail (`ocultoNoMenu`), mas a rota continua viva — é o
  // caminho que o menu de perfil usa, e vale para QUALQUER autenticado, não só
  // gestão: cada um gerencia o próprio MFA.
  it('Segurança continua acessível fora do menu, para qualquer autenticado', async () => {
    autenticacao.atendente = { id: 'atendente-1' }
    window.history.pushState({}, '', '/seguranca')
    render(<Rotas />)

    expect(await screen.findByRole('heading', { name: 'Segurança simulada' })).toBeInTheDocument()
    expect(window.location.pathname).toBe('/seguranca')
  })

  it('rota desconhecida cai na Visão Geral', async () => {
    autenticacao.atendente = { id: 'atendente-1' }
    window.history.pushState({}, '', '/isso-nao-existe')
    render(<Rotas />)

    expect(await screen.findByRole('heading', { name: 'Visão Geral simulada' })).toBeInTheDocument()
  })

  it('oferece recuperação quando uma página falha ao renderizar', async () => {
    const erroDoConsole = vi.spyOn(console, 'error').mockImplementation(() => undefined)
    autenticacao.atendente = { id: 'atendente-1' }
    window.history.pushState({}, '', '/clientes')

    vi.doMock('@/pages/pagina-clientes', () => ({
      PaginaClientes: () => {
        throw new Error('Falha simulada')
      },
    }))

    render(<Rotas />)

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Não foi possível abrir esta página',
    )
    expect(screen.getByRole('button', { name: 'Recarregar página' })).toBeInTheDocument()
    expect(erroDoConsole).toHaveBeenCalled()
  })
})
