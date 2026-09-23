import { useEffect, useState } from 'react'
import { TriangleAlert } from 'lucide-react'
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
import type { Mensagem } from '@/lib/api/tipos'

/**
 * Consertar, no aparelho do cliente, o que o cartório mandou errado.
 *
 * As duas ações vivem no mesmo arquivo porque são a mesma decisão vista de dois
 * ângulos — "dá para reescrever" e "não dá, tira de lá" —, e quem abre uma
 * costuma considerar a outra. Separá-las em dois arquivos faria o texto de uma
 * divergir do da outra no primeiro ajuste.
 *
 * Nenhuma das duas esconde o que aconteceu. O WhatsApp marca "editada" e "Esta
 * mensagem foi apagada" no aparelho do cliente, e o fio guarda o texto anterior
 * com quem mexeu e quando. O diálogo diz isso antes, em vez de deixar o
 * atendente descobrir depois.
 */

/** Mesmo limite da coluna de conteúdo no banco. */
const LIMITE_DO_TEXTO = 4000

export function DialogoCorrigirMensagem({
  mensagem,
  aoFechar,
  aoConfirmar,
}: {
  /** Mensagem em correção. `null` mantém o diálogo fechado. */
  mensagem: Mensagem | null
  aoFechar: () => void
  aoConfirmar: (conteudo: string) => Promise<void>
}) {
  const [conteudo, setConteudo] = useState('')
  const [salvando, setSalvando] = useState(false)

  useEffect(() => {
    if (mensagem) {
      setConteudo(mensagem.conteudo)
    }
  }, [mensagem])

  const mudou = mensagem !== null && conteudo.trim() !== mensagem.conteudo
  const vazio = conteudo.trim().length === 0

  async function salvar() {
    if (!mudou || vazio) return

    setSalvando(true)

    try {
      await aoConfirmar(conteudo.trim())
      aoFechar()
    } finally {
      setSalvando(false)
    }
  }

  return (
    <Dialog open={mensagem !== null} onOpenChange={(aberto) => !aberto && aoFechar()}>
      <DialogContent className="max-w-xl">
        <DialogHeader>
          <DialogTitle>Corrigir mensagem enviada</DialogTitle>
          <DialogDescription>
            O texto novo substitui o antigo no WhatsApp do cliente, e o aparelho dele mostra a
            mensagem como <strong className="text-tinta">editada</strong>. O texto anterior fica
            guardado aqui no atendimento.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-4">
          {/* O que o cliente leu, à vista de quem está reescrevendo: sem isto o
              atendente corrige de memória, e o engano que ele quer consertar é
              justamente de memória. */}
          <div className="flex flex-col gap-1.5">
            <Label>O cliente recebeu</Label>
            <p className="max-h-28 overflow-y-auto whitespace-pre-wrap rounded-md border border-linha bg-superficie px-3 py-2 text-xs leading-relaxed text-tinta-media">
              {mensagem?.conteudo}
            </p>
          </div>

          <div className="flex flex-col gap-1.5">
            <div className="flex items-baseline justify-between gap-2">
              <Label htmlFor="correcao-conteudo">Texto corrigido</Label>
              {conteudo.length > LIMITE_DO_TEXTO * 0.8 ? (
                <span className="font-dado text-[11px] text-tinta-fraca">
                  {conteudo.length}/{LIMITE_DO_TEXTO}
                </span>
              ) : null}
            </div>
            <Textarea
              id="correcao-conteudo"
              value={conteudo}
              onChange={(e) => setConteudo(e.target.value)}
              rows={6}
              maxLength={LIMITE_DO_TEXTO}
              className="resize-none"
              autoFocus
            />
          </div>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={aoFechar}>
            Cancelar
          </Button>
          <Button onClick={() => void salvar()} disabled={salvando || !mudou || vazio}>
            Corrigir no WhatsApp
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export function DialogoApagarParaTodos({
  mensagem,
  aoFechar,
  aoConfirmar,
}: {
  mensagem: Mensagem | null
  aoFechar: () => void
  aoConfirmar: () => Promise<void>
}) {
  const [apagando, setApagando] = useState(false)

  async function apagar() {
    setApagando(true)

    try {
      await aoConfirmar()
      aoFechar()
    } finally {
      setApagando(false)
    }
  }

  return (
    <Dialog open={mensagem !== null} onOpenChange={(aberto) => !aberto && aoFechar()}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>Apagar esta mensagem para o cliente?</DialogTitle>
          <DialogDescription>
            No aparelho dele, o texto some e fica no lugar o aviso{' '}
            <strong className="text-tinta">“Esta mensagem foi apagada”</strong>. Não é possível
            fazer isso em silêncio — o WhatsApp sempre avisa.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-3">
          <p className="max-h-28 overflow-y-auto whitespace-pre-wrap rounded-md border border-linha bg-superficie px-3 py-2 text-xs leading-relaxed text-tinta-media">
            {mensagem?.conteudo}
          </p>

          <p className="flex items-start gap-2 text-xs leading-relaxed text-tinta-media">
            <TriangleAlert aria-hidden className="mt-0.5 size-3.5 shrink-0" />
            <span>
              A mensagem <strong className="text-tinta">continua no atendimento</strong>, marcada
              como apagada, com o texto e o nome de quem apagou. Nada é excluído do registro da
              serventia.
            </span>
          </p>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={aoFechar}>
            Cancelar
          </Button>
          <Button onClick={() => void apagar()} disabled={apagando}>
            Apagar para o cliente
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
