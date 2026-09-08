import { CircleCheckBig, GitBranch, Inbox, Layers } from 'lucide-react'
import { CartaoDeIndicador } from '@/features/indicadores/cartao-de-indicador'
import type { Etapa } from '@/lib/api/tipos'

/**
 * A faixa de números que abre a tela — mesmo vocabulário de `CartaoDeIndicador`
 * que a Visão Geral, Relatórios, Equipe e Mensagens já usam.
 *
 * Os quatro saem do que a API já devolve; nenhum é estimativa. "Conclusão" é o
 * único com cor de acento, e em âmbar (não bordô): sem nenhuma etapa marcada
 * como final, o sistema continua funcionando — o que se perde é o sinal de
 * "atendimento concluído" que o painel e os relatórios leem. Isso é pendência
 * de cadastro, não erro.
 */
export function ResumoDasEtapas({ etapas }: { etapas: Etapa[] }) {
  const ativas = etapas.filter((e) => e.ativo)
  const inativas = etapas.length - ativas.length
  const noFunil = etapas.reduce((total, e) => total + e.conversasAtivas, 0)
  const finais = ativas.filter((e) => e.ehFinal).length

  // A etapa onde o trabalho se acumula — a pergunta que uma tela de funil
  // responde. Só as ativas concorrem: uma etapa desativada com conversa velha
  // dentro não é onde a fila está hoje.
  const maisCheia = ativas.reduce<Etapa | null>(
    (maior, atual) => (maior === null || atual.conversasAtivas > maior.conversasAtivas ? atual : maior),
    null,
  )

  return (
    <div className="grade-com-vida grid grid-cols-2 gap-3 @3xl:grid-cols-4">
      <CartaoDeIndicador
        rotulo="Etapas ativas"
        valor={String(ativas.length)}
        detalhe={inativas > 0 ? `${inativas} fora do funil` : 'Nenhuma desativada'}
        icone={GitBranch}
      />
      <CartaoDeIndicador
        rotulo="No funil"
        valor={String(noFunil)}
        detalhe="Conversas ativas, somando as etapas"
        icone={Inbox}
      />
      <CartaoDeIndicador
        rotulo="Mais cheia"
        valor={String(maisCheia?.conversasAtivas ?? 0)}
        detalhe={noFunil > 0 && maisCheia ? maisCheia.nome : 'Nenhuma conversa aberta'}
        icone={Layers}
      />
      <CartaoDeIndicador
        rotulo="Conclusão"
        valor={String(finais)}
        detalhe={finais === 0 ? 'Nenhuma etapa marca o fim' : 'Marcam atendimento concluído'}
        icone={CircleCheckBig}
        cor={finais === 0 ? 'ambar' : 'verde'}
      />
    </div>
  )
}
