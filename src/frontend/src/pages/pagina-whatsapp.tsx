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
 *
 * Fase 3.1 (DESIGN.md 17.5): era uma coluna de `max-w-2xl` (672px) com os
 * cinco blocos empilhados — numa tela de 1440px sobravam ~770px vazios e o
 * histórico ficava a três rolagens do topo. Agora é a moldura de 1600px das
 * outras telas de gestão, com duas colunas a partir de `@4xl`:
 *
 * - à esquerda, o CANAL — o número conectado e quem pode falar com ele, que é
 *   o que se vem configurar;
 * - à direita, a PROVA de que funciona — teste de envio e o que o filtro
 *   barrou, que se lê para conferir, não para mexer.
 *
 * A coluna da esquerda é mais larga porque a lista de números liberados é o
 * único bloco com tabela e ação de linha.
 *
 * O histórico de conexões fica FORA das duas colunas, na largura inteira: são
 * quatro colunas de data e número, e na coluna estreita cada linha quebrava em
 * três — o terceiro degrau de 12.4 (rolagem interna) resolveria o transbordo,
 * mas rolar na horizontal para ler data é pior que ocupar a largura que já
 * está sobrando embaixo.
 */
export function PaginaWhatsApp() {
  return (
    <div className="min-h-0 flex-1 overflow-y-auto p-3 md:p-6">
      <div className="@container mx-auto flex max-w-[1600px] flex-col gap-4">
        <div className="grade-com-vida grid gap-4 @4xl:grid-cols-[minmax(0,1.35fr)_minmax(0,1fr)] @4xl:items-start">
          <div className="flex flex-col gap-4">
            <PainelDeConexao />
            <RegrasDeNumeros />
          </div>

          <div className="flex flex-col gap-4">
            <TesteDeEnvio />
            <MensagensBloqueadas />
          </div>
        </div>

        <HistoricoDeConexoes />
      </div>
    </div>
  )
}
