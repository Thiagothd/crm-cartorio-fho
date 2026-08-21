export type ChaveDePeriodo = 'hoje' | '7dias' | '30dias' | 'mes' | 'personalizado'

export const CHAVES_DE_PERIODO: readonly ChaveDePeriodo[] = ['hoje', '7dias', '30dias', 'mes', 'personalizado']

export const ROTULO_DO_PERIODO: Record<ChaveDePeriodo, string> = {
  hoje: 'Hoje',
  '7dias': '7 dias',
  '30dias': '30 dias',
  mes: 'Este mês',
  personalizado: 'Personalizado',
}

export interface IntervaloDePeriodo {
  de: Date
  ate: Date
}

/**
 * Fim do dia local de hoje. É o `ate` de TODO período que termina "agora", e a
 * razão é performance, não estética: o `de`/`ate` viram string ISO dentro da
 * `queryKey` do React Query. Com `new Date()` cru, cada milissegundo era uma
 * chave nova — então voltar para a tela remontava o componente, gerava outra
 * chave e forçava refetch com skeleton, SEMPRE. Zero aproveitamento de cache.
 *
 * Ancorando no fim do dia, a chave é estável durante todo o dia: sair da tela
 * e voltar acerta o cache e a informação aparece na hora. Nada é perdido —
 * "até o fim de hoje" cobre qualquer atendimento que entre no meio do dia.
 */
function fimDoDiaDeHoje(): Date {
  const data = new Date()
  data.setHours(23, 59, 59, 999)
  return data
}

/** Meia-noite local de N dias atrás — mesma razão de estabilidade do `fimDoDiaDeHoje`. */
function inicioDoDiaHaDias(dias: number): Date {
  const data = new Date()
  data.setDate(data.getDate() - dias)
  data.setHours(0, 0, 0, 0)
  return data
}

/**
 * Resolve a chave de período em datas reais. "Hoje" usa meia-noite LOCAL do
 * navegador — que é a mesma zona do atendente, e é o fuso que importa aqui —
 * convertida para UTC só na hora de virar string pela própria `Date`.
 *
 * Todos os limites são ancorados em fronteira de DIA (meia-noite / fim do dia),
 * nunca no instante da chamada — ver `fimDoDiaDeHoje`.
 */
export function resolverIntervalo(chave: ChaveDePeriodo, personalizado?: IntervaloDePeriodo): IntervaloDePeriodo {
  const ate = fimDoDiaDeHoje()

  switch (chave) {
    case 'hoje':
      return { de: inicioDoDiaHaDias(0), ate }

    case '7dias':
      return { de: inicioDoDiaHaDias(7), ate }

    case '30dias':
      return { de: inicioDoDiaHaDias(30), ate }

    case 'mes': {
      const agora = new Date()
      return { de: new Date(agora.getFullYear(), agora.getMonth(), 1), ate }
    }

    case 'personalizado':
      return personalizado ?? { de: inicioDoDiaHaDias(7), ate }
  }
}

/** Para o `<input type="date">` do período personalizado: "2026-08-21". */
export function paraCampoDeData(data: Date): string {
  const ano = data.getFullYear()
  const mes = String(data.getMonth() + 1).padStart(2, '0')
  const dia = String(data.getDate()).padStart(2, '0')
  return `${ano}-${mes}-${dia}`
}
