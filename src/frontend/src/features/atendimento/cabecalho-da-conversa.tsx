import { useState, type ReactNode } from 'react'
import {
  ArrowLeft,
  ArrowRightLeft,
  CircleCheck,
  Hand,
  History,
  Images,
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

/** Qual painel lateral está aberto — `null` quando o fio ocupa a coluna toda. */
export type PainelDaConversa = 'historico' | 'midias' | null

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
  aoVoltar,
  painelAtivo,
  aoAlternarPainel,
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
  /** Só existe no celular, onde lista e conversa não cabem lado a lado. */
  aoVoltar: () => void
  /**
   * Um painel lateral por vez. Dois de 256px abertos juntos num monitor de
   * 1366px deixariam ~250px de fio — exatamente o aperto que a inbox compacta
   * veio resolver.
   */
  painelAtivo: PainelDaConversa
  aoAlternarPainel: (qual: Exclude<PainelDaConversa, null>) => void
}) {
  const [dialogoEncerrar, setDialogoEncerrar] = useState(false)
  const [dialogoTransferir, setDialogoTransferir] = useState(false)
  const [motivo, setMotivo] = useState('')
  const [destino, setDestino] = useState('')

  const podeEditar = souOResponsavel || podeOperarDeTerceiro
  const ativa = conversa.status === 'Ativa'
  const naFila = conversa.atendenteId === null && ativa

  return (
    <header className="flex shrink-0 items-start justify-between gap-2 border-b border-linha-forte bg-branco px-3 py-2 md:gap-4 md:px-6 md:py-2.5">
      <div className="flex min-w-0 flex-1 items-center gap-2 md:gap-3">
        {/* Voltar só no celular: no desktop a lista está do lado, e um botão de
            voltar para uma coluna que nunca saiu da tela é ruído. */}
        <button
          type="button"
          onClick={aoVoltar}
          aria-label="Voltar para a lista de conversas"
          title="Voltar"
          className="-ml-1 flex size-9 shrink-0 items-center justify-center rounded-md border border-transparent text-tinta-media transition-colors hover:border-linha hover:bg-superficie hover:text-tinta md:hidden"
        >
          <ArrowLeft className="size-4" />
        </button>

        {/* Cor da etapa atual, igual à mini-pílula da lista e à pílula do funil.
            Cede o lugar ao botão de voltar no celular: a cor da etapa continua
            na pílula do funil, na mini-pílula da lista e no seletor ao lado. */}
        <Avatar
          nome={conversa.clienteNome}
          fotoUrl={conversa.clienteFotoPerfilUrl}
          tamanho="md"
          corDeFundo={corSolidaDaEtapa(conversa.etapaCor)}
          className="hidden md:inline-flex"
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
          {/* NO DESKTOP nao quebra linha: encolhe, e no aperto extremo esconde.
              Quebrar la custa mais 24px na faixa fixa do topo, que e exatamente
              o que aquela tela nao tem. A ordem de sacrificio e deliberada:
                1. o nome de quem atende trunca (o inteiro fica no title);
                2. as pilulas truncam (o valor cheio esta no title e na lista);
                3. o telefone some abaixo de 42rem de coluna;
                4. o PROTOCOLO nunca sai — e o identificador do atendimento.

              NO CELULAR volta a quebrar. A regra acima e sobre FAIXA DE ALTURA
              FIXA num monitor, e o proprio DESIGN.md diz que onde o container
              nao tem altura fixa o flex-wrap continua certo. Em 360px de
              largura nao existe truncamento honesto que caiba protocolo,
              cadeado e tres pilulas numa linha so — o que sobraria seria o
              protocolo cortado, justamente o item que nunca pode sair. */}
          <div className="mt-0.5 flex min-w-0 flex-wrap items-center gap-x-1.5 gap-y-1 text-xs text-tinta-media md:flex-nowrap md:gap-y-0 md:overflow-hidden">
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

            {/* Some no celular: num inicio de linha quebrada o divisor fica
                orfao, separando nada de nada. */}
            <span className="mx-0.5 hidden h-3.5 w-px shrink-0 bg-linha-forte md:block" />

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

      <div className="flex shrink-0 items-center gap-1.5 md:gap-2.5">
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

        {/* Assumir, Histórico e Mídias saem da linha no celular e reaparecem
            como item do menu ⋮ logo abaixo. Em 360px o cluster inteiro não cabe
            junto do nome, e o que se perde primeiro é o que tem outro caminho. */}
        {naFila ? (
          <Button size="sm" className="hidden md:inline-flex" onClick={aoAssumir} disabled={assumindo}>
            <Hand />
            Assumir
          </Button>
        ) : null}

        <Button
          variant={painelAtivo === 'historico' ? 'secondary' : 'ghost'}
          size="icon-sm"
          className="hidden md:inline-flex"
          aria-pressed={painelAtivo === 'historico'}
          onClick={() => aoAlternarPainel('historico')}
          title="Histórico de etapas"
        >
          <History />
          <span className="sr-only">Histórico de etapas</span>
        </Button>

        {/* Container query, e nao breakpoint de tela: quem aperta esta linha e
            a largura QUE SOBRA para a coluna — o painel lateral abrindo ao lado
            muda isso com a janela do mesmo tamanho. Em coluna estreita o acesso
            continua pelo menu ⋮, que nunca sai. */}
        <Button
          variant={painelAtivo === 'midias' ? 'secondary' : 'ghost'}
          size="icon-sm"
          className="hidden @2xl:inline-flex"
          aria-pressed={painelAtivo === 'midias'}
          onClick={() => aoAlternarPainel('midias')}
          title="Mídias e arquivos"
        >
          <Images />
          <span className="sr-only">Mídias e arquivos</span>
        </Button>

        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button variant="ghost" size="icon-sm" className="size-9 md:size-8">
              <MoreVertical />
              <span className="sr-only">Ações</span>
            </Button>
          </DropdownMenuTrigger>

          <DropdownMenuContent align="end" className="w-56">
            {/* Consulta, não ação sobre o atendimento: fica fora do bloco de
                Transferir/Devolver/Encerrar e vale também para conversa
                encerrada — reler o arquivo de um atendimento fechado é
                justamente um dos casos de uso. */}
            <DropdownMenuItem onSelect={() => aoAlternarPainel('midias')}>
              <Images />
              Mídias e arquivos
            </DropdownMenuItem>

            <DropdownMenuItem className="md:hidden" onSelect={() => aoAlternarPainel('historico')}>
              <History />
              Histórico de etapas
            </DropdownMenuItem>

            {naFila ? (
              <DropdownMenuItem className="md:hidden" disabled={assumindo} onSelect={aoAssumir}>
                <Hand />
                Assumir atendimento
              </DropdownMenuItem>
            ) : null}

            <DropdownMenuSeparator />

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
