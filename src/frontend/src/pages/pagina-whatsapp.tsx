import { AbaWhatsApp } from '@/features/configuracoes/aba-whatsapp'

/**
 * `AbaWhatsApp` já tem `refetchInterval` condicional enquanto há QR pendente
 * — como página própria, isso liga e desliga com a navegação, o que é melhor
 * do que hoje (a aba já desmontava ao trocar de aba dentro de `Tabs`).
 */
export function PaginaWhatsApp() {
  return (
    <div className="flex-1 overflow-y-auto p-3 md:p-6">
      <AbaWhatsApp />
    </div>
  )
}
