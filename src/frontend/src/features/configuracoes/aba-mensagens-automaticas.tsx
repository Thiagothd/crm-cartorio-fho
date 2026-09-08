import { useEffect, useMemo, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
// MessageSquarePlus, e nao um icone de robo: a interface e livro de registro, nao
// painel de SaaS (DESIGN.md, secao 1). O icone diz "falta cadastrar mensagem", que
// e o que o estado vazio significa.
import { LayoutGrid, List, MessageSquarePlus, Plus, Search } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { AcoesDaPagina } from '@/components/layout/contexto-de-acoes-da-pagina'
import { EstadoVazio } from '@/components/layout/shell'
import { etapasApi, mensagensAutomaticasApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { normalizarTexto as normalizar } from './normalizar-texto'
import { usarPreferenciaDeTexto } from '@/lib/usar-preferencia-local'
import { cn } from '@/lib/utils'
import { MARCADORES } from '@/features/atendimento/placeholders'
import { FluxoDeMensagens } from './fluxo-de-mensagens'
import { ListaDeMensagensAutomaticas } from './lista-de-mensagens-automaticas'
import { ResumoDasMensagens } from './resumo-das-mensagens'
import {
  contarFluxo,
  explicacaoDoGatilho,
  GATILHOS,
  montarTrilhas,
} from './topologia-do-fluxo'
import type { Etapa, GatilhoDeMensagem, MensagemAutomatica } from '@/lib/api/tipos'

/**
 * Mesmo limite da coluna. É menor que os 4000 de uma mensagem porque o que se grava
 * aqui é o MODELO, e os marcadores crescem na substituição — {MenuSetores} em
 * especial.
 */
const LIMITE_DO_TEXTO = 1000

const VISOES = ['fluxo', 'lista'] as const
type Visao = (typeof VISOES)[number]

/** A escolha entre desenho e cadastro é de gosto de quem trabalha, e persiste. */
const CHAVE_DA_VISAO = 'crm-cartorio.mensagens.visao'

export function AbaMensagensAutomaticas() {
  const [emEdicao, setEmEdicao] = useState<MensagemAutomatica | null>(null)
  const [preenchimento, setPreenchimento] = useState<{
    gatilho: GatilhoDeMensagem
    etapaAlvoId: string | null
  } | null>(null)
  const [aberto, setAberto] = useState(false)

  const [visao, definirVisao] = usarPreferenciaDeTexto<Visao>(CHAVE_DA_VISAO, 'fluxo', VISOES)
  const [somentePendentes, setSomentePendentes] = useState(false)
  const [busca, setBusca] = useState('')

  const clienteDeConsultas = useQueryClient()

  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ['mensagens-automaticas-gestao'],
    queryFn: () => mensagensAutomaticasApi.listar(true),
  })

  const { data: etapas } = useQuery({
    queryKey: ['etapas-gestao'],
    queryFn: () => etapasApi.listar(true),
  })

  const mensagens = useMemo(() => data ?? [], [data])
  const trilhas = useMemo(() => montarTrilhas(etapas ?? []), [etapas])
  const contagem = useMemo(() => contarFluxo(trilhas, mensagens), [trilhas, mensagens])

  const filtradas = useMemo(() => {
    const alvo = normalizar(busca.trim())
    if (!alvo) return mensagens

    return mensagens.filter((m) =>
      [m.gatilhoDescrito, m.conteudo, m.etapaAlvoNome ?? ''].some((campo) =>
        normalizar(campo).includes(alvo),
      ),
    )
  }, [mensagens, busca])

  function recarregar() {
    void clienteDeConsultas.invalidateQueries({ queryKey: ['mensagens-automaticas-gestao'] })
  }

  function abrirNova() {
    setEmEdicao(null)
    setPreenchimento(null)
    setAberto(true)
  }

  /** Chamado pelo fluxo: edita, se o ponto já tem mensagem, ou abre em modo novo já com o gatilho certo. */
  function abrirPonto(
    gatilho: GatilhoDeMensagem,
    etapaAlvoId: string | null,
    existente: MensagemAutomatica | null,
  ) {
    setEmEdicao(existente)
    setPreenchimento(existente ? null : { gatilho, etapaAlvoId })
    setAberto(true)
  }

  return (
    <div className="flex flex-col gap-4">
      {/* A ação primária vai para o cabeçalho global, como em Equipe e
          Clientes — a tela não desenha o próprio cabeçalho desde a Fase 3. */}
      <AcoesDaPagina>
        <Button size="sm" onClick={abrirNova}>
          <Plus aria-hidden="true" />
          <span>
            Nova<span className="hidden lg:inline"> mensagem</span>
          </span>
        </Button>
      </AcoesDaPagina>

      <p className="max-w-3xl text-sm text-tinta-media">
        Mensagens que o sistema envia <strong className="text-tinta">sozinho</strong> quando um
        evento do atendimento acontece. Cada ponto do caminho abaixo é uma dessas mensagens —
        clique para escrever ou ajustar o texto.
      </p>

      {isLoading ? (
        <CarregandoMensagens />
      ) : isError ? (
        <EstadoDaInterface
          tipo="erro"
          titulo="Não foi possível carregar as mensagens automáticas"
          acao={
            <Button variant="outline" onClick={() => void refetch()}>
              Tentar de novo
            </Button>
          }
        />
      ) : (
        <>
          <ResumoDasMensagens contagem={contagem} />

          <div className="flex flex-col gap-2.5 @2xl:flex-row @2xl:items-center @2xl:justify-between">
            <div
              role="group"
              aria-label="Como ver as mensagens"
              className="flex items-center gap-1 self-start rounded-full border border-linha bg-superficie p-1"
            >
              <PilulaDeVisao
                ativa={visao === 'fluxo'}
                icone={LayoutGrid}
                rotulo="Caminho"
                aoClicar={() => definirVisao('fluxo')}
              />
              <PilulaDeVisao
                ativa={visao === 'lista'}
                icone={List}
                rotulo="Cadastradas"
                aoClicar={() => definirVisao('lista')}
              />
            </div>

            {visao === 'fluxo' ? (
              // Com 15 gatilhos fixos mais uma linha por etapa do funil, o
              // caminho passa de 20 pontos. "Me mostra só o que falta" é a
              // pergunta que quem abre esta tela faz — e sem isso a resposta
              // exige varrer a tela inteira com o olho.
              <div className="flex items-center gap-2.5 self-start @2xl:self-auto">
                <Label htmlFor="somente-pendentes" className="text-xs font-medium text-tinta-media">
                  Só o que falta escrever
                </Label>
                <Switch
                  id="somente-pendentes"
                  tom="tinta"
                  checked={somentePendentes}
                  onCheckedChange={setSomentePendentes}
                />
              </div>
            ) : (
              <div className="relative w-full @2xl:max-w-xs">
                <Search
                  aria-hidden
                  className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-tinta-fraca"
                />
                <Input
                  value={busca}
                  onChange={(e) => setBusca(e.target.value)}
                  placeholder="Gatilho, etapa ou texto"
                  aria-label="Buscar nas mensagens cadastradas"
                  className="w-full rounded-full bg-superficie pl-9 focus-visible:bg-branco"
                />
              </div>
            )}
          </div>

          {visao === 'fluxo' ? (
            <FluxoDeMensagens
              trilhas={trilhas}
              mensagens={mensagens}
              somentePendentes={somentePendentes}
              aoClicarPonto={abrirPonto}
            />
          ) : mensagens.length === 0 ? (
            /* Diferente das abas de setor e etapa, esta pode legitimamente estar vazia:
               nenhuma mensagem automática é obrigatória. Sem este estado, a tela seria
               uma grade vazia e ninguém saberia se faltou cadastro ou se falhou o
               carregamento. */
            <EstadoVazio
              icone={MessageSquarePlus}
              titulo="Nenhuma mensagem automática"
              descricao="Sem regra cadastrada, o sistema não envia nada sozinho — nenhum evento do atendimento dispara mensagem ao cliente."
              acao={
                <Button size="sm" onClick={abrirNova}>
                  <Plus />
                  Cadastrar a primeira
                </Button>
              }
            />
          ) : filtradas.length === 0 ? (
            <EstadoDaInterface
              tipo="sem-resultados"
              titulo="Nenhuma mensagem com esse termo"
              acao={
                <Button variant="outline" onClick={() => setBusca('')}>
                  Limpar busca
                </Button>
              }
            />
          ) : (
            <ListaDeMensagensAutomaticas
              mensagens={filtradas}
              aoEditar={(mensagem) => {
                setEmEdicao(mensagem)
                setPreenchimento(null)
                setAberto(true)
              }}
            />
          )}
        </>
      )}

      <DialogoDeMensagemAutomatica
        mensagem={emEdicao}
        preenchimento={preenchimento}
        etapas={etapas ?? []}
        aberto={aberto}
        aoFechar={() => setAberto(false)}
        aoSalvar={recarregar}
      />
    </div>
  )
}

