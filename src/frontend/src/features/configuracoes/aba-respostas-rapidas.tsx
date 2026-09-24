import { useEffect, useMemo, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Braces, Pencil, PauseCircle, Plus, Search, Send, Zap } from 'lucide-react'
import { toast } from 'sonner'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
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
import { CartaoDeIndicador } from '@/features/indicadores/cartao-de-indicador'
import { respostasRapidasApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { cn } from '@/lib/utils'
import { MARCADORES } from '@/features/atendimento/placeholders'
import { normalizarTexto as normalizar } from './normalizar-texto'
import type { RespostaRapida } from '@/lib/api/tipos'

/** Mesmo limite da coluna, igual ao da mensagem automática. */
const LIMITE_DO_TEXTO = 1000

/** Todo marcador tem a forma `{Nome}` — o backend recusa qualquer outra. */
const MARCADOR = /\{[A-Za-zÀ-ÿ]+\}/g

type Filtro = 'todas' | 'ativas' | 'desativadas'

const PILULAS: { chave: Filtro; rotulo: string }[] = [
  { chave: 'todas', rotulo: 'Todas' },
  { chave: 'ativas', rotulo: 'Ativas' },
  { chave: 'desativadas', rotulo: 'Desativadas' },
]

function marcadoresUsados(conteudo: string): string[] {
  return [...new Set(conteudo.match(MARCADOR) ?? [])]
}

/**
 * Respostas rápidas — o texto pronto que o ATENDENTE insere com "/", nunca o
 * que o sistema envia sozinho. É a fronteira que a DESIGN.md 10.6 protege, e
 * ela precisa ficar legível na tela: por isso a frase de abertura repete que
 * nada sai daqui sem alguém ler antes.
 *
 * Reescrita na Fase 3.3 (2026-09-07): a tabela virou grade de cartões, pelo
 * mesmo motivo da Equipe — o dado principal é um parágrafo, e parágrafo dentro
 * de célula ou vira rolagem horizontal ou vira uma linha cortada. O atalho
 * ganhou destaque próprio, porque é ele que a pessoa procura com o olho quando
 * está com o cliente esperando do outro lado.
 */
export function AbaRespostasRapidas() {
  const [emEdicao, setEmEdicao] = useState<RespostaRapida | null>(null)
  const [aberto, setAberto] = useState(false)
  const [busca, setBusca] = useState('')
  const [filtro, setFiltro] = useState<Filtro>('todas')

  const clienteDeConsultas = useQueryClient()

  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ['respostas-rapidas-gestao'],
    queryFn: () => respostasRapidasApi.listar(true),
  })

  const respostas = useMemo(() => data ?? [], [data])

  const contagem = useMemo(
    () => ({
      total: respostas.length,
      ativas: respostas.filter((r) => r.ativo).length,
      desativadas: respostas.filter((r) => !r.ativo).length,
      comMarcador: respostas.filter((r) => marcadoresUsados(r.conteudo).length > 0).length,
    }),
    [respostas],
  )

  const filtradas = useMemo(() => {
    const base =
      filtro === 'ativas'
        ? respostas.filter((r) => r.ativo)
        : filtro === 'desativadas'
          ? respostas.filter((r) => !r.ativo)
          : respostas

    const alvo = normalizar(busca.trim().replace(/^\//, ''))
    if (!alvo) return base

    return base.filter((r) =>
      [r.atalho, r.titulo, r.conteudo].some((campo) => normalizar(campo).includes(alvo)),
    )
  }, [respostas, filtro, busca])

  function recarregar() {
    void clienteDeConsultas.invalidateQueries({ queryKey: ['respostas-rapidas-gestao'] })
    // O seletor "/" da conversa lê a mesma lista, só que apenas as ativas.
    void clienteDeConsultas.invalidateQueries({ queryKey: ['respostas-rapidas'] })
  }

  function abrirNova() {
    setEmEdicao(null)
    setAberto(true)
  }

  return (
    <div className="flex flex-col gap-4">
      <AcoesDaPagina>
        <Button size="sm" onClick={abrirNova}>
          <Plus aria-hidden="true" />
          <span>
            Nova<span className="hidden lg:inline"> resposta</span>
          </span>
        </Button>
      </AcoesDaPagina>

      <p className="max-w-3xl text-sm text-tinta-media">
        Textos prontos para o dia a dia. Na conversa, o atendente digita{' '}
        <span className="font-dado">/</span> seguido do atalho, escolhe e o texto entra no campo.
        Ele <strong className="text-tinta">lê e edita antes de enviar</strong> — nada sai daqui
        sozinho.
      </p>

      {isLoading ? (
        <CarregandoRespostas />
      ) : isError ? (
        <EstadoDaInterface
          tipo="erro"
          titulo="Não foi possível carregar as respostas rápidas"
          acao={
            <Button variant="outline" onClick={() => void refetch()}>
              Tentar de novo
            </Button>
          }
        />
      ) : respostas.length === 0 ? (
        <EstadoVazio
          icone={Zap}
          titulo="Nenhuma resposta rápida"
          descricao="Sem cadastro, a barra não abre nada na conversa. Comece pelo que a equipe mais repete: horário, prazo, documentos."
          acao={
            <Button size="sm" onClick={abrirNova}>
              <Plus />
              Cadastrar a primeira
            </Button>
          }
        />
      ) : (
        <>
          <div className="grade-com-vida grid grid-cols-2 gap-3 @3xl:grid-cols-4">
            <CartaoDeIndicador
              rotulo="Cadastradas"
              valor={String(contagem.total)}
              detalhe="Textos prontos no acervo"
              icone={Zap}
            />
            <CartaoDeIndicador
              rotulo="No seletor"
              valor={String(contagem.ativas)}
              detalhe="Aparecem ao digitar /"
              icone={Send}
              cor="verde"
            />
            <CartaoDeIndicador
              rotulo="Desativadas"
              valor={String(contagem.desativadas)}
              detalhe={
                contagem.desativadas > 0 ? 'Guardadas, fora do seletor' : 'Nenhuma fora do ar'
              }
              icone={PauseCircle}
            />
            <CartaoDeIndicador
              rotulo="Com marcador"
              valor={String(contagem.comMarcador)}
              detalhe="Já entram com nome e protocolo"
              icone={Braces}
            />
          </div>

          <div className="flex flex-col gap-2.5 @2xl:flex-row @2xl:items-center @2xl:justify-between">
            <div className="relative w-full @2xl:max-w-xs">
              <Search
                aria-hidden
                className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-tinta-fraca"
              />
              <Input
                value={busca}
                onChange={(e) => setBusca(e.target.value)}
                placeholder="Atalho, título ou texto"
                aria-label="Buscar nas respostas rápidas"
                className="w-full rounded-full bg-superficie pl-9 focus-visible:bg-branco"
              />
            </div>

            <div
              role="group"
              aria-label="Filtro"
              className="flex items-center gap-1 self-start rounded-full border border-linha bg-superficie p-1 @2xl:self-auto"
            >
              {PILULAS.map((p) => (
                <button
                  key={p.chave}
                  type="button"
                  aria-pressed={filtro === p.chave}
                  onClick={() => setFiltro(p.chave)}
                  className={cn(
                    'min-h-9 rounded-full px-3 text-xs font-semibold transition-all duration-200',
                    filtro === p.chave
                      ? 'bg-selecao text-sobre-selecao shadow-card'
                      : 'text-tinta-media hover:bg-branco hover:text-tinta',
                  )}
                >
                  {p.rotulo}
                </button>
              ))}
            </div>
          </div>

          {filtradas.length === 0 ? (
            <EstadoDaInterface
              tipo="sem-resultados"
              titulo={busca ? 'Nenhuma resposta com esse termo' : 'Nenhuma resposta neste filtro'}
              acao={
                busca ? (
                  <Button variant="outline" onClick={() => setBusca('')}>
                    Limpar busca
                  </Button>
                ) : undefined
              }
            />
          ) : (
            <div className="grade-com-vida grid gap-2.5 @3xl:grid-cols-2 @6xl:grid-cols-3">
              {filtradas.map((resposta) => (
                <CartaoDeResposta
                  key={resposta.id}
                  resposta={resposta}
                  aoEditar={() => {
                    setEmEdicao(resposta)
                    setAberto(true)
                  }}
                />
              ))}
            </div>
          )}
        </>
      )}

      <DialogoDeRespostaRapida
        resposta={emEdicao}
        aberto={aberto}
        aoFechar={() => setAberto(false)}
        aoSalvar={recarregar}
      />
    </div>
  )
}

function CartaoDeResposta({
  resposta,
  aoEditar,
}: {
  resposta: RespostaRapida
  aoEditar: () => void
}) {
  const marcadores = marcadoresUsados(resposta.conteudo)

  return (
    <button
      type="button"
      onClick={aoEditar}
      className={cn(
        'group flex h-full flex-col gap-2 rounded-card border border-linha bg-branco p-3.5 text-left shadow-card transition-all duration-200 hover:-translate-y-0.5 hover:shadow-card-hover',
        !resposta.ativo && 'opacity-70',
      )}
    >
      <div className="flex items-start gap-2">
        {/* Mono e em destaque: é um literal que o atendente digita caractere a
            caractere no meio do atendimento, igual ao protocolo (DESIGN.md 3).
            Era coluna de tabela que sumia no celular; agora é a âncora do
            cartão em qualquer largura. */}
        <span className="shrink-0 rounded-md border border-linha bg-superficie px-1.5 py-0.5 font-dado text-[11px] font-medium text-tinta">
          /{resposta.atalho}
        </span>

        <span className="min-w-0 flex-1 text-[13px] font-semibold leading-tight text-tinta">
          {resposta.titulo}
        </span>

        {!resposta.ativo ? <Badge variant="secondary">Desativada</Badge> : null}

        <Pencil
          aria-hidden
          className="mt-px size-3.5 shrink-0 text-tinta-fraca opacity-0 transition-opacity duration-150 group-hover:opacity-100 group-focus-visible:opacity-100"
        />
      </div>

      <p className="line-clamp-4 whitespace-pre-wrap border-t border-linha pt-2 text-[11px] leading-relaxed text-tinta-media">
        {resposta.conteudo}
      </p>

      {marcadores.length > 0 ? (
        <div className="mt-auto flex flex-wrap gap-1">
          {marcadores.map((marcador) => (
            <span
              key={marcador}
              className="rounded-md border border-linha bg-superficie px-1.5 py-0.5 font-dado text-[10px] text-tinta-media"
            >
              {marcador}
            </span>
          ))}
        </div>
      ) : null}
    </button>
  )
}

/** O esqueleto imita a forma final: faixa de números, barra e grade de cartões. */
function CarregandoRespostas() {
  return (
    <div className="flex flex-col gap-4">
      <div className="grid grid-cols-2 gap-3 @3xl:grid-cols-4">
        {[0, 1, 2, 3].map((i) => (
          <Skeleton key={i} className="h-[104px] rounded-card" />
        ))}
      </div>
      <Skeleton className="h-11 w-72 rounded-full" />
      <div className="grid gap-2.5 @3xl:grid-cols-2 @6xl:grid-cols-3">
        {[0, 1, 2, 3, 4, 5].map((i) => (
          <Skeleton key={i} className="h-40 rounded-card" />
        ))}
      </div>
    </div>
  )
}

function DialogoDeRespostaRapida({
  resposta,
  aberto,
  aoFechar,
  aoSalvar,
}: {
  resposta: RespostaRapida | null
  aberto: boolean
  aoFechar: () => void
  aoSalvar: () => void
}) {
  const [atalho, setAtalho] = useState('')
  const [titulo, setTitulo] = useState('')
  const [conteudo, setConteudo] = useState('')
  const [ativo, setAtivo] = useState(true)
  const [salvando, setSalvando] = useState(false)
  const campo = useRef<HTMLTextAreaElement>(null)

  useEffect(() => {
    if (!aberto) return

    setAtalho(resposta?.atalho ?? '')
    setTitulo(resposta?.titulo ?? '')
    setConteudo(resposta?.conteudo ?? '')
    setAtivo(resposta?.ativo ?? true)
  }, [aberto, resposta])

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
      const dados = { atalho, titulo, conteudo, ativo }

      if (resposta) {
        await respostasRapidasApi.atualizar(resposta.id, dados)
      } else {
        await respostasRapidasApi.criar(dados)
      }

      toast.success(resposta ? 'Resposta atualizada.' : 'Resposta criada.')
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
          <DialogTitle>{resposta ? 'Editar resposta' : 'Nova resposta rápida'}</DialogTitle>
          <DialogDescription>
            O atalho é o que se digita depois da barra. Sem espaço e sem acento — ele é digitado no
            meio do atendimento, com pressa.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-4">
          <div className="flex gap-3">
            <div className="flex w-40 flex-col gap-1.5">
              <Label htmlFor="resposta-atalho">Atalho</Label>
              <div className="flex items-center gap-1">
                <span className="font-dado text-sm text-tinta-fraca">/</span>
                <Input
                  id="resposta-atalho"
                  value={atalho}
                  onChange={(e) => setAtalho(e.target.value)}
                  placeholder="prazo"
                  maxLength={30}
                  className="font-dado"
                  autoComplete="off"
                  spellCheck={false}
                />
              </div>
            </div>

            <div className="flex flex-1 flex-col gap-1.5">
              <Label htmlFor="resposta-titulo">Título</Label>
              <Input
                id="resposta-titulo"
                value={titulo}
                onChange={(e) => setTitulo(e.target.value)}
                placeholder="Ex.: Situação do protocolo"
                maxLength={120}
              />
              <p className="text-xs text-tinta-media">Aparece na lista do seletor, ao lado do atalho.</p>
            </div>
          </div>

          <div className="flex flex-col gap-1.5">
            <div className="flex items-baseline justify-between gap-2">
              <Label htmlFor="resposta-conteudo">Texto inserido</Label>
              {conteudo.length > LIMITE_DO_TEXTO * 0.8 ? (
                <span className="font-dado text-[11px] text-tinta-fraca">
                  {conteudo.length}/{LIMITE_DO_TEXTO}
                </span>
              ) : null}
            </div>
            <Textarea
              id="resposta-conteudo"
              ref={campo}
              value={conteudo}
              onChange={(e) => setConteudo(e.target.value)}
              rows={6}
              maxLength={LIMITE_DO_TEXTO}
              className="resize-none"
            />

            <div className="flex flex-col gap-1.5 rounded-md border border-linha bg-superficie px-3 py-2">
              <p className="text-xs text-tinta-media">
                Clique para inserir. Os marcadores já vêm resolvidos no campo do atendente.
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
              <Label htmlFor="resposta-ativa">Resposta ativa</Label>
              <p className="text-xs text-tinta-media">Desativada, some do seletor sem sair do cadastro.</p>
            </div>
            <Switch id="resposta-ativa" checked={ativo} onCheckedChange={setAtivo} />
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
