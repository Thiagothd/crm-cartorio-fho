import { useEffect, useState } from 'react'
import { Copy } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { atendentesApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import type { Atendente } from '@/lib/api/tipos'

/**
 * SEC-03 — emitir ticket de matrícula privilegiada ou resetar o MFA de outra
 * conta. As duas exigem step-up do PRÓPRIO ator (senha + código) — um JWT
 * roubado de um administrador não basta sozinho para nenhuma das duas.
 */
export function DialogoDeMfa({ atendente, aoFechar }: { atendente: Atendente | null; aoFechar: () => void }) {
  const [senhaDoAtor, setSenhaDoAtor] = useState('')
  const [codigoDoAtor, setCodigoDoAtor] = useState('')
  const [motivo, setMotivo] = useState('')
  const [enviando, setEnviando] = useState(false)
  const [erro, setErro] = useState<string | null>(null)
  const [ticket, setTicket] = useState<string | null>(null)

  useEffect(() => {
    setSenhaDoAtor('')
    setCodigoDoAtor('')
    setMotivo('')
    setErro(null)
    setTicket(null)
  }, [atendente])

  async function emitirTicket() {
    if (!atendente) return
    setEnviando(true)
    setErro(null)

    try {
      const resposta = await atendentesApi.emitirTicketDeMfa(atendente.id, senhaDoAtor, codigoDoAtor)
      setTicket(resposta.ticket)
    } catch (falha) {
      setErro(falha instanceof ErroDaApi ? falha.message : 'Não foi possível emitir o ticket.')
    } finally {
      setEnviando(false)
    }
  }

  async function resetar() {
    if (!atendente) return
    if (!motivo.trim()) {
      setErro('Informe o motivo do reset.')
      return
    }
    if (!window.confirm(`Resetar o MFA de ${atendente.nome}? A conta fica sem login até nova matrícula.`)) {
      return
    }

    setEnviando(true)
    setErro(null)

    try {
      await atendentesApi.resetarMfa(atendente.id, senhaDoAtor, codigoDoAtor, motivo)
      toast.success(`MFA de ${atendente.nome} resetado.`)
      aoFechar()
    } catch (falha) {
      setErro(falha instanceof ErroDaApi ? falha.message : 'Não foi possível resetar.')
    } finally {
      setEnviando(false)
    }
  }

  return (
    <Dialog open={Boolean(atendente)} onOpenChange={(v) => !v && aoFechar()}>
      <DialogContent className="max-w-sm">
        <DialogHeader>
          <DialogTitle>Segurança (MFA) — {atendente?.nome}</DialogTitle>
          <DialogDescription>
            Confirme sua própria senha e código de autenticador para qualquer ação aqui.
          </DialogDescription>
        </DialogHeader>

        {ticket ? (
          <div className="flex flex-col gap-2 rounded-lg border border-ambar/40 bg-ambar/[0.06] p-3">
            <p className="text-xs text-tinta-media">
              Ticket emitido — mostrado uma única vez. Entregue por canal fora de banda (nunca
              e-mail ou chat).
            </p>
            <code className="break-all rounded border border-linha bg-branco px-2 py-1.5 text-xs">
              {ticket}
            </code>
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => {
                void navigator.clipboard.writeText(ticket)
                toast.success('Ticket copiado.')
              }}
            >
              <Copy className="size-3.5" />
              Copiar
            </Button>
          </div>
        ) : (
          <div className="flex flex-col gap-3">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="mfa-senha-ator">Sua senha</Label>
              <Input
                id="mfa-senha-ator"
                type="password"
                autoComplete="current-password"
                value={senhaDoAtor}
                onChange={(e) => setSenhaDoAtor(e.target.value)}
              />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="mfa-codigo-ator">Seu código de autenticador</Label>
              <Input
                id="mfa-codigo-ator"
                inputMode="numeric"
                value={codigoDoAtor}
                onChange={(e) => setCodigoDoAtor(e.target.value)}
              />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="mfa-motivo">Motivo (obrigatório só para resetar)</Label>
              <Input id="mfa-motivo" value={motivo} onChange={(e) => setMotivo(e.target.value)} />
            </div>

            {erro ? (
              <p role="alert" className="text-xs text-erro">
                {erro}
              </p>
            ) : null}
          </div>
        )}

        <DialogFooter>
          <Button variant="outline" onClick={aoFechar}>
            Fechar
          </Button>
          {!ticket ? (
            <>
              <Button
                variant="outline"
                disabled={enviando || !senhaDoAtor || !codigoDoAtor}
                onClick={() => void emitirTicket()}
              >
                Emitir ticket
              </Button>
              <Button
                variant="destructive"
                disabled={enviando || !senhaDoAtor || !codigoDoAtor}
                onClick={() => void resetar()}
              >
                Resetar MFA
              </Button>
            </>
          ) : null}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
