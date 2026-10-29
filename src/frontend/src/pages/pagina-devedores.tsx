import { useDeferredValue, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  AlertTriangle,
  CalendarClock,
  CheckCircle2,
  Download,
  HandCoins,
  Layers,
  ListFilter,
  MessageSquare,
  Pencil,
  Plus,
  RotateCcw,
  Search,
  Send,
  Trash2,
  Upload,
  Users,
} from 'lucide-react'
import { toast } from 'sonner'
import { AcoesDaPagina } from '@/components/layout/contexto-de-acoes-da-pagina'
import { Avatar } from '@/components/ui/avatar'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { devedoresApi, servicosApi } from '@/lib/api/endpoints'
import { usarAutenticacao } from '@/lib/auth/usar-autenticacao'
import {
  formatarDataHora,
  formatarDocumento,
  formatarMomentoRelativo,
  formatarReais,
  formatarTelefone,
} from '@/lib/formatacao'
import { cn } from '@/lib/utils'
import {
  DialogoCobrarWhatsapp,
  type AlvoDeCobrancaWhatsapp,
} from '@/features/devedores/dialogo-cobrar-whatsapp'
import { DialogoImportarDevedores } from '@/features/devedores/dialogo-importar-devedores'
import {
  DialogoNovaPendencia,
  type ClientePreSelecionadoDevedor,
} from '@/features/devedores/dialogo-nova-pendencia'
import { DialogoRegistrarPagamento } from '@/features/devedores/dialogo-registrar-pagamento'
import {
  exportarDevedoresCsv,
  formatarDataIso,
  rotuloMeioDePagamento,
} from '@/features/devedores/utilitarios'
import type {
  DevedorConsolidado,
  OrdenacaoDeDevedores,
  PendenciaFinanceira,
  RecorteDeDevedores,
} from '@/lib/api/tipos'

const TODOS_SERVICOS = '__todos__'

const RECORTES: { id: RecorteDeDevedores; rotulo: string }[] = [
  { id: 'abertas', rotulo: 'Em aberto' },
  { id: 'vencidas', rotulo: 'Promessa vencida' },
  { id: 'hoje', rotulo: 'Promessa hoje' },
  { id: 'sem_cobranca', rotulo: 'Nunca cobrados' },
  { id: 'parciais', rotulo: 'Pagos parcialmente' },
  { id: 'sem_valor', rotulo: 'Valor a apurar' },
  { id: 'quitadas', rotulo: 'Quitados' },
  { id: 'todas', rotulo: 'Todos' },
]

const ORDENACOES: { valor: OrdenacaoDeDevedores; rotulo: string }[] = [
  { valor: 'maior_valor', rotulo: 'Maior saldo e urgência' },
  { valor: 'promessa_proxima', rotulo: 'Promessa mais próxima' },
  { valor: 'mais_antigas', rotulo: 'Mais antigos primeiro' },
  { valor: 'recentes', rotulo: 'Lançados recentemente' },
  { valor: 'nome', rotulo: 'Ordem alfabética (A-Z)' },
]

