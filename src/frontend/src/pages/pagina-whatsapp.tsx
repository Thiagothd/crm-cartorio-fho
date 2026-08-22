import { PainelDeConexao } from '@/features/integracoes/whatsapp/painel-de-conexao'
import { RegrasDeNumeros } from '@/features/integracoes/whatsapp/regras-de-numeros'
import { TesteDeEnvio } from '@/features/integracoes/whatsapp/teste-de-envio'
import { MensagensBloqueadas } from '@/features/integracoes/whatsapp/mensagens-bloqueadas'
import { HistoricoDeConexoes } from '@/features/integracoes/whatsapp/historico-de-conexoes'

/**
 * Gestão do canal de WhatsApp: número conectado, troca, regras de quem fala
 * com o canal, teste de envio e os dois históricos (conexões e bloqueios).
 *
 * Cada bloco tem sua própria consulta — nenhum estado é compartilhado por
 * fora do cache do React Query, então um bloco falhando não derruba os outros.
 */
export function PaginaWhatsApp() {
  return (
    <div className="flex-1 overflow-y-auto p-3 md:p-6">
      <div className="mx-auto flex max-w-2xl flex-col gap-4">
        <PainelDeConexao />
        <RegrasDeNumeros />
        <TesteDeEnvio />
        <MensagensBloqueadas />
        <HistoricoDeConexoes />
      </div>
    </div>
  )
}
