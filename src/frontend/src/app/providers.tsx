import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Toaster } from 'sonner'
import type { ReactNode } from 'react'
import { ProvedorDeAutenticacao } from '@/lib/auth/contexto-de-autenticacao'
import { ErroDaApi } from '@/lib/api/cliente-http'

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

export function Provedores({ children }: { children: ReactNode }) {
  return (
    <QueryClientProvider client={clienteDeConsultas}>
      <ProvedorDeAutenticacao>
        {children}
        <Toaster position="top-right" richColors closeButton />
      </ProvedorDeAutenticacao>
    </QueryClientProvider>
  )
}
