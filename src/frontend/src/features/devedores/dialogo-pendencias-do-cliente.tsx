import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { CheckCircle2, HandCoins, Plus } from 'lucide-react'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { devedoresApi } from '@/lib/api/endpoints'
import { usarAutenticacao } from '@/lib/auth/usar-autenticacao'
import { formatarReais, formatarTelefone } from '@/lib/formatacao'
import { formatarDataIso } from './utilitarios'
import { DialogoNovaPendencia } from './dialogo-nova-pendencia'
import { DialogoRegistrarPagamento } from './dialogo-registrar-pagamento'
import type { PendenciaFinanceira } from '@/lib/api/tipos'

export function DialogoPendenciasDoCliente({
  cliente,
  aoFechar,
}: {
  cliente: {
    id: string
    nome: string
    telefone: string
    conversaOrigemId?: string | null
    tipoServicoId?: string | null
    tipoServicoNome?: string | null
  } | null
  aoFechar: () => void
}) {
  const { pode } = usarAutenticacao()
  const podeEditar = pode('devedores.editar')

  const [novaAberta, setNovaAberta] = useState(false)
  const [emPagamento, setEmPagamento] = useState<PendenciaFinanceira | null>(null)

  const { data, isLoading } = useQuery({
    queryKey: ['devedores', 'cliente', cliente?.id],
    queryFn: () => devedoresApi.obterPorCliente(cliente!.id),
    enabled: Boolean(cliente?.id),
  })

  if (!cliente) return null

  const abertas = (data?.pendencias ?? []).filter((p) => p.status === 'Aberta')
  const quitadas = (data?.pendencias ?? []).filter((p) => p.status === 'Quitada')

  return (
    <>
      <Dialog open={Boolean(cliente)} onOpenChange={(ab) => !ab && aoFechar()}>
        <DialogContent className="max-h-[90dvh] overflow-y-auto sm:max-w-lg">
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <HandCoins className="size-4 text-ambar" />
              Pendências financeiras de {cliente.nome}
            </DialogTitle>
            <DialogDescription>
              {formatarTelefone(cliente.telefone)} · Consulte os lançamentos em aberto ou dê baixa
              sem sair do atendimento.
            </DialogDescription>
          </DialogHeader>

          {isLoading ? (
            <p className="py-6 text-center text-xs text-tinta-media">
              Carregando pendências do cliente...
            </p>
          ) : (
            <div className="space-y-4">
              <div className="flex items-center justify-between rounded-lg border border-ambar/35 bg-ambar/10 px-3.5 py-2.5">
                <div>
                  <p className="text-[11px] font-medium uppercase tracking-wider text-tinta-media">
                    Em aberto ({abertas.length})
                  </p>
                  <p className="font-dado text-base font-semibold text-tinta">
                    {data && data.saldoDevedor > 0
                      ? formatarReais(data.saldoDevedor)
                      : abertas.length > 0
                        ? 'Valor a apurar'
                        : 'R$ 0,00'}
                  </p>
                </div>
                {podeEditar ? (
                  <Button
                    type="button"
                    size="sm"
                    variant="outline"
                    onClick={() => setNovaAberta(true)}
                  >
                    <Plus className="size-3.5" />
                    Nova pendência
                  </Button>
                ) : null}
              </div>

              {abertas.length === 0 ? (
                <p className="rounded-lg border border-linha bg-superficie/40 px-3 py-4 text-center text-xs text-tinta-media">
                  Nenhuma pendência em aberto para este contato.
                </p>
              ) : (
                <div className="space-y-2">
                  {abertas.map((p) => (
                    <div
                      key={p.id}
                      className="flex items-center justify-between gap-3 rounded-lg border border-linha bg-branco p-3"
                    >
                      <div className="min-w-0 flex-1">
                        <p className="truncate text-xs font-semibold text-tinta">{p.titulo}</p>
                        <p className="mt-0.5 text-[11px] text-tinta-media">
                          Desde {formatarDataIso(p.dataReferencia)}
                          {p.dataPromessaPagamento
                            ? ` · Promessa: ${formatarDataIso(p.dataPromessaPagamento)}`
                            : ''}
                        </p>
                        {p.observacoes ? (
                          <p className="mt-1 text-[11px] text-tinta-fraca">{p.observacoes}</p>
                        ) : null}
                      </div>
                      <div className="flex shrink-0 flex-col items-end gap-1.5">
                        <span className="font-dado text-xs font-semibold text-tinta">
                          {p.saldoRestante !== null
                            ? formatarReais(p.saldoRestante)
                            : 'Valor a apurar'}
                        </span>
                        {podeEditar ? (
                          <Button
                            type="button"
                            size="sm"
                            variant="outline"
                            onClick={() => setEmPagamento(p)}
                            className="border-verde/40 text-verde hover:bg-verde/10"
                          >
                            <CheckCircle2 className="size-3" />
                            Dar baixa
                          </Button>
                        ) : null}
                      </div>
                    </div>
                  ))}
                </div>
              )}

              {quitadas.length > 0 ? (
                <div className="space-y-1.5 pt-1">
                  <p className="text-[11px] font-semibold uppercase tracking-wider text-tinta-fraca">
                    Histórico quitado ({quitadas.length})
                  </p>
                  <div className="divide-y divide-linha rounded-lg border border-linha bg-superficie/30">
                    {quitadas.slice(0, 5).map((q) => (
                      <div
                        key={q.id}
                        className="flex items-center justify-between px-3 py-2 text-xs text-tinta-media"
                      >
                        <span className="truncate">{q.titulo}</span>
                        <span className="shrink-0 font-dado text-verde">
                          Quitado ({formatarReais(q.valorPago)})
                        </span>
                      </div>
                    ))}
                  </div>
                </div>
              ) : null}
            </div>
          )}

          <DialogFooter>
            <Button type="button" variant="outline" onClick={aoFechar}>
              Fechar
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <DialogoNovaPendencia
        aberto={novaAberta}
        aoMudarAberto={setNovaAberta}
        clienteFixo={cliente}
      />

      <DialogoRegistrarPagamento
        pendencia={emPagamento}
        aoFechar={() => setEmPagamento(null)}
      />
    </>
  )
}
