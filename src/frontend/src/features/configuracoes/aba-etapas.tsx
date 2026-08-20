import { useEffect, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowDown, ArrowUp, Flag, Pencil, Plus } from 'lucide-react'
import { toast } from 'sonner'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Skeleton } from '@/components/ui/skeleton'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { etapasApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import type { Etapa } from '@/lib/api/tipos'

/**
 * Paleta FECHADA, vinda de docs/DESIGN.md, secao 5.
 * Nao e seletor de cor livre de proposito: com um seletor livre o cliente coloca
 * azul-piscina no funil e derruba a identidade inteira.
 */
const CORES: { valor: string; rotulo: string }[] = [
  { valor: '#2E5170', rotulo: 'Azul acinzentado — início do atendimento' },
  { valor: '#6B3F73', rotulo: 'Roxo suave — captação' },
  { valor: '#8A6516', rotulo: 'Âmbar — processamento e pendência' },
  { valor: '#3D5C3A', rotulo: 'Verde — concluído' },
]

export function AbaEtapas() {
  const [emEdicao, setEmEdicao] = useState<Etapa | null>(null)
  const [aberto, setAberto] = useState(false)

  const clienteDeConsultas = useQueryClient()

  const { data, isLoading } = useQuery({
    queryKey: ['etapas-gestao'],
    queryFn: () => etapasApi.listar(true),
  })

  function recarregar() {
    void clienteDeConsultas.invalidateQueries({ queryKey: ['etapas-gestao'] })
    void clienteDeConsultas.invalidateQueries({ queryKey: ['etapas'] })
  }

  async function mover(indice: number, direcao: -1 | 1) {
    if (!data) return

    const destino = indice + direcao
    if (destino < 0 || destino >= data.length) return

    const ordenadas = [...data]
    const [movida] = ordenadas.splice(indice, 1)
    ordenadas.splice(destino, 0, movida)

    try {
      await etapasApi.reordenar(ordenadas.map((e) => e.id))
      recarregar()
    } catch (falha) {
      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível reordenar.')
    }
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col items-stretch gap-3 sm:flex-row sm:items-start sm:justify-between sm:gap-4">
        <p className="max-w-2xl text-sm text-tinta-media">
          Etapa é <strong className="text-tinta">em que ponto está</strong> o atendimento — o
          eixo de progressão. A ordem abaixo define a sequência do funil no painel. Os nomes são
          sugestão de mercado e aguardam validação do cartório parceiro.
        </p>

        <Button
          size="sm"
          className="sm:shrink-0"
          onClick={() => {
            setEmEdicao(null)
            setAberto(true)
          }}
        >
          <Plus />
          Nova etapa
        </Button>
      </div>

      {isLoading ? (
        <div className="flex flex-col gap-2">
          {[0, 1, 2, 3].map((i) => (
            <Skeleton key={i} className="h-12 w-full" />
          ))}
        </div>
      ) : (
        <div className="overflow-hidden rounded-md border border-linha">
          <table className="w-full table-fixed text-sm md:table-auto">
            <thead className="bg-superficie text-xs text-tinta-media">
              <tr>
                <th className="w-28 px-2 py-2.5 text-left font-medium md:w-24 md:px-4">Ordem</th>
                <th className="px-3 py-2.5 text-left font-medium md:px-4">Etapa</th>
                <th className="hidden px-4 py-2.5 text-left font-medium md:table-cell">Conversas ativas</th>
                <th className="w-14 md:w-12" />
              </tr>
            </thead>

            <tbody>
              {data?.map((etapa, indice) => (
                <tr key={etapa.id} className="border-t border-linha hover:bg-superficie">
                  <td className="px-1 py-2.5 md:px-4">
                    <div className="flex items-center gap-1">
                      <Button
                        variant="ghost"
                        size="icon-sm"
                        disabled={indice === 0}
                        onClick={() => void mover(indice, -1)}
                      >
                        <ArrowUp />
                      </Button>
                      <Button
                        variant="ghost"
                        size="icon-sm"
                        disabled={indice === (data?.length ?? 0) - 1}
                        onClick={() => void mover(indice, 1)}
                      >
                        <ArrowDown />
                      </Button>
                    </div>
                  </td>

                  <td className="px-3 py-3 md:px-4 md:py-2.5">
                    <div className="flex items-center gap-2">
                      <span
                        className="size-3 shrink-0 rounded-full"
                        style={{ backgroundColor: etapa.cor }}
                      />
                      <span className="font-medium">{etapa.nome}</span>
                      {etapa.ehFinal ? (
                        <Badge variant="success">
                          <Flag className="size-3" />
                          Final
                        </Badge>
                      ) : null}
                      {!etapa.ativo ? <Badge variant="secondary">Inativa</Badge> : null}
                    </div>
                    {etapa.descricao ? (
                      <p className="text-xs text-tinta-media">{etapa.descricao}</p>
                    ) : null}
                    <p className="mt-1 text-[11px] text-tinta-fraca md:hidden">
                      {etapa.conversasAtivas}{' '}
                      {etapa.conversasAtivas === 1 ? 'conversa ativa' : 'conversas ativas'}
                    </p>
                  </td>

                  <td className="hidden px-4 py-2.5 tabular-nums text-tinta-media md:table-cell">
                    {etapa.conversasAtivas}
                  </td>

                  <td className="px-1 py-2.5 md:px-2">
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      onClick={() => {
                        setEmEdicao(etapa)
                        setAberto(true)
                      }}
                    >
                      <Pencil />
                      <span className="sr-only">Editar</span>
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <DialogoDeEtapa
        etapa={emEdicao}
        aberto={aberto}
        proximaOrdem={(data?.length ?? 0) + 1}
        aoFechar={() => setAberto(false)}
        aoSalvar={recarregar}
      />
    </div>
  )
}

function DialogoDeEtapa({
  etapa,
  aberto,
  proximaOrdem,
  aoFechar,
  aoSalvar,
}: {
  etapa: Etapa | null
  aberto: boolean
  proximaOrdem: number
  aoFechar: () => void
  aoSalvar: () => void
}) {
  const [nome, setNome] = useState('')
  const [descricao, setDescricao] = useState('')
  const [ordem, setOrdem] = useState('1')
  const [cor, setCor] = useState(CORES[0].valor)
  const [ehFinal, setEhFinal] = useState(false)
  const [ativo, setAtivo] = useState(true)
  const [salvando, setSalvando] = useState(false)

  useEffect(() => {
    if (!aberto) return

    setNome(etapa?.nome ?? '')
    setDescricao(etapa?.descricao ?? '')
    setOrdem(String(etapa?.ordem ?? proximaOrdem))
    setCor(etapa?.cor ?? CORES[0].valor)
    setEhFinal(etapa?.ehFinal ?? false)
    setAtivo(etapa?.ativo ?? true)
  }, [aberto, etapa, proximaOrdem])

  async function salvar() {
    setSalvando(true)

    try {
      const dados = {
        nome,
        descricao: descricao.trim() || null,
        ordem: Number(ordem),
        cor,
        ehFinal,
        ativo,
      }

      if (etapa) {
        await etapasApi.atualizar(etapa.id, dados)
      } else {
        await etapasApi.criar(dados)
      }

      toast.success(etapa ? 'Etapa atualizada.' : 'Etapa criada.')
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
          <DialogTitle>{etapa ? 'Editar etapa' : 'Nova etapa'}</DialogTitle>
          <DialogDescription>
            O funil precisa de pelo menos uma etapa ativa — sem isso, conversas novas nasceriam sem
            etapa nenhuma.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-4">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="etapa-nome">Nome</Label>
            <Input
              id="etapa-nome"
              value={nome}
              onChange={(e) => setNome(e.target.value)}
              placeholder="Ex.: Captação do serviço"
            />
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="etapa-descricao">Descrição (opcional)</Label>
            <Input
              id="etapa-descricao"
              value={descricao}
              onChange={(e) => setDescricao(e.target.value)}
            />
          </div>

          <div className="flex flex-col gap-1.5">
            <Label>Cor</Label>
            <div className="flex flex-wrap items-center gap-2">
              {CORES.map((opcao) => (
                <button
                  key={opcao.valor}
                  type="button"
                  onClick={() => setCor(opcao.valor)}
                  aria-label={opcao.rotulo}
                  title={opcao.rotulo}
                  className={`size-7 rounded-md border transition-colors ${
                    cor.toUpperCase() === opcao.valor
                      ? 'border-tinta'
                      : 'border-linha hover:border-linha-forte'
                  }`}
                  style={{ backgroundColor: opcao.valor }}
                />
              ))}
            </div>
            <p className="text-xs text-tinta-fraca">
              Paleta fechada, definida em docs/DESIGN.md.
            </p>
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="etapa-ordem">Ordem no funil</Label>
            <Input
              id="etapa-ordem"
              type="number"
              min={0}
              value={ordem}
              onChange={(e) => setOrdem(e.target.value)}
              className="w-28"
            />
          </div>

          <div className="flex items-center justify-between rounded-md border border-linha px-3 py-2">
            <div>
              <Label htmlFor="etapa-final">Etapa de conclusão</Label>
              <p className="text-xs text-tinta-media">
                Marca a etapa que representa atendimento concluído.
              </p>
            </div>
            <Switch id="etapa-final" checked={ehFinal} onCheckedChange={setEhFinal} />
          </div>

          {etapa ? (
            <div className="flex items-center justify-between rounded-md border border-linha px-3 py-2">
              <Label htmlFor="etapa-ativa">Etapa ativa</Label>
              <Switch id="etapa-ativa" checked={ativo} onCheckedChange={setAtivo} />
            </div>
          ) : null}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={aoFechar}>
            Cancelar
          </Button>
          <Button onClick={() => void salvar()} disabled={salvando}>
            Salvar
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
