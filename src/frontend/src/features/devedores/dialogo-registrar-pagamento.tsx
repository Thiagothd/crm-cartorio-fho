import { useEffect, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { CheckCircle2, MessageSquare } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { devedoresApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { usarAutenticacao } from '@/lib/auth/usar-autenticacao'
import { formatarReais } from '@/lib/formatacao'
import {
  interpretarValorMonetario,
  MEIOS_DE_PAGAMENTO,
  montarMensagemDeComprovante,
} from './utilitarios'
import type { MeioDePagamento, PendenciaFinanceira } from '@/lib/api/tipos'

export function DialogoRegistrarPagamento({
  pendencia,
  aoFechar,
}: {
  pendencia: PendenciaFinanceira | null
  aoFechar: () => void
}) {
  const queryClient = useQueryClient()
  const { tenant } = usarAutenticacao()
  const nomeCartorio = tenant?.nomeFantasia || tenant?.razaoSocial || 'Cartório'

  const [modo, setModo] = useState<'total' | 'parcial'>('total')
  const [valorTexto, setValorTexto] = useState('')
  const [meioPagamento, setMeioPagamento] = useState<MeioDePagamento>('Pix')
  const [observacao, setObservacao] = useState('')
  const [enviarComprovante, setEnviarComprovante] = useState(false)
  const [mensagemComprovante, setMensagemComprovante] = useState('')
  const [erro, setErro] = useState<string | null>(null)

  useEffect(() => {
    if (!pendencia) return
    setErro(null)
    setModo('total')
    const saldo = pendencia.saldoRestante
    setValorTexto(saldo !== null && saldo > 0 ? saldo.toFixed(2).replace('.', ',') : '')
    setMeioPagamento('Pix')
    setObservacao('')
    setEnviarComprovante(false)
  }, [pendencia])

  useEffect(() => {
    if (!pendencia) return
    const valorNum = interpretarValorMonetario(valorTexto) ?? pendencia.saldoRestante ?? 0
    const saldoApos =
      modo === 'total' || pendencia.saldoRestante === null
        ? 0
        : Math.max(0, pendencia.saldoRestante - valorNum)

    setMensagemComprovante(
      montarMensagemDeComprovante({
        clienteNome: pendencia.clienteNome,
        cartorioNome: nomeCartorio,
        titulo: pendencia.titulo,
        valorPagoAgora: valorNum,
        saldoRestanteApos: saldoApos,
      }),
    )
  }, [pendencia, valorTexto, modo, nomeCartorio])

  const mutacao = useMutation({
    mutationFn: async () => {
      if (!pendencia) throw new Error('Pendência ausente')
      const valorNum = interpretarValorMonetario(valorTexto)

      return devedoresApi.registrarPagamento(pendencia.id, {
        valor: valorNum,
        meioPagamento,
        observacao: observacao.trim() || null,
        quitarTotalmente: modo === 'total',
        enviarComprovanteWhatsapp: enviarComprovante,
        mensagemComprovante: enviarComprovante ? mensagemComprovante.trim() : null,
      })
    },
    onSuccess: (atualizada) => {
      queryClient.invalidateQueries({ queryKey: ['devedores'] })
      queryClient.invalidateQueries({ queryKey: ['conversas'] })
      toast.success(
        atualizada.status === 'Quitada'
          ? `Pendência de ${atualizada.clienteNome} quitada com sucesso.`
          : `Pagamento parcial registrado para ${atualizada.clienteNome}.`,
      )
      aoFechar()
    },
    onError: (falha) => {
      if (falha instanceof ErroDaApi) {
        setErro(falha.message)
      } else {
        setErro('Não foi possível registrar o pagamento.')
      }
    },
  })

  if (!pendencia) return null

  const saldoAtual = pendencia.saldoRestante

  return (
    <Dialog open={Boolean(pendencia)} onOpenChange={(ab) => !ab && aoFechar()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Dar baixa / registrar pagamento</DialogTitle>
          <DialogDescription>
            Registre o pagamento total ou parcial recebido de{' '}
            <strong className="text-tinta">{pendencia.clienteNome}</strong>.
          </DialogDescription>
        </DialogHeader>

        <form
          onSubmit={(e) => {
            e.preventDefault()
            setErro(null)
            mutacao.mutate()
          }}
          className="space-y-4"
        >
          <div className="flex items-center justify-between rounded-lg border border-linha bg-superficie/70 px-3.5 py-2.5">
            <div className="min-w-0">
              <p className="truncate text-xs font-semibold text-tinta">{pendencia.titulo}</p>
              <p className="text-[11px] text-tinta-media">
                {pendencia.valorPago > 0
                  ? `Já pago: ${formatarReais(pendencia.valorPago)}`
                  : 'Nenhum pagamento parcial anterior'}
              </p>
            </div>
            <div className="text-right">
              <p className="text-[10px] font-medium uppercase tracking-wider text-tinta-fraca">
                Saldo em aberto
              </p>
              <p className="font-dado text-sm font-semibold text-tinta">
                {saldoAtual !== null ? formatarReais(saldoAtual) : 'Valor a apurar'}
              </p>
            </div>
          </div>

          <div className="grid grid-cols-2 gap-2">
            <button
              type="button"
              onClick={() => {
                setModo('total')
                if (saldoAtual !== null && saldoAtual > 0) {
                  setValorTexto(saldoAtual.toFixed(2).replace('.', ','))
                }
              }}
              className={`flex items-center justify-center gap-1.5 rounded-lg border px-3 py-2 text-xs font-semibold transition-colors ${
                modo === 'total'
                  ? 'border-verde bg-verde/10 text-verde'
                  : 'border-linha bg-branco text-tinta-media hover:text-tinta'
              }`}
            >
              <CheckCircle2 className="size-3.5" />
              Quitar totalmente
            </button>
            <button
              type="button"
              onClick={() => {
                setModo('parcial')
                setValorTexto('')
              }}
              className={`flex items-center justify-center gap-1.5 rounded-lg border px-3 py-2 text-xs font-semibold transition-colors ${
                modo === 'parcial'
                  ? 'border-ambar bg-ambar/12 text-tinta'
                  : 'border-linha bg-branco text-tinta-media hover:text-tinta'
              }`}
            >
              Pagamento parcial
            </button>
          </div>

          <div className="grid gap-3 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="pagamento-valor">
                {modo === 'total' ? 'Valor recebido (R$)' : 'Valor parcial recebido (R$)'}
              </Label>
              <Input
                id="pagamento-valor"
                inputMode="decimal"
                value={valorTexto}
                onChange={(e) => setValorTexto(e.target.value)}
                placeholder={modo === 'total' ? 'Quitação integral' : 'Ex.: 100,00'}
              />
            </div>

            <div className="space-y-1.5">
              <Label>Meio de pagamento</Label>
              <Select
                value={meioPagamento}
                onValueChange={(v) => setMeioPagamento(v as MeioDePagamento)}
              >
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {MEIOS_DE_PAGAMENTO.map((m) => (
                    <SelectItem key={m.valor} value={m.valor}>
                      {m.rotulo}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="pagamento-obs">Observação do recebimento (opcional)</Label>
            <Input
              id="pagamento-obs"
              value={observacao}
              onChange={(e) => setObservacao(e.target.value)}
              placeholder="Ex.: Comprovante enviado no WhatsApp"
            />
          </div>

          <div className="rounded-lg border border-linha bg-superficie/40 p-3">
            <label className="flex cursor-pointer items-center gap-2 text-xs font-medium text-tinta">
              <input
                type="checkbox"
                checked={enviarComprovante}
                onChange={(e) => setEnviarComprovante(e.target.checked)}
                className="size-4 rounded border-linha accent-selo"
              />
              <MessageSquare className="size-3.5 text-tinta-media" />
              Enviar confirmação de recebimento no WhatsApp do cliente
            </label>

            {enviarComprovante ? (
              <div className="mt-2.5 space-y-1">
                <Textarea
                  rows={3}
                  value={mensagemComprovante}
                  onChange={(e) => setMensagemComprovante(e.target.value)}
                  className="bg-branco text-xs"
                />
                <p className="text-[11px] text-tinta-fraca">
                  A mensagem será enviada pelo canal do cartório e registrada no atendimento.
                </p>
              </div>
            ) : null}
          </div>

          {erro ? (
            <p role="alert" className="rounded-md border border-selo/30 bg-selo/8 px-3 py-2 text-xs text-selo">
              {erro}
            </p>
          ) : null}

          <DialogFooter className="gap-2 pt-1">
            <Button type="button" variant="outline" onClick={aoFechar}>
              Cancelar
            </Button>
            <Button type="submit" disabled={mutacao.isPending}>
              {mutacao.isPending
                ? 'Confirmando...'
                : modo === 'total'
                  ? 'Confirmar quitação'
                  : 'Registrar pagamento parcial'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