export function PaginaDevedores() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { pode } = usarAutenticacao()
  const podeEditar = pode('devedores.editar')

  const [visao, setVisao] = useState<'contatos' | 'lancamentos'>('contatos')
  const [recorte, setRecorte] = useState<RecorteDeDevedores>('abertas')
  const [ordenar, setOrdenar] = useState<OrdenacaoDeDevedores>('maior_valor')
  const [tipoServicoId, setTipoServicoId] = useState<string>(TODOS_SERVICOS)
  const [busca, setBusca] = useState('')
  const buscaAdiada = useDeferredValue(busca)

  const [novaAberta, setNovaAberta] = useState(false)
  const [clienteFixoNova, setClienteFixoNova] = useState<ClientePreSelecionadoDevedor | null>(null)
  const [emEdicao, setEmEdicao] = useState<PendenciaFinanceira | null>(null)
  const [emPagamento, setEmPagamento] = useState<PendenciaFinanceira | null>(null)
  const [alvoCobranca, setAlvoCobranca] = useState<AlvoDeCobrancaWhatsapp | null>(null)
  const [importacaoAberta, setImportacaoAberta] = useState(false)

  const filtroConsulta = {
    recorte,
    ordenar,
    tipoServicoId: tipoServicoId === TODOS_SERVICOS ? undefined : tipoServicoId,
    busca: buscaAdiada.trim() || undefined,
  }

  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ['devedores', filtroConsulta],
    queryFn: () => devedoresApi.listar(filtroConsulta),
  })

  const { data: servicos = [] } = useQuery({
    queryKey: ['tipos-servico', 'filtro-devedores'],
    queryFn: () => servicosApi.listar(false),
  })

  const mutacaoAbrirConversa = useMutation({
    mutationFn: (clienteId: string) => devedoresApi.abrirConversa(clienteId),
    onSuccess: (res) => {
      queryClient.invalidateQueries({ queryKey: ['devedores'] })
      queryClient.invalidateQueries({ queryKey: ['conversas'] })
      queryClient.invalidateQueries({ queryKey: ['painel-resumo'] })
      navigate(`/atendimentos/${res.conversaId}?escopo=minhas`)
    },
    onError: () => {
      toast.error('Não foi possível abrir a conversa com este contato.')
    },
  })

  const mutacaoCancelar = useMutation({
    mutationFn: (id: string) => devedoresApi.cancelar(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['devedores'] })
      queryClient.invalidateQueries({ queryKey: ['conversas'] })
      toast.success('Lançamento removido da lista.')
    },
  })

  const mutacaoReabrir = useMutation({
    mutationFn: (id: string) => devedoresApi.reabrir(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['devedores'] })
      queryClient.invalidateQueries({ queryKey: ['conversas'] })
      toast.success('Pendência reaberta.')
    },
  })

  const resumo = data?.resumo
  const devedores = data?.devedores ?? []
  const lancamentos = data?.lancamentos ?? []

  return (
    <div className="@container flex h-full flex-col overflow-y-auto">
      <AcoesDaPagina>
        <div className="flex items-center gap-2">
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={lancamentos.length === 0}
            onClick={() => exportarDevedoresCsv(devedores, lancamentos, visao)}
          >
            <Download className="size-4" />
            <span className="hidden sm:inline">Exportar CSV</span>
          </Button>
          {podeEditar ? (
            <>
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={() => setImportacaoAberta(true)}
              >
                <Upload className="size-4" />
                <span className="hidden sm:inline">Importar vários</span>
              </Button>
              <Button
                type="button"
                size="sm"
                onClick={() => {
                  setClienteFixoNova(null)
                  setEmEdicao(null)
                  setNovaAberta(true)
                }}
              >
                <Plus className="size-4" />
                Novo devedor
              </Button>
            </>
          ) : null}
        </div>
      </AcoesDaPagina>

      <div className="mx-auto flex w-full max-w-7xl flex-col gap-4 p-3 md:gap-5 md:p-5">
        {/* Bloco 1: Indicadores executivos clicáveis */}
        <section
          aria-label="Resumo financeiro de pendências"
          className="grid gap-3 sm:grid-cols-2 @4xl:grid-cols-4"
        >
          <button
            type="button"
            onClick={() => setRecorte('abertas')}
            className={cn(
              'flex flex-col justify-between rounded-card border bg-branco p-4 text-left shadow-card transition-colors',
              recorte === 'abertas'
                ? 'border-linha-forte ring-1 ring-tinta/10'
                : 'border-linha hover:border-linha-forte',
            )}
          >
            <div className="flex items-center justify-between gap-2">
              <span className="text-xs font-medium text-tinta-media">Total em aberto</span>
              <HandCoins className="size-4 text-ambar" />
            </div>
            <p className="mt-2 font-dado text-2xl font-semibold tracking-tight text-tinta">
              {resumo ? formatarReais(resumo.totalEmAberto) : '—'}
            </p>
            <p className="mt-1 text-xs text-tinta-media">
              {resumo ? (
                <>
                  <strong className="font-semibold text-tinta">{resumo.clientesDevedores}</strong>{' '}
                  {resumo.clientesDevedores === 1 ? 'contato' : 'contatos'} ·{' '}
                  {resumo.pendenciasAbertas}{' '}
                  {resumo.pendenciasAbertas === 1 ? 'pendência' : 'pendências'}
                  {resumo.pendenciasSemValor > 0
                    ? ` (${resumo.pendenciasSemValor} a apurar)`
                    : ''}
                </>
              ) : (
                'Calculando...'
              )}
            </p>
          </button>

          <button
            type="button"
            onClick={() => setRecorte('vencidas')}
            className={cn(
              'flex flex-col justify-between rounded-card border bg-branco p-4 text-left shadow-card transition-colors',
              recorte === 'vencidas'
                ? 'border-selo ring-1 ring-selo/20'
                : 'border-linha hover:border-linha-forte',
            )}
          >
            <div className="flex items-center justify-between gap-2">
              <span className="text-xs font-medium text-tinta-media">Promessas vencidas</span>
              <AlertTriangle
                className={cn(
                  'size-4',
                  (resumo?.promessasVencidasQuantidade ?? 0) > 0
                    ? 'text-selo'
                    : 'text-tinta-fraca',
                )}
              />
            </div>
            <p
              className={cn(
                'mt-2 font-dado text-2xl font-semibold tracking-tight',
                (resumo?.promessasVencidasQuantidade ?? 0) > 0 ? 'text-selo' : 'text-tinta',
              )}
            >
              {resumo ? formatarReais(resumo.promessasVencidasTotal) : '—'}
            </p>
            <p className="mt-1 text-xs text-tinta-media">
              {resumo
                ? `${resumo.promessasVencidasQuantidade} ${
                    resumo.promessasVencidasQuantidade === 1
                      ? 'promessa passou do prazo'
                      : 'promessas passaram do prazo'
                  }`
                : 'Calculando...'}
            </p>
          </button>

          <button
            type="button"
            onClick={() => setRecorte('hoje')}
            className={cn(
              'flex flex-col justify-between rounded-card border bg-branco p-4 text-left shadow-card transition-colors',
              recorte === 'hoje'
                ? 'border-ambar ring-1 ring-ambar/25'
                : 'border-linha hover:border-linha-forte',
            )}
          >
            <div className="flex items-center justify-between gap-2">
              <span className="text-xs font-medium text-tinta-media">Prometido para hoje</span>
              <CalendarClock className="size-4 text-ambar" />
            </div>
            <p className="mt-2 font-dado text-2xl font-semibold tracking-tight text-tinta">
              {resumo ? formatarReais(resumo.promessasParaHojeTotal) : '—'}
            </p>
            <p className="mt-1 text-xs text-tinta-media">
              {resumo
                ? `${resumo.promessasParaHojeQuantidade} ${
                    resumo.promessasParaHojeQuantidade === 1
                      ? 'acerto agendado para hoje'
                      : 'acertos agendados para hoje'
                  }`
                : 'Calculando...'}
            </p>
          </button>

          <button
            type="button"
            onClick={() => setRecorte('quitadas')}
            className={cn(
              'flex flex-col justify-between rounded-card border bg-branco p-4 text-left shadow-card transition-colors',
              recorte === 'quitadas'
                ? 'border-verde ring-1 ring-verde/25'
                : 'border-linha hover:border-linha-forte',
            )}
          >
            <div className="flex items-center justify-between gap-2">
              <span className="text-xs font-medium text-tinta-media">Recuperado no mês</span>
              <CheckCircle2 className="size-4 text-verde" />
            </div>
            <p className="mt-2 font-dado text-2xl font-semibold tracking-tight text-verde">
              {resumo ? formatarReais(resumo.recuperadoNoMesTotal) : '—'}
            </p>
            <p className="mt-1 text-xs text-tinta-media">
              {resumo
                ? `${resumo.recuperadoNoMesQuantidade} ${
                    resumo.recuperadoNoMesQuantidade === 1
                      ? 'pagamento recebido'
                      : 'pagamentos recebidos'
                  }`
                : 'Calculando...'}
            </p>
          </button>
        </section>

        {/* Bloco 2: Barra de ferramentas, busca, visão e recortes */}
        <section className="flex flex-col gap-3 rounded-card border border-linha bg-branco p-3 shadow-card md:p-4">
          <div className="flex flex-col gap-2.5 @2xl:flex-row @2xl:items-center @2xl:justify-between">
            <div className="relative flex-1">
              <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-tinta-media" />
              <Input
                value={busca}
                onChange={(e) => setBusca(e.target.value)}
                placeholder="Buscar por nome do cliente, WhatsApp ou motivo/serviço..."
                className="rounded-full bg-superficie pl-9"
              />
            </div>

            <div className="flex flex-wrap items-center gap-2">
              <Select value={tipoServicoId} onValueChange={setTipoServicoId}>
                <SelectTrigger className="w-full bg-superficie sm:w-48">
                  <ListFilter className="mr-1.5 size-3.5 text-tinta-fraca" />
                  <SelectValue placeholder="Todos os serviços" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={TODOS_SERVICOS}>Todos os serviços</SelectItem>
                  {servicos.map((s) => (
                    <SelectItem key={s.id} value={s.id}>
                      {s.nome}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>

              <Select
                value={ordenar}
                onValueChange={(v) => setOrdenar(v as OrdenacaoDeDevedores)}
              >
                <SelectTrigger className="w-full bg-superficie sm:w-52">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {ORDENACOES.map((o) => (
                    <SelectItem key={o.valor} value={o.valor}>
                      {o.rotulo}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>

              <div
                role="group"
                aria-label="Modo de visualização"
                className="inline-flex rounded-lg border border-linha bg-superficie p-0.5 text-xs"
              >
                <button
                  type="button"
                  onClick={() => setVisao('contatos')}
                  className={cn(
                    'inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 font-medium transition-colors',
                    visao === 'contatos'
                      ? 'bg-branco text-tinta shadow-xs'
                      : 'text-tinta-media hover:text-tinta',
                  )}
                >
                  <Users className="size-3.5" />
                  Por contato
                </button>
                <button
                  type="button"
                  onClick={() => setVisao('lancamentos')}
                  className={cn(
                    'inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 font-medium transition-colors',
                    visao === 'lancamentos'
                      ? 'bg-branco text-tinta shadow-xs'
                      : 'text-tinta-media hover:text-tinta',
                  )}
                >
                  <Layers className="size-3.5" />
                  Por lançamento
                </button>
              </div>
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-1.5">
            {RECORTES.map((item) => {
              const ativo = recorte === item.id
              return (
                <button
                  key={item.id}
                  type="button"
                  aria-pressed={ativo}
                  onClick={() => setRecorte(item.id)}
                  className={cn(
                    'rounded-full border px-3 py-1 text-xs font-medium transition-colors',
                    ativo
                      ? 'border-selecao bg-selecao text-sobre-selecao'
                      : 'border-linha bg-superficie/60 text-tinta-media hover:border-linha-forte hover:text-tinta',
                  )}
                >
                  {item.rotulo}
                </button>
              )
            })}
          </div>
        </section>

        {/* Bloco 3: Lista de Contatos ou Lançamentos */}
        {isLoading ? (
          <div className="grid gap-3 @xl:grid-cols-2">
            {Array.from({ length: 4 }).map((_, i) => (
              <Skeleton key={i} className="h-48 rounded-card" />
            ))}
          </div>
        ) : isError ? (
          <EstadoDaInterface
            tipo="erro"
            titulo="Não foi possível carregar a lista de devedores"
            descricao="Verifique sua conexão e tente novamente."
            acao={
              <Button type="button" variant="outline" onClick={() => refetch()}>
                Tentar novamente
              </Button>
            }
          />
        ) : (visao === 'contatos' ? devedores.length === 0 : lancamentos.length === 0) ? (
          <EstadoDaInterface
            tipo="vazio"
            icone={HandCoins}
            titulo="Nenhum devedor encontrado neste filtro"
            descricao={
              recorte === 'abertas'
                ? 'Adicione um contato individualmente, importe uma lista ou marque alguém direto pela conversa no Atendimento.'
                : 'Não há registros que correspondam ao recorte ou termo buscado.'
            }
            acao={
              podeEditar ? (
                <div className="flex flex-wrap items-center justify-center gap-2">
                  <Button
                    type="button"
                    size="sm"
                    onClick={() => {
                      setClienteFixoNova(null)
                      setEmEdicao(null)
                      setNovaAberta(true)
                    }}
                  >
                    <Plus className="size-4" />
                    Registrar primeiro devedor
                  </Button>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    onClick={() => setImportacaoAberta(true)}
                  >
                    <Upload className="size-4" />
                    Importar lista / contatos
                  </Button>
                </div>
              ) : undefined
            }
          />
        ) : visao === 'contatos' ? (
          <div
            role="list"
            aria-label="Contatos com pendências financeiras"
            className="grid gap-3 @2xl:grid-cols-2"
          >
            {devedores.map((devedor) => (
              <CardDevedorConsolidado
                key={devedor.clienteId}
                devedor={devedor}
                podeEditar={podeEditar}
                aoCobrar={() =>
                  setAlvoCobranca({
                    clienteId: devedor.clienteId,
                    clienteNome: devedor.clienteNome,
                    clienteTelefone: devedor.clienteTelefone,
                    pendencias: devedor.pendencias,
                  })
                }
                aoAbrirConversa={() => {
                  mutacaoAbrirConversa.mutate(devedor.clienteId)
                }}
                aoNovoLancamento={() => {
                  setClienteFixoNova({
                    id: devedor.clienteId,
                    nome: devedor.clienteNome,
                    telefone: devedor.clienteTelefone,
                  })
                  setEmEdicao(null)
                  setNovaAberta(true)
                }}
                aoDarBaixa={(p) => setEmPagamento(p)}
                aoEditar={(p) => {
                  setEmEdicao(p)
                  setClienteFixoNova(null)
                  setNovaAberta(true)
                }}
                aoCancelar={(id) => mutacaoCancelar.mutate(id)}
                aoReabrir={(id) => mutacaoReabrir.mutate(id)}
              />
            ))}
          </div>
        ) : (
          <div
            role="list"
            aria-label="Lançamentos financeiros"
            className="grid gap-3 @2xl:grid-cols-2"
          >
            {lancamentos.map((p) => (
              <CardLancamentoIndividual
                key={p.id}
                pendencia={p}
                podeEditar={podeEditar}
                aoCobrar={() =>
                  setAlvoCobranca({
                    clienteId: p.clienteId,
                    clienteNome: p.clienteNome,
                    clienteTelefone: p.clienteTelefone,
                    pendencias: [p],
                  })
                }
                aoAbrirConversa={() => {
                  mutacaoAbrirConversa.mutate(p.clienteId)
                }}
                aoDarBaixa={() => setEmPagamento(p)}
                aoEditar={() => {
                  setEmEdicao(p)
                  setClienteFixoNova(null)
                  setNovaAberta(true)
                }}
                aoCancelar={() => mutacaoCancelar.mutate(p.id)}
                aoReabrir={() => mutacaoReabrir.mutate(p.id)}
              />
            ))}
          </div>
        )}
      </div>

      <DialogoNovaPendencia
        aberto={novaAberta}
        aoMudarAberto={(ab) => {
          setNovaAberta(ab)
          if (!ab) {
            setEmEdicao(null)
            setClienteFixoNova(null)
          }
        }}
        clienteFixo={clienteFixoNova}
        pendenciaEmEdicao={emEdicao}
      />

      <DialogoRegistrarPagamento
        pendencia={emPagamento}
        aoFechar={() => setEmPagamento(null)}
      />

      <DialogoCobrarWhatsapp
        alvo={alvoCobranca}
        aoFechar={() => setAlvoCobranca(null)}
      />

      <DialogoImportarDevedores
        aberto={importacaoAberta}
        aoMudarAberto={setImportacaoAberta}
      />
    </div>
  )
}

function CardDevedorConsolidado({
  devedor,
  podeEditar,
  aoCobrar,
  aoAbrirConversa,
  aoNovoLancamento,
  aoDarBaixa,
  aoEditar,
  aoCancelar,
  aoReabrir,
}: {
  devedor: DevedorConsolidado
  podeEditar: boolean
  aoCobrar: () => void
  aoAbrirConversa: () => void
  aoNovoLancamento: () => void
  aoDarBaixa: (pendencia: PendenciaFinanceira) => void
  aoEditar: (pendencia: PendenciaFinanceira) => void
  aoCancelar: (id: string) => void
  aoReabrir: (id: string) => void
}) {
  const percentualPago =
    devedor.valorOriginalTotal > 0
      ? Math.min(100, Math.round((devedor.valorPagoTotal / devedor.valorOriginalTotal) * 100))
      : 0

  return (
    <article
      role="listitem"
      className="flex flex-col justify-between rounded-card border border-linha bg-branco p-4 shadow-card transition-colors hover:border-linha-forte"
    >
      <div>
        <header className="flex items-start justify-between gap-3">
          <div className="flex min-w-0 items-start gap-3">
            <Avatar
              nome={devedor.clienteNome}
              fotoUrl={devedor.clienteFotoPerfilUrl}
              tamanho="lg"
              className="rounded-lg bg-superficie"
            />
            <div className="min-w-0">
              <div className="flex flex-wrap items-center gap-1.5">
                <h3 className="truncate font-semibold text-tinta">{devedor.clienteNome}</h3>
                {devedor.temPromessaVencida ? (
                  <span className="inline-flex items-center gap-1 rounded-full border border-selo/30 bg-selo/10 px-2 py-0.5 text-[11px] font-medium text-selo">
                    Promessa vencida
                  </span>
                ) : devedor.temPromessaHoje ? (
                  <span className="inline-flex items-center gap-1 rounded-full border border-ambar/40 bg-ambar/15 px-2 py-0.5 text-[11px] font-medium text-tinta">
                    Prometeu hoje
                  </span>
                ) : null}
              </div>
              <p className="mt-0.5 font-dado text-xs text-tinta-media">
                {formatarTelefone(devedor.clienteTelefone)}
                {devedor.clienteDocumento
                  ? ` · ${formatarDocumento(devedor.clienteDocumento)}`
                  : ''}
              </p>
            </div>
          </div>

          <div className="shrink-0 text-right">
            <p className="text-[10px] font-medium uppercase tracking-wider text-tinta-fraca">
              {devedor.quantidadePendenciasAbertas > 0 ? 'Saldo devedor' : 'Quitado'}
            </p>
            <p
              className={cn(
                'font-dado text-lg font-semibold',
                devedor.quantidadePendenciasAbertas === 0
                  ? 'text-verde'
                  : devedor.temPromessaVencida
                    ? 'text-selo'
                    : 'text-tinta',
              )}
            >
              {devedor.saldoRestanteTotal > 0
                ? formatarReais(devedor.saldoRestanteTotal)
                : devedor.quantidadeSemValor > 0
                  ? 'Valor a apurar'
                  : formatarReais(0)}
            </p>
          </div>
        </header>

        {devedor.valorPagoTotal > 0 && devedor.valorOriginalTotal > 0 ? (
          <div className="mt-3 space-y-1">
            <div className="flex items-center justify-between text-[11px] text-tinta-media">
              <span>
                Já amortizado: <strong className="font-dado text-verde">{formatarReais(devedor.valorPagoTotal)}</strong> de{' '}
                <span className="font-dado">{formatarReais(devedor.valorOriginalTotal)}</span>
              </span>
              <span className="font-dado font-semibold">{percentualPago}%</span>
            </div>
            <div className="h-1.5 w-full overflow-hidden rounded-full bg-superficie">
              <div
                className="h-full rounded-full bg-verde transition-all"
                style={{ width: `${percentualPago}%` }}
              />
            </div>
          </div>
        ) : null}

        <div className="mt-3 divide-y divide-linha rounded-lg border border-linha bg-superficie/35">
          {devedor.pendencias.map((p) => (
            <div
              key={p.id}
              className="flex flex-wrap items-center justify-between gap-2 px-3 py-2 text-xs"
            >
              <div className="min-w-0 flex-1">
                <div className="flex flex-wrap items-center gap-1.5">
                  <span
                    className={cn(
                      'font-medium',
                      p.status === 'Quitada' ? 'text-tinta-fraca line-through' : 'text-tinta',
                    )}
                  >
                    {p.titulo}
                  </span>
                  {p.tipoServicoNome && p.tipoServicoNome !== p.titulo ? (
                    <Badge variant="secondary" className="text-[10px]">
                      {p.tipoServicoNome}
                    </Badge>
                  ) : null}
                </div>
                <p className="mt-0.5 text-[11px] text-tinta-media">
                  Desde {formatarDataIso(p.dataReferencia)} ({p.diasEmAberto}d)
                  {p.dataPromessaPagamento
                    ? ` · Promessa: ${formatarDataIso(p.dataPromessaPagamento)}`
                    : ''}
                  {p.diasDeAtraso ? (
                    <span className="ml-1 font-semibold text-selo">
                      ({p.diasDeAtraso}d em atraso)
                    </span>
                  ) : null}
                </p>
                {p.observacoes ? (
                  <p className="mt-0.5 text-[11px] text-tinta-fraca">{p.observacoes}</p>
                ) : null}
              </div>

              <div className="flex shrink-0 items-center gap-1.5">
                <span className="font-dado text-xs font-semibold text-tinta">
                  {p.status === 'Quitada'
                    ? `Pago (${formatarReais(p.valorPago)})`
                    : p.saldoRestante !== null
                      ? formatarReais(p.saldoRestante)
                      : 'A apurar'}
                </span>

                {podeEditar && p.status === 'Aberta' ? (
                  <>
                    <Button
                      type="button"
                      size="sm"
                      variant="outline"
                      onClick={() => aoDarBaixa(p)}
                      title="Dar baixa total ou parcial"
                      className="border-verde/40 text-verde hover:bg-verde/10"
                    >
                      <CheckCircle2 className="size-3" />
                      Baixa
                    </Button>
                    <Button
                      type="button"
                      size="icon-sm"
                      variant="ghost"
                      onClick={() => aoEditar(p)}
                      title="Editar lançamento"
                    >
                      <Pencil className="size-3" />
                    </Button>
                    <Button
                      type="button"
                      size="icon-sm"
                      variant="ghost"
                      onClick={() => aoCancelar(p.id)}
                      title="Remover lançamento"
                      className="text-tinta-fraca hover:text-selo"
                    >
                      <Trash2 className="size-3" />
                    </Button>
                  </>
                ) : podeEditar && p.status === 'Quitada' ? (
                  <Button
                    type="button"
                    size="sm"
                    variant="ghost"
                    onClick={() => aoReabrir(p.id)}
                    title="Reabrir pendência"
                  >
                    <RotateCcw className="size-3" />
                    Reabrir
                  </Button>
                ) : null}
              </div>
            </div>
          ))}
        </div>
      </div>

      <footer className="mt-3 flex flex-wrap items-center justify-between gap-2 border-t border-linha pt-3">
        <div className="text-[11px] text-tinta-media">
          {devedor.ultimaCobrancaEm ? (
            <span>
              Cobrado {formatarMomentoRelativo(devedor.ultimaCobrancaEm)} ({devedor.totalCobrancas}
              x)
            </span>
          ) : (
            <span>Ainda não cobrado pelo WhatsApp</span>
          )}
        </div>

        <div className="flex flex-wrap items-center gap-1.5">
          {podeEditar ? (
            <Button
              type="button"
              size="sm"
              variant="ghost"
              onClick={aoNovoLancamento}
              title="Adicionar mais um item para este contato"
            >
              <Plus className="size-3.5" />
              Item
            </Button>
          ) : null}
          <Button
            type="button"
            size="sm"
            variant="outline"
            onClick={aoAbrirConversa}
            title="Abre a conversa no Atendimento em Minhas (sem enviar mensagem pronta e sem robô)"
          >
            <MessageSquare className="size-3.5" />
            Abrir conversa
          </Button>
          {podeEditar && devedor.quantidadePendenciasAbertas > 0 ? (
            <Button type="button" size="sm" onClick={aoCobrar}>
              <Send className="size-3" />
              Cobrar no WhatsApp
            </Button>
          ) : null}
        </div>
      </footer>
    </article>
  )
}

function CardLancamentoIndividual({
  pendencia: p,
  podeEditar,
  aoCobrar,
  aoAbrirConversa,
  aoDarBaixa,
  aoEditar,
  aoCancelar,
  aoReabrir,
}: {
  pendencia: PendenciaFinanceira
  podeEditar: boolean
  aoCobrar: () => void
  aoAbrirConversa: () => void
  aoDarBaixa: () => void
  aoEditar: () => void
  aoCancelar: () => void
  aoReabrir: () => void
}) {
  return (
    <article
      role="listitem"
      className="flex flex-col justify-between rounded-card border border-linha bg-branco p-4 shadow-card"
    >
      <div>
        <header className="flex items-start justify-between gap-3">
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-1.5">
              <h3 className="truncate font-semibold text-tinta">{p.titulo}</h3>
              <Badge
                variant={p.status === 'Quitada' ? 'secondary' : 'outline'}
                className="text-[10px]"
              >
                {p.status === 'Quitada' ? 'Quitada' : 'Em aberto'}
              </Badge>
            </div>
            <p className="mt-0.5 text-xs text-tinta-media">
              <strong className="font-medium text-tinta">{p.clienteNome}</strong> ·{' '}
              <span className="font-dado">{formatarTelefone(p.clienteTelefone)}</span>
            </p>
          </div>

          <div className="shrink-0 text-right">
            <p className="font-dado text-base font-semibold text-tinta">
              {p.status === 'Quitada'
                ? formatarReais(p.valorPago)
                : p.saldoRestante !== null
                  ? formatarReais(p.saldoRestante)
                  : 'Valor a apurar'}
            </p>
            {p.valorOriginal !== null && p.valorPago > 0 && p.status === 'Aberta' ? (
              <p className="font-dado text-[11px] text-verde">
                Pago: {formatarReais(p.valorPago)} de {formatarReais(p.valorOriginal)}
              </p>
            ) : null}
          </div>
        </header>

        <div className="mt-3 grid grid-cols-2 gap-2 rounded-lg border border-linha bg-superficie/40 px-3 py-2 text-xs">
          <div>
            <span className="text-[10px] uppercase tracking-wider text-tinta-fraca">
              Data do serviço
            </span>
            <p className="font-dado font-medium text-tinta">
              {formatarDataIso(p.dataReferencia)} ({p.diasEmAberto}d)
            </p>
          </div>
          <div>
            <span className="text-[10px] uppercase tracking-wider text-tinta-fraca">
              Promessa de acerto
            </span>
            <p
              className={cn(
                'font-dado font-medium',
                p.diasDeAtraso ? 'text-selo' : 'text-tinta',
              )}
            >
              {formatarDataIso(p.dataPromessaPagamento)}
              {p.diasDeAtraso ? ` (${p.diasDeAtraso}d atraso)` : ''}
            </p>
          </div>
        </div>

        {p.observacoes ? (
          <p className="mt-2 text-xs text-tinta-media">{p.observacoes}</p>
        ) : null}

        {p.pagamentos.length > 0 ? (
          <div className="mt-3 space-y-1">
            <p className="text-[10px] font-semibold uppercase tracking-wider text-tinta-fraca">
              Pagamentos registrados ({p.pagamentos.length})
            </p>
            <div className="divide-y divide-linha rounded-md border border-linha bg-superficie/25">
              {p.pagamentos.map((pg) => (
                <div
                  key={pg.id}
                  className="flex items-center justify-between px-2.5 py-1.5 text-[11px]"
                >
                  <span className="text-tinta-media">
                    {rotuloMeioDePagamento(pg.meioPagamento)} · {formatarDataHora(pg.pagoEm)}
                  </span>
                  <span className="font-dado font-semibold text-verde">
                    + {formatarReais(pg.valor)}
                  </span>
                </div>
              ))}
            </div>
          </div>
        ) : null}
      </div>

      <footer className="mt-3 flex flex-wrap items-center justify-between gap-2 border-t border-linha pt-3">
        <span className="text-[11px] text-tinta-fraca">
          Registrado por {p.registradoPorNome ?? 'Sistema'}
        </span>

        <div className="flex flex-wrap items-center gap-1.5">
          <Button
            type="button"
            size="sm"
            variant="outline"
            onClick={aoAbrirConversa}
            title="Abre a conversa no Atendimento em Minhas (sem enviar mensagem pronta e sem robô)"
          >
            <MessageSquare className="size-3" />
            Abrir conversa
          </Button>
          {podeEditar && p.status === 'Aberta' ? (
            <>
              <Button
                type="button"
                size="sm"
                variant="outline"
                onClick={aoDarBaixa}
                className="border-verde/40 text-verde hover:bg-verde/10"
              >
                <CheckCircle2 className="size-3" />
                Dar baixa
              </Button>
              <Button type="button" size="sm" onClick={aoCobrar}>
                <Send className="size-3" />
                Cobrar
              </Button>
              <Button type="button" size="icon-sm" variant="ghost" onClick={aoEditar}>
                <Pencil className="size-3" />
              </Button>
              <Button
                type="button"
                size="icon-sm"
                variant="ghost"
                onClick={aoCancelar}
                className="text-tinta-fraca hover:text-selo"
              >
                <Trash2 className="size-3" />
              </Button>
            </>
          ) : podeEditar && p.status === 'Quitada' ? (
            <Button type="button" size="sm" variant="outline" onClick={aoReabrir}>
              <RotateCcw className="size-3" />
              Reabrir
            </Button>
          ) : null}
        </div>
      </footer>
    </article>
  )
}
