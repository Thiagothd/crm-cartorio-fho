import { useEffect, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { BookOpen, Loader2, Plus } from 'lucide-react'
import { toast } from 'sonner'
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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { AcoesDaPagina } from '@/components/layout/contexto-de-acoes-da-pagina'
import { conhecimentoDeIaApi, servicosApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { ResumoDoConhecimento } from './resumo-do-conhecimento'
import { ListaDeConhecimento } from './lista-de-conhecimento'
import { descreverVigencia } from './vigencia-de-conhecimento'
import type { ConhecimentoDeIa, VersaoDeConhecimentoDeIa } from '@/lib/api/tipos'

/** Mesmo limite da coluna (ConhecimentoDeIaConfiguration.cs). */
const LIMITE_DO_CONTEUDO = 2000

/** Valor do Select para "sem serviço vinculado" — Radix não aceita item com value vazio. */
const SEM_SERVICO = '_nenhum'

/**
 * O que a IA sabe — Fase 3.1 (DESIGN.md 17.5). A moldura (rolagem, teto de
 * largura e `@container`) é da `PaginaIa`, que as duas seções dividem.
 */
export function AbaConhecimentoDeIa() {
  const [emEdicao, setEmEdicao] = useState<ConhecimentoDeIa | null>(null)
  const [aberto, setAberto] = useState(false)
  const [emHistorico, setEmHistorico] = useState<ConhecimentoDeIa | null>(null)

  const clienteDeConsultas = useQueryClient()

  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ['conhecimento-de-ia-gestao'],
    queryFn: () => conhecimentoDeIaApi.listar(true),
  })

  function recarregar() {
    void clienteDeConsultas.invalidateQueries({ queryKey: ['conhecimento-de-ia-gestao'] })
  }

  function abrirNovo() {
    setEmEdicao(null)
    setAberto(true)
  }

  return (
    <div className="flex flex-col gap-4">
      <AcoesDaPagina>
        <Button size="sm" onClick={abrirNovo}>
          <Plus aria-hidden="true" />
          <span>
            Novo<span className="hidden lg:inline"> Conhecimento</span>
          </span>
        </Button>
      </AcoesDaPagina>

      <p className="max-w-2xl text-sm text-tinta-media">
        Conteúdo que a IA pode usar para responder — por serviço ou por tema livre. Toda edição
        preserva a versão anterior, com data e responsável. Diferente das{' '}
        <strong className="text-tinta">respostas rápidas</strong> (atalho que o atendente digita),
        este cadastro é lido direto pela IA.
      </p>

      {isLoading ? (
        <div className="flex flex-col gap-4">
          <div className="grid grid-cols-2 gap-3 @3xl:grid-cols-4">
            {[0, 1, 2, 3].map((i) => (
              <Skeleton key={i} className="h-[92px] rounded-card" />
            ))}
          </div>
          <div className="grid gap-2.5 @3xl:grid-cols-2 @6xl:grid-cols-3">
            {[0, 1, 2, 3].map((i) => (
              <Skeleton key={i} className="h-[168px] rounded-card" />
            ))}
          </div>
        </div>
      ) : isError ? (
        <EstadoDaInterface
          tipo="erro"
          titulo="Não foi possível carregar o conhecimento"
          acao={
            <Button variant="outline" onClick={() => void refetch()}>
              Tentar de novo
            </Button>
          }
        />
      ) : data?.length ? (
        <>
          <ResumoDoConhecimento conhecimentos={data} />
          <ListaDeConhecimento
            conhecimentos={data}
            aoEditar={(conhecimento) => {
              setEmEdicao(conhecimento)
              setAberto(true)
            }}
            aoVerHistorico={setEmHistorico}
          />
        </>
      ) : (
        <EstadoDaInterface
          tipo="vazio"
          icone={BookOpen}
          titulo="Nenhum conhecimento cadastrado"
          descricao="Sem cadastro, a IA responde só com o catálogo de serviços. Comece por uma política ou exceção que a equipe explica com frequência."
          acao={
            <Button size="sm" onClick={abrirNovo}>
              <Plus />
              Cadastrar o primeiro
            </Button>
          }
        />
      )}

      <DialogoDeConhecimentoDeIa
        conhecimento={emEdicao}
        aberto={aberto}
        aoFechar={() => setAberto(false)}
        aoSalvar={recarregar}
      />

      <DialogoDeHistorico conhecimento={emHistorico} aoFechar={() => setEmHistorico(null)} />
    </div>
  )
}

