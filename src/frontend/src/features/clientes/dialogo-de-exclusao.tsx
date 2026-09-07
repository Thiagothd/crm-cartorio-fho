import { useRef, useState } from 'react'
import { Archive, Loader2, Trash2 } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { clientesApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { nomeDeExibicao } from '@/lib/formatacao'
import type { Cliente } from '@/lib/api/tipos'

/** Montado apenas enquanto aberto: erros de um cliente nunca passam ao próximo. */
export function DialogoDeExclusao({ cliente, aoFechar, aoExcluir, aoAbrirDesativacao }: {
  cliente: Cliente
  aoFechar: () => void
  aoExcluir: () => void
  aoAbrirDesativacao: (cliente: Cliente) => void
}) {
  const [excluindo, setExcluindo] = useState(false)
  const emAndamento = useRef(false)
  const [erro, setErro] = useState<ErroDaApi | Error | null>(null)
  const temHistorico = cliente.totalConversas > 0 || cliente.temConversaAtiva || erro instanceof ErroDaApi && erro.codigo === 'cliente_tem_historico'
  const semPermissao = erro instanceof ErroDaApi && erro.status === 403
  const naoEncontrado = erro instanceof ErroDaApi && erro.status === 404
  const nome = nomeDeExibicao(cliente.nome, cliente.telefoneWhatsapp, cliente.nomePendente)

  async function excluir() {
    if (emAndamento.current || temHistorico || semPermissao || naoEncontrado) return
    emAndamento.current = true
    setExcluindo(true)
    setErro(null)
    try {
      await clientesApi.excluir(cliente.id)
      toast.success(`${nome} excluído.`)
      aoExcluir()
      aoFechar()
    } catch (falha) {
      setErro(falha instanceof ErroDaApi ? falha : new Error('Não foi possível excluir o cliente. Tente novamente.'))
      // A lista pode ter ficado antiga enquanto o diálogo estava aberto.
      aoExcluir()
    } finally {
      emAndamento.current = false
      setExcluindo(false)
    }
  }

  return (
    <Dialog open onOpenChange={(aberto) => { if (!aberto && !emAndamento.current) aoFechar() }}>
      <DialogContent mostrarFechar={!excluindo} onEscapeKeyDown={(e) => { if (emAndamento.current) e.preventDefault() }} onInteractOutside={(e) => { if (emAndamento.current) e.preventDefault() }}>
        <DialogHeader>
          <DialogTitle>{temHistorico ? 'Este cadastro tem histórico' : `Excluir ${nome}?`}</DialogTitle>
          <DialogDescription>
            {temHistorico
              ? `${nome} tem atendimento registrado e não pode ser excluído, mesmo que o atendimento esteja encerrado. A desativação preserva o cadastro e todo o histórico.`
              : 'O cadastro será apagado permanentemente. Esta ação não pode ser desfeita e só é permitida para clientes sem atendimento registrado.'}
          </DialogDescription>
        </DialogHeader>
        {temHistorico ? (
          <div className="flex items-start gap-3 rounded-lg bg-superficie p-3 text-sm text-tinta-media">
            <Archive aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
            <p>{cliente.ativo ? 'Para retirar este cliente da lista de ativos, abra o cadastro, desligue “Cliente ativo” e salve.' : 'Este cliente já está inativo. O histórico permanece disponível.'}</p>
          </div>
        ) : null}
        {erro ? <p role="alert" className="rounded-lg border border-erro/20 bg-erro/5 p-3 text-sm text-erro">{erro.message}</p> : null}
        <DialogFooter>
          <Button variant="outline" onClick={aoFechar} disabled={excluindo}>{temHistorico || semPermissao || naoEncontrado ? 'Fechar' : 'Cancelar'}</Button>
          {temHistorico && cliente.ativo && !semPermissao ? (
            <Button variant="secondary" onClick={() => aoAbrirDesativacao(cliente)}>Abrir cadastro para desativar</Button>
          ) : !temHistorico && !semPermissao && !naoEncontrado ? (
            <Button variant="destructive" onClick={() => void excluir()} disabled={excluindo}>
              {excluindo ? <Loader2 className="animate-spin" /> : <Trash2 />}{excluindo ? 'Excluindo…' : 'Excluir'}
            </Button>
          ) : null}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
