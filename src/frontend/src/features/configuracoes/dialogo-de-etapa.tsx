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
import { etapasApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { cn } from '@/lib/utils'
import type { Etapa } from '@/lib/api/tipos'

/**
 * Paleta FECHADA, vinda de docs/DESIGN.md, seção 2.2 — e são as TINTAS, que é
 * o que a coluna `EtapaFunil.Cor` guarda (fundo e borda saem do mapa do
 * frontend). Não é seletor livre de propósito: com um seletor livre o cliente
 * coloca azul-piscina no funil e derruba a identidade inteira.
 */
const CORES: { valor: string; rotulo: string }[] = [
  { valor: '#2E5170', rotulo: 'Azul acinzentado — início do atendimento' },
  { valor: '#6B3F73', rotulo: 'Roxo suave — captação' },
  { valor: '#8A6516', rotulo: 'Âmbar — processamento e pendência' },
  { valor: '#3D5C3A', rotulo: 'Verde — concluído' },
]

export function DialogoDeEtapa({
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
  const [erro, setErro] = useState<string | null>(null)

  useEffect(() => {
    if (!aberto) return

    setNome(etapa?.nome ?? '')
    setDescricao(etapa?.descricao ?? '')
    setOrdem(String(etapa?.ordem ?? proximaOrdem))
    setCor(etapa?.cor ?? CORES[0].valor)
    setEhFinal(etapa?.ehFinal ?? false)
    setAtivo(etapa?.ativo ?? true)
    setErro(null)
  }, [aberto, etapa, proximaOrdem])

  async function salvar() {
    // DESIGN.md 11.4/11.5: valida no campo, e a recusa do servidor também cai
    // aqui — não num toast que some antes de a pessoa ler.
    if (!nome.trim()) {
      setErro('Informe o nome da etapa.')
      return
    }

    setSalvando(true)
    setErro(null)

    try {
      const dados = {
        nome: nome.trim(),
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
              onChange={(e) => {
                setNome(e.target.value)
                if (erro) setErro(null)
              }}
              placeholder="Ex.: Captação do serviço"
              aria-invalid={erro !== null && !nome.trim()}
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
              {CORES.map((opcao) => {
                const escolhida = cor.toUpperCase() === opcao.valor

                return (
                  <button
                    key={opcao.valor}
                    type="button"
                    onClick={() => setCor(opcao.valor)}
                    aria-label={opcao.rotulo}
                    aria-pressed={escolhida}
                    title={opcao.rotulo}
                    className={cn(
                      'size-7 rounded-md border transition-all duration-200',
                      escolhida
                        ? 'border-tinta ring-2 ring-tinta/20'
                        : 'border-linha hover:border-linha-forte',
                    )}
                    style={{ backgroundColor: opcao.valor }}
                  />
                )
              })}
            </div>
            <p className="text-xs text-tinta-fraca">Paleta fechada, definida em docs/DESIGN.md.</p>
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
