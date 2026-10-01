import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { Bot, CircleHelp, Loader2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { Switch } from '@/components/ui/switch'
import { usarChavesGerais } from '@/features/configuracoes/chave-geral'
import { formatarDataHora, formatarHora } from '@/lib/formatacao'
import { cn } from '@/lib/utils'
import type { Conversa, ModoDeRetomadaDoRobo, Setor } from '@/lib/api/tipos'

/**
 * O robô NESTA conversa: ver o estado, pausar e ligar de novo. Pedido do cartório parceiro em
 * 2026-09-30, para o cliente que se confunde com o robô.
 *
 * Os estados, do mais forte para o mais fraco — o primeiro que valer é o que
 * aparece:
 *   1. mensagens automáticas E IA desligadas no geral → nada a fazer aqui. Só uma
 *      das duas desligada NÃO é robô desligado: com as mensagens desligadas a IA
 *      continua respondendo, e com a IA desligada os menus continuam saindo
 *      (achado no teste de 2026-09-30) — a janela diz o que vale e a pausa
 *      continua disponível;
 *   2. robô pausado (por alguém, ou porque o contato é "sem robô");
 *   3. com atendente (alguém assumiu, ou o cliente pediu uma pessoa);
 *   4. setor "só equipe" → quem escolheu o setor já caiu na equipe (é o 3);
 *   5. robô ativo.
 *
 * Ligar de novo devolve a conversa para a fila (o backend explica por quê:
 * `ConversaService.RetomarRoboAsync`), e isso aparece escrito antes do clique.
 */
export function ControleDoRobo({
  conversa,
  setores,
  podeControlar,
  aoPausar,
  aoLigar,
}: {
  conversa: Conversa
  setores: Setor[]
  podeControlar: boolean
  aoPausar: (sempreSemRoboParaOContato: boolean) => Promise<void>
  aoLigar: (modo: ModoDeRetomadaDoRobo, tirarMarcaDoContato: boolean) => Promise<void>
}) {
  const { data: chaves } = usarChavesGerais()
  const [aberto, setAberto] = useState(false)
  const [salvando, setSalvando] = useState(false)
  const [sempreSemRobo, setSempreSemRobo] = useState(false)
  const [modo, setModo] = useState<ModoDeRetomadaDoRobo>('SoResponder')
  const [tirarMarca, setTirarMarca] = useState(false)
  const [explicando, setExplicando] = useState(false)

  // Cada abertura começa do zero: uma escolha esquecida de ontem não pode ir
  // junto sem o atendente ver.
  useEffect(() => {
    if (aberto) {
      setSempreSemRobo(false)
      setModo('SoResponder')
      setTirarMarca(false)
      setExplicando(false)
    }
  }, [aberto])

  if (conversa.status !== 'Ativa') {
    return null
  }

  const mensagensLigadas = chaves?.mensagensAutomaticasHabilitadas ?? true
  const iaLigada = chaves?.respostaInteligenteHabilitada ?? true
  const desligadoNoGeral = !mensagensLigadas && !iaLigada
  const pausado = conversa.roboPausadoEm !== null
  const comAtendente = !pausado && conversa.emAtendimentoHumano
  const setorSoEquipe = setores.find((s) => s.id === conversa.setorId)?.modoDoRobo === 'SoEquipe'

  const estado: EstadoDoRobo = desligadoNoGeral
    ? 'desligado'
    : pausado
      ? 'pausado'
      : comAtendente
        ? 'atendente'
        : 'ativo'

  async function executar(acao: () => Promise<void>) {
    setSalvando(true)
    try {
      await acao()
      setAberto(false)
    } finally {
      setSalvando(false)
    }
  }

  return (
    <Popover open={aberto} onOpenChange={(v) => !salvando && setAberto(v)}>
      <PopoverTrigger asChild>
        <button
          type="button"
          aria-label={`${ROTULO[estado]} nesta conversa. Abrir controle do robô.`}
          className={cn(
            'inline-flex h-8 shrink-0 items-center gap-1.5 rounded-full border px-2.5 text-[11px] font-medium transition-colors duration-[var(--duracao-rapida)]',
            estado === 'ativo' && 'border-verde/35 text-verde hover:bg-verde/[0.06]',
            estado === 'atendente' && 'border-linha-forte text-tinta-media hover:bg-superficie',
            (estado === 'pausado' || estado === 'desligado') && 'border-ambar/40 bg-ambar/[0.06] text-ambar hover:bg-ambar/10',
          )}
        >
          <Bot className="size-3.5" aria-hidden />
          {/* Container query, igual aos outros botões desta linha: em coluna
              estreita fica só o ícone, e a cor diz o estado. */}
          <span className="hidden @xl:inline">
            {estado === 'pausado' && conversa.clienteSemRobo
              ? 'Sem robô'
              : estado === 'ativo' && !mensagensLigadas
                ? 'Só IA'
                : estado === 'ativo' && !iaLigada
                  ? 'Robô sem IA'
                  : ROTULO[estado]}
          </span>
        </button>
      </PopoverTrigger>

      <PopoverContent className="w-[21rem] p-0">
        {estado === 'desligado' ? (
          <Bloco titulo="Robô desligado no cartório inteiro">
            <p>
              As mensagens automáticas e a IA estão desligadas para todos os clientes. Para religar,
              use o botão <strong className="text-tinta">Robô</strong> no topo do painel.
            </p>
          </Bloco>
        ) : estado === 'ativo' ? (
          <>
            <Bloco titulo="O robô está atendendo esta conversa">
              <p>
                {!mensagensLigadas
                  ? 'Só a IA responde: as mensagens automáticas (menu, textos dos serviços e avisos) estão desligadas no cartório inteiro.'
                  : !iaLigada
                    ? 'Ele responde com o menu e os textos dos serviços. A IA está desligada no cartório inteiro.'
                    : 'Ele responde com o menu, os textos dos serviços e a IA.'}
              </p>
            </Bloco>

            <div className="border-t border-linha px-4 py-3">
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0 pt-1.5">
                  <div className="flex items-center gap-1">
                    <Label htmlFor="robo-sempre-sem" className="text-sm font-medium text-tinta">
                      Atender este contato sempre sem robô
                    </Label>
                    <button
                      type="button"
                      onClick={() => setExplicando((v) => !v)}
                      aria-expanded={explicando}
                      aria-label="O que é atender sem robô?"
                      className="flex size-6 shrink-0 items-center justify-center rounded-full text-tinta-fraca transition-colors hover:bg-superficie hover:text-tinta"
                    >
                      <CircleHelp className="size-3.5" aria-hidden />
                    </button>
                  </div>
                  <p className="mt-0.5 text-xs leading-relaxed text-tinta-media">
                    Vale também para as próximas conversas dele.
                  </p>
                </div>
                <Switch
                  id="robo-sempre-sem"
                  tom="tinta"
                  checked={sempreSemRobo}
                  onCheckedChange={setSempreSemRobo}
                  disabled={!podeControlar || salvando}
                  className="-mr-2"
                />
              </div>

              {explicando ? (
                <p className="mt-2 rounded-md bg-superficie px-3 py-2 text-xs leading-relaxed text-tinta-media">
                  O contato continua falando com o cartório normalmente. Só não recebe menu, IA nem
                  mensagens automáticas: tudo vai direto para a equipe. Quando ele escrever de novo,
                  recebe só um aviso de que a mensagem chegou. Dá para desfazer aqui ou na ficha do
                  cliente.
                </p>
              ) : null}
            </div>

            <Rodape podeControlar={podeControlar}>
              <Button
                size="sm"
                variant="outline"
                disabled={!podeControlar || salvando}
                onClick={() => void executar(() => aoPausar(sempreSemRobo))}
              >
                {salvando ? <Loader2 className="animate-spin" /> : null}
                Pausar o robô
              </Button>
            </Rodape>
          </>
        ) : (
          <>
            <Bloco
              titulo={
                estado === 'pausado'
                  ? conversa.roboPausadoPorNome
                    ? `Robô pausado por ${conversa.roboPausadoPorNome}`
                    : 'Robô pausado: contato atendido sem robô'
                  : setorSoEquipe
                    ? 'Setor atendido só pela equipe'
                    : 'Um atendente está com esta conversa'
              }
            >
              {estado === 'pausado' && conversa.roboPausadoEm ? (
                <p>Desde {quando(conversa.roboPausadoEm)}. Nada automático sai até alguém ligar de novo.</p>
              ) : (
                <p>O robô parou quando a conversa foi para uma pessoa.</p>
              )}
              <p>
                Ao ligar, a conversa volta para a fila
                {conversa.atendenteNome ? ` e sai de ${conversa.atendenteNome}` : ''}, e o robô volta a
                responder.
              </p>
            </Bloco>

            <fieldset className="border-t border-linha px-4 py-3" disabled={!podeControlar || salvando}>
              <legend className="sr-only">Como o robô volta</legend>
              <div className="flex flex-col gap-2" role="radiogroup" aria-label="Como o robô volta">
                <OpcaoDeRetomada
                  ativa={modo === 'SoResponder'}
                  aoEscolher={() => setModo('SoResponder')}
                  titulo="Só voltar a responder"
                  descricao="Não manda nada agora. O robô responde a próxima mensagem do cliente."
                />
                <OpcaoDeRetomada
                  ativa={modo === 'MenuInicial'}
                  aoEscolher={() => setModo('MenuInicial')}
                  titulo="Voltar ao menu inicial"
                  descricao="Manda as boas-vindas com o menu agora, como num atendimento novo."
                />
              </div>

              {conversa.clienteSemRobo ? (
                <div className="mt-3 flex items-start justify-between gap-3">
                  <div className="min-w-0 pt-1.5">
                    <Label htmlFor="robo-tirar-marca" className="text-sm font-medium text-tinta">
                      Tirar a marca “sem robô” do contato
                    </Label>
                    <p className="mt-0.5 text-xs leading-relaxed text-tinta-media">
                      Sem isso, a próxima conversa dele começa pausada de novo.
                    </p>
                  </div>
                  <Switch
                    id="robo-tirar-marca"
                    tom="tinta"
                    checked={tirarMarca}
                    onCheckedChange={setTirarMarca}
                    className="-mr-2"
                  />
                </div>
              ) : null}
            </fieldset>

            <Rodape podeControlar={podeControlar}>
              <Button
                size="sm"
                variant="outline"
                disabled={!podeControlar || salvando}
                onClick={() => void executar(() => aoLigar(modo, tirarMarca))}
              >
                {salvando ? <Loader2 className="animate-spin" /> : null}
                Ligar o robô
              </Button>
            </Rodape>
          </>
        )}
      </PopoverContent>
    </Popover>
  )
}

type EstadoDoRobo = 'desligado' | 'pausado' | 'atendente' | 'ativo'

/** "as 14:32" hoje; "30/09 14:32" em outro dia — a pausa pode ter dias. */
function quando(valor: string) {
  const data = new Date(valor)
  return data.toDateString() === new Date().toDateString() ? `as ${formatarHora(data)}` : formatarDataHora(data)
}

const ROTULO: Record<EstadoDoRobo, string> = {
  desligado: 'Robô desligado',
  pausado: 'Robô pausado',
  atendente: 'Com atendente',
  ativo: 'Robô ativo',
}

function Bloco({ titulo, children }: { titulo: string; children: ReactNode }) {
  return (
    <div className="px-4 py-3">
      <p className="text-sm font-semibold text-tinta">{titulo}</p>
      <div className="mt-1 flex flex-col gap-1.5 text-xs leading-relaxed text-tinta-media">{children}</div>
    </div>
  )
}

function Rodape({ podeControlar, children }: { podeControlar: boolean; children: ReactNode }) {
  return (
    <div className="flex items-center justify-end gap-3 border-t border-linha px-4 py-3">
      {!podeControlar ? (
        <p className="mr-auto text-[11px] leading-snug text-tinta-fraca">
          Só quem atende esta conversa pode mudar o robô.
        </p>
      ) : null}
      {children}
    </div>
  )
}

function OpcaoDeRetomada({
  ativa,
  aoEscolher,
  titulo,
  descricao,
}: {
  ativa: boolean
  aoEscolher: () => void
  titulo: string
  descricao: string
}) {
  return (
    <button
      type="button"
      role="radio"
      aria-checked={ativa}
      onClick={aoEscolher}
      className={cn(
        'flex w-full items-start gap-2.5 rounded-md border px-3 py-2.5 text-left transition-colors duration-[var(--duracao-rapida)] disabled:cursor-not-allowed disabled:opacity-60',
        ativa ? 'border-tinta/40 bg-superficie' : 'border-linha hover:bg-superficie',
      )}
    >
      <span
        aria-hidden
        className={cn(
          'mt-0.5 flex size-4 shrink-0 items-center justify-center rounded-full border',
          ativa ? 'border-tinta' : 'border-linha-forte',
        )}
      >
        {ativa ? <span className="size-2 rounded-full bg-tinta" /> : null}
      </span>
      <span className="min-w-0">
        <span className="block text-sm font-medium text-tinta">{titulo}</span>
        <span className="block text-xs leading-relaxed text-tinta-media">{descricao}</span>
      </span>
    </button>
  )
}
