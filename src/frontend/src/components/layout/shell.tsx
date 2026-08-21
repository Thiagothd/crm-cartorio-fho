import { useState, type ReactNode } from 'react'
import { useLocation } from 'react-router-dom'
import type { LucideIcon } from 'lucide-react'
import { RailDeNavegacao } from './rail-de-navegacao'
import { CabecalhoGlobal } from './cabecalho-global'
import { NavegacaoInferior } from './navegacao-inferior'
import { FolhaMais } from './folha-mais'
import { ContextoDoContainerDeAcoes } from './contexto-de-acoes-da-pagina'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'

/** `/atendimentos/:id` — a conversa já tem cabeçalho e navegação próprios. */
function ehRotaDeConversa(pathname: string) {
  return /^\/atendimentos\/[^/]+/.test(pathname)
}

/**
 * Shell global: rail de navegação (desktop/tablet), cabeçalho global, o
 * conteúdo da rota, e a navegação do celular (barra inferior + folha "Mais").
 *
 * Em `/atendimentos/:id` no celular, cabeçalho global e barra inferior somem
 * — a conversa já tem "voltar" e cabeçalho próprios, e as duas faixas juntas
 * custariam ~50px do fio de mensagens num aparelho de 360px. É exatamente o
 * comportamento que a inbox já tinha antes da Fase 3, só que decidido aqui em
 * vez de dentro da página.
 */
export function Shell({ children }: { children: ReactNode }) {
  const location = useLocation()
  const [containerDeAcoes, setContainerDeAcoes] = useState<HTMLDivElement | null>(null)
  const [folhaMaisAberta, setFolhaMaisAberta] = useState(false)

  const emConversa = ehRotaDeConversa(location.pathname)

  return (
    <div className="flex h-dvh min-h-dvh overflow-hidden bg-papel">
      <RailDeNavegacao />

      <div className="flex min-h-0 min-w-0 flex-1 flex-col overflow-hidden">
        <CabecalhoGlobal ocultoNoCelular={emConversa} aoRegistrarContainerDeAcoes={setContainerDeAcoes} />

        <ContextoDoContainerDeAcoes.Provider value={containerDeAcoes}>
          <main className="flex min-h-0 min-w-0 flex-1 flex-col overflow-hidden">{children}</main>
        </ContextoDoContainerDeAcoes.Provider>

        {!emConversa ? <NavegacaoInferior aoAbrirMais={() => setFolhaMaisAberta(true)} /> : null}
      </div>

      <FolhaMais aberta={folhaMaisAberta} aoFechar={() => setFolhaMaisAberta(false)} />
    </div>
  )
}

export function EstadoVazio({
  icone,
  titulo,
  descricao,
  acao,
}: {
  icone: LucideIcon
  titulo: string
  descricao?: string
  acao?: ReactNode
}) {
  return (
    <EstadoDaInterface
      tipo="vazio"
      icone={icone}
      titulo={titulo}
      descricao={descricao}
      acao={acao}
      preencher
    />
  )
}
