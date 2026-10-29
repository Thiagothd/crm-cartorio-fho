import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, FileSpreadsheet, Search, Smartphone, Upload, Users } from 'lucide-react'
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
import { clientesApi, devedoresApi, importacaoDeContatosApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { formatarReais, formatarTelefone } from '@/lib/formatacao'
import {
  interpretarTextoDeDevedores,
  interpretarValorMonetario,
} from './utilitarios'
import type { ItemDeImportacaoDeDevedor } from '@/lib/api/tipos'

type AbaImportacao = 'texto' | 'clientes' | 'whatsapp'

export function DialogoImportarDevedores({
  aberto,
  aoMudarAberto,
}: {
  aberto: boolean
  aoMudarAberto: (aberto: boolean) => void
}) {
  const queryClient = useQueryClient()

  const [aba, setAba] = useState<AbaImportacao>('texto')
  const [tituloPadrao, setTituloPadrao] = useState('Pendência de emolumentos / serviço')
  const [dataPromessaPadrao, setDataPromessaPadrao] = useState('')

  // Aba 1: Texto / CSV
  const [textoColado, setTextoColado] = useState('')

  // Aba 2: Clientes já no CRM
  const [buscaClientes, setBuscaClientes] = useState('')
  const [clientesMarcados, setClientesMarcados] = useState<
    Record<string, { nome: string; telefone: string; valorTexto: string; motivo: string }>
  >({})

  // Aba 3: Agenda do WhatsApp
  const [buscaWhatsapp, setBuscaWhatsapp] = useState('')
  const [whatsappMarcados, setWhatsappMarcados] = useState<
    Record<string, { nome: string; telefone: string; valorTexto: string; motivo: string }>
  >({})

  const [erro, setErro] = useState<string | null>(null)

  const itensDoTexto = useMemo(
    () => interpretarTextoDeDevedores(textoColado, tituloPadrao),
    [textoColado, tituloPadrao],
  )

  const { data: clientesCrm } = useQuery({
    queryKey: ['clientes', 'importar-devedores', buscaClientes],
    queryFn: () =>
      clientesApi.listar({
        busca: buscaClientes.trim() || undefined,
        tamanho: 60,
        ordenar: 'nome',
      }),
    enabled: aberto && aba === 'clientes',
  })

  const {
    data: previaWhatsapp,
    isLoading: carregandoWhatsapp,
    refetch: buscarAgendaWhatsapp,
  } = useQuery({
    queryKey: ['agenda-whatsapp-devedores'],
    queryFn: () => importacaoDeContatosApi.analisarWhatsApp(),
    enabled: aberto && aba === 'whatsapp',
    retry: false,
  })

  const contatosWhatsappFiltrados = useMemo(() => {
    const linhas = previaWhatsapp?.linhas ?? []
    const validas = linhas.filter((l) => Boolean(l.telefoneE164))
    const termo = buscaWhatsapp.trim().toLowerCase()
    if (!termo) return validas
    return validas.filter(
      (l) =>
        (l.nome ?? '').toLowerCase().includes(termo) ||
        (l.telefoneE164 ?? '').includes(termo),
    )
  }, [previaWhatsapp, buscaWhatsapp])

  function lerArquivoCsv(e: React.ChangeEvent<HTMLInputElement>) {
    const arquivo = e.target.files?.[0]
    if (!arquivo) return
    const reader = new FileReader()
    reader.onload = () => {
      if (typeof reader.result === 'string') {
        setTextoColado(reader.result)
      }
    }
    reader.readAsText(arquivo, 'utf-8')
  }

  function montarItensFinais(): ItemDeImportacaoDeDevedor[] {
    if (aba === 'texto') {
      return itensDoTexto
    }

    if (aba === 'clientes') {
      return Object.entries(clientesMarcados).map(([clienteId, info]) => ({
        clienteId,
        nome: info.nome,
        telefone: info.telefone,
        valorOriginal: interpretarValorMonetario(info.valorTexto),
        titulo: info.motivo.trim() || tituloPadrao,
      }))
    }

    return Object.values(whatsappMarcados).map((info) => ({
      nome: info.nome,
      telefone: info.telefone,
      valorOriginal: interpretarValorMonetario(info.valorTexto),
      titulo: info.motivo.trim() || tituloPadrao,
    }))
  }

  const itensFinais = montarItensFinais()

  const mutacaoImportar = useMutation({
    mutationFn: () =>
      devedoresApi.importarEmLote({
        tituloPadrao: tituloPadrao.trim() || 'Pendência de emolumentos / serviço',
        dataPromessaPadrao: dataPromessaPadrao || null,
        itens: itensFinais,
      }),
    onSuccess: (res) => {
      queryClient.invalidateQueries({ queryKey: ['devedores'] })
      queryClient.invalidateQueries({ queryKey: ['conversas'] })
      queryClient.invalidateQueries({ queryKey: ['clientes'] })
      toast.success(
        `${res.pendenciasCriadas} ${
          res.pendenciasCriadas === 1 ? 'devedor adicionado' : 'devedores adicionados'
        } com sucesso.`,
      )
      setTextoColado('')
      setClientesMarcados({})
      setWhatsappMarcados({})
      aoMudarAberto(false)
    },
    onError: (falha) => {
      if (falha instanceof ErroDaApi) {
        setErro(falha.message)
      } else {
        setErro('Não foi possível concluir a importação em lote.')
      }
    },
  })

  return (
    <Dialog open={aberto} onOpenChange={aoMudarAberto}>
      <DialogContent className="max-h-[92dvh] overflow-y-auto sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>Importar e adicionar vários devedores</DialogTitle>
          <DialogDescription>
            Adicione vários contatos de uma vez colando uma lista/planilha, selecionando clientes já
            cadastrados no CRM ou puxando direto da agenda do WhatsApp.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="grid grid-cols-3 gap-1.5 rounded-lg border border-linha bg-superficie p-1">
            <button
              type="button"
              onClick={() => setAba('texto')}
              className={`flex items-center justify-center gap-1.5 rounded-md px-2.5 py-2 text-xs font-semibold transition-colors ${
                aba === 'texto'
                  ? 'bg-branco text-tinta shadow-xs'
                  : 'text-tinta-media hover:text-tinta'
              }`}
            >
              <FileSpreadsheet className="size-3.5" />
              Lista ou Planilha
            </button>
            <button
              type="button"
              onClick={() => setAba('clientes')}
              className={`flex items-center justify-center gap-1.5 rounded-md px-2.5 py-2 text-xs font-semibold transition-colors ${
                aba === 'clientes'
                  ? 'bg-branco text-tinta shadow-xs'
                  : 'text-tinta-media hover:text-tinta'
              }`}
            >
              <Users className="size-3.5" />
              Clientes do CRM
            </button>
            <button
              type="button"
              onClick={() => setAba('whatsapp')}
              className={`flex items-center justify-center gap-1.5 rounded-md px-2.5 py-2 text-xs font-semibold transition-colors ${
                aba === 'whatsapp'
                  ? 'bg-branco text-tinta shadow-xs'
                  : 'text-tinta-media hover:text-tinta'
              }`}
            >
              <Smartphone className="size-3.5" />
              Agenda do WhatsApp
            </button>
          </div>

          <div className="grid gap-3 rounded-lg border border-linha bg-superficie/40 p-3 sm:grid-cols-2">
            <div className="space-y-1">
              <Label htmlFor="import-motivo-padrao">Motivo padrão (quando não informado na linha)</Label>
              <Input
                id="import-motivo-padrao"
                value={tituloPadrao}
                onChange={(e) => setTituloPadrao(e.target.value)}
                placeholder="Ex.: Pendência de emolumentos / serviço"
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="import-promessa-padrao">Promessa de pagamento padrão (opcional)</Label>
              <Input
                id="import-promessa-padrao"
                type="date"
                value={dataPromessaPadrao}
                onChange={(e) => setDataPromessaPadrao(e.target.value)}
              />
            </div>
          </div>

          {aba === 'texto' ? (
            <div className="space-y-3">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <Label htmlFor="import-texto-area">
                  Cole uma linha por pessoa: <span className="font-dado text-tinta-media">Nome; WhatsApp; Valor; Motivo</span>
                </Label>
                <label className="inline-flex cursor-pointer items-center gap-1.5 rounded-md border border-linha bg-branco px-2.5 py-1 text-xs font-medium text-tinta hover:bg-superficie">
                  <Upload className="size-3.5" />
                  Carregar arquivo .CSV / .TXT
                  <input
                    type="file"
                    accept=".csv,.txt"
                    onChange={lerArquivoCsv}
                    className="hidden"
                  />
                </label>
              </div>
              <Textarea
                id="import-texto-area"
                rows={5}
                value={textoColado}
                onChange={(e) => setTextoColado(e.target.value)}
                placeholder={
                  'Dr. Roberto Almeida; (38) 99912-3456; 320,00; Escritura de compra e venda\nMaria Aparecida; 38998112233; 95,00; 2ª via de certidão\nConstrutora Horizonte; 38991445566'
                }
                className="font-dado text-xs"
              />

              {itensDoTexto.length > 0 ? (
                <div className="rounded-lg border border-linha bg-branco">
                  <div className="border-b border-linha bg-superficie/60 px-3 py-1.5 text-xs font-semibold text-tinta">
                    Prévia reconhecida ({itensDoTexto.length}{' '}
                    {itensDoTexto.length === 1 ? 'contato' : 'contatos'})
                  </div>
                  <div className="max-h-40 divide-y divide-linha overflow-y-auto">
                    {itensDoTexto.map((item, idx) => (
                      <div
                        key={idx}
                        className="flex items-center justify-between gap-2 px-3 py-1.5 text-xs"
                      >
                        <div className="min-w-0">
                          <span className="font-semibold text-tinta">{item.nome}</span>
                          <span className="ml-2 font-dado text-tinta-media">{item.telefone}</span>
                          <span className="ml-2 text-tinta-fraca">· {item.titulo}</span>
                        </div>
                        <span className="shrink-0 font-dado font-semibold text-tinta">
                          {item.valorOriginal !== null && item.valorOriginal !== undefined
                            ? formatarReais(item.valorOriginal)
                            : 'Valor a apurar'}
                        </span>
                      </div>
                    ))}
                  </div>
                </div>
              ) : null}
            </div>
          ) : null}

          {aba === 'clientes' ? (
            <div className="space-y-3">
              <div className="relative">
                <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-tinta-fraca" />
                <Input
                  value={buscaClientes}
                  onChange={(e) => setBuscaClientes(e.target.value)}
                  placeholder="Filtrar clientes já cadastrados por nome ou telefone..."
                  className="pl-9"
                />
              </div>
              <div className="max-h-64 divide-y divide-linha overflow-y-auto rounded-lg border border-linha bg-branco">
                {(clientesCrm?.itens ?? []).map((c) => {
                  const selecionado = Boolean(clientesMarcados[c.id])
                  return (
                    <div key={c.id} className="p-2.5">
                      <div className="flex items-center justify-between gap-2">
                        <button
                          type="button"
                          onClick={() => {
                            setClientesMarcados((atual) => {
                              const copia = { ...atual }
                              if (copia[c.id]) {
                                delete copia[c.id]
                              } else {
                                copia[c.id] = {
                                  nome: c.nome,
                                  telefone: c.telefoneWhatsapp,
                                  valorTexto: '',
                                  motivo: '',
                                }
                              }
                              return copia
                            })
                          }}
                          className="flex min-w-0 flex-1 items-center gap-2 text-left text-xs"
                        >
                          <span
                            className={`flex size-4 shrink-0 items-center justify-center rounded border ${
                              selecionado
                                ? 'border-selo bg-selo text-branco'
                                : 'border-linha bg-branco'
                            }`}
                          >
                            {selecionado ? <Check className="size-3" /> : null}
                          </span>
                          <span className="truncate font-semibold text-tinta">{c.nome}</span>
                          <span className="shrink-0 font-dado text-tinta-media">
                            {formatarTelefone(c.telefoneWhatsapp)}
                          </span>
                        </button>
                      </div>

                      {selecionado ? (
                        <div className="mt-2 grid gap-2 pl-6 sm:grid-cols-2">
                          <Input
                            value={clientesMarcados[c.id]?.valorTexto ?? ''}
                            onChange={(e) =>
                              setClientesMarcados((atual) => ({
                                ...atual,
                                [c.id]: { ...atual[c.id], valorTexto: e.target.value },
                              }))
                            }
                            placeholder="Valor R$ (opcional)"
                            className="h-8 text-xs"
                          />
                          <Input
                            value={clientesMarcados[c.id]?.motivo ?? ''}
                            onChange={(e) =>
                              setClientesMarcados((atual) => ({
                                ...atual,
                                [c.id]: { ...atual[c.id], motivo: e.target.value },
                              }))
                            }
                            placeholder={tituloPadrao || 'Motivo / serviço'}
                            className="h-8 text-xs"
                          />
                        </div>
                      ) : null}
                    </div>
                  )
                })}
              </div>
            </div>
          ) : null}

          {aba === 'whatsapp' ? (
            <div className="space-y-3">
              <div className="flex items-center gap-2">
                <div className="relative flex-1">
                  <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-tinta-fraca" />
                  <Input
                    value={buscaWhatsapp}
                    onChange={(e) => setBuscaWhatsapp(e.target.value)}
                    placeholder="Buscar na agenda do WhatsApp pareado..."
                    className="pl-9"
                  />
                </div>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() => buscarAgendaWhatsapp()}
                  disabled={carregandoWhatsapp}
                >
                  Atualizar agenda
                </Button>
              </div>

              <div className="max-h-64 divide-y divide-linha overflow-y-auto rounded-lg border border-linha bg-branco">
                {carregandoWhatsapp ? (
                  <p className="p-4 text-center text-xs text-tinta-media">
                    Consultando agenda do WhatsApp do cartório...
                  </p>
                ) : contatosWhatsappFiltrados.length === 0 ? (
                  <p className="p-4 text-center text-xs text-tinta-media">
                    Nenhum contato retornado pela agenda do WhatsApp neste momento. Use a aba{' '}
                    <button
                      type="button"
                      onClick={() => setAba('texto')}
                      className="font-semibold text-selo underline"
                    >
                      Lista ou Planilha
                    </button>{' '}
                    ou{' '}
                    <button
                      type="button"
                      onClick={() => setAba('clientes')}
                      className="font-semibold text-selo underline"
                    >
                      Clientes do CRM
                    </button>
                    .
                  </p>
                ) : (
                  contatosWhatsappFiltrados.map((item) => {
                    const chave = item.telefoneE164!
                    const selecionado = Boolean(whatsappMarcados[chave])
                    const nome = item.nome || formatarTelefone(chave)
                    return (
                      <div key={chave} className="p-2.5">
                        <button
                          type="button"
                          onClick={() => {
                            setWhatsappMarcados((atual) => {
                              const copia = { ...atual }
                              if (copia[chave]) {
                                delete copia[chave]
                              } else {
                                copia[chave] = {
                                  nome,
                                  telefone: chave,
                                  valorTexto: '',
                                  motivo: '',
                                }
                              }
                              return copia
                            })
                          }}
                          className="flex w-full items-center gap-2 text-left text-xs"
                        >
                          <span
                            className={`flex size-4 shrink-0 items-center justify-center rounded border ${
                              selecionado
                                ? 'border-selo bg-selo text-branco'
                                : 'border-linha bg-branco'
                            }`}
                          >
                            {selecionado ? <Check className="size-3" /> : null}
                          </span>
                          <span className="truncate font-semibold text-tinta">{nome}</span>
                          <span className="shrink-0 font-dado text-tinta-media">
                            {formatarTelefone(chave)}
                          </span>
                        </button>

                        {selecionado ? (
                          <div className="mt-2 grid gap-2 pl-6 sm:grid-cols-2">
                            <Input
                              value={whatsappMarcados[chave]?.valorTexto ?? ''}
                              onChange={(e) =>
                                setWhatsappMarcados((atual) => ({
                                  ...atual,
                                  [chave]: { ...atual[chave], valorTexto: e.target.value },
                                }))
                              }
                              placeholder="Valor R$ (opcional)"
                              className="h-8 text-xs"
                            />
                            <Input
                              value={whatsappMarcados[chave]?.motivo ?? ''}
                              onChange={(e) =>
                                setWhatsappMarcados((atual) => ({
                                  ...atual,
                                  [chave]: { ...atual[chave], motivo: e.target.value },
                                }))
                              }
                              placeholder={tituloPadrao || 'Motivo / serviço'}
                              className="h-8 text-xs"
                            />
                          </div>
                        ) : null}
                      </div>
                    )
                  })
                )}
              </div>
            </div>
          ) : null}

          {erro ? (
            <p role="alert" className="rounded-md border border-selo/30 bg-selo/8 px-3 py-2 text-xs text-selo">
              {erro}
            </p>
          ) : null}

          <DialogFooter className="gap-2 pt-1">
            <Button type="button" variant="outline" onClick={() => aoMudarAberto(false)}>
              Cancelar
            </Button>
            <Button
              type="button"
              disabled={itensFinais.length === 0 || mutacaoImportar.isPending}
              onClick={() => {
                setErro(null)
                mutacaoImportar.mutate()
              }}
            >
              {mutacaoImportar.isPending
                ? 'Importando...'
                : `Importar ${itensFinais.length} ${itensFinais.length === 1 ? 'devedor' : 'devedores'}`}
            </Button>
          </DialogFooter>
        </div>
      </DialogContent>
    </Dialog>
  )
}
