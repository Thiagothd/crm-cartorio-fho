import { useState } from 'react'
import {
  ArrowRightLeft,
  CircleCheck,
  Hand,
  History,
  MoreVertical,
  RotateCcw,
  Undo2,
} from 'lucide-react'
import { toast } from 'sonner'
import { Avatar } from '@/components/ui/avatar'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { formatarTelefone } from '@/lib/formatacao'
import { EtiquetaDePrazo } from './etiqueta-de-prazo'
import { corSolidaDaEtapa } from './cores-de-etapa'
import type { Atendente, Conversa, Etapa, Setor, TipoServico } from '@/lib/api/tipos'

const SEM_VALOR = '__nenhum__'

export function CabecalhoDaConversa({
  conversa,
  souOResponsavel,
  podeOperarDeTerceiro,
  etapas,
  setores,
  servicos,
  atendentes,
  assumindo,
  etapaEhFinal,
  aoAssumir,
  aoLiberar,
  aoMover,
  aoVincularServico,
  aoTransferir,
  aoEncerrar,
  aoReabrir,
  aoAlternarHistorico,
  historicoVisivel,
}: {
  conversa: Conversa
  souOResponsavel: boolean
  podeOperarDeTerceiro: boolean
  etapas: Etapa[]
  setores: Setor[]
  servicos: TipoServico[]
  atendentes: Atendente[]
  assumindo: boolean
  etapaEhFinal: boolean
  aoAssumir: () => void
  aoLiberar: () => void
  aoMover: (dados: { etapaId?: string; setorId?: string }) => void
  aoVincularServico: (tipoServicoId: string) => void
  aoTransferir: (atendenteId: string) => void
  aoEncerrar: (motivo?: string) => void
  aoReabrir: () => void
  aoAlternarHistorico: () => void
  historicoVisivel: boolean
}) {
  const [dialogoEncerrar, setDialogoEncerrar] = useState(false)
  const [dialogoTransferir, setDialogoTransferir] = useState(false)
  const [motivo, setMotivo] = useState('')
  const [destino, setDestino] = useState('')

  const podeEditar = souOResponsavel || podeOperarDeTerceiro
  const ativa = conversa.status === 'Ativa'
  const naFila = conversa.atendenteId === null && ativa

  return (
    <header className="flex shrink-0 flex-col gap-3 border-b border-linha-forte bg-branco px-6 py-3">
      <div className="flex items-start justify-between gap-4">
        <div className="flex min-w-0 items-center gap-3">
          {/* Cor da etapa atual, igual à mini-pílula da lista e à pílula do funil. */}
          <Avatar
            nome={conversa.clienteNome}
            tamanho="lg"
            corDeFundo={corSolidaDaEtapa(conversa.etapaCor)}
          />

          <div className="min-w-0">
            <div className="flex items-center gap-2">
              <h2 className="truncate font-titulo text-base leading-tight text-tinta">
                {conversa.clienteNome}
              </h2>
              {!ativa ? <Badge variant="secondary">Encerrada</Badge> : null}
            </div>

            <div className="flex flex-wrap items-center gap-x-2 gap-y-0.5 text-xs text-tinta-media">
              <span className="font-dado">{formatarTelefone(conversa.clienteTelefone)}</span>
              <span className="text-linha-forte">|</span>
              {/* Etiqueta com borda — DESIGN.md, secao 10. */}
              <span className="etiqueta-protocolo">{conversa.protocolo}</span>
              {conversa.atendenteNome ? (
                <>
                  <span className="text-linha-forte">|</span>
                  <span>
                    {souOResponsavel ? 'Você atende' : `Atendido por ${conversa.atendenteNome}`}
                  </span>
                </>
              ) : null}
            </div>
          </div>
        </div>

        <div className="flex shrink-0 items-center gap-2.5">
          {/* O selo, em tamanho cheio. DESIGN.md, seção 6. */}
          {etapaEhFinal ? (
            <span className="selo-concluido mr-1" aria-label="Ato concluído">
              Ato
              <br />
              pronto
            </span>
          ) : null}

          <EtiquetaDePrazo situacao={conversa.situacaoPrazo} prazoLimite={conversa.prazoLimite} />

          {naFila ? (
            <Button size="sm" onClick={aoAssumir} disabled={assumindo}>
              <Hand />
              Assumir
            </Button>
          ) : null}

          <Button
            variant={historicoVisivel ? 'secondary' : 'ghost'}
            size="icon-sm"
            onClick={aoAlternarHistorico}
            title="Histórico de etapas"
          >
            <History />
          </Button>

          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="ghost" size="icon-sm">
                <MoreVertical />
                <span className="sr-only">Ações</span>
              </Button>
            </DropdownMenuTrigger>

            <DropdownMenuContent align="end" className="w-56">
              {ativa ? (
                <>
                  <DropdownMenuItem
                    disabled={!podeEditar}
                    onSelect={() => setDialogoTransferir(true)}
                  >
                    <ArrowRightLeft />
                    Transferir atendimento
                  </DropdownMenuItem>

                  <DropdownMenuItem disabled={!souOResponsavel} onSelect={aoLiberar}>
                    <Undo2 />
                    Devolver para a fila
                  </DropdownMenuItem>

                  <DropdownMenuSeparator />

                  <DropdownMenuItem disabled={!podeEditar} onSelect={() => setDialogoEncerrar(true)}>
                    <CircleCheck />
                    Encerrar atendimento
                  </DropdownMenuItem>
                </>
              ) : (
                <DropdownMenuItem onSelect={aoReabrir}>
                  <RotateCcw />
                  Reabrir atendimento
                </DropdownMenuItem>
              )}
            </DropdownMenuContent>
          </DropdownMenu>
        </div>
      </div>

      {/* Etapa, setor e serviço: os três eixos que o atendente move no dia a dia. */}
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
        <Select
          value={conversa.etapaId ?? SEM_VALOR}
          disabled={!podeEditar || !ativa}
          onValueChange={(v) => aoMover({ etapaId: v })}
        >
          <SelectTrigger className="h-8 text-xs">
            <SelectValue placeholder="Etapa" />
          </SelectTrigger>
          <SelectContent>
            {conversa.etapaId === null ? (
              <SelectItem value={SEM_VALOR} disabled>
                Sem etapa
              </SelectItem>
            ) : null}
            {etapas.map((etapa) => (
              <SelectItem key={etapa.id} value={etapa.id}>
                {etapa.nome}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>

        <Select
          value={conversa.setorId ?? SEM_VALOR}
          disabled={!podeEditar || !ativa}
          onValueChange={(v) => aoMover({ setorId: v })}
        >
          <SelectTrigger className="h-8 text-xs">
            <SelectValue placeholder="Setor" />
          </SelectTrigger>
          <SelectContent>
            {conversa.setorId === null ? (
              <SelectItem value={SEM_VALOR} disabled>
                Sem setor
              </SelectItem>
            ) : null}
            {setores.map((setor) => (
              <SelectItem key={setor.id} value={setor.id}>
                {setor.nome}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>

        <Select
          value={conversa.tipoServicoId ?? SEM_VALOR}
          disabled={!podeEditar || !ativa}
          onValueChange={aoVincularServico}
        >
          <SelectTrigger className="h-8 text-xs">
            <SelectValue placeholder="Serviço" />
          </SelectTrigger>
          <SelectContent>
            {conversa.tipoServicoId === null ? (
              <SelectItem value={SEM_VALOR} disabled>
                Serviço não definido
              </SelectItem>
            ) : null}
            {servicos.map((servico) => (
              <SelectItem key={servico.id} value={servico.id}>
                {servico.nome}
                {servico.prazo ? ` · ${servico.prazo.prazoDescrito}` : ''}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      {/* Encerrar */}
      <Dialog open={dialogoEncerrar} onOpenChange={setDialogoEncerrar}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Encerrar atendimento</DialogTitle>
            <DialogDescription>
              A conversa sai da lista de ativas e vai para o arquivo. Nada é apagado — todo o
              histórico continua consultável e o atendimento pode ser reaberto.
            </DialogDescription>
          </DialogHeader>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="motivo">Motivo (opcional)</Label>
            <Input
              id="motivo"
              value={motivo}
              onChange={(e) => setMotivo(e.target.value)}
              placeholder="Ex.: certidão entregue no balcão"
              maxLength={500}
            />
          </div>

          <DialogFooter>
            <Button variant="outline" onClick={() => setDialogoEncerrar(false)}>
              Cancelar
            </Button>
            <Button
              onClick={() => {
                aoEncerrar(motivo.trim() || undefined)
                setMotivo('')
                setDialogoEncerrar(false)
              }}
            >
              Encerrar
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Transferir */}
      <Dialog open={dialogoTransferir} onOpenChange={setDialogoTransferir}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Transferir atendimento</DialogTitle>
            <DialogDescription>
              O novo responsável passa a ser o único que pode responder. As mensagens já enviadas
              continuam com o nome de quem as escreveu.
            </DialogDescription>
          </DialogHeader>

          <div className="flex flex-col gap-1.5">
            <Label>Novo responsável</Label>
            <Select value={destino} onValueChange={setDestino}>
              <SelectTrigger>
                <SelectValue placeholder="Escolha o atendente" />
              </SelectTrigger>
              <SelectContent>
                {atendentes
                  .filter((a) => a.id !== conversa.atendenteId)
                  .map((atendente) => (
                    <SelectItem key={atendente.id} value={atendente.id}>
                      {atendente.nome} · {atendente.perfil}
                    </SelectItem>
                  ))}
              </SelectContent>
            </Select>
          </div>

          <DialogFooter>
            <Button variant="outline" onClick={() => setDialogoTransferir(false)}>
              Cancelar
            </Button>
            <Button
              disabled={!destino}
              onClick={() => {
                if (!destino) {
                  toast.info('Escolha o atendente de destino.')
                  return
                }
                aoTransferir(destino)
                setDestino('')
                setDialogoTransferir(false)
              }}
            >
              Transferir
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </header>
  )
}
