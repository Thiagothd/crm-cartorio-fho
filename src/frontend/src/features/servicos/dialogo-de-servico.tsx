import { useEffect, useState } from 'react'
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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { servicosApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import type { CategoriaServico, TipoServico } from '@/lib/api/tipos'

const categorias: { valor: CategoriaServico; rotulo: string }[] = [
  { valor: 'Notas', rotulo: 'Tabelionato de Notas' },
  { valor: 'RegistroCivil', rotulo: 'Registro Civil' },
  { valor: 'RegistroImoveis', rotulo: 'Registro de Imóveis' },
  { valor: 'TitulosDocumentos', rotulo: 'Títulos e Documentos' },
  { valor: 'Protesto', rotulo: 'Protesto' },
  { valor: 'Outros', rotulo: 'Outros' },
]

export function DialogoDeServico({
  servico,
  aberto,
  aoFechar,
  aoSalvar,
}: {
  servico: TipoServico | null
  aberto: boolean
  aoFechar: () => void
  aoSalvar: () => void
}) {
  const [nome, setNome] = useState('')
  const [codigo, setCodigo] = useState('')
  const [categoria, setCategoria] = useState<CategoriaServico>('Outros')
  const [ativo, setAtivo] = useState(true)
  const [temPrazo, setTemPrazo] = useState(true)
  const [prazoHoras, setPrazoHoras] = useState('24')
  const [percentualAlerta, setPercentualAlerta] = useState('80')
  const [apenasDiasUteis, setApenasDiasUteis] = useState(true)
  const [salvando, setSalvando] = useState(false)

  useEffect(() => {
    if (!aberto) return

    setNome(servico?.nome ?? '')
    setCodigo(servico?.codigo ?? '')
    setCategoria(servico?.categoria ?? 'Outros')
    setAtivo(servico?.ativo ?? true)
    setTemPrazo(Boolean(servico?.prazo) || !servico)
    setPrazoHoras(String(servico?.prazo?.prazoHoras ?? 24))
    setPercentualAlerta(String(servico?.prazo?.percentualAlerta ?? 80))
    setApenasDiasUteis(servico?.prazo?.apenasDiasUteis ?? true)
  }, [aberto, servico])

  async function salvar() {
    setSalvando(true)

    try {
      const dados = {
        nome,
        codigo: codigo.trim() || null,
        categoria,
        descricao: null,
        ativo,
        prazo: temPrazo
          ? {
              prazoHoras: Number(prazoHoras),
              percentualAlerta: Number(percentualAlerta),
              apenasDiasUteis,
            }
          : null,
      }

      if (servico) {
        await servicosApi.atualizar(servico.id, dados)
      } else {
        await servicosApi.criar(dados)
      }

      toast.success(servico ? 'Serviço atualizado.' : 'Serviço cadastrado.')
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
          <DialogTitle>{servico ? 'Editar serviço' : 'Novo serviço'}</DialogTitle>
          <DialogDescription>
            O prazo começa a contar quando o serviço é vinculado à conversa, não na abertura do
            atendimento — a triagem não consome o SLA do ato.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-4">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="servico-nome">Nome</Label>
            <Input
              id="servico-nome"
              value={nome}
              onChange={(e) => setNome(e.target.value)}
              placeholder="Ex.: Certidão de nascimento"
            />
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="flex flex-col gap-1.5">
              <Label>Categoria</Label>
              <Select value={categoria} onValueChange={(v) => setCategoria(v as CategoriaServico)}>
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {categorias.map((c) => (
                    <SelectItem key={c.valor} value={c.valor}>
                      {c.rotulo}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className="flex flex-col gap-1.5">
              <Label htmlFor="servico-codigo">Código interno (opcional)</Label>
              <Input
                id="servico-codigo"
                value={codigo}
                onChange={(e) => setCodigo(e.target.value)}
                placeholder="RC-NASC"
              />
            </div>
          </div>

          <div className="flex items-center justify-between rounded-md border border-linha px-3 py-2">
            <div>
              <Label htmlFor="tem-prazo">Controlar prazo (SLA)</Label>
              <p className="text-xs text-tinta-media">
                Sem prazo, a conversa não entra nos contadores de alerta e atraso.
              </p>
            </div>
            <Switch id="tem-prazo" checked={temPrazo} onCheckedChange={setTemPrazo} />
          </div>

          {temPrazo ? (
            <div className="flex flex-col gap-3 rounded-md border border-linha p-3">
              <div className="grid grid-cols-2 gap-3">
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor="prazo-horas">Prazo em horas</Label>
                  <Input
                    id="prazo-horas"
                    type="number"
                    min={1}
                    value={prazoHoras}
                    onChange={(e) => setPrazoHoras(e.target.value)}
                  />
                  <p className="text-xs text-tinta-media">24h = 1 dia, 240h = 10 dias.</p>
                </div>

                <div className="flex flex-col gap-1.5">
                  <Label htmlFor="alerta">Alerta em (%)</Label>
                  <Input
                    id="alerta"
                    type="number"
                    min={1}
                    max={99}
                    value={percentualAlerta}
                    onChange={(e) => setPercentualAlerta(e.target.value)}
                  />
                  <p className="text-xs text-tinta-media">
                    Percentual do prazo consumido que acende o alerta.
                  </p>
                </div>
              </div>

              <div className="flex items-center justify-between">
                <Label htmlFor="dias-uteis">Contar só dias úteis</Label>
                <Switch
                  id="dias-uteis"
                  checked={apenasDiasUteis}
                  onCheckedChange={setApenasDiasUteis}
                />
              </div>
            </div>
          ) : null}

          {servico ? (
            <div className="flex items-center justify-between rounded-md border border-linha px-3 py-2">
              <Label htmlFor="servico-ativo">Serviço ativo</Label>
              <Switch id="servico-ativo" checked={ativo} onCheckedChange={setAtivo} />
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
