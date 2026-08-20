import { useEffect, useRef, useState, type ReactNode } from 'react'
import { toast } from 'sonner'
import { Check, Loader2, TriangleAlert } from 'lucide-react'
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
import { clientesApi, consultasApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { cn } from '@/lib/utils'
import {
  cnpjEhValido,
  cpfEhValido,
  mascararCep,
  mascararCnpj,
  mascararCpf,
  mascararTelefone,
  somenteDigitos,
} from '@/lib/mascaras'
import type {
  Cliente,
  DadosDeCliente,
  EnderecoConsultado,
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
  const [estadoDoCep, setEstadoDoCep] = useState<EstadoDoCampo | null>(null)
  const [estadoDoDocumento, setEstadoDoDocumento] = useState<EstadoDoCampo | null>(null)

  // A aba é controlada porque a recusa do backend precisa poder trocar de aba: o
  // erro não serve de nada exibido numa aba que o atendente não está vendo.
  const [aba, setAba] = useState<Aba>('contato')
  const [erroDeSalvamento, setErroDeSalvamento] = useState<ErroDeCampo | null>(null)

  const fisica = (dados.tipo ?? 'Fisica') === 'Fisica'

  // Guarda o que já foi consultado: sem isso, apagar e redigitar o último dígito
  // do CEP dispara a mesma consulta de novo a cada tecla.
  const ultimoCepConsultado = useRef('')
  const ultimoCnpjConsultado = useRef('')
  const consultaEmVoo = useRef<AbortController | null>(null)
  const campoNumero = useRef<HTMLInputElement>(null)

  useEffect(() => {
    if (!aberto) return

    setDados(dadosDoCliente(cliente))
    setEstadoDoCep(null)
    setEstadoDoDocumento(null)
    setErroDeSalvamento(null)
    setAba('contato')
    ultimoCepConsultado.current = ''
    ultimoCnpjConsultado.current = ''
  }, [aberto, cliente])

  // Fechar o diálogo no meio de uma consulta não pode deixar a resposta chegar
  // depois e escrever num formulário que já é de outro cliente.
  useEffect(() => () => consultaEmVoo.current?.abort(), [])

  function atualizar<K extends keyof DadosDeCliente>(campo: K, valor: DadosDeCliente[K]) {
    setDados((atuais) => ({ ...atuais, [campo]: valor }))

    // Mexeu no campo acusado, o aviso sai. Erro que continua na tela depois de
    // corrigido ensina o atendente a ignorar aviso.
    setErroDeSalvamento((atual) => (atual?.campo === campo ? null : atual))
  }

  /**
   * Estado a exibir sob um campo. A recusa do salvamento ganha do estado próprio
   * do campo: é ela que está impedindo o cadastro de fechar.
   */
  function estadoDe(campo: keyof DadosDeCliente, proprio?: EstadoDoCampo | null) {
    return erroDeSalvamento?.campo === campo
      ? { tom: 'aviso' as const, texto: erroDeSalvamento.texto }
      : (proprio ?? null)
  }

  /** Abre uma consulta cancelando a anterior, e devolve o controle para quem chamou. */
  function abrirConsulta() {
    consultaEmVoo.current?.abort()
    const controle = new AbortController()
    consultaEmVoo.current = controle
    return controle
  }

  async function consultarCep(cep: string) {
    if (ultimoCepConsultado.current === cep) return
    ultimoCepConsultado.current = cep

    const controle = abrirConsulta()
    setEstadoDoCep({ tom: 'carregando', texto: 'Consultando o CEP…' })

    try {
      const endereco = await consultasApi.cep(cep, controle.signal)

      // Aqui sobrescreve mesmo: quem trocou o CEP trocou o endereço. Número e
      // complemento ficam de fora — não vêm no resultado e são do imóvel.
      setDados((atuais) => ({
        ...atuais,
        logradouro: endereco.logradouro,
        bairro: endereco.bairro,
        cidade: endereco.cidade,
        uf: endereco.uf,
      }))

      setEstadoDoCep({ tom: 'ok', texto: 'Endereço preenchido. Falta o número.' })
      campoNumero.current?.focus()
    } catch (falha) {
      if (controle.signal.aborted) return
      setEstadoDoCep({ tom: 'aviso', texto: explicarFalha(falha, 'CEP') })
    }
  }

  async function consultarCnpj(cnpj: string) {
    if (ultimoCnpjConsultado.current === cnpj) return
    ultimoCnpjConsultado.current = cnpj

    const controle = abrirConsulta()
    setEstadoDoDocumento({ tom: 'carregando', texto: 'Consultando o CNPJ…' })

    try {
      const empresa = await consultasApi.cnpj(cnpj, controle.signal)
      let preenchidos: string[] = []

      setDados((atuais) => {
        const aplicado = aplicarEmpresa(atuais, empresa)
        preenchidos = aplicado.preenchidos
        return aplicado.dados
      })

      setEstadoDoDocumento({ tom: 'ok', texto: 'Empresa encontrada.' })

      // O resultado cai em OUTRAS abas (Contato e Endereço). Sem o aviso, o
      // preenchimento acontece fora do campo de visão e ninguém vê.
      if (preenchidos.length > 0) {
        toast.success(`Preenchido pelo CNPJ: ${preenchidos.join(', ')}.`)
      }
    } catch (falha) {
      if (controle.signal.aborted) return
      setEstadoDoDocumento({ tom: 'aviso', texto: explicarFalha(falha, 'CNPJ') })
    }
  }

  function aoMudarCep(valor: string) {
    const digitos = somenteDigitos(valor).slice(0, 8)
    atualizar('cep', digitos || null)

    if (digitos.length === 8) {
      void consultarCep(digitos)
      return
    }

    setEstadoDoCep(null)
    ultimoCepConsultado.current = ''
  }

  function aoMudarDocumento(valor: string) {
    const digitos = somenteDigitos(valor).slice(0, fisica ? 11 : 14)
    atualizar('documento', digitos || null)

    if (fisica) {
      // CPF só confere e avisa. Não existe consulta, e a ausência dela é
      // decisão registrada — ver lib/mascaras.ts e DESIGN.md, seção 11.
      setEstadoDoDocumento(
        digitos.length === 11 && !cpfEhValido(digitos)
          ? { tom: 'aviso', texto: 'Dígito verificador não confere. Confira o documento.' }
          : null,
      )
      return
    }

    if (digitos.length < 14) {
      setEstadoDoDocumento(null)
      ultimoCnpjConsultado.current = ''
      return
    }

    if (!cnpjEhValido(digitos)) {
      // Consultar um CNPJ que já se sabe inválido só gasta a espera do atendente.
      setEstadoDoDocumento({
        tom: 'aviso',
        texto: 'Dígito verificador não confere. Confira o documento.',
      })
      ultimoCnpjConsultado.current = ''
      return
    }

    void consultarCnpj(digitos)
  }

  function aoMudarTipo(valor: TipoDePessoa) {
    // Documento não sobrevive à troca: CPF tem 11 dígitos e CNPJ tem 14, e o que
    // ficava para trás só reaparecia como erro do backend na hora de salvar.
    setDados((atuais) => ({ ...atuais, tipo: valor, documento: null }))
    setEstadoDoDocumento(null)
    ultimoCnpjConsultado.current = ''
  }

  async function salvar() {
    setSalvando(true)
    setErroDeSalvamento(null)

    try {
      if (cliente) await clientesApi.atualizar(cliente.id, dados)
      else await clientesApi.criar(dados)

      toast.success(cliente ? 'Cliente atualizado.' : 'Cliente cadastrado.')
      aoSalvar()
      aoFechar()
    } catch (falha) {
      mostrarFalhaDeSalvamento(falha)
    } finally {
      setSalvando(false)
    }
  }

  /**
   * Leva a recusa do backend até o campo que a causou.
   *
   * Antes tudo caía num toast: o atendente clicava Salvar da aba Observações,
   * lia "CEP deve ter 8 digitos" e ia procurar o campo pelas quatro abas. O
   * código do erro é estável (ClientesServiceException) e diz exatamente onde
   * fica o problema — só faltava alguém usar.
   */
  function mostrarFalhaDeSalvamento(falha: unknown) {
    if (!(falha instanceof ErroDaApi)) {
      toast.error('Não foi possível salvar.')
      return
    }

    const campo = campoDoErro(falha.codigo, dados)
    const local = campo ? LOCAL_DO_CAMPO[campo] : undefined

    // Erro que não é de campo — sessão expirada, conflito de versão, rede.
    // Continua no toast, que é onde erro sem endereço pertence.
    if (!campo || !local) {
      toast.error(falha.message)
      return
    }

    setErroDeSalvamento({ campo, texto: falha.message })
    setAba(local.aba)

    // Depois da troca de aba, senão o campo ainda não está montado.
    requestAnimationFrame(() => document.getElementById(local.id)?.focus())
  }

  return (
    <Dialog open={aberto} onOpenChange={(valor) => !valor && aoFechar()}>
      <DialogContent className="max-w-3xl">
        <DialogHeader>
          <DialogTitle>{cliente ? 'Editar cliente' : 'Novo cliente'}</DialogTitle>
          <DialogDescription>
            Nome e WhatsApp bastam para iniciar. A qualificação pode ser completada conforme o ato avança.
          </DialogDescription>
        </DialogHeader>

        <Tabs value={aba} onValueChange={(valor) => setAba(valor as Aba)}>
          {/* Duas colunas no celular, não rolagem: com scroll horizontal a
              última aba ("Observações") ficava com a palavra cortada na borda
              do diálogo, parecendo defeito em vez de convite pra arrastar. Em
              duas linhas de duas, as quatro ficam inteiras e visíveis de uma
              vez. Do sm em diante a largura sobra e volta a ser uma linha só. */}
          <TabsList className="grid h-auto w-full grid-cols-2 gap-1 sm:grid-cols-4">
            <TabsTrigger value="contato">Contato</TabsTrigger>
            <TabsTrigger value="qualificacao">Qualificação</TabsTrigger>
            <TabsTrigger value="endereco">Endereço</TabsTrigger>
            <TabsTrigger value="observacoes">Observações</TabsTrigger>
          </TabsList>

          <TabsContent value="contato" className="mt-4 grid gap-4">
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-[1fr_180px]">
              <Campo rotulo="Nome" htmlFor="cliente-nome" obrigatorio estado={estadoDe('nome')}>
                <Input id="cliente-nome" autoFocus value={dados.nome} onChange={(e) => atualizar('nome', e.target.value)} />
              </Campo>
              <Campo rotulo="Tipo de pessoa">
                <Select value={dados.tipo ?? 'Fisica'} onValueChange={(valor) => aoMudarTipo(valor as TipoDePessoa)}>
                  <SelectTrigger><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="Fisica">Pessoa física</SelectItem>
                    <SelectItem value="Juridica">Pessoa jurídica</SelectItem>
                  </SelectContent>
                </Select>
              </Campo>
            </div>

            {/* Os dois campos aceitam dígito corrido, colado ou digitado, e a
                máscara formata sozinha. Na edição o valor chega em E.164
                (5538999000045) e sai daqui legível — antes ele aparecia cru ao
                lado de um placeholder que prometia outro formato, e o atendente
                "corrigia" o número até quebrar. */}
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              <Campo rotulo="WhatsApp" htmlFor="cliente-whatsapp" obrigatorio ajuda="DDD e número; o sistema inclui o código 55." estado={estadoDe('telefoneWhatsapp')}>
                <Input id="cliente-whatsapp" type="tel" autoComplete="tel" className="font-dado" value={mascararTelefone(dados.telefoneWhatsapp)} onChange={(e) => atualizar('telefoneWhatsapp', mascararTelefone(e.target.value))} placeholder="(38) 99999-0001" />
              </Campo>
              <Campo rotulo="Telefone alternativo" htmlFor="cliente-telefone-alternativo" ajuda="Fixo ou segundo celular. Mesmo formato." estado={estadoDe('telefoneAlternativo')}>
                <Input id="cliente-telefone-alternativo" type="tel" className="font-dado" value={mascararTelefone(dados.telefoneAlternativo ?? '')} onChange={(e) => atualizar('telefoneAlternativo', mascararTelefone(e.target.value) || null)} placeholder="(38) 3221-1234" />
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
              {/* CNPJ consulta; CPF não. Não é assimetria por preguiça: dado de
                  empresa é público e a Receita publica, enquanto dado pessoal por
                  CPF não tem fonte pública e legal nenhuma. Ver DESIGN.md, seção
                  11, e Modules/Consultas/README.md antes de "completar" isto. */}
              <Campo
                rotulo={fisica ? 'CPF' : 'CNPJ'}
                htmlFor="cliente-documento"
                ajuda={fisica ? undefined : 'Preenche nome e endereço da empresa.'}
                estado={estadoDe('documento', estadoDoDocumento)}
              >
                <Input
                  id="cliente-documento"
                  inputMode="numeric"
                  className="font-dado"
                  maxLength={fisica ? 14 : 18}
                  placeholder={fisica ? '000.000.000-00' : '00.000.000/0000-00'}
                  value={(fisica ? mascararCpf : mascararCnpj)(dados.documento ?? '')}
                  onChange={(e) => aoMudarDocumento(e.target.value)}
                />
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
                    <Campo rotulo="UF" htmlFor="cliente-uf-orgao" estado={estadoDe('ufOrgaoEmissor')}>
                      <Input id="cliente-uf-orgao" maxLength={2} className="uppercase" value={dados.ufOrgaoEmissor ?? ''} onChange={(e) => atualizar('ufOrgaoEmissor', e.target.value.toUpperCase() || null)} />
                    </Campo>
                  </div>
                </>
              ) : null}
            </div>

            {fisica ? (
              <>
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
                  <Campo rotulo="Nascimento" htmlFor="cliente-nascimento" estado={estadoDe('dataNascimento')}>
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
              <Campo
                rotulo="CEP"
                htmlFor="cliente-cep"
                ajuda="Ao completar, o endereço vem sozinho."
                estado={estadoDe('cep', estadoDoCep)}
              >
                <Input
                  id="cliente-cep"
                  inputMode="numeric"
                  className="font-dado"
                  maxLength={9}
                  placeholder="00000-000"
                  value={mascararCep(dados.cep ?? '')}
                  onChange={(e) => aoMudarCep(e.target.value)}
                />
              </Campo>
              <Campo rotulo="Logradouro" htmlFor="cliente-logradouro">
                <Input id="cliente-logradouro" autoComplete="street-address" value={dados.logradouro ?? ''} onChange={(e) => atualizar('logradouro', e.target.value || null)} />
              </Campo>
              <Campo rotulo="Número" htmlFor="cliente-numero">
                <Input id="cliente-numero" ref={campoNumero} value={dados.numero ?? ''} onChange={(e) => atualizar('numero', e.target.value || null)} />
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
              <Campo rotulo="UF" htmlFor="cliente-uf" estado={estadoDe('uf')}>
                <Input id="cliente-uf" maxLength={2} className="uppercase" value={dados.uf ?? ''} onChange={(e) => atualizar('uf', e.target.value.toUpperCase() || null)} />
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
  estado,
  children,
}: {
  rotulo: string
  htmlFor?: string
  obrigatorio?: boolean
  ajuda?: string
  estado?: EstadoDoCampo | null
  children: ReactNode
}) {
  const Icone = ICONE_DO_TOM[estado?.tom ?? 'carregando']

  return (
    <div className="flex min-w-0 flex-col gap-1.5">
      <Label htmlFor={htmlFor}>
        {rotulo}{obrigatorio ? <span className="text-selo"> *</span> : null}
      </Label>
      {children}
      {estado ? (
        // Ícone junto do texto porque cor nunca é o único sinal (DESIGN.md, 10.4).
        // Âmbar e verde bastam aqui: erro de consulta não é falha de prazo legal,
        // e gastar bordô nisso comeria a cota de 3 por tela.
        <p
          className={cn(
            'flex items-start gap-1.5 text-[11px] leading-snug',
            TOM_DO_ESTADO[estado.tom],
          )}
          // Aviso interrompe: é o que está impedindo o cadastro de fechar.
          // Carregando e concluído só ficam disponíveis, sem cortar a leitura.
          role={estado.tom === 'aviso' ? 'alert' : 'status'}
        >
          <Icone
            className={cn('mt-px size-3 shrink-0', estado.tom === 'carregando' && 'animate-spin')}
          />
          {estado.texto}
        </p>
      ) : ajuda ? (
        <p className="text-[11px] text-tinta-fraca">{ajuda}</p>
      ) : null}
    </div>
  )
}

/** Estado de um campo com preenchimento assistido. Ver DESIGN.md, seção 11. */
type EstadoDoCampo = {
  tom: 'carregando' | 'aviso' | 'ok'
  texto: string
}

type Aba = 'contato' | 'qualificacao' | 'endereco' | 'observacoes'

type ErroDeCampo = {
  campo: keyof DadosDeCliente
  texto: string
}

/** Onde cada campo mora, para a recusa do backend saber que aba abrir. */
const LOCAL_DO_CAMPO: Partial<Record<keyof DadosDeCliente, { aba: Aba; id: string }>> = {
  nome: { aba: 'contato', id: 'cliente-nome' },
  telefoneWhatsapp: { aba: 'contato', id: 'cliente-whatsapp' },
  telefoneAlternativo: { aba: 'contato', id: 'cliente-telefone-alternativo' },
  documento: { aba: 'qualificacao', id: 'cliente-documento' },
  dataNascimento: { aba: 'qualificacao', id: 'cliente-nascimento' },
  ufOrgaoEmissor: { aba: 'qualificacao', id: 'cliente-uf-orgao' },
  cep: { aba: 'endereco', id: 'cliente-cep' },
  uf: { aba: 'endereco', id: 'cliente-uf' },
}

/** Códigos que apontam um campo só. Os ambíguos são resolvidos em campoDoErro. */
const CAMPO_POR_CODIGO: Record<string, keyof DadosDeCliente> = {
  nome_obrigatorio: 'nome',
  telefone_em_uso: 'telefoneWhatsapp',
  cpf_invalido: 'documento',
  cnpj_invalido: 'documento',
  nascimento_no_futuro: 'dataNascimento',
  nascimento_implausivel: 'dataNascimento',
  cep_invalido: 'cep',
}

/**
 * Traduz o código do erro no campo que o causou.
 *
 * Dois códigos servem a mais de um campo no backend, e nesses o código sozinho
 * não basta — a mesma exceção sai de `ValidarTelefone` e de
 * `LimparTelefoneOpcional`, e de `ValidarUf` chamado duas vezes. Em vez de
 * pedir códigos novos ao backend, a tela repete a regra dele e descobre qual
 * dos dois campos está fora. Errar aqui só custa apontar o campo vizinho.
 */
function campoDoErro(codigo: string, dados: DadosDeCliente): keyof DadosDeCliente | null {
  if (codigo === 'telefone_invalido') {
    return telefoneAceitavel(dados.telefoneWhatsapp) && dados.telefoneAlternativo
      ? 'telefoneAlternativo'
      : 'telefoneWhatsapp'
  }

  if (codigo === 'uf_invalida') {
    return ufAceitavel(dados.uf) ? 'ufOrgaoEmissor' : 'uf'
  }

  return CAMPO_POR_CODIGO[codigo] ?? null
}

/** Mesma janela de ClienteService.ValidarTelefone: 10 ou 11 dígitos, ou 12 a 15. */
function telefoneAceitavel(valor: string | null | undefined) {
  const digitos = somenteDigitos(valor ?? '')
  const comPais = digitos.length === 10 || digitos.length === 11 ? digitos.length + 2 : digitos.length

  return comPais >= 12 && comPais <= 15
}

function ufAceitavel(valor: string | null | undefined) {
  const limpo = valor?.trim() ?? ''

  return limpo.length === 0 || /^[A-Za-z]{2}$/.test(limpo)
}

const TOM_DO_ESTADO = {
  carregando: 'text-tinta-fraca',
  aviso: 'text-ambar',
  ok: 'text-verde',
} as const

const ICONE_DO_TOM = {
  carregando: Loader2,
  aviso: TriangleAlert,
  ok: Check,
} as const

/**
 * Traduz a falha da consulta para o que o atendente precisa fazer. O código vem
 * do backend e é estável; a mensagem, não — por isso a decisão é pelo código.
 */
function explicarFalha(falha: unknown, oQue: string) {
  if (falha instanceof ErroDaApi) {
    if (falha.codigo === 'consulta_nao_encontrada') {
      return `${oQue} não encontrado na base pública. Preencha manualmente.`
    }

    if (falha.codigo === 'consulta_indisponivel') {
      return 'Consulta indisponível agora. Preencha manualmente.'
    }
  }

  return 'Não foi possível consultar. Preencha manualmente.'
}

/**
 * Aplica o resultado do CNPJ sem apagar o que o atendente já digitou, e devolve
 * o que de fato mudou — é o que o aviso da tela precisa nomear.
 *
 * Pura de propósito: quem chama a usa dentro de um atualizador de estado, e
 * atualizador que guarda efeito colateral roda duas vezes em desenvolvimento.
 */
function aplicarEmpresa(
  atuais: DadosDeCliente,
  empresa: { razaoSocial: string | null; nomeFantasia: string | null; endereco: EnderecoConsultado | null },
): { dados: DadosDeCliente; preenchidos: string[] } {
  const dados = { ...atuais }
  const preenchidos: string[] = []

  // Razão social é campo formal e vem da Receita: sobrescreve.
  if (empresa.razaoSocial) {
    dados.razaoSocial = empresa.razaoSocial
    preenchidos.push('razão social')
  }

  // Nome é o que o atendente vê no WhatsApp e pode já ter ajustado à mão —
  // só entra se estiver vazio.
  const nome = empresa.nomeFantasia ?? empresa.razaoSocial
  if (nome && !dados.nome.trim()) {
    dados.nome = nome
    preenchidos.push('nome')
  }

  // Endereço aqui é brinde, não o que foi pedido: preenche só o que está vazio.
  // Diferente do CEP, onde sobrescrever é o comportamento certo.
  const endereco = empresa.endereco
  let mexeuNoEndereco = false

  if (endereco) {
    const campos = [
      ['cep', endereco.cep],
      ['logradouro', endereco.logradouro],
      ['bairro', endereco.bairro],
      ['cidade', endereco.cidade],
      ['uf', endereco.uf],
    ] as const

    for (const [campo, valor] of campos) {
      if (valor && !dados[campo]?.trim()) {
        dados[campo] = valor
        mexeuNoEndereco = true
      }
    }
  }

  if (mexeuNoEndereco) {
    preenchidos.push('endereço')
  }

  return { dados, preenchidos }
}
