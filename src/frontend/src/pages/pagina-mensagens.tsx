import { Navigate, useParams } from 'react-router-dom'
import { MessageSquare, TimerOff, Zap } from 'lucide-react'
import { AbasDeSecao, ehSecaoDe, type SecaoDeAba } from '@/components/layout/abas-de-secao'
import { AbaMensagensAutomaticas } from '@/features/configuracoes/aba-mensagens-automaticas'
import { AbaEncerramentoAutomatico } from '@/features/configuracoes/aba-encerramento-automatico'
import { AbaRespostasRapidas } from '@/features/configuracoes/aba-respostas-rapidas'

export type SecaoDeMensagens = 'automaticas' | 'encerramento' | 'respostas'

/**
 * A ordem importa e não é alfabética: começa no que o sistema manda sozinho
 * por gatilho, passa pelo que ele manda sozinho por TEMPO, e termina no que o
 * atendente manda com a própria mão. É a mesma fronteira que a DESIGN.md §10.6
 * protege — resposta rápida NUNCA envia sozinha —, agora legível na própria
 * sequência das abas em vez de escondida em telas separadas.
 */
const SECOES: readonly SecaoDeAba<SecaoDeMensagens>[] = [
  { chave: 'automaticas', rotulo: 'Automáticas', icone: MessageSquare },
  { chave: 'encerramento', rotulo: 'Encerramento', icone: TimerOff },
  { chave: 'respostas', rotulo: 'Respostas rápidas', icone: Zap },
]

/**
 * Tudo que é TEXTO que sai da serventia, numa tela só, dividida em seções.
 *
 * Antes eram três itens no menu do rail — "Mensagens automáticas",
 * "Encerramento automático" e "Respostas rápidas" —, três destinos para
 * variações da mesma coisa, configuradas pelo mesmo perfil (gestão) e na mesma
 * frequência (uma vez, e raramente de novo). Viraram uma tela com abas; o
 * menu perdeu dois itens sem perder um caminho sequer, porque as rotas antigas
 * continuam resolvendo por redirect (`app/rotas.tsx`).
 */
export function PaginaMensagens() {
  const { secao } = useParams<{ secao?: string }>()

  // Sem seção (ou com uma inventada na URL) cai na primeira, com `replace`
  // para não deixar um passo morto no histórico do navegador.
  if (!ehSecaoDe(SECOES, secao)) {
    return <Navigate to="/automacao/mensagens/automaticas" replace />
  }

  return (
    <div className="min-h-0 flex-1 overflow-y-auto p-3 md:p-6">
      {/* Mesma moldura de Equipe e Clientes: teto de 1600px em vez dos 1152 de
          `max-w-6xl`, e `@container` local para as grades das abas medirem a
          largura que sobra DEPOIS do padding e do teto — não a do shell.
          O teto subiu porque o desenho do caminho automático passa de 20
          pontos: a 1152px ele empilhava em duas colunas e obrigava a rolar
          para ver o roteiro inteiro, que é justamente o que ele existe para
          mostrar de uma vez. */}
      <div className="@container mx-auto flex max-w-[1600px] flex-col gap-4">
        <AbasDeSecao
          base="/automacao/mensagens"
          secoes={SECOES}
          rotuloDaNavegacao="Seções de Mensagens"
          className="self-start"
        />

        {secao === 'automaticas' ? (
          <AbaMensagensAutomaticas />
        ) : secao === 'encerramento' ? (
          <AbaEncerramentoAutomatico />
        ) : (
          <AbaRespostasRapidas />
        )}
      </div>
    </div>
  )
}
