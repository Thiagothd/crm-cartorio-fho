import { useState, type ReactNode } from 'react'
import {
  ArrowRightLeft,
  CircleCheck,
  Hand,
  History,
  Lock,
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
import { cn } from '@/lib/utils'
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
    <header className="flex shrink-0 items-start justify-between gap-4 border-b border-linha-forte bg-branco px-6 py-2.5">
      <div className="flex min-w-0 flex-1 items-center gap-3">
        {/* Cor da etapa atual, igual à mini-pílula da lista e à pílula do funil. */}
        <Avatar
          nome={conversa.clienteNome}
          tamanho="md"
          corDeFundo={corSolidaDaEtapa(conversa.etapaCor)}
        />

        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <h2 className="truncate font-titulo text-base leading-tight text-tinta">
              {conversa.clienteNome}
            </h2>
            {!ativa ? <Badge variant="secondary">Encerrada</Badge> : null}
          </div>

          {/* Identificacao e os tres eixos na MESMA linha.
              Etapa, setor e servico ocupavam uma faixa inteira logo abaixo, com
              tres seletores de 32px de altura; num monitor de 1366x768 essa
              faixa saia direto do fio de mensagens. Viraram pilulas — continuam
              sendo os MESMOS <Select>, com as mesmas opcoes e a mesma trava de
              permissao, so que do tamanho do dado que mostram.

              Setor e servico, na maioria das conversas, ja chegam preenchidos
              pela triagem automatica (FLUXO-AUTOMACAO, secoes 6 e 7). Continuam
              editaveis porque cliente erra opcao de menu — e a etapa nao e
              automatica em fase nenhuma: quem move e o atendente.

              Neutras de proposito: cor de etapa e status passivo e nao entra em
              controle (DESIGN.md, 2.2). A cor da etapa continua no avatar ao
              lado. */}
          {/* NAO quebra linha: encolhe, e no aperto extremo esconde.
              Quebrar aqui custa mais 24px na faixa fixa do topo, que e exatamente
              o que esta tela nao tem. A ordem de sacrificio e deliberada:
                1. o nome de quem atende trunca (o inteiro fica no title);
                2. as pilulas truncam (o valor cheio esta no title e na lista);
                3. o telefone some abaixo de 42rem de coluna;
                4. o PROTOCOLO nunca sai — e o identificador do atendimento. */}
          <div className="mt-0.5 flex min-w-0 items-center gap-x-1.5 overflow-hidden text-xs text-tinta-media">
            {/* Container query, nao breakpoint de tela: quem aperta esta linha
                e o painel de historico abrindo ao lado, com a janela do mesmo
                tamanho. Abaixo de 42rem de COLUNA o telefone sai e o protocolo
                fica — protocolo e o que o atendente procura na lista e o que o
                cliente repete ao telefone. O numero continua no cadastro e na
                linha da inbox. */}
            <span className="hidden shrink-0 font-dado @2xl:inline">
              {formatarTelefone(conversa.clienteTelefone)}
            </span>
            <span className="hidden shrink-0 text-tinta-fraca @2xl:inline">·</span>
            {/* Etiqueta com borda — DESIGN.md, secao 10. */}
            <span className="etiqueta-protocolo shrink-0">{conversa.protocolo}</span>
            {/* Cadeado no lugar de "Atendido por": mesma informacao, ~70px a
                menos de linha, e o mesmo tratamento que a linha da inbox ja usa
                (verde para quem atende, DESIGN.md secao 5). O texto inteiro fica
                no title. */}
            {conversa.atendenteNome ? (
              <>
                <span className="shrink-0 text-tinta-fraca">·</span>
                <span
                  className={cn(
                    'flex min-w-0 shrink items-center gap-1',
                    souOResponsavel ? 'text-verde' : 'text-tinta-media',
                  )}
                  title={
                    souOResponsavel
                      ? 'Você é o responsável por este atendimento'
                      : `Atendido por ${conversa.atendenteNome}`
                  }
                >
                  <Lock className="size-3 shrink-0" />
                  <span className="truncate">
                    {souOResponsavel ? 'Você atende' : conversa.atendenteNome}
                  </span>
                </span>
              </>
            ) : null}

            <span className="mx-0.5 h-3.5 w-px shrink-0 bg-linha-forte" />

            <PilulaDeSelecao
              valor={conversa.etapaId}
              rotulo="Etapa do atendimento"
              desabilitado={!podeEditar || !ativa}
              aoMudar={(v) => aoMover({ etapaId: v })}
            >
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
            </PilulaDeSelecao>

            <PilulaDeSelecao
              valor={conversa.setorId}
              rotulo="Setor responsável"
              desabilitado={!podeEditar || !ativa}
              aoMudar={(v) => aoMover({ setorId: v })}
            >
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
            </PilulaDeSelecao>

            <PilulaDeSelecao
              valor={conversa.tipoServicoId}
              rotulo="Serviço solicitado"
              desabilitado={!podeEditar || !ativa}
              aoMudar={aoVincularServico}
            >
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
            </PilulaDeSelecao>
          </div>
        </div>
      </div>

      <div className="flex shrink-0 items-center gap-2.5">
        {/* Mini, e nao o de 68px: o cabecalho virou uma linha so, e o selo
            cheio sozinho devolveria a altura que a linha unica economizou.
            Continua sendo um selo por conversa (DESIGN.md, secao 6). */}
        {etapaEhFinal ? (
          <span className="selo-concluido selo-concluido-mini mr-1" aria-label="Ato concluído">
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

/**
 * Um Select do tamanho do dado que ele mostra.
 *
 * Mesmo componente, mesmas opcoes e mesma trava de permissao do seletor de caixa
 * cheia que existia antes — muda o trajeto visual, nao o que o atendente pode
 * fazer. Neutra: cor de etapa e status passivo e nao entra em controle
 * (DESIGN.md, 2.2).
 */
function PilulaDeSelecao({
  valor,
  rotulo,
  desabilitado,
  aoMudar,
  children,
}: {
  valor: string | null
  rotulo: string
  desabilitado: boolean
  aoMudar: (valor: string) => void
  children: ReactNode
}) {
  return (
    <Select value={valor ?? SEM_VALOR} disabled={desabilitado} onValueChange={aoMudar}>
      <SelectTrigger
        title={rotulo}
        aria-label={rotulo}
        className="h-auto w-auto min-w-[4.5rem] max-w-[13rem] shrink gap-1 rounded-md border-linha bg-superficie px-1.5 py-0.5 text-[11px] font-medium text-tinta [&>svg]:size-3 [&>svg]:opacity-60"
      >
        <SelectValue placeholder={rotulo} />
      </SelectTrigger>
      <SelectContent>{children}</SelectContent>
    </Select>
  )
}
