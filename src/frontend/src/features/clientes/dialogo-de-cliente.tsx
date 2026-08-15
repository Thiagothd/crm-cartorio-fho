import { useEffect, useState, type ReactNode } from 'react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Textarea } from '@/components/ui/textarea'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
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
import { clientesApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import type {
  Cliente,
  DadosDeCliente,
  EstadoCivil,
  RegimeDeBens,
  TipoDePessoa,
} from '@/lib/api/tipos'

const estadosCivis: { valor: EstadoCivil; rotulo: string }[] = [
  { valor: 'NaoInformado', rotulo: 'Não informado' },
  { valor: 'Solteiro', rotulo: 'Solteiro(a)' },
  { valor: 'Casado', rotulo: 'Casado(a)' },
  { valor: 'Divorciado', rotulo: 'Divorciado(a)' },
  { valor: 'Viuvo', rotulo: 'Viúvo(a)' },
  { valor: 'Separado', rotulo: 'Separado(a)' },
  { valor: 'UniaoEstavel', rotulo: 'União estável' },
]

const regimes: { valor: RegimeDeBens; rotulo: string }[] = [
  { valor: 'NaoInformado', rotulo: 'Não informado' },
  { valor: 'ComunhaoParcial', rotulo: 'Comunhão parcial' },
  { valor: 'ComunhaoUniversal', rotulo: 'Comunhão universal' },
  { valor: 'SeparacaoTotal', rotulo: 'Separação total' },
  { valor: 'SeparacaoObrigatoria', rotulo: 'Separação obrigatória' },
  { valor: 'ParticipacaoFinalNosAquestos', rotulo: 'Participação final nos aquestos' },
]

const VAZIO: DadosDeCliente = {
  nome: '',
  telefoneWhatsapp: '',
  tipo: 'Fisica',
  estadoCivil: 'NaoInformado',
  regimeDeBens: 'NaoInformado',
  ativo: true,
}

function dadosDoCliente(cliente: Cliente | null): DadosDeCliente {
  if (!cliente) return { ...VAZIO }

  const { id: _id, totalConversas: _total, temConversaAtiva: _ativa, percentualDeQualificacao: _percentual, idade: _idade, primeiroContatoEm: _primeiro, ...dados } = cliente
  return dados
}

export function DialogoDeCliente({
  cliente,
  aberto,
  aoFechar,
  aoSalvar,
}: {
  cliente: Cliente | null
  aberto: boolean
  aoFechar: () => void
  aoSalvar: () => void
}) {
  const [dados, setDados] = useState<DadosDeCliente>(VAZIO)
  const [salvando, setSalvando] = useState(false)

  useEffect(() => {
    if (aberto) setDados(dadosDoCliente(cliente))
  }, [aberto, cliente])

  function atualizar<K extends keyof DadosDeCliente>(campo: K, valor: DadosDeCliente[K]) {
    setDados((atuais) => ({ ...atuais, [campo]: valor }))
  }

  async function salvar() {
    setSalvando(true)

    try {
      if (cliente) await clientesApi.atualizar(cliente.id, dados)
      else await clientesApi.criar(dados)

      toast.success(cliente ? 'Cliente atualizado.' : 'Cliente cadastrado.')
      aoSalvar()
      aoFechar()
    } catch (falha) {
      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível salvar.')
    } finally {
      setSalvando(false)
    }
  }

  const fisica = (dados.tipo ?? 'Fisica') === 'Fisica'

  return (
    <Dialog open={aberto} onOpenChange={(valor) => !valor && aoFechar()}>
      <DialogContent className="max-w-3xl">
        <DialogHeader>
          <DialogTitle>{cliente ? 'Editar cliente' : 'Novo cliente'}</DialogTitle>
          <DialogDescription>
            Nome e WhatsApp bastam para iniciar. A qualificação pode ser completada conforme o ato avança.
          </DialogDescription>
        </DialogHeader>

        <Tabs defaultValue="contato">
          <TabsList className="grid h-auto w-full grid-cols-4">
            <TabsTrigger value="contato">Contato</TabsTrigger>
            <TabsTrigger value="qualificacao">Qualificação</TabsTrigger>
            <TabsTrigger value="endereco">Endereço</TabsTrigger>
            <TabsTrigger value="observacoes">Observações</TabsTrigger>
          </TabsList>

          <TabsContent value="contato" className="mt-4 grid gap-4">
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-[1fr_180px]">
              <Campo rotulo="Nome" htmlFor="cliente-nome" obrigatorio>
                <Input id="cliente-nome" autoFocus value={dados.nome} onChange={(e) => atualizar('nome', e.target.value)} />
              </Campo>
              <Campo rotulo="Tipo de pessoa">
                <Select value={dados.tipo ?? 'Fisica'} onValueChange={(valor) => atualizar('tipo', valor as TipoDePessoa)}>
                  <SelectTrigger><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="Fisica">Pessoa física</SelectItem>
                    <SelectItem value="Juridica">Pessoa jurídica</SelectItem>
                  </SelectContent>
                </Select>
              </Campo>
            </div>

            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              <Campo rotulo="WhatsApp" htmlFor="cliente-whatsapp" obrigatorio ajuda="DDD e número; o sistema inclui o código 55.">
                <Input id="cliente-whatsapp" type="tel" autoComplete="tel" value={dados.telefoneWhatsapp} onChange={(e) => atualizar('telefoneWhatsapp', e.target.value)} placeholder="(38) 99999-0001" />
              </Campo>
              <Campo rotulo="Telefone alternativo" htmlFor="cliente-telefone-alternativo">
                <Input id="cliente-telefone-alternativo" type="tel" value={dados.telefoneAlternativo ?? ''} onChange={(e) => atualizar('telefoneAlternativo', e.target.value || null)} />
              </Campo>
            </div>

            <Campo rotulo="E-mail" htmlFor="cliente-email">
              <Input id="cliente-email" type="email" autoComplete="email" value={dados.email ?? ''} onChange={(e) => atualizar('email', e.target.value || null)} />
            </Campo>

            {!fisica ? (
              <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                <Campo rotulo="Razão social" htmlFor="cliente-razao">
                  <Input id="cliente-razao" value={dados.razaoSocial ?? ''} onChange={(e) => atualizar('razaoSocial', e.target.value || null)} />
                </Campo>
                <Campo rotulo="Representante legal" htmlFor="cliente-representante">
                  <Input id="cliente-representante" value={dados.representanteLegal ?? ''} onChange={(e) => atualizar('representanteLegal', e.target.value || null)} />
                </Campo>
              </div>
            ) : null}
          </TabsContent>

          <TabsContent value="qualificacao" className="mt-4 grid gap-4">
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
              <Campo rotulo={fisica ? 'CPF' : 'CNPJ'} htmlFor="cliente-documento">
                <Input id="cliente-documento" inputMode="numeric" className="font-dado" value={dados.documento ?? ''} onChange={(e) => atualizar('documento', e.target.value || null)} />
              </Campo>

              {fisica ? (
                <>
                  <Campo rotulo="RG" htmlFor="cliente-rg">
                    <Input id="cliente-rg" className="font-dado" value={dados.rg ?? ''} onChange={(e) => atualizar('rg', e.target.value || null)} />
                  </Campo>
                  <div className="grid grid-cols-[1fr_70px] gap-2">
                    <Campo rotulo="Órgão emissor" htmlFor="cliente-orgao">
                      <Input id="cliente-orgao" value={dados.orgaoEmissor ?? ''} onChange={(e) => atualizar('orgaoEmissor', e.target.value || null)} />
                    </Campo>
                    <Campo rotulo="UF" htmlFor="cliente-uf-orgao">
                      <Input id="cliente-uf-orgao" maxLength={2} value={dados.ufOrgaoEmissor ?? ''} onChange={(e) => atualizar('ufOrgaoEmissor', e.target.value || null)} />
                    </Campo>
                  </div>
                </>
              ) : null}
            </div>

            {fisica ? (
              <>
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
                  <Campo rotulo="Nascimento" htmlFor="cliente-nascimento">
                    <Input id="cliente-nascimento" type="date" value={dados.dataNascimento ?? ''} onChange={(e) => atualizar('dataNascimento', e.target.value || null)} />
                  </Campo>
                  <Campo rotulo="Estado civil">
                    <Select value={dados.estadoCivil ?? 'NaoInformado'} onValueChange={(valor) => atualizar('estadoCivil', valor as EstadoCivil)}>
                      <SelectTrigger><SelectValue /></SelectTrigger>
                      <SelectContent>{estadosCivis.map((estado) => <SelectItem key={estado.valor} value={estado.valor}>{estado.rotulo}</SelectItem>)}</SelectContent>
                    </Select>
                  </Campo>
                  <Campo rotulo="Regime de bens">
                    <Select disabled={dados.estadoCivil !== 'Casado'} value={dados.regimeDeBens ?? 'NaoInformado'} onValueChange={(valor) => atualizar('regimeDeBens', valor as RegimeDeBens)}>
                      <SelectTrigger><SelectValue /></SelectTrigger>
                      <SelectContent>{regimes.map((regime) => <SelectItem key={regime.valor} value={regime.valor}>{regime.rotulo}</SelectItem>)}</SelectContent>
                    </Select>
                  </Campo>
                </div>

                <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
                  <Campo rotulo="Profissão" htmlFor="cliente-profissao">
                    <Input id="cliente-profissao" value={dados.profissao ?? ''} onChange={(e) => atualizar('profissao', e.target.value || null)} />
                  </Campo>
                  <Campo rotulo="Nacionalidade" htmlFor="cliente-nacionalidade">
                    <Input id="cliente-nacionalidade" value={dados.nacionalidade ?? ''} onChange={(e) => atualizar('nacionalidade', e.target.value || null)} />
                  </Campo>
                  <Campo rotulo="Naturalidade" htmlFor="cliente-naturalidade">
                    <Input id="cliente-naturalidade" placeholder="Cidade/UF" value={dados.naturalidade ?? ''} onChange={(e) => atualizar('naturalidade', e.target.value || null)} />
                  </Campo>
                </div>

                <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                  <Campo rotulo="Nome do pai" htmlFor="cliente-pai">
                    <Input id="cliente-pai" value={dados.nomeDoPai ?? ''} onChange={(e) => atualizar('nomeDoPai', e.target.value || null)} />
                  </Campo>
                  <Campo rotulo="Nome da mãe" htmlFor="cliente-mae">
                    <Input id="cliente-mae" value={dados.nomeDaMae ?? ''} onChange={(e) => atualizar('nomeDaMae', e.target.value || null)} />
                  </Campo>
                </div>
              </>
            ) : (
              <div className="rounded-md border border-linha bg-superficie px-3 py-2 text-sm text-tinta-media">
                Para pessoa jurídica, razão social e representante legal ficam na aba Contato.
              </div>
            )}
          </TabsContent>

          <TabsContent value="endereco" className="mt-4 grid gap-4">
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-[150px_1fr_120px]">
              <Campo rotulo="CEP" htmlFor="cliente-cep">
                <Input id="cliente-cep" inputMode="numeric" className="font-dado" value={dados.cep ?? ''} onChange={(e) => atualizar('cep', e.target.value || null)} />
              </Campo>
              <Campo rotulo="Logradouro" htmlFor="cliente-logradouro">
                <Input id="cliente-logradouro" autoComplete="street-address" value={dados.logradouro ?? ''} onChange={(e) => atualizar('logradouro', e.target.value || null)} />
              </Campo>
              <Campo rotulo="Número" htmlFor="cliente-numero">
                <Input id="cliente-numero" value={dados.numero ?? ''} onChange={(e) => atualizar('numero', e.target.value || null)} />
              </Campo>
            </div>
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              <Campo rotulo="Complemento" htmlFor="cliente-complemento">
                <Input id="cliente-complemento" value={dados.complemento ?? ''} onChange={(e) => atualizar('complemento', e.target.value || null)} />
              </Campo>
              <Campo rotulo="Bairro" htmlFor="cliente-bairro">
                <Input id="cliente-bairro" value={dados.bairro ?? ''} onChange={(e) => atualizar('bairro', e.target.value || null)} />
              </Campo>
            </div>
            <div className="grid grid-cols-[1fr_90px] gap-3">
              <Campo rotulo="Cidade" htmlFor="cliente-cidade">
                <Input id="cliente-cidade" value={dados.cidade ?? ''} onChange={(e) => atualizar('cidade', e.target.value || null)} />
              </Campo>
              <Campo rotulo="UF" htmlFor="cliente-uf">
                <Input id="cliente-uf" maxLength={2} value={dados.uf ?? ''} onChange={(e) => atualizar('uf', e.target.value || null)} />
              </Campo>
            </div>
          </TabsContent>

          <TabsContent value="observacoes" className="mt-4 grid gap-4">
            <Campo rotulo="Observações internas" htmlFor="cliente-observacoes" ajuda="Não aparece para o cliente.">
              <Textarea id="cliente-observacoes" value={dados.observacoes ?? ''} onChange={(e) => atualizar('observacoes', e.target.value || null)} rows={6} maxLength={1000} />
            </Campo>

            {cliente ? (
              <div className="flex items-center justify-between rounded-md border border-linha px-3 py-2">
                <div>
                  <Label htmlFor="cliente-ativo">Cliente ativo</Label>
                  <p className="text-xs text-tinta-media">Desativar não apaga o histórico.</p>
                </div>
                <Switch id="cliente-ativo" checked={dados.ativo ?? true} onCheckedChange={(valor) => atualizar('ativo', valor)} />
              </div>
            ) : null}
          </TabsContent>
        </Tabs>

        <DialogFooter>
          <Button variant="outline" onClick={aoFechar}>Cancelar</Button>
          <Button onClick={() => void salvar()} disabled={salvando || !dados.nome.trim() || !dados.telefoneWhatsapp.trim()}>
            {salvando ? 'Salvando…' : 'Salvar'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function Campo({
  rotulo,
  htmlFor,
  obrigatorio,
  ajuda,
  children,
}: {
  rotulo: string
  htmlFor?: string
  obrigatorio?: boolean
  ajuda?: string
  children: ReactNode
}) {
  return (
    <div className="flex min-w-0 flex-col gap-1.5">
      <Label htmlFor={htmlFor}>
        {rotulo}{obrigatorio ? <span className="text-selo"> *</span> : null}
      </Label>
      {children}
      {ajuda ? <p className="text-[11px] text-tinta-fraca">{ajuda}</p> : null}
    </div>
  )
}
