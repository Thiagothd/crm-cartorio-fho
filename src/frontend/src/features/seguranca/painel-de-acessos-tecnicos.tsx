import { toast } from 'sonner'
import { Painel } from '@/components/ui/painel'
import { Skeleton } from '@/components/ui/skeleton'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { Button } from '@/components/ui/button'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { formatarDataHora } from '@/lib/formatacao'
import { usarAutenticacao } from '@/lib/auth/usar-autenticacao'
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

/**
 * Seção da tela "Acesso aos dados" — só para quem tem
 * `seguranca.acesso_tecnico.aprovar` (preset Administração). Aprovar, negar
 * e revogar não pedem segundo fator adicional aqui — decisão do Thiago em
 * 2026-09-12: o ator já autenticou (senha + MFA, quando a política do
 * tenant exige) para abrir o painel; a trava real é a permissão
 * `seguranca.acesso_tecnico.aprovar` + a trilha registrada em "Acessos".
 */
export function PainelDeAcessosTecnicos() {
  const { pode } = usarAutenticacao()
  const { data, isLoading, isError } = usarSolicitacoesDeAcessoTecnico({})
  const aprovar = usarAprovarAcessoTecnico()
  const negar = usarNegarAcessoTecnico()
  const revogar = usarRevogarAcessoTecnico()

  if (!pode('seguranca.acesso_tecnico.aprovar')) {
    return null
  }

  async function aprovarPedido(id: string) {
    if (!window.confirm('Aprovar este acesso técnico? O suporte poderá ler os dados no escopo e prazo pedidos.')) {
      return
    }
    try {
      await aprovar.mutateAsync(id)
      toast.success('Acesso técnico aprovado.')
    } catch (falha) {
      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível aprovar.')
    }
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
      descricao="O suporte técnico pede, o cartório aprova, a trilha registra."
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
                  <Button size="sm" onClick={() => aprovarPedido(item.id)} disabled={aprovar.isPending}>
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
    </Painel>
  )
}
