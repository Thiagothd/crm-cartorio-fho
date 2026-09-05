import { useEffect, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { BookOpen, History, Pencil, Plus } from 'lucide-react'
import { toast } from 'sonner'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'
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
import { EstadoVazio } from '@/components/layout/shell'
import { conhecimentoDeIaApi, servicosApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import type { ConhecimentoDeIa, VersaoDeConhecimentoDeIa } from '@/lib/api/tipos'

/** Mesmo limite da coluna (ConhecimentoDeIaConfiguration.cs). */
const LIMITE_DO_CONTEUDO = 2000

/** Valor do Select para "sem serviço vinculado" — Radix não aceita item com value vazio. */
const SEM_SERVICO = '_nenhum'

/**
 * VigenteDesde/VigenteAte chegam como "AAAA-MM-DD" puro (DateOnly, sem hora).
 * Formata por string, sem passar por Date — evitar `new Date("AAAA-MM-DD")`
 * aqui é o que evita o dia deslocar por fuso (ele é interpretado como
 * meia-noite UTC, e o Intl formata no fuso local do navegador).
 */
function formatarDataOnly(iso: string) {
  const [ano, mes, dia] = iso.split('-')
  return `${dia}/${mes}/${ano}`
}

function descreverVigencia(desde: string | null, ate: string | null) {
  if (!desde && !ate) return 'Sempre'
  if (desde && ate) return `${formatarDataOnly(desde)} – ${formatarDataOnly(ate)}`
  if (desde) return `A partir de ${formatarDataOnly(desde)}`
  return `Até ${formatarDataOnly(ate!)}`
}

export function AbaConhecimentoDeIa() {
  const [emEdicao, setEmEdicao] = useState<ConhecimentoDeIa | null>(null)
  const [aberto, setAberto] = useState(false)
  const [emHistorico, setEmHistorico] = useState<ConhecimentoDeIa | null>(null)

  const clienteDeConsultas = useQueryClient()

  const { data, isLoading } = useQuery({
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
      <div className="flex flex-col items-stretch gap-3 sm:flex-row sm:items-start sm:justify-between sm:gap-4">
        <p className="max-w-2xl text-sm text-tinta-media">
          Conteúdo que a IA pode usar para responder — por serviço ou por tema livre. Toda edição
          preserva a versão anterior, com data e responsável. Diferente das{' '}
          <strong className="text-tinta">respostas rápidas</strong> (atalho que o atendente
          digita), este cadastro é lido direto pela IA.
        </p>

        <Button size="sm" className="sm:shrink-0" onClick={abrirNovo}>
          <Plus />
          Novo conhecimento
        </Button>
      </div>

      {isLoading ? (
        <div className="flex flex-col gap-2">
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} className="h-12 w-full" />
          ))}
        </div>
      ) : data?.length ? (
        <div className="overflow-hidden rounded-md border border-linha">
          <table className="w-full table-fixed text-sm md:table-auto">
            <thead className="bg-superficie text-xs text-tinta-media">
              <tr>
                <th className="px-3 py-2.5 text-left font-medium md:px-4">Título</th>
                <th className="hidden px-4 py-2.5 text-left font-medium md:table-cell">
                  Tema / Serviço
                </th>
                <th className="hidden px-4 py-2.5 text-left font-medium md:table-cell">Vigência</th>
                <th className="hidden w-20 px-4 py-2.5 text-left font-medium md:table-cell">Versão</th>
                <th className="w-20 md:w-24" />
              </tr>
            </thead>

            <tbody>
              {data.map((conhecimento) => (
                <tr
                  key={conhecimento.id}
                  className="border-t border-linha align-top hover:bg-superficie"
                >
                  <td className="px-3 py-3 md:px-4 md:py-2.5">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-medium">{conhecimento.titulo}</span>
                      {!conhecimento.ativo ? <Badge variant="secondary">Inativo</Badge> : null}
                    </div>
                    <p className="mt-1 line-clamp-2 text-[11px] text-tinta-fraca md:hidden">
                      {conhecimento.conteudo}
                    </p>
                    <p className="mt-1 text-[11px] text-tinta-fraca md:hidden">
                      {conhecimento.tema}
                      {conhecimento.tipoServicoNome ? ` · ${conhecimento.tipoServicoNome}` : ''}
                      {' — '}
                      {descreverVigencia(conhecimento.vigenteDesde, conhecimento.vigenteAte)}
                    </p>
                  </td>

                  <td className="hidden px-4 py-2.5 md:table-cell">
                    <span className="text-xs text-tinta-media">{conhecimento.tema}</span>
                    {conhecimento.tipoServicoNome ? (
                      <p className="mt-0.5 text-[11px] text-tinta-fraca">
                        {conhecimento.tipoServicoNome}
                      </p>
                    ) : null}
                  </td>

                  <td className="hidden px-4 py-2.5 md:table-cell">
                    <span className="text-xs text-tinta-media">
                      {descreverVigencia(conhecimento.vigenteDesde, conhecimento.vigenteAte)}
                    </span>
                  </td>

                  <td className="hidden px-4 py-2.5 font-dado text-xs text-tinta-media md:table-cell">
                    v{conhecimento.versao}
                  </td>

                  <td className="px-1 py-2.5 md:px-2">
                    <div className="flex items-center justify-end gap-0.5">
                      <Button
                        variant="ghost"
                        size="icon-sm"
                        onClick={() => setEmHistorico(conhecimento)}
                        aria-label={`Ver histórico de ${conhecimento.titulo}`}
                      >
                        <History />
                      </Button>
                      <Button
                        variant="ghost"
                        size="icon-sm"
                        onClick={() => {
                          setEmEdicao(conhecimento)
                          setAberto(true)
                        }}
                        aria-label={`Editar ${conhecimento.titulo}`}
                      >
                        <Pencil />
                      </Button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <EstadoVazio
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
  }, [aberto, conhecimento])

  async function salvar() {
    setSalvando(true)

    try {
      const dados = {
        titulo,
        conteudo,
        tema,
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
      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível salvar.')
    } finally {
      setSalvando(false)
    }
  }

  return (
    <Dialog open={aberto} onOpenChange={(v) => !v && aoFechar()}>
      <DialogContent className="max-w-xl">
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
              onChange={(e) => setTitulo(e.target.value)}
              placeholder="Ex.: Política de reagendamento"
              maxLength={160}
            />
          </div>

          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="conhecimento-tema">Tema</Label>
              <Input
                id="conhecimento-tema"
                value={tema}
                onChange={(e) => setTema(e.target.value)}
                placeholder="Ex.: Atendimento"
                maxLength={80}
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
              onChange={(e) => setConteudo(e.target.value)}
              rows={6}
              maxLength={LIMITE_DO_CONTEUDO}
              className="resize-none"
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
            Deixe em branco para valer desde sempre / para sempre. Fora da janela, a IA para de
            ver este conteúdo sem precisar desativar.
          </p>

          <div className="flex items-center justify-between rounded-md border border-linha px-3 py-2">
            <div>
              <Label htmlFor="conhecimento-ativo">Conhecimento ativo</Label>
              <p className="text-xs text-tinta-media">Desativado, some da resposta da IA.</p>
            </div>
            <Switch id="conhecimento-ativo" checked={ativo} onCheckedChange={setAtivo} />
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
              <div key={versao.versao} className="rounded-md border border-linha p-3">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span className="font-dado text-xs text-tinta-media">v{versao.versao}</span>
                  <span className="text-xs text-tinta-fraca">
                    {versao.alteradoPorNome ?? 'Sistema'} —{' '}
                    {new Date(versao.ocorridoEm).toLocaleString('pt-BR')}
                  </span>
                </div>
                <p className="mt-1.5 font-medium">{versao.titulo}</p>
                <p className="mt-1 text-sm text-tinta-media">{versao.conteudo}</p>
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
