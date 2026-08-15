import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { Loader2 } from 'lucide-react'
import type { ReactNode } from 'react'
import { usarAutenticacao } from '@/lib/auth/usar-autenticacao'
import { Shell } from '@/components/layout/shell'
import { PaginaLogin } from '@/pages/pagina-login'
import { PaginaInbox } from '@/pages/pagina-inbox'
import { PaginaClientes } from '@/pages/pagina-clientes'
import { PaginaServicos } from '@/pages/pagina-servicos'
import { PaginaConfiguracoes } from '@/pages/pagina-configuracoes'

function Carregando() {
  return (
    <div className="flex min-h-dvh items-center justify-center">
      <Loader2 className="size-6 animate-spin text-muted-foreground" />
    </div>
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

/** Rota restrita a Supervisor e Administrador. */
function ExigeGestao({ children }: { children: ReactNode }) {
  const { podeGerirCadastros } = usarAutenticacao()

  return (
    <ExigeAutenticacao>{podeGerirCadastros ? children : <Navigate to="/" replace />}</ExigeAutenticacao>
  )
}

export function Rotas() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={<PaginaLogin />} />

        <Route
          path="/"
          element={
            <ExigeAutenticacao>
              <PaginaInbox />
            </ExigeAutenticacao>
          }
        />

        <Route
          path="/conversas/:id"
          element={
            <ExigeAutenticacao>
              <PaginaInbox />
            </ExigeAutenticacao>
          }
        />

        <Route
          path="/clientes"
          element={
            <ExigeAutenticacao>
              <PaginaClientes />
            </ExigeAutenticacao>
          }
        />

        <Route
          path="/servicos"
          element={
            <ExigeGestao>
              <PaginaServicos />
            </ExigeGestao>
          }
        />

        <Route
          path="/configuracoes"
          element={
            <ExigeGestao>
              <PaginaConfiguracoes />
            </ExigeGestao>
          }
        />

        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  )
}
