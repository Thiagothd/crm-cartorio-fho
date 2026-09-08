import { useEffect, useState } from 'react'
import { Dices } from 'lucide-react'
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
import { ErroDaApi } from '@/lib/api/cliente-http'
import { usarRedefinirSenha } from './consultas'
import type { Atendente } from '@/lib/api/tipos'

function gerarSenhaForte() {
  const alfabeto = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%'
  const bytes = new Uint32Array(14)
  crypto.getRandomValues(bytes)
  return Array.from(bytes, (b) => alfabeto[b % alfabeto.length]).join('')
}

export function DialogoDeSenha({ atendente, aoFechar }: { atendente: Atendente | null; aoFechar: () => void }) {
  const [novaSenha, setNovaSenha] = useState('')
  const redefinir = usarRedefinirSenha()

  useEffect(() => {
    setNovaSenha('')
  }, [atendente])

  async function salvar() {
    if (!atendente) return

    try {
      await redefinir.mutateAsync({ id: atendente.id, novaSenha })
      toast.success(`Senha de ${atendente.nome} redefinida.`)
      aoFechar()
    } catch (falha) {
      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível redefinir.')
    }
  }

  return (
    <Dialog open={Boolean(atendente)} onOpenChange={(v) => !v && aoFechar()}>
      <DialogContent className="max-w-sm">
        <DialogHeader>
          <DialogTitle>Redefinir senha</DialogTitle>
          <DialogDescription>{atendente?.nome}</DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-1.5">
          <Label htmlFor="nova-senha">Nova senha</Label>
          <div className="flex gap-2">
            <Input
              id="nova-senha"
              type="text"
              value={novaSenha}
              onChange={(e) => setNovaSenha(e.target.value)}
              placeholder="Mínimo de 8 caracteres"
              className="font-mono"
            />
            <Button type="button" variant="outline" size="icon" title="Gerar senha forte" onClick={() => setNovaSenha(gerarSenhaForte())}>
              <Dices />
            </Button>
          </div>
          <p className="text-xs text-tinta-media">
            Também desbloqueia a conta, se estava bloqueada por tentativas erradas.
          </p>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={aoFechar}>
            Cancelar
          </Button>
          <Button onClick={() => void salvar()} disabled={redefinir.isPending || novaSenha.length < 8}>
            Redefinir
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
