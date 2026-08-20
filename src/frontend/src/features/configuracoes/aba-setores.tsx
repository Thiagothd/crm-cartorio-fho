import { useEffect, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Pencil, Plus } from 'lucide-react'
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
import { setoresApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import type { Setor } from '@/lib/api/tipos'

export function AbaSetores() {
  const [emEdicao, setEmEdicao] = useState<Setor | null>(null)
  const [aberto, setAberto] = useState(false)

  const clienteDeConsultas = useQueryClient()

  const { data, isLoading } = useQuery({
    queryKey: ['setores-gestao'],
    queryFn: () => setoresApi.listar(true),
  })

  function recarregar() {
    void clienteDeConsultas.invalidateQueries({ queryKey: ['setores-gestao'] })
    void clienteDeConsultas.invalidateQueries({ queryKey: ['setores'] })
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col items-stretch gap-3 sm:flex-row sm:items-start sm:justify-between sm:gap-4">
        <p className="max-w-2xl text-sm text-tinta-media">
          Setor é <strong className="text-tinta">quem atende</strong> — o eixo de roteamento da
          conversa. Não confundir com etapa, que é o ponto do atendimento no funil. Os nomes abaixo
          são sugestão de mercado e aguardam validação do cartório parceiro.
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
          Novo setor
        </Button>
      </div>

      {isLoading ? (
        <div className="flex flex-col gap-2">
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} className="h-12 w-full" />
          ))}
        </div>
      ) : (
        <div className="overflow-hidden rounded-md border border-linha">
          <table className="w-full table-fixed text-sm md:table-auto">
            <thead className="bg-superficie text-xs text-tinta-media">
              <tr>
                <th className="w-16 px-3 py-2.5 text-left font-medium md:px-4">Ordem</th>
                <th className="px-3 py-2.5 text-left font-medium md:px-4">Setor</th>
                <th className="hidden px-4 py-2.5 text-left font-medium md:table-cell">Conversas ativas</th>
                <th className="w-14 md:w-12" />
              </tr>
            </thead>

            <tbody>
              {data?.map((setor) => (
                <tr key={setor.id} className="border-t border-linha hover:bg-superficie">
                  <td className="px-3 py-3 tabular-nums text-tinta-media md:px-4 md:py-2.5">{setor.ordem}</td>

                  <td className="px-3 py-3 md:px-4 md:py-2.5">
                    <div className="flex items-center gap-2">
                      <span className="font-medium">{setor.nome}</span>
                      {!setor.ativo ? <Badge variant="secondary">Inativo</Badge> : null}
                    </div>
                    {setor.descricao ? (
                      <p className="text-xs text-tinta-media">{setor.descricao}</p>
                    ) : null}
                    <p className="mt-1 text-[11px] text-tinta-fraca md:hidden">
                      {setor.conversasAtivas}{' '}
                      {setor.conversasAtivas === 1 ? 'conversa ativa' : 'conversas ativas'}
                    </p>
                  </td>

                  <td className="hidden px-4 py-2.5 tabular-nums text-tinta-media md:table-cell">
                    {setor.conversasAtivas}
                  </td>

                  <td className="px-1 py-2.5 md:px-2">
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      onClick={() => {
                        setEmEdicao(setor)
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

      <DialogoDeSetor
        setor={emEdicao}
        aberto={aberto}
        proximaOrdem={(data?.length ?? 0) + 1}
        aoFechar={() => setAberto(false)}
        aoSalvar={recarregar}
      />
    </div>
  )
}

function DialogoDeSetor({
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

  useEffect(() => {
    if (!aberto) return

    setNome(setor?.nome ?? '')
    setDescricao(setor?.descricao ?? '')
    setOrdem(String(setor?.ordem ?? proximaOrdem))
    setAtivo(setor?.ativo ?? true)
  }, [aberto, setor, proximaOrdem])

  async function salvar() {
    setSalvando(true)

    try {
      const dados = {
        nome,
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
      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível salvar.')
    } finally {
      setSalvando(false)
    }
  }

  return (
    <Dialog open={aberto} onOpenChange={(v) => !v && aoFechar()}>
      <DialogContent>
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
              onChange={(e) => setNome(e.target.value)}
              placeholder="Ex.: Tabelionato de Notas"
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
          </div>

          {setor ? (
            <div className="flex items-center justify-between rounded-md border border-linha px-3 py-2">
              <Label htmlFor="setor-ativo">Setor ativo</Label>
              <Switch id="setor-ativo" checked={ativo} onCheckedChange={setAtivo} />
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
