import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Search } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { conversasApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { cn } from '@/lib/utils'

/**
 * Escolhe a conversa de destino de um encaminhamento.
 *
 * Só conversas ATIVAS aparecem: encaminhar para um atendimento encerrado seria
 * reabrir a discussão num lugar que o cartório já fechou.
 */
export function DialogoEncaminhar({
  conversaOrigemId,
  mensagemIds,
  aberto,
  aoFechar,
  aoConcluir,
}: {
  conversaOrigemId: string
  mensagemIds: string[]
  aberto: boolean
  aoFechar: () => void
  aoConcluir: () => void
}) {
  const quantidade = mensagemIds.length

  const [busca, setBusca] = useState('')
  const [destinoId, setDestinoId] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)

  useEffect(() => {
    if (!aberto) return

    setBusca('')
    setDestinoId(null)
  }, [aberto])

  const { data, isLoading } = useQuery({
    queryKey: ['destinos-encaminhamento', conversaOrigemId, busca],
    queryFn: () => conversasApi.destinos(conversaOrigemId, busca.trim() || undefined),
    enabled: aberto,
  })

  async function encaminhar() {
    if (!destinoId || enviando) return

    setEnviando(true)

    try {
      const r = await conversasApi.encaminharMensagens(conversaOrigemId, destinoId, mensagemIds)

      toast.success(
        r.encaminhadas === 1
          ? 'Mensagem encaminhada.'
          : `${r.encaminhadas} mensagens encaminhadas.`,
      )
      aoConcluir()
      aoFechar()
    } catch (falha) {
      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível encaminhar.')
    } finally {
      setEnviando(false)
    }
  }

  return (
    <Dialog open={aberto} onOpenChange={(v) => !v && aoFechar()}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>
            Encaminhar {quantidade === 1 ? 'mensagem' : `${quantidade} mensagens`}
          </DialogTitle>
          <DialogDescription>
            O texto é reenviado <strong className="text-tinta">em seu nome</strong> na conversa de
            destino, com registro da conversa de origem. Só atendimentos ativos aparecem aqui.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-3">
          <div className="relative">
            <Search className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-tinta-media" />
            <Input
              value={busca}
              onChange={(e) => setBusca(e.target.value)}
              placeholder="Nome do cliente ou protocolo"
              className="pl-8"
              autoComplete="off"
            />
          </div>

          {isLoading ? (
            <div className="flex flex-col gap-1.5">
              {[0, 1, 2].map((i) => (
                <Skeleton key={i} className="h-14 w-full" />
              ))}
            </div>
          ) : data?.length ? (
            <ul
              role="radiogroup"
              aria-label="Conversa de destino"
              className="flex max-h-72 flex-col gap-1 overflow-y-auto"
            >
              {data.map((destino) => {
                const escolhido = destinoId === destino.id

                return (
                  <li key={destino.id}>
                    <button
                      type="button"
                      role="radio"
                      aria-checked={escolhido}
                      onClick={() => setDestinoId(destino.id)}
                      className={cn(
                        'w-full rounded-md border px-3 py-2 text-left transition-colors',
                        escolhido
                          ? 'border-tinta bg-superficie'
                          : 'border-linha hover:border-linha-forte hover:bg-superficie',
                      )}
                    >
                      <div className="flex items-center gap-2">
                        <span className="etiqueta-protocolo text-[11px]">{destino.protocolo}</span>
                        <span className="truncate text-sm font-medium text-tinta">
                          {destino.clienteNome}
                        </span>
                      </div>
                      <p className="mt-0.5 truncate text-xs text-tinta-media">
                        {destino.setorNome ?? 'Sem setor'}
                        {' · '}
                        {destino.atendenteNome ?? 'na fila'}
                      </p>
                    </button>
                  </li>
                )
              })}
            </ul>
          ) : (
            <p className="rounded-md border border-linha bg-superficie px-3 py-3 text-xs text-tinta-media">
              {busca.trim()
                ? `Nenhum atendimento ativo para "${busca.trim()}".`
                : 'Não há outro atendimento ativo para receber o encaminhamento.'}
            </p>
          )}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={aoFechar}>
            Cancelar
          </Button>
          <Button onClick={() => void encaminhar()} disabled={!destinoId || enviando}>
            Encaminhar
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
