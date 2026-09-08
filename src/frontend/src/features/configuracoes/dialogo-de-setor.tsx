import { useEffect, useState } from 'react'
import { Loader2 } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { setoresApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import type { Setor } from '@/lib/api/tipos'

export function DialogoDeSetor({
  setor,
  aberto,
  proximaOrdem,
  aoFechar,
  aoSalvar,
}: {
  setor: Setor | null
  aberto: boolean
  proximaOrdem: number
  aoFechar: () => void
  aoSalvar: () => void
}) {
  const [nome, setNome] = useState('')
  const [descricao, setDescricao] = useState('')
  const [ordem, setOrdem] = useState('1')
  const [ativo, setAtivo] = useState(true)
  const [salvando, setSalvando] = useState(false)
  const [erro, setErro] = useState<string | null>(null)

  useEffect(() => {
    if (!aberto) return

    setNome(setor?.nome ?? '')
    setDescricao(setor?.descricao ?? '')
    setOrdem(String(setor?.ordem ?? proximaOrdem))
    setAtivo(setor?.ativo ?? true)
    setErro(null)
  }, [aberto, setor, proximaOrdem])

  async function salvar() {
    // DESIGN.md 11.4/11.5 — a recusa cai aqui, não num toast que some.
    if (!nome.trim()) {
      setErro('Informe o nome do setor.')
      return
    }

    setSalvando(true)
    setErro(null)

    try {
      const dados = {
        nome: nome.trim(),
        descricao: descricao.trim() || null,
        ordem: Number(ordem),
        ativo,
      }

      if (setor) {
        await setoresApi.atualizar(setor.id, dados)
      } else {
        await setoresApi.criar(dados)
      }

      toast.success(setor ? 'Setor atualizado.' : 'Setor criado.')
      aoSalvar()
      aoFechar()
    } catch (falha) {
      setErro(falha instanceof ErroDaApi ? falha.message : 'Não foi possível salvar. Tente novamente.')
    } finally {
      setSalvando(false)
    }
  }

  return (
    <Dialog open={aberto} onOpenChange={(v) => !v && !salvando && aoFechar()}>
      <DialogContent
        mostrarFechar={!salvando}
        onEscapeKeyDown={(e) => { if (salvando) e.preventDefault() }}
        onInteractOutside={(e) => { if (salvando) e.preventDefault() }}
      >
        <DialogHeader>
          <DialogTitle>{setor ? 'Editar setor' : 'Novo setor'}</DialogTitle>
          <DialogDescription>
            Desativar um setor não afeta as conversas que já apontam para ele — o setor só sai da
            lista de opções.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-4">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="setor-nome">Nome</Label>
            <Input
              id="setor-nome"
              value={nome}
              onChange={(e) => {
                setNome(e.target.value)
                if (erro) setErro(null)
              }}
              placeholder="Ex.: Tabelionato de Notas"
              aria-invalid={erro !== null && !nome.trim()}
            />
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="setor-descricao">Descrição (opcional)</Label>
            <Input
              id="setor-descricao"
              value={descricao}
              onChange={(e) => setDescricao(e.target.value)}
            />
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="setor-ordem">Ordem na lista</Label>
            <Input
              id="setor-ordem"
              type="number"
              min={0}
              value={ordem}
              onChange={(e) => setOrdem(e.target.value)}
              className="w-28"
            />
            <p className="text-xs text-tinta-fraca">
              Define a posição no menu que o cliente recebe no primeiro contato.
            </p>
          </div>

          {setor ? (
            <div className="flex items-center justify-between rounded-md border border-linha px-3 py-2">
              <Label htmlFor="setor-ativo">Setor ativo</Label>
              <Switch id="setor-ativo" checked={ativo} onCheckedChange={setAtivo} />
            </div>
          ) : null}

          {erro ? (
            <p role="alert" className="rounded-lg border border-erro/20 bg-erro/5 p-3 text-sm text-erro">
              {erro}
            </p>
          ) : null}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={aoFechar} disabled={salvando}>
            Cancelar
          </Button>
          <Button onClick={() => void salvar()} disabled={salvando}>
            {salvando ? <Loader2 className="animate-spin" /> : null}
            {salvando ? 'Salvando…' : 'Salvar'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
