import { AbaAparencia } from '@/features/configuracoes/aba-aparencia'
import { SeletorDeTema } from '@/components/ui/seletor-de-tema'

export function PaginaAparencia() {
  return (
    <div className="min-w-0 flex-1 overflow-y-auto p-3 md:p-6">
      <section className="mb-6 border-b border-linha pb-6">
        <SeletorDeTema expandido />
      </section>
      <AbaAparencia />
    </div>
  )
}
