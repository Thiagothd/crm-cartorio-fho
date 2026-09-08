import { BookOpen, CalendarOff, Sparkles, Tags } from 'lucide-react'
import { CartaoDeIndicador } from '@/features/indicadores/cartao-de-indicador'
import { estaEmUsoPelaIa, estaForaDeVigencia, hojeIso } from './vigencia-de-conhecimento'
import type { ConhecimentoDeIa } from '@/lib/api/tipos'

/**
 * "Em uso pela IA" é o número que esta tela existe para responder — e não é o
 * mesmo que "cadastrados": o contexto da IA lê só o que está ativo E dentro da
 * janela de vigência (`MontadorDeContexto`). Um cadastro pode estar salvo,
 * correto e mudo.
 *
 * "Fora de vigência" fica em âmbar quando existe, pelo mesmo critério de
 * Mensagens (17.4): é silêncio deliberado ou esquecimento, nunca erro — e
 * nenhum indicador gasta bordô.
 */
export function ResumoDoConhecimento({ conhecimentos }: { conhecimentos: ConhecimentoDeIa[] }) {
  const hoje = hojeIso()
  const emUso = conhecimentos.filter((c) => estaEmUsoPelaIa(c, hoje)).length
  const foraDeVigencia = conhecimentos.filter((c) => estaForaDeVigencia(c, hoje)).length
  const desativados = conhecimentos.filter((c) => !c.ativo).length
  const temas = new Set(conhecimentos.map((c) => c.tema.trim().toLowerCase()).filter(Boolean)).size

  return (
    <div className="grade-com-vida grid grid-cols-2 gap-3 @3xl:grid-cols-4">
      <CartaoDeIndicador
        rotulo="Em uso"
        valor={String(emUso)}
        detalhe="Entram na resposta agora"
        icone={Sparkles}
        cor={emUso > 0 ? 'verde' : 'tinta'}
      />
      <CartaoDeIndicador
        rotulo="Cadastrados"
        valor={String(conhecimentos.length)}
        detalhe={desativados > 0 ? `${desativados} desativado(s)` : 'Nenhum desativado'}
        icone={BookOpen}
      />
      <CartaoDeIndicador
        rotulo="Fora de vigência"
        valor={String(foraDeVigencia)}
        detalhe={
          foraDeVigencia > 0 ? 'Ativos, fora da janela de datas' : 'Todos dentro da janela'
        }
        icone={CalendarOff}
        cor={foraDeVigencia > 0 ? 'ambar' : 'tinta'}
      />
      <CartaoDeIndicador
        rotulo="Temas"
        valor={String(temas)}
        detalhe="Assuntos diferentes cobertos"
        icone={Tags}
      />
    </div>
  )
}