function DialogoDeConhecimentoDeIa({
  conhecimento,
  aberto,
  aoFechar,
  aoSalvar,
}: {
  conhecimento: ConhecimentoDeIa | null
  aberto: boolean
  aoFechar: () => void
  aoSalvar: () => void
}) {
  const [titulo, setTitulo] = useState('')
  const [conteudo, setConteudo] = useState('')
  const [tema, setTema] = useState('')
  const [tipoServicoId, setTipoServicoId] = useState(SEM_SERVICO)
  const [vigenteDesde, setVigenteDesde] = useState<string | null>(null)
  const [vigenteAte, setVigenteAte] = useState<string | null>(null)
  const [ativo, setAtivo] = useState(true)
  const [salvando, setSalvando] = useState(false)
  const [erro, setErro] = useState<string | null>(null)

  const { data: servicos } = useQuery({
    queryKey: ['servicos-gestao'],
    queryFn: () => servicosApi.listar(true),
    enabled: aberto,
  })

  useEffect(() => {
    if (!aberto) return

    setTitulo(conhecimento?.titulo ?? '')
    setConteudo(conhecimento?.conteudo ?? '')
    setTema(conhecimento?.tema ?? '')
    setTipoServicoId(conhecimento?.tipoServicoId ?? SEM_SERVICO)
    setVigenteDesde(conhecimento?.vigenteDesde ?? null)
    setVigenteAte(conhecimento?.vigenteAte ?? null)
    setAtivo(conhecimento?.ativo ?? true)
    setErro(null)
  }, [aberto, conhecimento])

  async function salvar() {
    // DESIGN.md 11.4/11.5 — a recusa cai no diálogo, não num toast que some
    // antes de a pessoa ler o que faltou.
    if (!titulo.trim() || !tema.trim() || !conteudo.trim()) {
      setErro('Título, tema e conteúdo são obrigatórios.')
      return
    }

    if (vigenteDesde && vigenteAte && vigenteDesde > vigenteAte) {
      setErro('A data inicial da vigência não pode ser depois da final.')
      return
    }

    setSalvando(true)
    setErro(null)

    try {
      const dados = {
        titulo: titulo.trim(),
        conteudo: conteudo.trim(),
        tema: tema.trim(),
        tipoServicoId: tipoServicoId === SEM_SERVICO ? null : tipoServicoId,
        vigenteDesde,
        vigenteAte,
        ativo,
      }

      if (conhecimento) {
        await conhecimentoDeIaApi.atualizar(conhecimento.id, dados)
      } else {
        await conhecimentoDeIaApi.criar(dados)
      }

      toast.success(conhecimento ? 'Conhecimento atualizado.' : 'Conhecimento criado.')
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
        className="max-w-xl"
        mostrarFechar={!salvando}
        onEscapeKeyDown={(e) => { if (salvando) e.preventDefault() }}
        onInteractOutside={(e) => { if (salvando) e.preventDefault() }}
      >
        <DialogHeader>
          <DialogTitle>{conhecimento ? 'Editar conhecimento' : 'Novo conhecimento'}</DialogTitle>
          <DialogDescription>
            {conhecimento
              ? `Salvar preserva a versão ${conhecimento.versao} no histórico antes de aplicar a mudança.`
              : 'Só o texto cadastrado aqui entra na resposta da IA — nada é inventado.'}
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-4">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="conhecimento-titulo">Título</Label>
            <Input
              id="conhecimento-titulo"
              value={titulo}
              onChange={(e) => {
                setTitulo(e.target.value)
                if (erro) setErro(null)
              }}
              placeholder="Ex.: Política de reagendamento"
              maxLength={160}
              aria-invalid={erro !== null && !titulo.trim()}
            />
          </div>

          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="conhecimento-tema">Tema</Label>
              <Input
                id="conhecimento-tema"
                value={tema}
                onChange={(e) => {
                  setTema(e.target.value)
                  if (erro) setErro(null)
                }}
                placeholder="Ex.: Atendimento"
                maxLength={80}
                aria-invalid={erro !== null && !tema.trim()}
              />
            </div>

            <div className="flex flex-col gap-1.5">
              <Label>Serviço (opcional)</Label>
              <Select value={tipoServicoId} onValueChange={setTipoServicoId}>
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={SEM_SERVICO}>Nenhum</SelectItem>
                  {servicos?.map((s) => (
                    <SelectItem key={s.id} value={s.id}>
                      {s.nome}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>

          <div className="flex flex-col gap-1.5">
            <div className="flex items-baseline justify-between gap-2">
              <Label htmlFor="conhecimento-conteudo">Conteúdo</Label>
              {conteudo.length > LIMITE_DO_CONTEUDO * 0.8 ? (
                <span className="font-dado text-[11px] text-tinta-fraca">
                  {conteudo.length}/{LIMITE_DO_CONTEUDO}
                </span>
              ) : null}
            </div>
            <Textarea
              id="conhecimento-conteudo"
              value={conteudo}
              onChange={(e) => {
                setConteudo(e.target.value)
                if (erro) setErro(null)
              }}
              rows={6}
              maxLength={LIMITE_DO_CONTEUDO}
              className="resize-none"
              aria-invalid={erro !== null && !conteudo.trim()}
            />
          </div>

          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="conhecimento-vigente-desde">Vigente desde (opcional)</Label>
              <Input
                id="conhecimento-vigente-desde"
                type="date"
                value={vigenteDesde ?? ''}
                onChange={(e) => setVigenteDesde(e.target.value || null)}
              />
            </div>

            <div className="flex flex-col gap-1.5">
              <Label htmlFor="conhecimento-vigente-ate">Vigente até (opcional)</Label>
              <Input
                id="conhecimento-vigente-ate"
                type="date"
                value={vigenteAte ?? ''}
                onChange={(e) => setVigenteAte(e.target.value || null)}
              />
            </div>
          </div>
          <p className="-mt-2 text-xs text-tinta-media">
            Deixe em branco para valer desde sempre / para sempre. Fora da janela, a IA para de ver
            este conteúdo sem precisar desativar.
          </p>

          <div className="flex items-center justify-between rounded-md border border-linha px-3 py-2">
            <div>
              <Label htmlFor="conhecimento-ativo">Conhecimento ativo</Label>
              <p className="text-xs text-tinta-media">Desativado, some da resposta da IA.</p>
            </div>
            <Switch id="conhecimento-ativo" checked={ativo} onCheckedChange={setAtivo} />
          </div>

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

function DialogoDeHistorico({
  conhecimento,
  aoFechar,
}: {
  conhecimento: ConhecimentoDeIa | null
  aoFechar: () => void
}) {
  const { data, isLoading } = useQuery({
    queryKey: ['conhecimento-de-ia-historico', conhecimento?.id],
    queryFn: () => conhecimentoDeIaApi.historico(conhecimento!.id),
    enabled: conhecimento !== null,
  })

  return (
    <Dialog open={conhecimento !== null} onOpenChange={(v) => !v && aoFechar()}>
      <DialogContent className="max-w-xl">
        <DialogHeader>
          <DialogTitle>Histórico — {conhecimento?.titulo}</DialogTitle>
          <DialogDescription>
            Versão {conhecimento?.versao} é a atual. As demais são só leitura.
          </DialogDescription>
        </DialogHeader>

        <div className="flex max-h-[60vh] flex-col gap-3 overflow-y-auto">
          {isLoading ? (
            <>
              <Skeleton className="h-16 w-full" />
              <Skeleton className="h-16 w-full" />
            </>
          ) : data?.length ? (
            data.map((versao: VersaoDeConhecimentoDeIa) => (
              <div key={versao.versao} className="rounded-card border border-linha p-3">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span className="font-dado text-xs text-tinta-media">v{versao.versao}</span>
                  <span className="text-xs text-tinta-fraca">
                    {versao.alteradoPorNome ?? 'Sistema'} —{' '}
                    {new Date(versao.ocorridoEm).toLocaleString('pt-BR')}
                  </span>
                </div>
                <p className="mt-1.5 font-medium">{versao.titulo}</p>
                <p className="mt-1 whitespace-pre-wrap text-sm text-tinta-media">{versao.conteudo}</p>
                <p className="mt-1.5 text-[11px] text-tinta-fraca">
                  {versao.tema}
                  {versao.tipoServicoNome ? ` · ${versao.tipoServicoNome}` : ''}
                  {' — '}
                  {descreverVigencia(versao.vigenteDesde, versao.vigenteAte)}
                  {!versao.ativo ? ' — estava inativo' : ''}
                </p>
              </div>
            ))
          ) : (
            <p className="py-6 text-center text-sm text-tinta-media">
              Nenhuma edição anterior — este é o primeiro cadastro.
            </p>
          )}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={aoFechar}>
            Fechar
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
