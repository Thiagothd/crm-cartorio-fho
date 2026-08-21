import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Toaster } from 'sonner'
import type { ReactNode } from 'react'
import { ProvedorDeAutenticacao } from '@/lib/auth/contexto-de-autenticacao'
import { usarAutenticacao } from '@/lib/auth/usar-autenticacao'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { TooltipProvider } from '@/components/ui/tooltip'
import { ProvedorDoHubDeAtendimento } from '@/lib/realtime/contexto-do-hub-de-atendimento'

const clienteDeConsultas = new QueryClient({
  defaultOptions: {
    queries: {
      // O tempo real ja invalida o que muda; refetch por foco so somaria ruido.
      refetchOnWindowFocus: false,
      staleTime: 15_000,

      // Nao repetir erro 4xx. "Conversa nao encontrada" e "sem permissao" nao
      // mudam de resposta na segunda tentativa — repetir so dobra a carga e
      // atrasa a mensagem de erro na tela. Falha de rede e 5xx ainda repetem.
      retry: (tentativas, erro) => {
        if (erro instanceof ErroDaApi && erro.status >= 400 && erro.status < 500) {
          return false
        }

        return tentativas < 1
      },
    },
  },
})

/**
 * A conexão do hub só faz sentido depois de saber se há sessão — por isso este
 * componente vem DEPOIS de `ProvedorDeAutenticacao` na árvore, não dentro dele.
 */
function ProvedorDoHubComAutenticacao({ children }: { children: ReactNode }) {
  const { atendente } = usarAutenticacao()

  return (
    <ProvedorDoHubDeAtendimento habilitado={Boolean(atendente)}>{children}</ProvedorDoHubDeAtendimento>
  )
}

export function Provedores({ children }: { children: ReactNode }) {
  return (
    <QueryClientProvider client={clienteDeConsultas}>
      <ProvedorDeAutenticacao>
        <ProvedorDoHubComAutenticacao>
          <TooltipProvider delayDuration={500} skipDelayDuration={300}>
            {children}
          </TooltipProvider>
          <Toaster
            position="top-right"
            duration={5_000}
            visibleToasts={4}
            closeButton
            toastOptions={{
              classNames: {
                toast: '!rounded-md !border-linha-forte !bg-branco !text-tinta !shadow-none',
                title: '!font-medium',
                description: '!text-tinta-media',
                actionButton: '!rounded-md !bg-tinta !text-branco',
                cancelButton: '!rounded-md !bg-superficie !text-tinta',
                closeButton: '!size-8 !border-linha-forte !bg-branco !text-tinta',
              },
            }}
          />
        </ProvedorDoHubComAutenticacao>
      </ProvedorDeAutenticacao>
    </QueryClientProvider>
  )
}
