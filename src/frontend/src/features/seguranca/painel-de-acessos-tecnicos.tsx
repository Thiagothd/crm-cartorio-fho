import { useState } from 'react'
import { toast } from 'sonner'
import { Painel } from '@/components/ui/painel'
import { Skeleton } from '@/components/ui/skeleton'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { EntradaDeCodigoOtp } from '@/components/ui/entrada-de-codigo-otp'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { formatarDataHora } from '@/lib/formatacao'
import { usarAutenticacao } from '@/lib/auth/usar-autenticacao'
import type { AutorizacaoDeAcessoTecnico } from '@/lib/api/tipos'
import {
  usarAprovarAcessoTecnico,
  usarNegarAcessoTecnico,
  usarRevogarAcessoTecnico,
  usarSolicitacoesDeAcessoTecnico,
} from './consultas-acesso-tecnico'

const ROTULO_DO_ESTADO: Record<string, string> = {
  Pendente: 'Aguardando aprovação',
  Ativa: 'Ativa',
  Expirada: 'Expirada',
  Negada: 'Negada',
  Revogada: 'Revogada',
}

function DialogoDeAprovacao({
  autorizacao,
  aberto,
  aoFechar,
}: {
  autorizacao: AutorizacaoDeAcessoTecnico | null
  aberto: boolean
  aoFechar: () => void
}) {
  const [codigo, setCodigo] = useState('')
  const aprovar = usarAprovarAcessoTecnico()

  async function confirmar() {
    if (!autorizacao) return
    try {
      await aprovar.mutateAsync({ id: autorizacao.id, codigoTotp: codigo })
      toast.success('Acesso técnico aprovado.')
      setCodigo('')
      aoFechar()
    } catch (falha) {
      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível aprovar.')
    }
  }

  return (
    <Dialog open={aberto} onOpenChange={(v) => !v && aoFechar()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Confirmar aprovação</DialogTitle>
          <DialogDescription>
            Digite o código da sua verificação em duas etapas para aprovar este acesso técnico.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-1.5">
          <Label htmlFor="codigo-aprovacao">Código</Label>
          <EntradaDeCodigoOtp
            id="codigo-aprovacao"
            value={codigo}
            onChange={setCodigo}
            className="rounded-md border border-linha"
          />
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={aoFechar}>
            Cancelar
          </Button>
          <Button onClick={confirmar} disabled={codigo.length !== 6 || aprovar.isPending}>
            {aprovar.isPending ? 'Aprovando…' : 'Aprovar'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

/**
 * Seção da tela "Acesso aos dados" — só para quem tem
 * `seguranca.acesso_tecnico.aprovar` (preset Administração). Aprovar exige
 * segundo fator do PRÓPRIO aprovador; negar e revogar não (reduzir/recusar
 * acesso nunca é ação sensível).
 */
export function PainelDeAcessosTecnicos() {
  const { pode } = usarAutenticacao()
  const { data, isLoading, isError } = usarSolicitacoesDeAcessoTecnico({})
  const negar = usarNegarAcessoTecnico()
  const revogar = usarRevogarAcessoTecnico()
  const [emAprovacao, setEmAprovacao] = useState<AutorizacaoDeAcessoTecnico | null>(null)

  if (!pode('seguranca.acesso_tecnico.aprovar')) {
    return null
  }

  async function negarPedido(id: string) {
    try {
      await negar.mutateAsync(id)
      toast.success('Pedido negado.')
    } catch (falha) {
      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível negar.')
    }
  }

  async function revogarAcesso(id: string) {
    try {
      await revogar.mutateAsync(id)
      toast.success('Acesso revogado.')
    } catch (falha) {
      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível revogar.')
    }
  }

  return (
    <Painel
      titulo="Acessos técnicos"
      descricao="O suporte técnico pede, o cartório aprova com segundo fator, a trilha registra."
    >
      {isLoading ? (
        <Skeleton className="h-32 w-full" />
      ) : isError || !data ? (
        <EstadoDaInterface tipo="erro" titulo="Não foi possível carregar" descricao="Tente novamente em instantes." />
      ) : data.itens.length === 0 ? (
        <p className="rounded-md border border-linha bg-superficie px-3 py-3 text-xs text-tinta-media">
          Nenhum pedido de acesso técnico ainda.
        </p>
      ) : (
        <div className="flex flex-col gap-2">
          {data.itens.map((item) => (
            <div key={item.id} className="flex flex-col gap-2 rounded-md border border-linha px-3 py-2 text-sm sm:flex-row sm:items-center sm:justify-between">
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-2">
                  <span className="font-medium text-tinta">{ROTULO_DO_ESTADO[item.estado] ?? item.estado}</span>
                  <span className="font-dado text-xs text-tinta-media">{formatarDataHora(item.solicitadaEm)}</span>
                </div>
                <p className="mt-1 text-xs text-tinta-media">{item.motivo}</p>
                <p className="mt-1 text-xs text-tinta-media">
                  {item.escopo === 'Conversa' ? `Conversa ${item.conversaProtocolo ?? '—'}` : 'Escopo geral'}
                  {item.estado === 'Ativa' && item.validaAte ? ` · até ${formatarDataHora(item.validaAte)}` : ''}
                </p>
              </div>

              {item.estado === 'Pendente' ? (
                <div className="flex shrink-0 gap-2">
                  <Button size="sm" variant="outline" onClick={() => negarPedido(item.id)} disabled={negar.isPending}>
                    Negar
                  </Button>
                  <Button size="sm" onClick={() => setEmAprovacao(item)}>
                    Aprovar
                  </Button>
                </div>
              ) : item.estado === 'Ativa' ? (
                <Button size="sm" variant="outline" onClick={() => revogarAcesso(item.id)} disabled={revogar.isPending}>
                  Revogar
                </Button>
              ) : null}
            </div>
          ))}
        </div>
      )}

      <DialogoDeAprovacao autorizacao={emAprovacao} aberto={emAprovacao !== null} aoFechar={() => setEmAprovacao(null)} />
    </Painel>
  )
}
