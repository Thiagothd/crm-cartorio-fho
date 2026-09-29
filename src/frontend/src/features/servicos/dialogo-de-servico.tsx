import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
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
import { servicosApi, setoresApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import type { CategoriaServico, TipoServico } from '@/lib/api/tipos'

/** Mesmo limite da coluna (TipoServicoConfiguration.cs). */
const LIMITE_DO_CHECKLIST = 800

/** Mesmo limite das colunas de mensagem do serviço (TipoServicoConfiguration.cs). */
const LIMITE_DA_MENSAGEM = 4000

/** Valor do Select para "nenhum setor" — Radix não aceita item com value vazio. */
const SEM_SETOR = '_nenhum'

/** Valor do Select para "fica no menu do setor" (sem serviço que agrupe). */
const SEM_PAI = '_nenhum'

const categorias: { valor: CategoriaServico; rotulo: string }[] = [
  { valor: 'Notas', rotulo: 'Tabelionato de Notas' },
  { valor: 'RegistroCivil', rotulo: 'Registro Civil' },
  { valor: 'RegistroImoveis', rotulo: 'Registro de Imóveis' },
  { valor: 'TitulosDocumentos', rotulo: 'Títulos e Documentos' },
  { valor: 'Protesto', rotulo: 'Protesto' },
  { valor: 'Outros', rotulo: 'Outros' },
]

export function DialogoDeServico({
  servico,
  aberto,
  aoFechar,
  aoSalvar,
}: {
  servico: TipoServico | null
  aberto: boolean
  aoFechar: () => void
  aoSalvar: () => void
}) {
  const [nome, setNome] = useState('')
  const [codigo, setCodigo] = useState('')
  const [categoria, setCategoria] = useState<CategoriaServico>('Outros')
  const [setorId, setSetorId] = useState(SEM_SETOR)
  const [documentosNecessarios, setDocumentosNecessarios] = useState('')
  const [ordem, setOrdem] = useState('0')
  const [servicoPaiId, setServicoPaiId] = useState(SEM_PAI)
  const [mensagemAoEscolher, setMensagemAoEscolher] = useState('')
  const [mensagemAposDados, setMensagemAposDados] = useState('')
  const [ativo, setAtivo] = useState(true)
  const [temPrazo, setTemPrazo] = useState(true)
  const [prazoHoras, setPrazoHoras] = useState('24')
  const [percentualAlerta, setPercentualAlerta] = useState('80')
  const [apenasDiasUteis, setApenasDiasUteis] = useState(true)
  const [salvando, setSalvando] = useState(false)

  const { data: setores } = useQuery({
    queryKey: ['setores-gestao'],
    queryFn: () => setoresApi.listar(true),
    enabled: aberto,
  })

  const { data: servicos } = useQuery({
    queryKey: ['tipos-servico', 'todos'],
    queryFn: () => servicosApi.listar(true),
    enabled: aberto,
  })

  // Quem pode agrupar: serviço do mesmo setor que não esteja dentro de outro
  // (submenu de um nível só) e que não seja este mesmo.
  const possiveisPais = (servicos ?? []).filter(
    (s) =>
      s.id !== servico?.id &&
      !s.servicoPaiId &&
      setorId !== SEM_SETOR &&
      s.setorId === setorId,
  )

  useEffect(() => {
    if (!aberto) return

    setNome(servico?.nome ?? '')
    setCodigo(servico?.codigo ?? '')
    setCategoria(servico?.categoria ?? 'Outros')
    setSetorId(servico?.setorId ?? SEM_SETOR)
    setDocumentosNecessarios(servico?.documentosNecessarios ?? '')
    setOrdem(String(servico?.ordem ?? 0))
    setServicoPaiId(servico?.servicoPaiId ?? SEM_PAI)
    setMensagemAoEscolher(servico?.mensagemAoEscolher ?? '')
    setMensagemAposDados(servico?.mensagemAposDados ?? '')
    setAtivo(servico?.ativo ?? true)
    setTemPrazo(Boolean(servico?.prazo) || !servico)
    setPrazoHoras(String(servico?.prazo?.prazoHoras ?? 24))
    setPercentualAlerta(String(servico?.prazo?.percentualAlerta ?? 80))
    setApenasDiasUteis(servico?.prazo?.apenasDiasUteis ?? true)
  }, [aberto, servico])

  async function salvar() {
    if (salvando) return
    setSalvando(true)

    try {
      const dados = {
        nome,
        codigo: codigo.trim() || null,
        categoria,
        descricao: servico?.descricao ?? null,
        ativo,
        setorId: setorId === SEM_SETOR ? null : setorId,
        documentosNecessarios: documentosNecessarios.trim() || null,
        ordem: Number(ordem) || 0,
        servicoPaiId: servicoPaiId === SEM_PAI || setorId === SEM_SETOR ? null : servicoPaiId,
        mensagemAoEscolher: mensagemAoEscolher.trim() || null,
        mensagemAposDados: mensagemAposDados.trim() || null,
        prazo: temPrazo
          ? {
              prazoHoras: Number(prazoHoras),
              percentualAlerta: Number(percentualAlerta),
              apenasDiasUteis,
            }
          : null,
      }

      if (servico) {
        await servicosApi.atualizar(servico.id, dados)
      } else {
        await servicosApi.criar(dados)
      }

      toast.success(servico ? 'Serviço atualizado.' : 'Serviço cadastrado.')
      aoSalvar()
      aoFechar()
    } catch (falha) {
      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível salvar.')
    } finally {
      setSalvando(false)
    }
  }

  return (
    <Dialog open={aberto} onOpenChange={(v) => !v && !salvando && aoFechar()}>
      <DialogContent className="max-w-3xl" mostrarFechar={!salvando} onEscapeKeyDown={(e) => { if (salvando) e.preventDefault() }} onInteractOutside={(e) => { if (salvando) e.preventDefault() }}>
        <DialogHeader>
          <DialogTitle>{servico ? 'Editar serviço' : 'Novo serviço'}</DialogTitle>
          <DialogDescription>
            O prazo começa a contar quando o serviço é vinculado à conversa, não na abertura do
            atendimento — a triagem não consome o SLA do ato.
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={(e) => { e.preventDefault(); void salvar() }}>
        <fieldset disabled={salvando} className="grid min-w-0 gap-5 md:grid-cols-[minmax(0,1.15fr)_minmax(0,1fr)]">
        <div className="flex min-w-0 flex-col gap-4">
          <h3 className="text-sm font-semibold text-tinta">Cadastro e documentos</h3>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="servico-nome">Nome</Label>
            <Input
              id="servico-nome"
              required
              value={nome}
              onChange={(e) => setNome(e.target.value)}
              placeholder="Ex.: Certidão de nascimento"
            />
          </div>

          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="servico-categoria">Categoria</Label>
              <Select value={categoria} onValueChange={(v) => setCategoria(v as CategoriaServico)}>
                <SelectTrigger id="servico-categoria">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {categorias.map((c) => (
                    <SelectItem key={c.valor} value={c.valor}>
                      {c.rotulo}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className="flex flex-col gap-1.5">
              <Label htmlFor="servico-codigo">Código interno (opcional)</Label>
              <Input
                id="servico-codigo"
                value={codigo}
                onChange={(e) => setCodigo(e.target.value)}
                placeholder="RC-NASC"
              />
            </div>
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="servico-setor">Setor no menu de atendimento</Label>
            <Select value={setorId} onValueChange={setSetorId}>
              <SelectTrigger id="servico-setor">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={SEM_SETOR}>Nenhum</SelectItem>
                {setores?.map((s) => (
                  <SelectItem key={s.id} value={s.id}>
                    {s.nome}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <p className="text-xs text-tinta-media">
              Sem setor, este serviço não aparece no menu automático que o cliente vê depois de
              escolher o setor no WhatsApp — só fica disponível pelo painel.
            </p>
          </div>

          <div className="flex flex-col gap-1.5">
            <div className="flex items-baseline justify-between gap-2">
              <Label htmlFor="servico-documentos">Documentos necessários (opcional)</Label>
              {documentosNecessarios.length > LIMITE_DO_CHECKLIST * 0.8 ? (
                <span className="font-dado text-[11px] text-tinta-fraca">
                  {documentosNecessarios.length}/{LIMITE_DO_CHECKLIST}
                </span>
              ) : null}
            </div>
            <Textarea
              id="servico-documentos"
              value={documentosNecessarios}
              onChange={(e) => setDocumentosNecessarios(e.target.value)}
              rows={4}
              maxLength={LIMITE_DO_CHECKLIST}
              className="resize-none"
              placeholder="Ex.: RG e CPF originais, comprovante de residência recente."
            />
            <p className="text-xs text-tinta-media">
              Entra no marcador {'{DocumentosNecessarios}'} das mensagens automáticas.
            </p>
          </div>

          <div className="flex flex-col gap-4 border-t border-linha pt-4">
            <div>
              <h3 className="text-sm font-semibold text-tinta">No menu do WhatsApp</h3>
              <p className="mt-1 text-xs text-tinta-media">
                Onde este serviço aparece para o cliente e o que o robô responde quando ele é escolhido.
              </p>
            </div>

            <div className="grid grid-cols-1 gap-3 sm:grid-cols-[7rem_minmax(0,1fr)]">
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="servico-ordem">Posição</Label>
                <Input
                  id="servico-ordem"
                  type="number"
                  min={0}
                  max={999}
                  step={1}
                  value={ordem}
                  onChange={(e) => setOrdem(e.target.value)}
                />
              </div>

              <div className="flex flex-col gap-1.5">
                <Label htmlFor="servico-pai">Aparece dentro de</Label>
                <Select
                  value={servicoPaiId}
                  onValueChange={setServicoPaiId}
                  disabled={setorId === SEM_SETOR}
                >
                  <SelectTrigger id="servico-pai">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value={SEM_PAI}>Direto no menu do setor</SelectItem>
                    {possiveisPais.map((s) => (
                      <SelectItem key={s.id} value={s.id}>
                        {s.nome}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            </div>
            <p className="-mt-2 text-xs text-tinta-media">
              Menor posição aparece primeiro; com a mesma posição, vale a ordem alfabética. Dentro de
              outro serviço, este só aparece quando o cliente escolhe aquele.
            </p>

            <div className="flex flex-col gap-1.5">
              <div className="flex items-baseline justify-between gap-2">
                <Label htmlFor="servico-ao-escolher">Mensagem ao escolher (opcional)</Label>
                {mensagemAoEscolher.length > LIMITE_DA_MENSAGEM * 0.8 ? (
                  <span className="font-dado text-[11px] text-tinta-fraca">
                    {mensagemAoEscolher.length}/{LIMITE_DA_MENSAGEM}
                  </span>
                ) : null}
              </div>
              <Textarea
                id="servico-ao-escolher"
                value={mensagemAoEscolher}
                onChange={(e) => setMensagemAoEscolher(e.target.value)}
                rows={5}
                maxLength={LIMITE_DA_MENSAGEM}
                className="resize-y"
                placeholder="Vazio, o robô usa a mensagem geral de serviço escolhido."
              />
              <p className="text-xs text-tinta-media">
                Aceita os marcadores das mensagens automáticas. Num serviço que agrupa outros,{' '}
                {'{MenuServicos}'} vira a lista dos serviços de dentro.
              </p>
            </div>

            <div className="flex flex-col gap-1.5">
              <div className="flex items-baseline justify-between gap-2">
                <Label htmlFor="servico-apos-dados">Mensagem depois que o cliente enviar os dados (opcional)</Label>
                {mensagemAposDados.length > LIMITE_DA_MENSAGEM * 0.8 ? (
                  <span className="font-dado text-[11px] text-tinta-fraca">
                    {mensagemAposDados.length}/{LIMITE_DA_MENSAGEM}
                  </span>
                ) : null}
              </div>
              <Textarea
                id="servico-apos-dados"
                value={mensagemAposDados}
                onChange={(e) => setMensagemAposDados(e.target.value)}
                rows={4}
                maxLength={LIMITE_DA_MENSAGEM}
                className="resize-y"
                placeholder="Ex.: a chave Pix para pagamento e o pedido do comprovante."
              />
              <p className="text-xs text-tinta-media">
                Preenchida, o robô espera o cliente mandar os dados, responde com este texto e aguarda o
                comprovante. Na mensagem seguinte, o atendimento passa para a equipe.
              </p>
            </div>
          </div>

        </div>
        <div className="flex min-w-0 flex-col gap-4 rounded-card bg-superficie p-4">
          <h3 className="text-sm font-semibold text-tinta">Prazo e disponibilidade</h3>
          <div className="flex items-center justify-between gap-3">
            <div>
              <Label htmlFor="tem-prazo">Controlar prazo (SLA)</Label>
              <p className="text-xs text-tinta-media">
                Sem prazo, a conversa não entra nos contadores de alerta e atraso.
              </p>
            </div>
            <Switch id="tem-prazo" checked={temPrazo} onCheckedChange={setTemPrazo} />
          </div>

          {temPrazo ? (
            <div className="flex flex-col gap-3 border-t border-linha pt-3">
              <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor="prazo-horas">Prazo em horas</Label>
                  <Input
                    id="prazo-horas"
                    type="number"
                    min={1}
                    required
                    step={1}
                    value={prazoHoras}
                    onChange={(e) => setPrazoHoras(e.target.value)}
                  />
                  <p className="text-xs text-tinta-media">24h = 1 dia, 240h = 10 dias.</p>
                </div>

                <div className="flex flex-col gap-1.5">
                  <Label htmlFor="alerta">Alerta em (%)</Label>
                  <Input
                    id="alerta"
                    type="number"
                    min={1}
                    max={99}
                    required
                    step={1}
                    value={percentualAlerta}
                    onChange={(e) => setPercentualAlerta(e.target.value)}
                  />
                  <p className="text-xs text-tinta-media">
                    Percentual do prazo consumido que acende o alerta.
                  </p>
                </div>
              </div>

              <div className="flex items-center justify-between">
                <Label htmlFor="dias-uteis">Contar só dias úteis</Label>
                <Switch
                  id="dias-uteis"
                  checked={apenasDiasUteis}
                  onCheckedChange={setApenasDiasUteis}
                />
              </div>
            </div>
          ) : null}

          {servico ? (
            <div className="flex items-center justify-between gap-3 border-t border-linha pt-3">
              <div><Label htmlFor="servico-ativo">Serviço ativo</Label><p className="mt-1 text-xs text-tinta-media">Desativar mantém os atendimentos já registrados.</p></div>
              <Switch id="servico-ativo" checked={ativo} onCheckedChange={setAtivo} />
            </div>
          ) : null}
          {servico ? <p className="text-xs leading-relaxed text-tinta-media">Alterar este prazo não recalcula os prazos dos atendimentos já vinculados.</p> : null}
        </div>
        </fieldset>

        <DialogFooter className="mt-5 border-t border-linha pt-4">
          <Button type="button" variant="outline" onClick={aoFechar} disabled={salvando}>
            Cancelar
          </Button>
          <Button type="submit" disabled={salvando}>
            {salvando ? 'Salvando…' : 'Salvar'}
          </Button>
        </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
