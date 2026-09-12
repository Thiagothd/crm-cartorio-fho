import { BrowserRouter, Navigate, Route, Routes, useParams } from 'react-router-dom'
import { Component, lazy, Suspense, type ErrorInfo, type ReactNode } from 'react'
import { usarAutenticacao } from '@/lib/auth/usar-autenticacao'
import { Shell } from '@/components/layout/shell'
import { Button } from '@/components/ui/button'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { podeVer, todosOsItens, type PermissaoDeAcesso } from '@/app/navegacao'

const PaginaLogin = lazy(() =>
  import('@/pages/pagina-login').then((modulo) => ({ default: modulo.PaginaLogin })),
)

const PaginaMatriculaMfa = lazy(() =>
  import('@/pages/pagina-matricula-mfa').then((modulo) => ({ default: modulo.PaginaMatriculaMfa })),
)

/**
 * Central de IA privada do desenvolvedor (Modules/CentralDeIa) — área FORA do
 * sistema do cartório, com identidade e autenticação próprias (endpoint
 * `POST /api/central-ia/entrar`, nunca o login de Atendente). Por isso a rota
 * vive aqui, ao lado de `/login` e `/mfa/matricular`, e não dentro de
 * `todosOsItens()`: sem `ExigeAutenticacao`, sem `Shell`, e deliberadamente
 * ausente de `app/navegacao.ts` — quem não tem o link não sabe que esta tela
 * existe.
 */
