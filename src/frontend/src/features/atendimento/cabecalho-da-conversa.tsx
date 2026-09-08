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
  DropdownMenuLabel,
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
import { formatarTelefone, nomeDeExibicao } from '@/lib/formatacao'
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
  const nomeExibido = nomeDeExibicao(
    conversa.clienteNome,
    conversa.clienteTelefone,
    conversa.clienteNomePendente,
  )

  return (
    <header className="flex shrink-0 items-center justify-between gap-2 border-b border-linha-forte bg-branco px-3 py-2 md:gap-4 md:px-6 md:py-2.5">
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
          nome={nomeExibido}
          fotoUrl={conversa.clienteFotoPerfilUrl}
          tamanho="md"
          corDeFundo={corSolidaDaEtapa(conversa.etapaCor)}
          className="hidden md:inline-flex"
        />

        <div className="min-w-0 flex-1">
          {/* Linha 1 — identidade: quem é o cliente e quem atende. Nome,
              telefone e cadeado+atendente ficam juntos aqui porque respondem à
              mesma pergunta ("quem"); a etapa/setor/serviço, que respondem
              "em que pé está", moram na linha 2. Antes tudo — telefone,
              cadeado, TRÊS pílulas — dividia uma faixa só embaixo do nome; a
              pílula de serviço em particular perdia a disputa e cortava até
              virar 3 caracteres. Separar por PERGUNTA, não por caber, deixou
              cada linha com menos coisa competindo pelo mesmo espaço.

              Nunca quebra — o nome do cliente é quem cede, truncando com
              reticências antes que a linha precise de uma segunda altura. */}
          <div className="flex flex-nowrap items-center gap-x-2 overflow-hidden">
            {/* Fase 3.2 (secao 16): Inter bold no lugar da Spectral — a
                "manchete" da 3.1, nao mais o titulo serifado. A Spectral
                continua no selo e na linha de autoria do fio (10.4),
                intocados. */}
            <h2 className="min-w-0 shrink truncate text-[15px] font-semibold leading-tight tracking-tight text-tinta">
              {nomeExibido}
            </h2>

            {/* O contato chegou sem nome utilizável e o título acima está
                mostrando o telefone. Sem este selo, o atendente leria um número
                no lugar do nome e suporia cadastro quebrado. */}
            {conversa.clienteNomePendente ? (
              <span
                className="shrink-0 rounded border border-linha px-1 text-[10px] text-tinta-fraca"
                title="O WhatsApp deste contato não traz um nome utilizável. O sistema já pediu, ou vai pedir, o nome pela conversa."
              >
                sem nome
              </span>
            ) : null}

            {!ativa ? <Badge variant="secondary">Encerrada</Badge> : null}

            {/* Container query, nao breakpoint de tela: quem aperta esta
                linha e o painel de historico abrindo ao lado, com a janela do
                mesmo tamanho. Abaixo de 42rem de COLUNA o telefone some. */}
            {/* Escondido quando o telefone JA e o titulo acima, para nao sair
                duas vezes na mesma linha. */}
            {conversa.clienteNomePendente ? null : (
              <span className="hidden shrink-0 font-dado text-xs text-tinta-media @2xl:inline">
                {formatarTelefone(conversa.clienteTelefone)}
              </span>
            )}

            {/* Cadeado no lugar de "Atendido por": mesma informacao, ~70px a
                menos de linha, e o mesmo tratamento que a linha da inbox ja usa
                (verde para quem atende, DESIGN.md secao 5). O texto inteiro fica
                no title. */}
            {conversa.atendenteNome ? (
              <>
                {/* Só aparece junto do telefone (mesmo @2xl): sem isso, o
                    cadeado vira o primeiro item da linha em coluna estreita, e
                    um separador sem nada antes fica órfão. */}
                <span className="hidden shrink-0 text-xs text-tinta-fraca @2xl:inline">·</span>
                <span
                  className={cn(
                    'flex min-w-0 shrink items-center gap-1 text-xs',
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
          </div>

          {/* Linha 2 — em que pé está: os três eixos do atendimento.
              Continuam sendo os MESMOS <Select>, com as mesmas opções e a
              mesma trava de permissão, só que do tamanho do dado que mostram.

              Setor e servico, na maioria das conversas, ja chegam preenchidos
              pela triagem automatica (FLUXO-AUTOMACAO, secoes 6 e 7). Continuam
              editaveis porque cliente erra opcao de menu — e a etapa nao e
              automatica em fase nenhuma: quem move e o atendente.

              Neutras de proposito: cor de etapa e status passivo e nao entra em
              controle (DESIGN.md, 2.2). A cor da etapa continua no avatar ao
              lado.

              Nunca quebra. Etapa e Setor ficam no tamanho natural
              (`shrink-0`) — nomes curtos, raramente cortam; Serviço é
              `flexivel` e cede o espaço que sobrar, truncando por último. É
              o eixo com nome mais comprido em geral ("Escritura pública de
              compra e venda...") e o único dos três cujo valor completo não
              precisa de leitura instantânea — etapa e setor guiam a
              triagem, serviço só confirma o que a triagem já decidiu. */}
          <div className="mt-1 flex flex-nowrap items-center gap-1.5 overflow-hidden">
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
              rotuloExibido={servicos.find((s) => s.id === conversa.tipoServicoId)?.nome}
              desabilitado={!podeEditar || !ativa}
              aoMudar={aoVincularServico}
              flexivel
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
        {/* Mini, e nao o de 68px: o cabecalho e deliberadamente so duas
            linhas (identidade + status), e o selo cheio sozinho devolveria a
            altura que essa compactacao economizou. Continua sendo um selo por
            conversa (DESIGN.md, secao 6). */}
        {etapaEhFinal ? (
          <span className="selo-concluido selo-concluido-mini mr-1" aria-label="Ato concluído">
            Ato
            <br />
            pronto
          </span>
        ) : null}

        <EtiquetaDePrazo situacao={conversa.situacaoPrazo} prazoLimite={conversa.prazoLimite} compacta />

        {/* Assumir, Histórico e Mídias saem da linha quando a coluna aperta e
            reaparecem como item do menu ⋮ logo abaixo — container query
            (`@xl`, 36rem), não breakpoint de tela: quem tira espaço desta
            linha é a largura que SOBRA para a coluna da conversa (rail
            empurrando, lista de conversas ao lado), não o tamanho do
            aparelho. Par completo com os itens homônimos do ⋮ mais abaixo —
            os dois lados do par têm que usar o MESMO limiar, ou a ação
            aparece duas vezes ou nenhuma. */}
        {naFila ? (
          <Button size="sm" className="hidden @xl:inline-flex" onClick={aoAssumir} disabled={assumindo}>
            <Hand />
            Assumir
          </Button>
        ) : null}

        <Button
          variant={painelAtivo === 'historico' ? 'secondary' : 'ghost'}
          size="icon-sm"
          className="hidden rounded-full @xl:inline-flex"
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
          className="hidden rounded-full @2xl:inline-flex"
          aria-pressed={painelAtivo === 'midias'}
          onClick={() => aoAlternarPainel('midias')}
          title="Mídias e arquivos"
        >
          <Images />
          <span className="sr-only">Mídias e arquivos</span>
        </Button>

        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button variant="ghost" size="icon-sm" className="size-9 rounded-full md:size-8">
              <MoreVertical />
              <span className="sr-only">Ações</span>
            </Button>
          </DropdownMenuTrigger>

          <DropdownMenuContent align="end" className="w-56">
            {/* O protocolo morava na linha de identificação e disputava
                espaço com telefone, cadeado e as três pílulas — saiu de lá e
                ficou aqui, sempre a um toque de distância. Não é ação, por
                isso não é DropdownMenuItem — é dado, igual o e-mail no menu
                do atendente (components/layout/shell.tsx). */}
            <DropdownMenuLabel className="font-dado text-xs font-normal normal-case tracking-normal text-tinta-media">
              Protocolo {conversa.protocolo}
            </DropdownMenuLabel>
            <DropdownMenuSeparator />

            {/* Consulta, não ação sobre o atendimento: fica fora do bloco de
                Transferir/Devolver/Encerrar e vale também para conversa
                encerrada — reler o arquivo de um atendimento fechado é
                justamente um dos casos de uso. */}
            <DropdownMenuItem onSelect={() => aoAlternarPainel('midias')}>
              <Images />
              Mídias e arquivos
            </DropdownMenuItem>

            {/* `@xl:hidden` — par complementar exato do botão homônimo da
                linha, acima. Mesmo limiar dos dois lados: senão a ação
                aparece na linha E no menu ao mesmo tempo, ou em nenhum dos
                dois. */}
            <DropdownMenuItem className="@xl:hidden" onSelect={() => aoAlternarPainel('historico')}>
              <History />
              Histórico de etapas
            </DropdownMenuItem>

            {naFila ? (
              <DropdownMenuItem className="@xl:hidden" disabled={assumindo} onSelect={aoAssumir}>
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
              histórico continua consultável e o atendimento pode ser reaberto. O cliente recebe um
              aviso de encerramento pelo WhatsApp.
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
            <p className="text-xs text-tinta-fraca">
              Fica só no registro interno — o cliente não vê o motivo.
            </p>
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
  rotuloExibido,
  desabilitado,
  aoMudar,
  flexivel = false,
  children,
}: {
  valor: string | null
  rotulo: string
  /**
   * Texto mostrado na pílula fechada, quando é diferente do item selecionado.
   * Sem isso, o Radix espelha o conteúdo do SelectItem escolhido — certo para
   * etapa e setor (só o nome), rico demais para serviço, cujo item também
   * carrega o prazo ("Certidão · 5 dias úteis") para fazer sentido na lista
   * aberta. Fechada, a pílula só precisa do nome; o prazo continua ao abrir.
   */
  rotuloExibido?: string
  desabilitado: boolean
  aoMudar: (valor: string) => void
  /**
   * true faz esta pílula ceder espaço (e truncar primeiro) quando a linha
   * não cabe todo mundo; false (padrão) mantém o tamanho natural e nunca
   * encolhe. A linha inteira nunca quebra — alguém precisa ceder, e só uma
   * pílula por linha deve fazer isso, senão as três cortam um pouco cada
   * uma e nenhuma sobra legível.
   */
  flexivel?: boolean
  children: ReactNode
}) {
  return (
    <Select value={valor ?? SEM_VALOR} disabled={desabilitado} onValueChange={aoMudar}>
      <SelectTrigger
        title={rotulo}
        aria-label={rotulo}
        /* md:h-auto e md:text-[11px] repetem o valor de base de propósito: o
           select.tsx global despacha h-11/text-base no celular e md:h-9/
           md:text-sm no desktop para o campo comum, e um regra COM modificador
           só perde para outra COM o MESMO modificador — texto sem prefixo
           nunca derruba `md:text-sm` no CSS final, só o texto sem prefixo de
           baixo. Sem o par `md:`, a pílula nascia pequena e virava um select de
           36px/14px assim que a tela passava de 768px, truncando cedo demais
           ("Atendimen...") dentro do mesmo max-w-[13rem]. */
        className={cn(
          // Fase 3.2: rounded-full no lugar de rounded-md. O par md:h-auto/
          // md:text-[11px] continua EXATAMENTE como estava (ver comentario
          // acima) — nao e redundancia, e o que impede a pilula de virar
          // select de 36px a partir de 768px.
          'h-auto w-auto min-w-[4.5rem] max-w-[13rem] gap-1 rounded-full border-linha bg-superficie px-1.5 py-0.5 text-[11px] font-medium text-tinta transition-colors duration-[var(--duracao-rapida)] hover:bg-papel md:h-auto md:text-[11px] [&>svg]:size-3 [&>svg]:opacity-60',
          flexivel ? 'shrink' : 'shrink-0',
        )}
      >
        <SelectValue placeholder={rotulo}>{rotuloExibido}</SelectValue>
      </SelectTrigger>
      <SelectContent>{children}</SelectContent>
    </Select>
  )
}
