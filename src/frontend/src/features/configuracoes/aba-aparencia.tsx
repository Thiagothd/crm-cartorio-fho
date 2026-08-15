import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Check } from 'lucide-react'
import { toast } from 'sonner'
import { Skeleton } from '@/components/ui/skeleton'
import { serventiaApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { cn } from '@/lib/utils'
import type { AparenciaDaConversa } from '@/lib/api/tipos'

/**
 * Espelho de `AparenciaDaConversa` no backend.
 *
 * Lista fechada: cada valor corresponde a uma classe de estilo que precisa
 * existir no CSS. Um campo livre aqui deixaria alguém colar papel de parede no
 * fundo de um livro de registro.
 */
const APARENCIAS: {
  valor: AparenciaDaConversa
  rotulo: string
  descricao: string
  classe: string
}[] = [
  {
    valor: 'PapelLiso',
    rotulo: 'Papel liso',
    descricao: 'Só a marca d’água do selo, no canto. É o padrão.',
    classe: 'marca-dagua-selo',
  },
  {
    valor: 'SeloRepetido',
    rotulo: 'Selo repetido',
    descricao: 'O anel do selo em grade larga, a 3% — presente de perto, invisível de longe.',
    classe: 'fundo-conversa-selo',
  },
  {
    valor: 'PautaFina',
    rotulo: 'Pauta fina',
    descricao: 'Linhas horizontais finas, como papel pautado de livro de registro.',
    classe: 'fundo-conversa-pauta',
  },
]

export function AbaAparencia() {
  const [salvando, setSalvando] = useState<AparenciaDaConversa | null>(null)
  const clienteDeConsultas = useQueryClient()

  const { data, isLoading } = useQuery({
    queryKey: ['aparencia-da-conversa'],
    queryFn: () => serventiaApi.aparencia(),
  })

  const atual = data?.aparenciaDaConversa ?? 'PapelLiso'

  async function escolher(valor: AparenciaDaConversa) {
    if (valor === atual || salvando) return

    setSalvando(valor)

    try {
      await serventiaApi.definirAparencia(valor)
      void clienteDeConsultas.invalidateQueries({ queryKey: ['aparencia-da-conversa'] })
      toast.success('Aparência da conversa atualizada.')
    } catch (falha) {
      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível salvar.')
    } finally {
      setSalvando(null)
    }
  }

  return (
    <div className="flex flex-col gap-4">
      <p className="max-w-2xl text-sm text-tinta-media">
        Textura de fundo do fio de mensagens. Vale para{' '}
        <strong className="text-tinta">toda a serventia</strong>, não por atendente — a mesma tela
        vista por duas pessoas do mesmo cartório não deveria parecer dois produtos. As três usam
        apenas os tons da paleta e ficam sempre mais discretas que o texto que sustentam.
      </p>

      {isLoading ? (
        <div className="grid gap-3 sm:grid-cols-3">
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} className="h-40 w-full" />
          ))}
        </div>
      ) : (
        /* radiogroup, não botões soltos: são opções mutuamente exclusivas, e o
           leitor de tela precisa anunciar "1 de 3" em vez de três botões avulsos. */
        <div role="radiogroup" aria-label="Aparência da conversa" className="grid gap-3 sm:grid-cols-3">
          {APARENCIAS.map((opcao) => {
            const escolhida = atual === opcao.valor
            const emAndamento = salvando === opcao.valor

            return (
              <button
                key={opcao.valor}
                type="button"
                role="radio"
                aria-checked={escolhida}
                disabled={salvando !== null}
                onClick={() => void escolher(opcao.valor)}
                className={cn(
                  'flex flex-col overflow-hidden rounded-md border text-left transition-colors disabled:opacity-60',
                  escolhida
                    ? 'border-tinta'
                    : 'border-linha hover:border-linha-forte',
                )}
              >
                {/* Pré-visualização com a textura de verdade, não uma imagem de
                    exemplo: o que se vê aqui é literalmente a classe que vai para
                    o fio, então não há como a amostra mentir sobre o resultado. */}
                <span
                  aria-hidden
                  className={cn('h-24 w-full border-b border-linha bg-papel', opcao.classe)}
                />

                <span className="flex flex-1 flex-col gap-1 p-3">
                  <span className="flex items-center gap-1.5">
                    <span className="text-sm font-medium text-tinta">{opcao.rotulo}</span>
                    {escolhida ? (
                      <Check className="size-3.5 shrink-0 text-verde" aria-label="Em uso" />
                    ) : null}
                    {emAndamento ? (
                      <span className="text-[11px] text-tinta-fraca">salvando…</span>
                    ) : null}
                  </span>
                  <span className="text-xs leading-snug text-tinta-media">{opcao.descricao}</span>
                </span>
              </button>
            )
          })}
        </div>
      )}
    </div>
  )
}