const PaginaCentralDeIa = lazy(() =>
  import('@/pages/pagina-central-de-ia').then((modulo) => ({ default: modulo.PaginaCentralDeIa })),
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
const permissaoDeIndicadores: PermissaoDeAcesso =
  todosOsItens().find((item) => item.rota === '/indicadores')?.permissao ?? 'relatorios.ver'

/**
 * `/equipe/:id/permissoes` é tela de detalhe fora do menu, mesmo padrão de
 * `/indicadores/:secao`: a permissão é a MESMA que protege `/equipe`, mas a
 * meta-permissão (`equipe.permissoes`) é conferida DENTRO da própria página
 * — ver `pagina-permissoes-do-atendente.tsx`. Só ter `equipe.ver` abre a
 * tela em modo leitura; editar exige `equipe.permissoes`, e isso o backend
 * decide de verdade a cada chamada.
 */
const permissaoDeEquipe: PermissaoDeAcesso =
  todosOsItens().find((item) => item.rota === '/equipe')?.permissao ?? 'equipe.ver'

/**
 * Mesma ideia das duas de cima, agora para as telas que absorveram itens do
 * menu: Mensagens (automáticas | encerramento | respostas) e IA (situação |
 * conhecimento). A permissão sai da MESMA entrada de `navegacao.ts` que
 * protege a rota-mãe — repetir a chave aqui criaria duas verdades. Cada
 * `permissao` já é "qualquer uma destas" (`PermissaoDeAcesso`), então quem
 * tem só uma das seções da tela já abre a rota — é a própria página quem
 * decide, aba por aba, qual seção esta conta específica alcança.
 */
const permissaoDeMensagens: PermissaoDeAcesso =
  todosOsItens().find((item) => item.rota === '/automacao/mensagens')?.permissao ?? 'automacao.mensagens.ver'

const permissaoDeIa: PermissaoDeAcesso =
  todosOsItens().find((item) => item.rota === '/automacao/ia')?.permissao ?? 'ia.ver'

const PaginaPermissoesDoAtendente = lazy(() =>
  import('@/pages/pagina-permissoes-do-atendente').then((modulo) => ({
    default: modulo.PaginaPermissoesDoAtendente,
  })),
)

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

/**
 * Mesma permissão declarada em `app/navegacao.ts` — nunca amplia, só barra.
 * `podeVer` já resolve os dois formatos: uma chave, ou um array ("qualquer
 * uma destas", usado pelas telas que fundem seções de permissões diferentes).
 */
function ExigePermissao({
  permissao,
  children,
}: {
  permissao: PermissaoDeAcesso
  children: ReactNode
}) {
  const { pode } = usarAutenticacao()
  const permitido = podeVer(permissao, pode)

  return <ExigeAutenticacao>{permitido ? children : <Navigate to="/" replace />}</ExigeAutenticacao>
}

/** Compatibilidade: link antigo de conversa continua funcionando. */
function RedirecionarConversaAntiga() {
  const { id } = useParams<{ id: string }>()
  return <Navigate to={id ? `/atendimentos/${id}` : '/atendimentos'} replace />
}

export function Rotas() {
  const PaginaIndicadores = componentesPorRota.get('/indicadores')!
  const PaginaMensagens = componentesPorRota.get('/automacao/mensagens')!
  const PaginaIa = componentesPorRota.get('/automacao/ia')!

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

        {/* Central de IA — pública de propósito, pelo mesmo motivo do login:
            a autenticação é feita DENTRO da própria página, contra um
            segredo exclusivo, nunca contra a sessão do Atendente. */}
        <Route
          path="/central-ia"
          element={
            <PaginaSobDemanda>
              <PaginaCentralDeIa />
            </PaginaSobDemanda>
          }
        />

        {todosOsItens().map((item) => {
          const Componente = componentesPorRota.get(item.rota)!

          // `/atendimentos` e `/atendimentos/:id` sao UMA rota com segmento
          // opcional, nao duas. Como dois <Route> separados (ate 2026-09-12),
          // abrir uma conversa trocava de elemento de rota e REMONTAVA a
          // PaginaInbox — todo estado local, inclusive o filtro escolhido
          // (Minhas, Todas...), voltava ao padrao de entrada ("Fila") a cada
          // conversa aberta. Segmento opcional mantem o mesmo elemento montado:
          // "Fila" continua sendo o padrao AO ENTRAR na tela, e so ao entrar.
          const path = item.rota === '/atendimentos' ? '/atendimentos/:id?' : item.rota

          return (
            <Route
              key={item.rota}
              path={path}
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

        <Route
          path="/equipe/:id/permissoes"
          element={
            <ExigePermissao permissao={permissaoDeEquipe}>
              <PaginaSobDemanda>
                <PaginaPermissoesDoAtendente />
              </PaginaSobDemanda>
            </ExigePermissao>
          }
        />

        {/* As seções de Mensagens e de IA vivem na URL, pela mesma razão das
            de Indicadores. Ficam fora de `navegacao.ts` de propósito: são
            subdivisões de UMA tela — foi juntá-las que tirou três itens do
            menu do rail. */}
        <Route
          path="/automacao/mensagens/:secao"
          element={
            <ExigePermissao permissao={permissaoDeMensagens}>
              <PaginaSobDemanda>
                <PaginaMensagens />
              </PaginaSobDemanda>
            </ExigePermissao>
          }
        />
        <Route
          path="/automacao/ia/:secao"
          element={
            <ExigePermissao permissao={permissaoDeIa}>
              <PaginaSobDemanda>
                <PaginaIa />
              </PaginaSobDemanda>
            </ExigePermissao>
          }
        />

        {/* Compatibilidade com links antigos — refresh, F5 e link compartilhado
            continuam funcionando depois da Fase 3. */}
        <Route path="/conversas/:id" element={<RedirecionarConversaAntiga />} />
        <Route path="/configuracoes" element={<Navigate to="/estrutura/etapas" replace />} />

        {/* Os três itens de menu que viraram seção. Quem tinha `/automacao/
            respostas` favoritado, ou um link antigo colado num chamado, cai na
            aba certa em vez de na Visão Geral. */}
        <Route
          path="/automacao/respostas"
          element={<Navigate to="/automacao/mensagens/respostas" replace />}
        />
        <Route
          path="/automacao/encerramento"
          element={<Navigate to="/automacao/mensagens/encerramento" replace />}
        />
        <Route
          path="/automacao/conhecimento-ia"
          element={<Navigate to="/automacao/ia/conhecimento" replace />}
        />

        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  )
}
