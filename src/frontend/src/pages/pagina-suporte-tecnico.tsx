import { useState } from 'react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import { Painel } from '@/components/ui/painel'
import { Skeleton } from '@/components/ui/skeleton'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { formatarDataHora } from '@/lib/formatacao'
import type { EscopoDeAcesso } from '@/lib/api/tipos'
import {
  usarSolicitacoesDeAcessoTecnico,
  usarSolicitarAcessoTecnico,
} from '@/features/seguranca/consultas-acesso-tecnico'

const ROTULO_DO_ESTADO: Record<string, string> = {
  Pendente: 'Aguardando aprovação',
  Ativa: 'Ativa',
  Expirada: 'Expirada',
  Negada: 'Negada',
  Revogada: 'Revogada',
}

/**
 * Tela exclusiva da conta técnica da equipe do projeto (`suporte.solicitar_acesso`,
 * excluída de todo preset do cartório). Pedir aqui NUNCA lê dado nenhum —
 * só cria o pedido; a leitura só começa depois de um administrador do
 * cartório aprovar com segundo fator, pela tela "Acesso aos dados".
 */
export function PaginaSuporteTecnico() {
  const [motivo, setMotivo] = useState('')
  const [escopo, setEscopo] = useState<EscopoDeAcesso>('Conversa')
  const [protocolo, setProtocolo] = useState('')
  const [duracaoMinutos, setDuracaoMinutos] = useState(60)

  const solicitar = usarSolicitarAcessoTecnico()
  const { data, isLoading, isError } = usarSolicitacoesDeAcessoTecnico({})

  function limpar() {
    setMotivo('')
    setProtocolo('')
    setDuracaoMinutos(60)
  }

  async function enviar() {
    try {
      await solicitar.mutateAsync({
        motivo,
        escopo,
        conversaProtocolo: escopo === 'Conversa' ? protocolo : null,
        duracaoMinutos,
      })
      toast.success('Pedido enviado. Aguarde a aprovação de um administrador do cartório.')
      limpar()
    } catch (falha) {
      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível enviar o pedido.')
    }
  }

  const podeEnviar = motivo.trim().length > 0 && (escopo === 'Geral' || protocolo.trim().length > 0)

  return (
    <div className="min-h-0 flex-1 overflow-y-auto p-3 md:p-6">
      <div className="mx-auto flex max-w-2xl flex-col gap-4">
        <div>
          <h1 className="text-lg font-bold tracking-tight text-tinta">Suporte técnico</h1>
          <p className="text-xs text-tinta-media">
            Pedir acesso técnico temporário, com motivo. Um administrador do cartório precisa aprovar.
          </p>
        </div>

        <Painel titulo="Novo pedido">
          <div className="flex flex-col gap-4">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="motivo">Motivo</Label>
              <Textarea
                id="motivo"
                value={motivo}
                onChange={(e) => setMotivo(e.target.value)}
                placeholder="Descreva por que precisa do acesso"
                rows={3}
              />
            </div>

            <div className="grid grid-cols-2 gap-4">
              <div className="flex flex-col gap-1.5">
                <Label>Escopo</Label>
                <Select value={escopo} onValueChange={(v) => setEscopo(v as EscopoDeAcesso)}>
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="Conversa">Uma conversa</SelectItem>
                    <SelectItem value="Geral">Geral (qualquer conversa/ficha)</SelectItem>
                  </SelectContent>
                </Select>
              </div>

              <div className="flex flex-col gap-1.5">
                <Label htmlFor="duracao">Duração (minutos)</Label>
                <Input
                  id="duracao"
                  type="number"
                  min={1}
                  max={480}
                  value={duracaoMinutos}
                  onChange={(e) => setDuracaoMinutos(Number(e.target.value))}
                />
              </div>
            </div>

            {escopo === 'Conversa' ? (
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="protocolo">Protocolo da conversa</Label>
                <Input
                  id="protocolo"
                  value={protocolo}
                  onChange={(e) => setProtocolo(e.target.value)}
                  placeholder="Ex.: 202609-0042"
                />
              </div>
            ) : null}

            <Button onClick={enviar} disabled={!podeEnviar || solicitar.isPending}>
              {solicitar.isPending ? 'Enviando…' : 'Enviar pedido'}
            </Button>
          </div>
        </Painel>

        <Painel titulo="Meus pedidos">
          {isLoading ? (
            <Skeleton className="h-32 w-full" />
          ) : isError || !data ? (
            <EstadoDaInterface tipo="erro" titulo="Não foi possível carregar" descricao="Tente novamente em instantes." />
          ) : data.itens.length === 0 ? (
            <p className="rounded-md border border-linha bg-superficie px-3 py-3 text-xs text-tinta-media">
              Nenhum pedido ainda.
            </p>
          ) : (
            <div className="flex flex-col gap-2">
              {data.itens.map((item) => (
                <div key={item.id} className="rounded-md border border-linha px-3 py-2 text-sm">
                  <div className="flex items-center justify-between gap-2">
                    <span className="font-medium text-tinta">{ROTULO_DO_ESTADO[item.estado] ?? item.estado}</span>
                    <span className="font-dado text-xs text-tinta-media">{formatarDataHora(item.solicitadaEm)}</span>
                  </div>
                  <p className="mt-1 text-xs text-tinta-media">{item.motivo}</p>
                  {item.escopo === 'Conversa' && item.conversaProtocolo ? (
                    <p className="mt-1 text-xs text-tinta-media">Conversa: {item.conversaProtocolo}</p>
                  ) : null}
                  {item.estado === 'Ativa' && item.validaAte ? (
                    <p className="mt-1 text-xs text-tinta-media">Válido até {formatarDataHora(item.validaAte)}</p>
                  ) : null}
                </div>
              ))}
            </div>
          )}
        </Painel>
      </div>
    </div>
  )
}
