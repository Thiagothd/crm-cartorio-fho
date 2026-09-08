import { Building2, CircleDashed, Inbox, Layers } from 'lucide-react'
import { CartaoDeIndicador } from '@/features/indicadores/cartao-de-indicador'
import type { Setor } from '@/lib/api/tipos'

/**
 * Gêmea de `ResumoDasEtapas`, de propósito — as duas telas de Estrutura da
 * operação descrevem eixos diferentes (setor é QUEM atende, etapa é EM QUE
 * PONTO está) e desenhá-las diferente só ajudaria a confundir os dois.
 *
 * "Setores ativos" carrega no detalhe o fato que mais importa aqui: setor
 * ativo é opção no menu que o cliente recebe no primeiro contato (RF-039).
 * Nenhum indicador gasta bordô.
 */
export function ResumoDosSetores({ setores }: { setores: Setor[] }) {
  const ativos = setores.filter((s) => s.ativo)
  const roteadas = setores.reduce((total, s) => total + s.conversasAtivas, 0)
  const ociosos = ativos.filter((s) => s.conversasAtivas === 0).length

  const maisCheio = ativos.reduce<Setor | null>(
    (maior, atual) => (maior === null || atual.conversasAtivas > maior.conversasAtivas ? atual : maior),
    null,
  )

  return (
    <div className="grade-com-vida grid grid-cols-2 gap-3 @3xl:grid-cols-4">
      <CartaoDeIndicador
        rotulo="Setores ativos"
        valor={String(ativos.length)}
        detalhe="Opções no menu do primeiro contato"
        icone={Building2}
      />
      <CartaoDeIndicador
        rotulo="Conversas roteadas"
        valor={String(roteadas)}
        detalhe="Ativas, somando os setores"
        icone={Inbox}
      />
      <CartaoDeIndicador
        rotulo="Mais cheio"
        valor={String(maisCheio?.conversasAtivas ?? 0)}
        detalhe={roteadas > 0 && maisCheio ? maisCheio.nome : 'Nenhuma conversa aberta'}
        icone={Layers}
      />
      <CartaoDeIndicador
        rotulo="Sem conversa"
        valor={String(ociosos)}
        detalhe={ociosos > 0 ? 'Ativos, sem atendimento agora' : 'Todos com atendimento'}
        icone={CircleDashed}
      />
    </div>
  )
}
