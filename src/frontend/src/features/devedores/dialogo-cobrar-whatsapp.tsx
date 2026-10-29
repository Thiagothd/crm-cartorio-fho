import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { ExternalLink, Send } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
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
import { devedoresApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { usarAutenticacao } from '@/lib/auth/usar-autenticacao'
import { formatarReais, formatarTelefone } from '@/lib/formatacao'
import {
  ESTILOS_DE_COBRANCA,
  montarMensagemDeCobranca,
  type EstiloDeMensagemDeCobranca,
} from './utilitarios'
import type { PendenciaFinanceira } from '@/lib/api/tipos'

export interface AlvoDeCobrancaWhatsapp {
  clienteId: string
  clienteNome: string
  clienteTelefone: string
  pendencias: PendenciaFinanceira[]
}

export function DialogoCobrarWhatsapp({
  alvo,
  aoFechar,
}: {
  alvo: AlvoDeCobrancaWhatsapp | null
  aoFechar: () => void
}) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { tenant } = usarAutenticacao()
  const nomeCartorio = tenant?.nomeFantasia || tenant?.razaoSocial || 'Cartório'

  const [estilo, setEstilo] = useState<EstiloDeMensagemDeCobranca>('cordial')
  const [idsSelecionados, setIdsSelecionados] = useState<string[]>([])
  const [mensagem, setMensagem] = useState('')
  const [abrirAposEnviar, setAbrirAposEnviar] = useState(true)
  const [erro, setErro] = useState<string | null>(null)

  useEffect(() => {
    if (!alvo) return
    setErro(null)
    const abertas = alvo.pendencias.filter((p) => p.status === 'Aberta')
    const base = abertas.length > 0 ? abertas : alvo.pendencias
    const ids = base.map((p) => p.id)
    setIdsSelecionados(ids)

    const temPromessa = base.some((p) => Boolean(p.dataPromessaPagamento))
    const estiloInicial: EstiloDeMensagemDeCobranca = temPromessa ? 'promessa' : 'cordial'
    setEstilo(estiloInicial)
    setMensagem(
      montarMensagemDeCobranca({
        estilo: estiloInicial,
        clienteNome: alvo.clienteNome,
        cartorioNome: nomeCartorio,
        pendencias: base,
      }),
    )
  }, [alvo, nomeCartorio])

  function trocarEstilo(novoEstilo: EstiloDeMensagemDeCobranca) {
    setEstilo(novoEstilo)
    if (!alvo) return
    const selecionadas = alvo.pendencias.filter((p) => idsSelecionados.includes(p.id))
    setMensagem(
      montarMensagemDeCobranca({
        estilo: novoEstilo,
        clienteNome: alvo.clienteNome,
        cartorioNome: nomeCartorio,
        pendencias: selecionadas,
      }),
    )
  }

  function alternarPendencia(id: string) {
    if (!alvo) return
    const proximo = idsSelecionados.includes(id)
      ? idsSelecionados.filter((x) => x !== id)
      : [...idsSelecionados, id]

    if (proximo.length === 0) return
    setIdsSelecionados(proximo)
    const selecionadas = alvo.pendencias.filter((p) => proximo.includes(p.id))
    setMensagem(
      montarMensagemDeCobranca({
        estilo,
        clienteNome: alvo.clienteNome,
        cartorioNome: nomeCartorio,
        pendencias: selecionadas,
      }),
    )
  }

  const mutacaoCobrar = useMutation({
    mutationFn: async () => {
      if (!alvo) throw new Error('Alvo ausente')
      return devedoresApi.cobrarNoWhatsapp({
        clienteId: alvo.clienteId,
        pendenciaIds: idsSelecionados,
        mensagem: mensagem.trim(),
      })
    },
    onSuccess: (resposta) => {
      queryClient.invalidateQueries({ queryKey: ['devedores'] })
      queryClient.invalidateQueries({ queryKey: ['conversas'] })
      queryClient.invalidateQueries({ queryKey: ['painel-resumo'] })
      toast.success(`Mensagem enviada para ${alvo?.clienteNome} (adicionada em Minhas).`)
      aoFechar()
      if (abrirAposEnviar) {
        navigate(`/atendimentos/${resposta.conversaId}?escopo=minhas`)
      }
    },
    onError: (falha) => {
      if (falha instanceof ErroDaApi) {
        setErro(falha.message)
      } else {
        setErro('Não foi possível enviar a mensagem no WhatsApp.')
      }
    },
  })

  const mutacaoApenasAbrir = useMutation({
    mutationFn: async () => {
      if (!alvo) throw new Error('Alvo ausente')
      return devedoresApi.abrirConversa(alvo.clienteId)
    },
    onSuccess: (resposta) => {
      queryClient.invalidateQueries({ queryKey: ['devedores'] })
      queryClient.invalidateQueries({ queryKey: ['conversas'] })
      queryClient.invalidateQueries({ queryKey: ['painel-resumo'] })
      aoFechar()
      navigate(`/atendimentos/${resposta.conversaId}?escopo=minhas`)
    },
  })

  if (!alvo) return null

  const abertas = alvo.pendencias.filter((p) => p.status === 'Aberta')
  const listaExibida = abertas.length > 0 ? abertas : alvo.pendencias

  return (
    <Dialog open={Boolean(alvo)} onOpenChange={(ab) => !ab && aoFechar()}>
      <DialogContent className="max-h-[92dvh] overflow-y-auto sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Cobrar pelo WhatsApp do cartório</DialogTitle>
          <DialogDescription>
            A conversa entra diretamente em <strong>Minhas</strong> no seu nome e o robô de triagem
            fica desativado para que vocês conversem normalmente.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="flex items-center justify-between gap-2 rounded-lg border border-linha bg-superficie/70 px-3.5 py-2.5">
            <div className="min-w-0">
              <p className="truncate text-sm font-semibold text-tinta">{alvo.clienteNome}</p>
              <p className="font-dado text-xs text-tinta-media">
                {formatarTelefone(alvo.clienteTelefone)}
              </p>
            </div>
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={mutacaoApenasAbrir.isPending}
              onClick={() => mutacaoApenasAbrir.mutate()}
              title="Abrir conversa normal sem enviar mensagem pronta"
              className="shrink-0 text-xs"
            >
              <ExternalLink className="size-3.5" />
              Só abrir conversa
            </Button>
          </div>

          {listaExibida.length > 1 ? (
            <div className="space-y-1.5">
              <Label>Pendências incluídas neste lembrete</Label>
              <div className="divide-y divide-linha rounded-lg border border-linha bg-branco">
                {listaExibida.map((p) => {
                  const marcada = idsSelecionados.includes(p.id)
                  return (
                    <label
                      key={p.id}
                      className="flex cursor-pointer items-center justify-between gap-2 px-3 py-2 text-xs hover:bg-superficie/50"
                    >
                      <span className="flex min-w-0 items-center gap-2">
                        <input
                          type="checkbox"
                          checked={marcada}
                          onChange={() => alternarPendencia(p.id)}
                          className="size-3.5 rounded border-linha accent-selo"
                        />
                        <span className="truncate font-medium text-tinta">{p.titulo}</span>
                      </span>
                      <span className="shrink-0 font-dado text-tinta-media">
                        {p.saldoRestante !== null ? formatarReais(p.saldoRestante) : 'A apurar'}
                      </span>
                    </label>
                  )
                })}
              </div>
            </div>
          ) : null}

          <div className="space-y-1.5">
            <Label>Modelo da mensagem</Label>
            <div className="grid gap-2 sm:grid-cols-3">
              {ESTILOS_DE_COBRANCA.map((item) => {
                const ativo = estilo === item.id
                return (
                  <button
                    key={item.id}
                    type="button"
                    onClick={() => trocarEstilo(item.id)}
                    className={`flex flex-col rounded-lg border p-2.5 text-left transition-colors ${
                      ativo
                        ? 'border-selo bg-selo/6 text-tinta'
                        : 'border-linha bg-branco text-tinta-media hover:border-linha-forte hover:text-tinta'
                    }`}
                  >
                    <span className="text-xs font-semibold text-tinta">{item.rotulo}</span>
                    <span className="mt-0.5 text-[11px] leading-snug text-tinta-media">
                      {item.descricao}
                    </span>
                  </button>
                )
              })}
            </div>
          </div>

          <div className="space-y-1.5">
            <div className="flex items-center justify-between">
              <Label htmlFor="cobranca-texto">Mensagem a enviar</Label>
              <span className="text-[11px] text-tinta-fraca">Editável antes do envio</span>
            </div>
            <Textarea
              id="cobranca-texto"
              rows={4}
              value={mensagem}
              onChange={(e) => setMensagem(e.target.value)}
            />
          </div>

          <label className="flex cursor-pointer items-center gap-2 text-xs text-tinta-media">
            <input
              type="checkbox"
              checked={abrirAposEnviar}
              onChange={(e) => setAbrirAposEnviar(e.target.checked)}
              className="size-3.5 rounded border-linha accent-selo"
            />
            Ir para a conversa no Atendimento logo após disparar a mensagem
          </label>

          {erro ? (
            <p role="alert" className="rounded-md border border-selo/30 bg-selo/8 px-3 py-2 text-xs text-selo">
              {erro}
            </p>
          ) : null}

          <DialogFooter className="gap-2 pt-1">
            <Button type="button" variant="outline" onClick={aoFechar}>
              Cancelar
            </Button>
            <Button
              type="button"
              disabled={mutacaoCobrar.isPending || !mensagem.trim()}
              onClick={() => {
                setErro(null)
                mutacaoCobrar.mutate()
              }}
            >
              {mutacaoCobrar.isPending ? (
                'Enviando...'
              ) : (
                <>
                  <Send className="size-3.5" />
                  Enviar cobrança no WhatsApp
                </>
              )}
            </Button>
          </DialogFooter>
        </div>
      </DialogContent>
    </Dialog>
  )
}