function PilulaDeVisao({
  ativa,
  icone: Icone,
  rotulo,
  aoClicar,
}: {
  ativa: boolean
  icone: LucideIcon
  rotulo: string
  aoClicar: () => void
}) {
  return (
    <button
      type="button"
      aria-pressed={ativa}
      onClick={aoClicar}
      className={cn(
        'flex min-h-9 items-center gap-1.5 rounded-full px-3 text-xs font-semibold transition-all duration-200',
        ativa ? 'bg-tinta text-branco shadow-card' : 'text-tinta-media hover:bg-branco hover:text-tinta',
      )}
    >
      <Icone className="size-3.5 shrink-0" aria-hidden />
      {rotulo}
    </button>
  )
}

/** O esqueleto imita a forma final: faixa de números, barra e grade de cartões. */
function CarregandoMensagens() {
  return (
    <div className="flex flex-col gap-4">
      <div className="grid grid-cols-2 gap-3 @3xl:grid-cols-4">
        {[0, 1, 2, 3].map((i) => (
          <Skeleton key={i} className="h-[104px] rounded-card" />
        ))}
      </div>
      <Skeleton className="h-11 w-64 rounded-full" />
      <div className="grid gap-3 @4xl:grid-cols-2">
        {[0, 1, 2, 3].map((i) => (
          <Skeleton key={i} className="h-52 rounded-card" />
        ))}
      </div>
    </div>
  )
}

