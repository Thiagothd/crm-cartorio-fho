import { BrowserRouter, Navigate, Route, Routes, useParams } from 'react-router-dom'
import { Component, lazy, Suspense, type ErrorInfo, type ReactNode } from 'react'
import { usarAutenticacao } from '@/lib/auth/usar-autenticacao'
import { Shell } from '@/components/layout/shell'
import { Button } from '@/components/ui/button'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { todosOsItens, type PerfilNecessario } from '@/app/navegacao'

const PaginaLogin = lazy(() =>
  import('@/pages/pagina-login').then((modulo) => ({ default: modulo.PaginaLogin })),
)

const PaginaMatriculaMfa = lazy(() =>
  import('@/pages/pagina-matricula-mfa').then((modulo) => ({ default: modulo.PaginaMatriculaMfa })),
)

/**
 * Um `lazy()` por rota, derivado da MESMA lista que o rail, o cabeçalho e a
 * navegação do celular usam (`app/navegacao.ts`) — nenhuma rota existe em dois
 * lugares. `/atendimentos/:id` reaproveita o componente de `/atendimentos`
 * (é a mesma página; a conversa aberta é só um parâmetro dela).
 */
const componentesPorRota = new Map(todosOsItens().map((item) => [item.rota, lazy(item.carregar)]))

/**
 * A permissão de `/indicadores/:secao` é lida da MESMA entrada de
 * `navegacao.ts` que protege `/indicadores`. Repetir `'gestao'` aqui criaria
 * duas verdades, e a que ficasse esquecida seria a que abre a tela para quem
 * não devia.
 */
const permissaoDeIndicadores: PerfilNecessario =
  todosOsItens().find((item) => item.rota === '/indicadores')?.permissao ?? 'gestao'

function Carregando({ pagina = false }: { pagina?: boolean }) {
  return (
    <EstadoDaInterface
      tipo="carregando"
      titulo="Carregando o CRM"
      descricao="Preparando as informações da serventia."
      preencher={pagina}
      className={pagina ? undefined : 'min-h-dvh'}
    />
  )
}

class LimiteDeErroDaPagina extends Component<
  { children: ReactNode },
  { falhou: boolean }
> {
  state = { falhou: false }

  static getDerivedStateFromError() {
    return { falhou: true }
  }

  componentDidCatch(erro: Error, informacoes: ErrorInfo) {
    console.error('Falha ao carregar página do CRM.', erro, informacoes)
  }

  render() {
    if (this.state.falhou) {
      return (
        <EstadoDaInterface
          tipo="erro"
          titulo="Não foi possível abrir esta página"
          descricao="A conexão pode ter sido interrompida durante o carregamento."
          acao={
            <Button type="button" variant="outline" onClick={() => window.location.reload()}>
              Recarregar página
            </Button>
          }
          preencher
        />
      )
    }

    return this.props.children
  }
}

function PaginaSobDemanda({ children }: { children: ReactNode }) {
  return (
    <LimiteDeErroDaPagina>
      <Suspense fallback={<Carregando pagina />}>{children}</Suspense>
    </LimiteDeErroDaPagina>
  )
}

function ExigeAutenticacao({ children }: { children: ReactNode }) {
  const { atendente, carregando } = usarAutenticacao()

  if (carregando) {
    return <Carregando />
  }

  if (!atendente) {
    return <Navigate to="/login" replace />
  }

  return <Shell>{children}</Shell>
}

/** Mesma permissão declarada em `app/navegacao.ts` — nunca amplia, só barra. */
function ExigePermissao({
  permissao,
  children,
}: {
  permissao: PerfilNecessario
  children: ReactNode
}) {
  const { podeGerirCadastros, podeGerirAtendentes } = usarAutenticacao()

  const permitido =
    permissao === 'administrador'
      ? podeGerirAtendentes
      : permissao === 'gestao'
        ? podeGerirCadastros
        : true

  return <ExigeAutenticacao>{permitido ? children : <Navigate to="/" replace />}</ExigeAutenticacao>
}

/** Compatibilidade: link antigo de conversa continua funcionando. */
function RedirecionarConversaAntiga() {
  const { id } = useParams<{ id: string }>()
  return <Navigate to={id ? `/atendimentos/${id}` : '/atendimentos'} replace />
}

export function Rotas() {
  const PaginaAtendimento = componentesPorRota.get('/atendimentos')!
  const PaginaIndicadores = componentesPorRota.get('/indicadores')!

  return (
    <BrowserRouter>
      <Routes>
        <Route
          path="/login"
          element={
            <PaginaSobDemanda>
              <PaginaLogin />
            </PaginaSobDemanda>
          }
        />

        {/* SEC-03 — pública de propósito: a conta ainda não tem sessão. E-mail
            + senha + ticket fora de banda substituem o JWT. */}
        <Route
          path="/mfa/matricular"
          element={
            <PaginaSobDemanda>
              <PaginaMatriculaMfa />
            </PaginaSobDemanda>
          }
        />

        {todosOsItens().map((item) => {
          const Componente = componentesPorRota.get(item.rota)!

          return (
            <Route
              key={item.rota}
              path={item.rota}
              element={
                <ExigePermissao permissao={item.permissao}>
                  <PaginaSobDemanda>
                    <Componente />
                  </PaginaSobDemanda>
                </ExigePermissao>
              }
            />
          )
        })}

        <Route
          path="/atendimentos/:id"
          element={
            <ExigeAutenticacao>
              <PaginaSobDemanda>
                <PaginaAtendimento />
              </PaginaSobDemanda>
            </ExigeAutenticacao>
          }
        />

        {/* As quatro seções de Indicadores vivem na URL, não em estado local —
            F5 e link compartilhado abrem na mesma aba. Fora de `navegacao.ts`
            de propósito: são subdivisões de UMA tela, e inflariam o menu do
            rail com detalhe de uma página só. A permissão é a MESMA declarada
            para `/indicadores` lá, lida daqui para não haver duas verdades. */}
        <Route
          path="/indicadores/:secao"
          element={
            <ExigePermissao permissao={permissaoDeIndicadores}>
              <PaginaSobDemanda>
                <PaginaIndicadores />
              </PaginaSobDemanda>
            </ExigePermissao>
          }
        />

        {/* Compatibilidade com links antigos — refresh, F5 e link compartilhado
            continuam funcionando depois da Fase 3. */}
        <Route path="/conversas/:id" element={<RedirecionarConversaAntiga />} />
        <Route path="/configuracoes" element={<Navigate to="/estrutura/etapas" replace />} />

        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  )
}
