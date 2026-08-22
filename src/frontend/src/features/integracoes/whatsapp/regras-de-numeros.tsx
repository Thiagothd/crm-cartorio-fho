import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, Info, Plus, Trash2, TriangleAlert } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { Skeleton } from '@/components/ui/skeleton'
import { whatsappApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { formatarTelefone } from '@/lib/formatacao'
import { cn } from '@/lib/utils'
import type { ModoDeRecebimento, NumeroDoCanal, TipoDeNumeroDoCanal } from '@/lib/api/tipos'
import { DialogoDeNumero } from './dialogo-de-numero'

const MODOS: { valor: ModoDeRecebimento; rotulo: string; descricao: string }[] = [
  {
    valor: 'Todos',
    rotulo: 'Qualquer número',
    descricao: 'Sem filtro. Qualquer contato manda mensagem e recebe resposta.',
  },
  {
    valor: 'SomenteLiberados',
    rotulo: 'Somente os liberados',
    descricao: 'Só os números da lista abaixo falam com o canal — os demais são descartados.',
  },
  {
    valor: 'TodosMenosBloqueados',
    rotulo: 'Todos, menos os bloqueados',
    descricao: 'Qualquer número fala com o canal, exceto os da lista abaixo.',
  },
]

/**
 * Modo de recebimento + a lista correspondente (liberados ou bloqueados).
 *
 * A regra vale para os dois sentidos — entrada e saída —, e não só um deles:
 * é o backend (`IRegrasDoCanal`) que garante isso, esta tela só mostra o
 * resultado e deixa isso explícito no texto.
 */
export function RegrasDeNumeros() {
  const [dialogoAberto, setDialogoAberto] = useState(false)
  const [salvandoModo, setSalvandoModo] = useState<ModoDeRecebimento | null>(null)

  const clienteDeConsultas = useQueryClient()

  const { data, isLoading, isError } = useQuery({
    queryKey: ['whatsapp-regras'],
    queryFn: () => whatsappApi.regras(),
  })

  function recarregar() {
    void clienteDeConsultas.invalidateQueries({ queryKey: ['whatsapp-regras'] })
    void clienteDeConsultas.invalidateQueries({ queryKey: ['whatsapp-descartes'] })
  }

  async function escolherModo(modo: ModoDeRecebimento) {
    if (!data || modo === data.modo || salvandoModo) return

    setSalvandoModo(modo)

    try {
      const novo = await whatsappApi.definirModo(modo)
      clienteDeConsultas.setQueryData(['whatsapp-regras'], novo)
      toast.success('Regra de recebimento atualizada.')
    } catch (falha) {
      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível salvar.')
    } finally {
      setSalvandoModo(null)
    }
  }

  async function remover(numero: NumeroDoCanal) {
    try {
      await whatsappApi.removerNumero(numero.id)
      recarregar()
      toast.success('Número removido.')
    } catch (falha) {
      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível remover.')
    }
  }

  if (isLoading) {
    return (
      <div className="flex flex-col gap-2">
        <Skeleton className="h-32 w-full" />
        <Skeleton className="h-24 w-full" />
      </div>
    )
  }

  if (isError || !data) {
    return (
      <EstadoDaInterface
        tipo="erro"
        titulo="Não foi possível carregar as regras do canal"
        descricao="Tente novamente em instantes."
      />
    )
  }

  const tipoDaLista: TipoDeNumeroDoCanal | null =
    data.modo === 'SomenteLiberados'
      ? 'Liberado'
      : data.modo === 'TodosMenosBloqueados'
        ? 'Bloqueado'
        : null

  const lista = tipoDaLista === 'Liberado' ? data.liberados : tipoDaLista === 'Bloqueado' ? data.bloqueados : []

  return (
    <div className="flex flex-col gap-4 rounded-md border border-linha bg-branco p-4">
      <div>
        <h2 className="font-titulo text-base text-tinta">Quem pode falar com este número</h2>
        <p className="mt-1 text-sm text-tinta-media">
          A regra vale para os dois sentidos: número fora dela não entra, e o painel não consegue
          enviar mensagem para ele.
        </p>
      </div>

      {data.origem === 'Appsettings' ? (
        <p className="flex items-start gap-2 rounded-md border border-linha bg-superficie px-3 py-2 text-xs text-tinta-media">
          <Info className="mt-0.5 size-3.5 shrink-0 text-tinta-fraca" />
          Esta regra ainda vem da configuração do servidor. Escolher uma opção abaixo passa a valer a
          partir de agora.
        </p>
      ) : null}

      <div role="radiogroup" aria-label="Quem pode falar com este número" className="flex flex-col gap-2">
        {MODOS.map((opcao) => {
          const escolhida = data.modo === opcao.valor
          const emAndamento = salvandoModo === opcao.valor

          return (
            <button
              key={opcao.valor}
              type="button"
              role="radio"
              aria-checked={escolhida}
              disabled={salvandoModo !== null}
              onClick={() => void escolherModo(opcao.valor)}
              className={cn(
                'flex flex-col gap-0.5 rounded-md border px-3 py-2.5 text-left transition-colors disabled:opacity-60',
                escolhida ? 'border-tinta bg-superficie' : 'border-linha hover:border-linha-forte',
              )}
            >
              <span className="flex items-center gap-1.5 text-sm font-medium text-tinta">
                {opcao.rotulo}
                {escolhida ? <Check className="size-3.5 text-verde" aria-label="Em uso" /> : null}
                {emAndamento ? (
                  <span className="text-[11px] font-normal text-tinta-fraca">salvando…</span>
                ) : null}
              </span>
              <span className="text-xs text-tinta-media">{opcao.descricao}</span>
            </button>
          )
        })}
      </div>

      {tipoDaLista ? (
        <div className="flex flex-col gap-3">
          {tipoDaLista === 'Liberado' && lista.length === 0 ? (
            <p className="flex items-start gap-2 rounded-md border border-ambar/40 bg-superficie px-3 py-2 text-xs text-tinta">
              <TriangleAlert className="mt-0.5 size-3.5 shrink-0 text-ambar" />
              A lista está vazia — nenhuma mensagem vai entrar enquanto nenhum número for liberado.
            </p>
          ) : null}

          <div className="flex items-center justify-between gap-3">
            <p className="text-sm font-medium text-tinta">
              {tipoDaLista === 'Liberado' ? 'Números liberados' : 'Números bloqueados'}
            </p>
            <Button size="sm" variant="outline" onClick={() => setDialogoAberto(true)}>
              <Plus />
              Adicionar número
            </Button>
          </div>

          {lista.length === 0 ? (
            <p className="rounded-md border border-linha bg-superficie px-3 py-3 text-xs text-tinta-media">
              Nenhum número {tipoDaLista === 'Liberado' ? 'liberado' : 'bloqueado'} ainda.
            </p>
          ) : (
            <div className="overflow-x-auto overflow-y-hidden rounded-md border border-linha">
              <table className="w-full text-sm">
                <thead className="bg-superficie text-xs text-tinta-media">
                  <tr>
                    <th className="px-3 py-2 text-left font-medium">Número</th>
                    <th className="hidden px-3 py-2 text-left font-medium md:table-cell">
                      Adicionado por
                    </th>
                    <th className="w-10" />
                  </tr>
                </thead>
                <tbody>
                  {lista.map((numero) => (
                    <tr key={numero.id} className="border-t border-linha">
                      <td className="px-3 py-2">
                        <span className="font-dado text-tinta">{formatarTelefone(numero.telefone)}</span>
                        {numero.rotulo ? (
                          <span className="ml-2 text-xs text-tinta-media">{numero.rotulo}</span>
                        ) : null}
                      </td>
                      <td className="hidden px-3 py-2 text-xs text-tinta-media md:table-cell">
                        {numero.criadoPorNome ?? '—'}
                      </td>
                      <td className="px-1 py-2">
                        <Button variant="ghost" size="icon-sm" onClick={() => void remover(numero)}>
                          <Trash2 />
                          <span className="sr-only">Remover {formatarTelefone(numero.telefone)}</span>
                        </Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      ) : null}

      <DialogoDeNumero
        tipo={tipoDaLista}
        aberto={dialogoAberto}
        aoFechar={() => setDialogoAberto(false)}
        aoSalvar={recarregar}
      />
    </div>
  )
}
