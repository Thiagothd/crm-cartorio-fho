import { AbaMensagensAutomaticas } from '@/features/configuracoes/aba-mensagens-automaticas'

export function PaginaMensagensAutomaticas() {
  return (
    <div className="flex-1 overflow-y-auto p-3 md:p-6">
      <AbaMensagensAutomaticas />
    </div>
  )
}
