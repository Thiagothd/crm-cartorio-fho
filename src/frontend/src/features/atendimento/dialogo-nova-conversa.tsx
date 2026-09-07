import { useState } from 'react'
import { Plus } from 'lucide-react'
import { toast } from 'sonner'
import { useQuery } from '@tanstack/react-query'
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
  DialogTrigger,
} from '@/components/ui/dialog'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { clientesApi, conversasApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { formatarTelefone } from '@/lib/formatacao'
import { usarInvalidarAtendimento, usarEtapas, usarSetores } from './consultas'

const SEM_VALOR = '__nenhum__'

/**
 * Abre um atendimento a partir de um cliente já cadastrado, ou cadastra o cliente
 * na hora. Na Fase 2 a conversa nasce sozinha quando a mensagem chega no webhook —
 * este diálogo continua útil para atendimento que começou no balcão.
 */
export function DialogoNovaConversa({
  aoCriar,
  aberto: abertoControlado,
  aoMudarAberto,
}: {
  aoCriar: (conversaId: string) => void
  /**
   * Omitido: o diálogo é dono do próprio estado e desenha o botão "Novo
   * atendimento" — é o uso da barra de filtros da inbox, inalterado.
   * Informado: quem chama controla o aberto/fechado, e NENHUM gatilho é
   * desenhado — o gatilho é de quem chama (ex.: o cartão de destaque da
   * Visão Geral).
   */
  aberto?: boolean
  aoMudarAberto?: (aberto: boolean) => void
}) {
  const controlado = abertoControlado !== undefined
  const [abertoInterno, setAbertoInterno] = useState(false)
  const aberto = controlado ? abertoControlado : abertoInterno

  function definirAberto(valor: boolean) {
    if (!controlado) setAbertoInterno(valor)
    aoMudarAberto?.(valor)
    if (!valor) limpar()
  }

  const [busca, setBusca] = useState('')
  const [clienteId, setClienteId] = useState('')
  const [setorId, setSetorId] = useState(SEM_VALOR)
  const [etapaId, setEtapaId] = useState(SEM_VALOR)
  const [primeiraMensagem, setPrimeiraMensagem] = useState('')
  const [novoNome, setNovoNome] = useState('')
  const [novoTelefone, setNovoTelefone] = useState('')
  const [modoNovoCliente, setModoNovoCliente] = useState(false)
  const [salvando, setSalvando] = useState(false)

  const { data: setores } = usarSetores()
  const { data: etapas } = usarEtapas()
  const invalidar = usarInvalidarAtendimento()

  const { data: clientes } = useQuery({
    queryKey: ['clientes-para-conversa', busca],
    queryFn: () => clientesApi.listar({ busca: busca || undefined, tamanho: 20 }),
    enabled: aberto && !modoNovoCliente,
  })

  function limpar() {
    setBusca('')
    setClienteId('')
    setSetorId(SEM_VALOR)
    setEtapaId(SEM_VALOR)
    setPrimeiraMensagem('')
    setNovoNome('')
    setNovoTelefone('')
    setModoNovoCliente(false)
  }

  async function salvar() {
    setSalvando(true)

    try {
      let id = clienteId

      if (modoNovoCliente) {
        if (!novoNome.trim() || !novoTelefone.trim()) {
          toast.info('Informe nome e WhatsApp do cliente.')
          return
        }

        const criado = await clientesApi.criar({
          nome: novoNome.trim(),
          telefoneWhatsapp: novoTelefone.trim(),
        })

        id = criado.id
      }

      if (!id) {
        toast.info('Escolha o cliente.')
        return
      }

      const conversa = await conversasApi.criar({
        clienteId: id,
        setorId: setorId === SEM_VALOR ? null : setorId,
        etapaId: etapaId === SEM_VALOR ? null : etapaId,
        primeiraMensagem: primeiraMensagem.trim() || null,
      })

      invalidar()
      aoCriar(conversa.id)
      definirAberto(false)
      toast.success(`Atendimento ${conversa.protocolo} aberto.`)
    } catch (falha) {
      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível abrir o atendimento.')
    } finally {
      setSalvando(false)
    }
  }

  return (
    <Dialog open={aberto} onOpenChange={definirAberto}>
      {controlado ? null : (
        <DialogTrigger asChild>
          {/* Fase 3.2 (docs/DESIGN.md, secao 16.5): desceu de bordo (default)
              para outline em 2026-09-06. Com o badge de nao lidas virando
              solido em bordo (item-de-conversa.tsx), o orcamento de 3 por tela
              (2.1) passa a marcar so o que exige acao — o que chegou sem
              resposta, e Enviar — nao mais a criacao manual de atendimento. */}
          <Button size="sm" variant="outline">
            <Plus />
            Novo atendimento
          </Button>
        </DialogTrigger>
      )}

      <DialogContent>
        <DialogHeader>
          <DialogTitle>Novo atendimento</DialogTitle>
          <DialogDescription>
            Abre uma conversa e gera o protocolo automaticamente.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-4">
          {modoNovoCliente ? (
            <>
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="novo-nome">Nome do cliente</Label>
                <Input
                  id="novo-nome"
                  value={novoNome}
                  onChange={(e) => setNovoNome(e.target.value)}
                  placeholder="Maria Aparecida Souza"
                />
              </div>

              <div className="flex flex-col gap-1.5">
                <Label htmlFor="novo-telefone">WhatsApp</Label>
                <Input
                  id="novo-telefone"
                  value={novoTelefone}
                  onChange={(e) => setNovoTelefone(e.target.value)}
                  placeholder="38999000045"
                />
                <p className="text-xs text-tinta-media">
                  Com DDD, só números. O código do país é preenchido sozinho.
                </p>
              </div>
            </>
          ) : (
            <>
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="busca-cliente">Cliente</Label>
                <Input
                  id="busca-cliente"
                  value={busca}
                  onChange={(e) => setBusca(e.target.value)}
                  placeholder="Buscar por nome ou telefone"
                />
              </div>

              <div className="max-h-48 overflow-y-auto rounded-md border border-linha">
                {clientes?.itens.length ? (
                  clientes.itens.map((cliente) => (
                    <button
                      key={cliente.id}
                      type="button"
                      onClick={() => setClienteId(cliente.id)}
                      className={`flex w-full items-center justify-between gap-2 border-b border-linha px-3 py-2 text-left text-sm last:border-b-0 transition-colors ${
                        clienteId === cliente.id ? 'bg-primary/10' : 'hover:bg-accent'
                      }`}
                    >
                      <span className="min-w-0">
                        <span className="block truncate font-medium">{cliente.nome}</span>
                        <span className="block text-xs text-tinta-media">
                          {formatarTelefone(cliente.telefoneWhatsapp)}
                        </span>
                      </span>

                      {cliente.temConversaAtiva ? (
                        <span className="shrink-0 text-[11px] text-tinta-media">
                          já tem conversa aberta
                        </span>
                      ) : null}
                    </button>
                  ))
                ) : (
                  <p className="px-3 py-4 text-center text-xs text-tinta-media">
                    Nenhum cliente encontrado.
                  </p>
                )}
              </div>
            </>
          )}

          <button
            type="button"
            onClick={() => setModoNovoCliente((v) => !v)}
            className="w-fit text-xs text-primary hover:underline"
          >
            {modoNovoCliente ? 'Escolher cliente já cadastrado' : 'Cadastrar cliente novo'}
          </button>

          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <div className="flex flex-col gap-1.5">
              <Label>Etapa inicial</Label>
              <Select value={etapaId} onValueChange={setEtapaId}>
                <SelectTrigger>
                  <SelectValue placeholder="Primeira etapa" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={SEM_VALOR}>Primeira do funil</SelectItem>
                  {etapas?.map((etapa) => (
                    <SelectItem key={etapa.id} value={etapa.id}>
                      {etapa.nome}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className="flex flex-col gap-1.5">
              <Label>Setor</Label>
              <Select value={setorId} onValueChange={setSetorId}>
                <SelectTrigger>
                  <SelectValue placeholder="Setor" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={SEM_VALOR}>Definir depois</SelectItem>
                  {setores?.map((setor) => (
                    <SelectItem key={setor.id} value={setor.id}>
                      {setor.nome}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="primeira">Primeira mensagem do cliente (opcional)</Label>
            <Textarea
              id="primeira"
              value={primeiraMensagem}
              onChange={(e) => setPrimeiraMensagem(e.target.value)}
              placeholder="O que o cliente pediu"
              rows={2}
            />
          </div>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => definirAberto(false)}>
            Cancelar
          </Button>
          <Button onClick={() => void salvar()} disabled={salvando}>
            Abrir atendimento
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