function DialogoDeMensagemAutomatica({
  mensagem,
  preenchimento,
  etapas,
  aberto,
  aoFechar,
  aoSalvar,
}: {
  mensagem: MensagemAutomatica | null
  /** Gatilho (e etapa, se for o caso) já escolhidos ao abrir em modo "nova" a partir do fluxo. */
  preenchimento: { gatilho: GatilhoDeMensagem; etapaAlvoId: string | null } | null
  etapas: Etapa[]
  aberto: boolean
  aoFechar: () => void
  aoSalvar: () => void
}) {
  const [gatilho, setGatilho] = useState<GatilhoDeMensagem>('NovoAtendimento')
  const [etapaAlvoId, setEtapaAlvoId] = useState('')
  const [conteudo, setConteudo] = useState('')
  const [ativo, setAtivo] = useState(true)
  const [salvando, setSalvando] = useState(false)
  const campo = useRef<HTMLTextAreaElement>(null)

  const exigeEtapa = gatilho === 'EtapaAlterada'
  const explicacao = explicacaoDoGatilho(gatilho)

  useEffect(() => {
    if (!aberto) return

    setGatilho(mensagem?.gatilho ?? preenchimento?.gatilho ?? 'NovoAtendimento')
    setEtapaAlvoId(mensagem?.etapaAlvoId ?? preenchimento?.etapaAlvoId ?? '')
    setConteudo(mensagem?.conteudo ?? '')
    setAtivo(mensagem?.ativo ?? true)
  }, [aberto, mensagem, preenchimento])

  /**
   * Insere o marcador na posição do cursor.
   *
   * Existe porque o backend RECUSA marcador desconhecido: um "{NomeClinte}" digitado
   * errado sairia literal na mensagem que o cliente do cartório lê. Clicar na lista
   * é mais rápido que digitar e não erra.
   */
  function inserirMarcador(token: string) {
    const elemento = campo.current

    if (!elemento) {
      setConteudo((atual) => atual + token)
      return
    }

    const inicio = elemento.selectionStart
    const fim = elemento.selectionEnd

    setConteudo((atual) => atual.slice(0, inicio) + token + atual.slice(fim))

    requestAnimationFrame(() => {
      elemento.focus()
      elemento.setSelectionRange(inicio + token.length, inicio + token.length)
    })
  }

  async function salvar() {
    setSalvando(true)

    try {
      const dados = {
        gatilho,
        etapaAlvoId: exigeEtapa ? etapaAlvoId || null : null,
        conteudo,
        ativo,
      }

      if (mensagem) {
        await mensagensAutomaticasApi.atualizar(mensagem.id, dados)
      } else {
        await mensagensAutomaticasApi.criar(dados)
      }

      toast.success(mensagem ? 'Mensagem atualizada.' : 'Mensagem criada.')
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
      <DialogContent className="max-w-xl">
        <DialogHeader>
          <DialogTitle>{mensagem ? 'Editar mensagem' : 'Nova mensagem automática'}</DialogTitle>
          <DialogDescription>
            Desativar interrompe os disparos daqui para a frente. As mensagens que esta regra já
            colocou nas conversas continuam no histórico — mensagem nunca é apagada.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-4">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="mensagem-gatilho">Quando dispara</Label>
            <Select value={gatilho} onValueChange={(v) => setGatilho(v as GatilhoDeMensagem)}>
              <SelectTrigger id="mensagem-gatilho">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {GATILHOS.map((opcao) => (
                  <SelectItem key={opcao.valor} value={opcao.valor}>
                    {opcao.rotulo}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            {explicacao ? (
              <p className="rounded-md border border-linha bg-superficie px-3 py-2 text-xs leading-relaxed text-tinta-media">
                {explicacao}
              </p>
            ) : null}
          </div>

          {/* Só aparece no gatilho que usa: perguntar a etapa em "prazo vencido"
              seria pedir um dado que a regra ignora. */}
          {exigeEtapa ? (
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="mensagem-etapa">Etapa que dispara</Label>
              <Select value={etapaAlvoId} onValueChange={setEtapaAlvoId}>
                <SelectTrigger id="mensagem-etapa">
                  <SelectValue placeholder="Escolha a etapa" />
                </SelectTrigger>
                <SelectContent>
                  {etapas.map((etapa) => (
                    <SelectItem key={etapa.id} value={etapa.id}>
                      {etapa.nome}
                      {etapa.ehFinal ? ' (conclusão)' : ''}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          ) : null}

          <div className="flex flex-col gap-1.5">
            <div className="flex items-baseline justify-between gap-2">
              <Label htmlFor="mensagem-conteudo">Texto da mensagem</Label>
              {/* Só aparece perto do limite. Sem isso o campo simplesmente para de
                  aceitar texto aos 1000 caracteres, sem dizer por quê. */}
              {conteudo.length > LIMITE_DO_TEXTO * 0.8 ? (
                <span className="font-dado text-[11px] text-tinta-fraca">
                  {conteudo.length}/{LIMITE_DO_TEXTO}
                </span>
              ) : null}
            </div>
            <Textarea
              id="mensagem-conteudo"
              ref={campo}
              value={conteudo}
              onChange={(e) => setConteudo(e.target.value)}
              rows={7}
              maxLength={LIMITE_DO_TEXTO}
              className="resize-none"
              placeholder="Ex.: Olá, {NomeCliente}! Aqui é o {NomeCartorio}."
            />

            <div className="flex flex-col gap-1.5 rounded-md border border-linha bg-superficie px-3 py-2">
              <p className="text-xs text-tinta-media">
                Clique para inserir. Marcador escrito à mão e com erro é recusado ao salvar.
              </p>
              <div className="flex flex-wrap gap-1.5">
                {MARCADORES.map((marcador) => (
                  <button
                    key={marcador.token}
                    type="button"
                    onClick={() => inserirMarcador(marcador.token)}
                    title={marcador.descricao}
                    className="rounded-md border border-linha-forte bg-branco px-1.5 py-0.5 font-dado text-[11px] text-tinta-media transition-colors hover:border-tinta hover:text-tinta"
                  >
                    {marcador.token}
                  </button>
                ))}
              </div>
            </div>
          </div>

          <div className="flex items-center justify-between rounded-md border border-linha px-3 py-2">
            <div>
              <Label htmlFor="mensagem-ativa">Regra ativa</Label>
              <p className="text-xs text-tinta-media">Desativada, o evento acontece e nada é enviado.</p>
            </div>
            <Switch id="mensagem-ativa" checked={ativo} onCheckedChange={setAtivo} />
          </div>
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
