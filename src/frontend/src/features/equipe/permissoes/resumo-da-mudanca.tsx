import type { ChaveDePermissao, PermissaoDoCatalogo } from '@/lib/api/tipos'

/**
 * "O que muda" — diff contra o estado salvo, sempre visível ao lado dos
 * blocos (desktop) ou acima da barra de ação (celular). Sem isto, marcar 15
 * caixas espalhadas por 8 blocos e não ter nenhuma confirmação do que
 * realmente vai mudar é o tipo de coisa que faz alguém salvar sem querer.
 */
export function ResumoDaMudanca({
  catalogoPorChave,
  salvas,
  pendentes,
}: {
  catalogoPorChave: Map<string, PermissaoDoCatalogo>
  salvas: Set<ChaveDePermissao>
  pendentes: Set<ChaveDePermissao>
}) {
  const ganhos = [...pendentes].filter((c) => !salvas.has(c))
  const perdas = [...salvas].filter((c) => !pendentes.has(c))
  const totalDeMudancas = ganhos.length + perdas.length

  if (totalDeMudancas === 0) {
    return <p className="text-xs text-tinta-media">Nenhuma mudança pendente.</p>
  }

  return (
    <div className="flex flex-col gap-3">
      {ganhos.length > 0 ? (
        <div className="flex flex-col gap-1">
          <span className="text-xs font-semibold text-verde">+ {ganhos.length} ganha</span>
          <ul className="flex flex-col gap-0.5">
            {ganhos.map((chave) => (
              <li key={chave} className="truncate text-xs text-tinta-media">
                {catalogoPorChave.get(chave)?.rotulo ?? chave}
              </li>
            ))}
          </ul>
        </div>
      ) : null}

      {perdas.length > 0 ? (
        <div className="flex flex-col gap-1">
          <span className="text-xs font-semibold text-selo">− {perdas.length} perde</span>
          <ul className="flex flex-col gap-0.5">
            {perdas.map((chave) => (
              <li key={chave} className="truncate text-xs text-tinta-media">
                {catalogoPorChave.get(chave)?.rotulo ?? chave}
              </li>
            ))}
          </ul>
        </div>
      ) : null}
    </div>
  )
}
