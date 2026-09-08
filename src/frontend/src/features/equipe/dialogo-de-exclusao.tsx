import { useRef, useState } from 'react'
import { Loader2, Trash2, UserX } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { usarExcluirAtendente } from '@/features/equipe/consultas'
import type { Atendente } from '@/lib/api/tipos'

/**
 * Diferente de Clientes, não há como saber de antemão se o atendente tem
 * histórico — não existe um `totalConversas` na resposta. A tentativa é que
 * revela isso, pelo código `nao_pode_excluir_com_historico` do backend.
 */
export function DialogoDeExclusaoDeAtendente({ atendente, aoFechar, aoAbrirEdicao }: {
  atendente: Atendente
  aoFechar: () => void
  aoAbrirEdicao: (atendente: Atendente) => void
}) {
  const excluir = usarExcluirAtendente()
  const emAndamento = useRef(false)
  const [erro, setErro] = useState<ErroDaApi | Error | null>(null)
  const temHistorico = erro instanceof ErroDaApi && erro.codigo === 'nao_pode_excluir_com_historico'
  const semPermissao = erro instanceof ErroDaApi && erro.status === 403
  const naoEncontrado = erro instanceof ErroDaApi && erro.status === 404
  const bloqueado = temHistorico || semPermissao || naoEncontrado

  async function confirmar() {
    if (emAndamento.current || bloqueado) return
    emAndamento.current = true
    setErro(null)
    try {
      await excluir.mutateAsync(atendente.id)
      toast.success(`${atendente.nome} excluído.`)
      aoFechar()
    } catch (falha) {
      setErro(falha instanceof ErroDaApi ? falha : new Error('Não foi possível excluir o atendente. Tente novamente.'))
    } finally {
      emAndamento.current = false
    }
  }

  return (
    <Dialog open onOpenChange={(aberto) => { if (!aberto && !emAndamento.current) aoFechar() }}>
      <DialogContent
        mostrarFechar={!excluir.isPending}
        onEscapeKeyDown={(e) => { if (emAndamento.current) e.preventDefault() }}
        onInteractOutside={(e) => { if (emAndamento.current) e.preventDefault() }}
      >
        <DialogHeader>
          <DialogTitle>{temHistorico ? 'Este cadastro tem histórico' : `Excluir ${atendente.nome}?`}</DialogTitle>
          <DialogDescription>
            {temHistorico
              ? `${atendente.nome} já tem atendimento ou ação registrada no sistema e não pode ser excluído. A desativação preserva o cadastro e todo o histórico.`
              : 'O cadastro será apagado permanentemente. Esta ação não pode ser desfeita e só é permitida para atendentes sem histórico registrado.'}
          </DialogDescription>
        </DialogHeader>
        {temHistorico ? (
          <div className="flex items-start gap-3 rounded-lg bg-superficie p-3 text-sm text-tinta-media">
            <UserX aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
            <p>
              {atendente.ativo
                ? 'Para retirar o acesso desta pessoa, abra o cadastro, desligue o acesso e salve.'
                : 'Este atendente já está sem acesso. O histórico permanece disponível.'}
            </p>
          </div>
        ) : null}
        {erro ? (
          <p role="alert" className="rounded-lg border border-erro/20 bg-erro/5 p-3 text-sm text-erro">
            {erro.message}
          </p>
        ) : null}
        <DialogFooter>
          <Button variant="outline" onClick={aoFechar} disabled={excluir.isPending}>
            {bloqueado ? 'Fechar' : 'Cancelar'}
          </Button>
          {temHistorico && atendente.ativo && !semPermissao ? (
            <Button variant="secondary" onClick={() => aoAbrirEdicao(atendente)}>
              Abrir cadastro para desativar
            </Button>
          ) : !bloqueado ? (
            <Button variant="destructive" onClick={() => void confirmar()} disabled={excluir.isPending}>
              {excluir.isPending ? <Loader2 className="animate-spin" /> : <Trash2 />}
              {excluir.isPending ? 'Excluindo…' : 'Excluir'}
            </Button>
          ) : null}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
