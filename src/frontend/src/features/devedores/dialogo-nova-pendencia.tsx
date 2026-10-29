import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Calendar, Check, MessageSquare, Search, UserPlus, Users } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
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
import { clientesApi, devedoresApi, servicosApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { formatarReais, formatarTelefone } from '@/lib/formatacao'
import {
  hojeIsoLocal,
  interpretarValorMonetario,
  somarDiasIsoLocal,
} from './utilitarios'
import type { PendenciaFinanceira } from '@/lib/api/tipos'

export interface ClientePreSelecionadoDevedor {
  id: string
  nome: string
  telefone: string
  conversaOrigemId?: string | null
  tipoServicoId?: string | null
  tipoServicoNome?: string | null
}

const SUGESTOES_DE_MOTIVO = [
  '2ª via de certidão',
  'Reconhecimento de firma / autenticação',
  'Procuração pública',
  'Escritura pública',
  'Custas complementares / averbação',
]

const NENHUM_SERVICO = '__nenhum__'

export function DialogoNovaPendencia({
  aberto,
  aoMudarAberto,
  clienteFixo = null,
  pendenciaEmEdicao = null,
  aoSalvar,
}: {
  aberto: boolean
  aoMudarAberto: (aberto: boolean) => void
  clienteFixo?: ClientePreSelecionadoDevedor | null
  pendenciaEmEdicao?: PendenciaFinanceira | null
  aoSalvar?: (pendencia: PendenciaFinanceira) => void
}) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const editando = Boolean(pendenciaEmEdicao)

  const [modoContato, setModoContato] = useState<'existente' | 'novo'>('existente')
  const [buscaCliente, setBuscaCliente] = useState('')
  const [clienteSelecionadoId, setClienteSelecionadoId] = useState<string>('')
  const [clienteSelecionadoResumo, setClienteSelecionadoResumo] = useState<{
    nome: string
    telefone: string
  } | null>(null)

  const [novoNome, setNovoNome] = useState('')
  const [novoTelefone, setNovoTelefone] = useState('')
  const [novoDocumento, setNovoDocumento] = useState('')

  const [titulo, setTitulo] = useState('')
  const [tipoServicoId, setTipoServicoId] = useState<string>(NENHUM_SERVICO)
  const [valorTexto, setValorTexto] = useState('')
  const [dataReferencia, setDataReferencia] = useState(hojeIsoLocal())
  const [dataPromessa, setDataPromessa] = useState('')
  const [observacoes, setObservacoes] = useState('')
  const [erroFormulario, setErroFormulario] = useState<string | null>(null)

  useEffect(() => {
    if (!aberto) return
    setErroFormulario(null)

    if (pendenciaEmEdicao) {
      setModoContato('existente')
      setClienteSelecionadoId(pendenciaEmEdicao.clienteId)
      setClienteSelecionadoResumo({
        nome: pendenciaEmEdicao.clienteNome,
        telefone: pendenciaEmEdicao.clienteTelefone,
      })
      setTitulo(pendenciaEmEdicao.titulo)
      setTipoServicoId(pendenciaEmEdicao.tipoServicoId ?? NENHUM_SERVICO)
      setValorTexto(
        pendenciaEmEdicao.valorOriginal !== null
          ? pendenciaEmEdicao.valorOriginal.toFixed(2).replace('.', ',')
          : '',
      )
      setDataReferencia(pendenciaEmEdicao.dataReferencia.slice(0, 10))
      setDataPromessa(pendenciaEmEdicao.dataPromessaPagamento?.slice(0, 10) ?? '')
      setObservacoes(pendenciaEmEdicao.observacoes ?? '')
      return
    }

    if (clienteFixo) {
      setModoContato('existente')
      setClienteSelecionadoId(clienteFixo.id)
      setClienteSelecionadoResumo({
        nome: clienteFixo.nome,
        telefone: clienteFixo.telefone,
      })
      setTipoServicoId(clienteFixo.tipoServicoId ?? NENHUM_SERVICO)
      setTitulo(clienteFixo.tipoServicoNome ?? '')
    } else {
      setModoContato('existente')
      setClienteSelecionadoId('')
      setClienteSelecionadoResumo(null)
      setTipoServicoId(NENHUM_SERVICO)
      setTitulo('')
    }

    setBuscaCliente('')
    setNovoNome('')
    setNovoTelefone('')
    setNovoDocumento('')
    setValorTexto('')
    setDataReferencia(hojeIsoLocal())
    setDataPromessa('')
    setObservacoes('')
  }, [aberto, clienteFixo, pendenciaEmEdicao])

  const { data: clientesPaginados } = useQuery({
    queryKey: ['clientes', 'busca-devedor', buscaCliente],
    queryFn: () =>
      clientesApi.listar({
        busca: buscaCliente.trim() || undefined,
        tamanho: 8,
        ordenar: 'nome',
      }),
    enabled: aberto && !editando && !clienteFixo && modoContato === 'existente',
  })

  const { data: servicos = [] } = useQuery({
    queryKey: ['tipos-servico', 'ativos-devedor'],
    queryFn: () => servicosApi.listar(false),
    enabled: aberto,
  })

  const mutacaoSalvar = useMutation({
    mutationFn: async (abrirConversaDepois: boolean) => {
      const valorNumerico = interpretarValorMonetario(valorTexto)
      const servicoFinal = tipoServicoId === NENHUM_SERVICO ? null : tipoServicoId

      if (pendenciaEmEdicao) {
        const atualizada = await devedoresApi.atualizar(pendenciaEmEdicao.id, {
          tipoServicoId: servicoFinal,
          titulo: titulo.trim(),
          observacoes: observacoes.trim() || null,
          valorOriginal: valorNumerico,
          dataReferencia: dataReferencia || hojeIsoLocal(),
          dataPromessaPagamento: dataPromessa || null,
        })
        return { salva: atualizada, conversaAbertaId: null as string | null }
      }

      const criada = await devedoresApi.criar({
        clienteId:
          clienteFixo?.id || (modoContato === 'existente' ? clienteSelecionadoId || null : null),
        novoClienteNome: modoContato === 'novo' ? novoNome.trim() : null,
        novoClienteTelefone: modoContato === 'novo' ? novoTelefone.trim() : null,
        novoClienteDocumento: modoContato === 'novo' ? novoDocumento.trim() || null : null,
        conversaOrigemId: clienteFixo?.conversaOrigemId ?? null,
        tipoServicoId: servicoFinal,
        titulo: titulo.trim(),
        observacoes: observacoes.trim() || null,
        valorOriginal: valorNumerico,
        dataReferencia: dataReferencia || hojeIsoLocal(),
        dataPromessaPagamento: dataPromessa || null,
      })

      let conversaAbertaId: string | null = null
      if (abrirConversaDepois) {
        const resConversa = await devedoresApi.abrirConversa(criada.clienteId)
        conversaAbertaId = resConversa.conversaId
      }

      return { salva: criada, conversaAbertaId }
    },
    onSuccess: ({ salva, conversaAbertaId }) => {
      queryClient.invalidateQueries({ queryKey: ['devedores'] })
      queryClient.invalidateQueries({ queryKey: ['conversas'] })
      queryClient.invalidateQueries({ queryKey: ['painel-resumo'] })
      queryClient.invalidateQueries({ queryKey: ['clientes'] })
      toast.success(
        editando
          ? 'Pendência financeira atualizada.'
          : conversaAbertaId
            ? `${salva.clienteNome} registrado e conversa aberta em Minhas.`
            : salva.valorOriginal !== null
              ? `Pendência de ${formatarReais(salva.valorOriginal)} registrada para ${salva.clienteNome}.`
              : `${salva.clienteNome} adicionado à lista de devedores.`,
      )
      aoSalvar?.(salva)
      aoMudarAberto(false)
      if (conversaAbertaId) {
        navigate(`/atendimentos/${conversaAbertaId}?escopo=minhas`)
      }
    },
    onError: (erro) => {
      if (erro instanceof ErroDaApi) {
        setErroFormulario(erro.message)
      } else {
        setErroFormulario('Não foi possível salvar a pendência financeira.')
      }
    },
  })

  function validarFormulario() {
    setErroFormulario(null)

    if (!editando && !clienteFixo) {
      if (modoContato === 'existente' && !clienteSelecionadoId) {
        setErroFormulario('Selecione um cliente da lista ou cadastre um novo contato.')
        return false
      }
      if (modoContato === 'novo' && (!novoNome.trim() || !novoTelefone.trim())) {
        setErroFormulario('Informe o nome e o WhatsApp do novo contato.')
        return false
      }
    }

    if (!titulo.trim()) {
      setErroFormulario('Informe o motivo ou serviço referente à pendência.')
      return false
    }

    return true
  }

  function submeter(e: React.FormEvent) {
    e.preventDefault()
    if (!validarFormulario()) return
    mutacaoSalvar.mutate(false)
  }

  return (
    <Dialog open={aberto} onOpenChange={aoMudarAberto}>
      <DialogContent className="max-h-[92dvh] overflow-y-auto sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>
            {editando ? 'Editar pendência financeira' : 'Registrar contato devedor'}
          </DialogTitle>
          <DialogDescription>
            {editando
              ? 'Ajuste o valor, o motivo ou a data combinada para o pagamento.'
              : 'Registre quem ficou com valor em aberto no cartório. O valor é opcional se você ainda for fechar a conta depois.'}
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={submeter} className="space-y-4">
          {clienteFixo || editando ? (
            <div className="rounded-lg border border-linha bg-superficie/70 px-3.5 py-2.5">
              <p className="text-[11px] font-medium uppercase tracking-wider text-tinta-fraca">
                Contato vinculado
              </p>
              <p className="mt-0.5 text-sm font-semibold text-tinta">
                {clienteFixo?.nome ?? clienteSelecionadoResumo?.nome}
              </p>
              <p className="font-dado text-xs text-tinta-media">
                {formatarTelefone(
                  clienteFixo?.telefone ?? clienteSelecionadoResumo?.telefone ?? '',
                )}
              </p>
            </div>
          ) : (
            <div className="space-y-3">
              <div className="flex items-center justify-between gap-2">
                <Label>Quem está devendo?</Label>
                <div className="inline-flex rounded-md border border-linha bg-superficie p-0.5 text-xs">
                  <button
                    type="button"
                    onClick={() => setModoContato('existente')}
                    className={`inline-flex items-center gap-1 rounded px-2.5 py-1 font-medium transition-colors ${
                      modoContato === 'existente'
                        ? 'bg-branco text-tinta shadow-xs'
                        : 'text-tinta-media hover:text-tinta'
                    }`}
                  >
                    <Users className="size-3.5" />
                    Já cadastrado
                  </button>
                  <button
                    type="button"
                    onClick={() => setModoContato('novo')}
                    className={`inline-flex items-center gap-1 rounded px-2.5 py-1 font-medium transition-colors ${
                      modoContato === 'novo'
                        ? 'bg-branco text-tinta shadow-xs'
                        : 'text-tinta-media hover:text-tinta'
                    }`}
                  >
                    <UserPlus className="size-3.5" />
                    Novo contato
                  </button>
                </div>
              </div>

              {modoContato === 'existente' ? (
                <div className="space-y-2">
                  <div className="relative">
                    <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-tinta-fraca" />
                    <Input
                      value={buscaCliente}
                      onChange={(e) => setBuscaCliente(e.target.value)}
                      placeholder="Buscar cliente por nome ou WhatsApp..."
                      className="pl-9"
                    />
                  </div>
                  <div className="max-h-40 divide-y divide-linha overflow-y-auto rounded-lg border border-linha bg-branco">
                    {(clientesPaginados?.itens ?? []).length === 0 ? (
                      <div className="px-3 py-3 text-center text-xs text-tinta-media">
                        Nenhum cliente encontrado. Você pode usar a aba{' '}
                        <button
                          type="button"
                          onClick={() => {
                            setNovoNome(buscaCliente)
                            setModoContato('novo')
                          }}
                          className="font-semibold text-selo underline"
                        >
                          Novo contato
                        </button>{' '}
                        acima.
                      </div>
                    ) : (
                      (clientesPaginados?.itens ?? []).map((c) => {
                        const selecionado = c.id === clienteSelecionadoId
                        return (
                          <button
                            key={c.id}
                            type="button"
                            onClick={() => {
                              setClienteSelecionadoId(c.id)
                              setClienteSelecionadoResumo({
                                nome: c.nome,
                                telefone: c.telefoneWhatsapp,
                              })
                            }}
                            className={`flex w-full items-center justify-between gap-2 px-3 py-2 text-left text-xs transition-colors ${
                              selecionado ? 'bg-ambar/10 font-semibold text-tinta' : 'hover:bg-superficie/60'
                            }`}
                          >
                            <span className="truncate text-tinta">{c.nome}</span>
                            <span className="flex shrink-0 items-center gap-2 font-dado text-tinta-media">
                              {formatarTelefone(c.telefoneWhatsapp)}
                              {selecionado ? <Check className="size-3.5 text-selo" /> : null}
                            </span>
                          </button>
                        )
                      })
                    )}
                  </div>
                </div>
              ) : (
                <div className="grid gap-3 rounded-lg border border-linha bg-superficie/50 p-3 sm:grid-cols-2">
                  <div className="space-y-1 sm:col-span-2">
                    <Label htmlFor="novo-devedor-nome">Nome completo ou razão social</Label>
                    <Input
                      id="novo-devedor-nome"
                      value={novoNome}
                      onChange={(e) => setNovoNome(e.target.value)}
                      placeholder="Ex.: Dr. Roberto Almeida"
                    />
                  </div>
                  <div className="space-y-1">
                    <Label htmlFor="novo-devedor-telefone">WhatsApp com DDD</Label>
                    <Input
                      id="novo-devedor-telefone"
                      value={novoTelefone}
                      onChange={(e) => setNovoTelefone(e.target.value)}
                      placeholder="(38) 99912-3456"
                    />
                  </div>
                  <div className="space-y-1">
                    <Label htmlFor="novo-devedor-doc">CPF / CNPJ (opcional)</Label>
                    <Input
                      id="novo-devedor-doc"
                      value={novoDocumento}
                      onChange={(e) => setNovoDocumento(e.target.value)}
                      placeholder="Somente números"
                    />
                  </div>
                </div>
              )}
            </div>
          )}

          <div className="space-y-1.5">
            <Label htmlFor="pendencia-titulo">Motivo ou ato da pendência</Label>
            <Input
              id="pendencia-titulo"
              value={titulo}
              onChange={(e) => setTitulo(e.target.value)}
              placeholder="Ex.: 2ª via de certidão de inteiro teor"
            />
            <div className="flex flex-wrap gap-1 pt-0.5">
              {SUGESTOES_DE_MOTIVO.map((sugestao) => (
                <button
                  key={sugestao}
                  type="button"
                  onClick={() => setTitulo(sugestao)}
                  className="rounded-full border border-linha bg-superficie px-2 py-0.5 text-[11px] text-tinta-media transition-colors hover:border-linha-forte hover:text-tinta"
                >
                  {sugestao}
                </button>
              ))}
            </div>
          </div>

          <div className="grid gap-3 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="pendencia-valor">Valor em aberto (R$)</Label>
              <Input
                id="pendencia-valor"
                inputMode="decimal"
                value={valorTexto}
                onChange={(e) => setValorTexto(e.target.value)}
                placeholder="Ex.: 185,00 (opcional)"
              />
              <p className="text-[11px] text-tinta-fraca">
                Deixe em branco para marcar como devedor com valor a apurar.
              </p>
            </div>

            <div className="space-y-1.5">
              <Label>Serviço do catálogo (opcional)</Label>
              <Select
                value={tipoServicoId}
                onValueChange={(valor) => {
                  setTipoServicoId(valor)
                  if (!titulo.trim() && valor !== NENHUM_SERVICO) {
                    const encontrado = servicos.find((s) => s.id === valor)
                    if (encontrado) setTitulo(encontrado.nome)
                  }
                }}
              >
                <SelectTrigger>
                  <SelectValue placeholder="Vincular serviço..." />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={NENHUM_SERVICO}>Sem vínculo de catálogo</SelectItem>
                  {servicos.map((s) => (
                    <SelectItem key={s.id} value={s.id}>
                      {s.nome}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>

          <div className="grid gap-3 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="pendencia-data-ref">Data do serviço</Label>
              <Input
                id="pendencia-data-ref"
                type="date"
                value={dataReferencia}
                onChange={(e) => setDataReferencia(e.target.value)}
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="pendencia-promessa">Prometeu pagar em (opcional)</Label>
              <Input
                id="pendencia-promessa"
                type="date"
                value={dataPromessa}
                onChange={(e) => setDataPromessa(e.target.value)}
              />
              <div className="flex flex-wrap gap-1 pt-0.5">
                {[
                  { rotulo: 'Hoje', dias: 0 },
                  { rotulo: 'Amanhã', dias: 1 },
                  { rotulo: '+7 dias', dias: 7 },
                  { rotulo: '+15 dias', dias: 15 },
                ].map((atalho) => (
                  <button
                    key={atalho.rotulo}
                    type="button"
                    onClick={() => setDataPromessa(somarDiasIsoLocal(atalho.dias))}
                    className="inline-flex items-center gap-1 rounded-full border border-linha bg-superficie px-2 py-0.5 text-[11px] text-tinta-media hover:border-linha-forte hover:text-tinta"
                  >
                    <Calendar className="size-2.5" />
                    {atalho.rotulo}
                  </button>
                ))}
                {dataPromessa ? (
                  <button
                    type="button"
                    onClick={() => setDataPromessa('')}
                    className="rounded-full px-1.5 py-0.5 text-[11px] text-tinta-fraca hover:text-tinta"
                  >
                    Limpar
                  </button>
                ) : null}
              </div>
            </div>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="pendencia-obs">Observações internas (opcional)</Label>
            <Textarea
              id="pendencia-obs"
              rows={2}
              value={observacoes}
              onChange={(e) => setObservacoes(e.target.value)}
              placeholder="Ex.: Retirou a certidão pelo portador e combinou de transferir via Pix até sexta-feira."
            />
          </div>

          {erroFormulario ? (
            <p role="alert" className="rounded-md border border-selo/30 bg-selo/8 px-3 py-2 text-xs text-selo">
              {erroFormulario}
            </p>
          ) : null}

          <DialogFooter className="flex-wrap gap-2 pt-1">
            <Button type="button" variant="outline" onClick={() => aoMudarAberto(false)}>
              Cancelar
            </Button>
            {!editando ? (
              <Button
                type="button"
                variant="outline"
                disabled={mutacaoSalvar.isPending}
                onClick={() => {
                  if (!validarFormulario()) return
                  mutacaoSalvar.mutate(true)
                }}
                title="Salva o contato devedor e já abre a conversa em Minhas (sem mensagem pronta e sem robô)"
              >
                <MessageSquare className="size-3.5" />
                Salvar e abrir conversa
              </Button>
            ) : null}
            <Button type="submit" disabled={mutacaoSalvar.isPending}>
              {mutacaoSalvar.isPending
                ? 'Salvando...'
                : editando
                  ? 'Salvar alterações'
                  : 'Registrar devedor'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
