import { cn } from '@/lib/utils'
import type { ChaveDePermissao, PermissaoDoCatalogo } from '@/lib/api/tipos'

/** `"servicos.editar"` → `"servicos"`; `"automacao.mensagens.editar"` → `"automacao.mensagens"`. */
function grupoDaChave(chave: string): string {
  return chave.split('.').slice(0, -1).join('.')
}

export function dependeDeTela(item: PermissaoDoCatalogo, catalogo: PermissaoDoCatalogo[]): ChaveDePermissao | null {
  const chaveDaTela = `${grupoDaChave(item.chave)}.ver`
  if (chaveDaTela === item.chave) return null
  return catalogo.find((c) => c.chave === chaveDaTela)?.chave ?? null
}

/**
 * Um bloco do catálogo (ex.: "Atendimento", 11 itens) — checkbox por linha.
 * Quando a tela-mãe de um item está desmarcada, a linha esmaece com a nota
 * "sem efeito": marcar a ação sem marcar a tela não abre nada pra ninguém, e
 * essa é a confusão mais comum ao montar permissão à mão.
 */
export function BlocoDePermissoes({
  bloco,
  itens,
  catalogo,
  marcadas,
  aoAlternar,
  aberto,
  aoAlternarAberto,
}: {
  bloco: string
  itens: PermissaoDoCatalogo[]
  catalogo: PermissaoDoCatalogo[]
  marcadas: Set<ChaveDePermissao>
  aoAlternar: (chave: ChaveDePermissao) => void
  /** Controla o `<details>` no celular — no desktop o bloco fica sempre aberto. */
  aberto: boolean
  aoAlternarAberto: () => void
}) {
  const contagem = itens.filter((i) => marcadas.has(i.chave)).length

  return (
    <details
      open={aberto}
      onToggle={(e) => {
        if (e.currentTarget.open !== aberto) aoAlternarAberto()
      }}
      className="group rounded-card border border-linha bg-branco p-3.5 shadow-card md:open:pb-3.5 [&_summary::-webkit-details-marker]:hidden"
    >
      <summary className="flex cursor-pointer items-center justify-between gap-2 md:cursor-default md:pointer-events-none">
        <span className="text-sm font-semibold text-tinta">{bloco}</span>
        <span className="font-dado text-xs text-tinta-media md:hidden">
          {contagem}/{itens.length}
        </span>
      </summary>

      <div className="mt-3 flex flex-col gap-2.5">
        {itens.map((item) => {
          const chaveDaTela = dependeDeTela(item, catalogo)
          const semEfeito = chaveDaTela !== null && !marcadas.has(chaveDaTela)

          return (
            <label
              key={item.chave}
              className={cn(
                'flex items-start gap-2.5 rounded-lg border border-transparent px-1.5 py-1 transition-opacity duration-150',
                semEfeito && 'opacity-45',
              )}
            >
              <input
                type="checkbox"
                checked={marcadas.has(item.chave)}
                onChange={() => aoAlternar(item.chave)}
                className="mt-0.5 size-4 shrink-0 rounded border-linha-forte accent-tinta"
              />
              <span className="min-w-0 flex-1">
                <span className="flex flex-wrap items-center gap-1.5">
                  <span className="text-sm text-tinta">{item.rotulo}</span>
                  {item.ehTela ? (
                    <span className="rounded-full border border-linha-forte px-1.5 py-0 text-[10px] font-medium uppercase tracking-wide text-tinta-fraca">
                      tela
                    </span>
                  ) : null}
                </span>
                <span className="block text-xs text-tinta-media">{item.descricao}</span>
                {semEfeito ? (
                  <span className="block text-[11px] font-medium text-ambar">
                    Sem acesso à tela, esta ação não faz efeito.
                  </span>
                ) : null}
              </span>
            </label>
          )
        })}
      </div>
    </details>
  )
}
