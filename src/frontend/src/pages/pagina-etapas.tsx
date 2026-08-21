import { AbaEtapas } from '@/features/configuracoes/aba-etapas'

/**
 * Rota própria dentro de Estrutura da operação. O componente da aba não
 * mudou — só ganhou um container de página com rolagem própria; título e
 * descrição vêm do cabeçalho global (ver `app/navegacao.ts`).
 */
export function PaginaEtapas() {
  return (
    <div className="flex-1 overflow-y-auto p-3 md:p-6">
      <AbaEtapas />
    </div>
  )
}
