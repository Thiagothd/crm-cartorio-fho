import { CircleDashed, PauseCircle, Route, Send } from 'lucide-react'
import { CartaoDeIndicador } from '@/features/indicadores/cartao-de-indicador'
import type { ContagemDoFluxo } from './topologia-do-fluxo'

/**
 * A faixa de números que abre a aba — mesmo vocabulário de `CartaoDeIndicador`
 * que a Visão Geral, Relatórios e Equipe já usam.
 *
 * Os quatro somam: pontos = com texto + falta escrever + desativadas. Isso é
 * deliberado — o número que decide o trabalho de quem abre esta tela é "quantos
 * pontos do caminho ainda estão mudos", e ele só significa alguma coisa ao lado
 * do total.
 *
 * "Falta escrever" é o único em cor de acento, e só quando tem número: ponto
 * sem texto é pendência de configuração, não um dado neutro. Fica em âmbar, não
 * em bordô — o sistema continua funcionando, só fica em silêncio naquele
 * momento. Nenhuma ocorrência de bordô é gasta aqui (DESIGN.md 17.2).
 */
export function ResumoDasMensagens({ contagem }: { contagem: ContagemDoFluxo }) {
  return (
    <div className="grade-com-vida grid grid-cols-2 gap-3 @3xl:grid-cols-4">
      <CartaoDeIndicador
        rotulo="No caminho"
        valor={String(contagem.pontos)}
        detalhe="Momentos em que o sistema pode falar"
        icone={Route}
      />
      <CartaoDeIndicador
        rotulo="Com texto"
        valor={String(contagem.comTexto)}
        detalhe="Prontos para enviar"
        icone={Send}
        cor="verde"
      />
      <CartaoDeIndicador
        rotulo="Falta texto"
        valor={String(contagem.faltando)}
        detalhe={
          contagem.faltando > 0 ? 'O sistema fica em silêncio aí' : 'Nenhum ponto em aberto'
        }
        icone={CircleDashed}
        cor={contagem.faltando > 0 ? 'ambar' : 'verde'}
      />
      <CartaoDeIndicador
        rotulo="Desativadas"
        valor={String(contagem.desativados)}
        detalhe={
          contagem.desativados > 0 ? 'Texto salvo, disparo desligado' : 'Nenhuma regra pausada'
        }
        icone={PauseCircle}
      />
    </div>
  )
}
