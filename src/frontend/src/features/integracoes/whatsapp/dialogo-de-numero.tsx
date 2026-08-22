import { useEffect, useState } from 'react'
import { Loader2 } from 'lucide-react'
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
import { whatsappApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { mascararTelefone, somenteDigitos } from '@/lib/mascaras'
import type { TipoDeNumeroDoCanal } from '@/lib/api/tipos'

/**
 * `tipo` chega fixo de quem abre o diálogo — a lista sendo gerenciada
 * (liberados ou bloqueados) já está decidida pelo modo ativo em
 * `RegrasDeNumeros`; o atendente não escolhe de novo aqui.
 */
export function DialogoDeNumero({
  tipo,
  aberto,
  aoFechar,
  aoSalvar,
}: {
  tipo: TipoDeNumeroDoCanal | null
  aberto: boolean
  aoFechar: () => void
  aoSalvar: () => void
}) {
  const [telefone, setTelefone] = useState('')
  const [rotulo, setRotulo] = useState('')
  const [salvando, setSalvando] = useState(false)

  useEffect(() => {
    if (!aberto) return

    setTelefone('')
    setRotulo('')
  }, [aberto])

  async function salvar() {
    if (!tipo || salvando) return

    setSalvando(true)

    try {
      await whatsappApi.adicionarNumero({
        telefone: somenteDigitos(telefone),
        rotulo: rotulo.trim() || null,
        tipo,
      })

      toast.success(tipo === 'Liberado' ? 'Número liberado.' : 'Número bloqueado.')
      aoSalvar()
      aoFechar()
    } catch (falha) {
      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível salvar.')
    } finally {
      setSalvando(false)
    }
  }

  return (
    <Dialog open={aberto} onOpenChange={(v) => !v && aoFechar()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{tipo === 'Liberado' ? 'Liberar número' : 'Bloquear número'}</DialogTitle>
          <DialogDescription>
            {tipo === 'Liberado'
              ? 'Só números liberados falam com o canal enquanto este modo estiver ativo.'
              : 'Este número deixa de falar com o canal nos dois sentidos — não entra e não recebe envio.'}
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-4">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="numero-telefone">Telefone</Label>
            <Input
              id="numero-telefone"
              value={telefone}
              onChange={(e) => setTelefone(mascararTelefone(e.target.value))}
              placeholder="(38) 99999-0000"
              inputMode="numeric"
              className="font-dado"
            />
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="numero-rotulo">Rótulo (opcional)</Label>
            <Input
              id="numero-rotulo"
              value={rotulo}
              onChange={(e) => setRotulo(e.target.value)}
              placeholder="Ex.: cartório parceiro (dono)"
            />
          </div>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={aoFechar} disabled={salvando}>
            Cancelar
          </Button>
          <Button onClick={() => void salvar()} disabled={salvando || !telefone.trim()}>
            {salvando ? <Loader2 className="animate-spin" /> : null}
            Salvar
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
