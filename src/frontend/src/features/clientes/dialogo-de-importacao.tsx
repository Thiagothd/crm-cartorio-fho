import { useRef, useState } from 'react'
import { toast } from 'sonner'
import { FileUp, Loader2, MessageCircle, Upload } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'
import { Badge } from '@/components/ui/badge'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { importacaoDeContatosApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { formatarTelefone } from '@/lib/formatacao'
import type { AnalisarImportacaoResponse, FonteDeImportacao, SituacaoDaLinhaImportada } from '@/lib/api/tipos'

type Aba = 'texto' | 'csv' | 'whatsapp'

const rotuloDaSituacao: Record<SituacaoDaLinhaImportada, string> = {
  Nova: 'Nova',
  JaCadastrada: 'Já cadastrada',
  TelefoneInvalido: 'Telefone inválido',
  DddInvalido: 'DDD inválido',
  Ambigua: 'Revisar',
  DuplicadaNoArquivo: 'Duplicada',
}

function variantDaSituacao(situacao: SituacaoDaLinhaImportada) {
  switch (situacao) {
    case 'Nova':
      return 'success' as const
    case 'TelefoneInvalido':
    case 'DddInvalido':
      return 'destructive' as const
    case 'Ambigua':
      return 'warning' as const
    default:
      return 'secondary' as const
  }
}

/**
 * Diálogo de importação em lote. Só cadastra — nunca envia mensagem, ver
 * CLAUDE.md §2 sobre por que disparo em massa não faz parte deste ajuste.
 *
 * Só a linha `Nova` é selecionável: é a única situação com um E.164 pronto
 * para gravar. `Ambigua` (DDD válido, 12 dígitos — pode ser fixo ou celular
 * sem o nono dígito) fica visível na prévia para o operador entender por que
 * ficou de fora, mas corrigir o número é trabalho de cadastro manual, não
 * desta tela.
 */
export function DialogoDeImportacao({
  aberto,
  aoFechar,
  aoImportar,
}: {
  aberto: boolean
  aoFechar: () => void
  aoImportar: () => void
}) {
  const [aba, setAba] = useState<Aba>('texto')
  const [texto, setTexto] = useState('')
  const [analisando, setAnalisando] = useState(false)
  const [confirmando, setConfirmando] = useState(false)
  const [resultado, setResultado] = useState<AnalisarImportacaoResponse | null>(null)
  const [fonteDoResultado, setFonteDoResultado] = useState<FonteDeImportacao>('Texto')
  const [selecionadas, setSelecionadas] = useState<Set<number>>(new Set())
  const seletorDeArquivo = useRef<HTMLInputElement>(null)

  function reiniciar() {
    setTexto('')
    setResultado(null)
    setSelecionadas(new Set())
    setAba('texto')
  }

  function fechar() {
    reiniciar()
    aoFechar()
  }

  function aplicarResultado(fonte: FonteDeImportacao, resposta: AnalisarImportacaoResponse) {
    setFonteDoResultado(fonte)
    setResultado(resposta)
    setSelecionadas(
      new Set(resposta.linhas.filter((linha) => linha.situacao === 'Nova').map((linha) => linha.numeroDaLinha)),
    )
  }

  async function analisar(conteudo: string) {
    if (!conteudo.trim()) {
      toast.error('Cole a lista de contatos ou escolha um arquivo.')
      return
    }

    setAnalisando(true)
    setResultado(null)
    setSelecionadas(new Set())

    try {
      aplicarResultado('Texto', await importacaoDeContatosApi.analisarTexto(conteudo))
    } catch (falha) {
      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível analisar a lista.')
    } finally {
      setAnalisando(false)
    }
  }

  async function analisarWhatsApp() {
    setAnalisando(true)
    setResultado(null)
    setSelecionadas(new Set())

    try {
      aplicarResultado('WhatsApp', await importacaoDeContatosApi.analisarWhatsApp())
    } catch (falha) {
      toast.error(
        falha instanceof ErroDaApi ? falha.message : 'Não foi possível consultar os contatos do WhatsApp.',
      )
    } finally {
      setAnalisando(false)
    }
  }

  function escolherArquivo(arquivo: File | null) {
    if (!arquivo) return

    const leitor = new FileReader()
    leitor.onload = () => {
      const conteudo = typeof leitor.result === 'string' ? leitor.result : ''
      setTexto(conteudo)
      void analisar(conteudo)
    }
    leitor.readAsText(arquivo, 'utf-8')
  }

  function alternarSelecao(numeroDaLinha: number) {
    setSelecionadas((atuais) => {
      const proximo = new Set(atuais)
      if (proximo.has(numeroDaLinha)) {
        proximo.delete(numeroDaLinha)
      } else {
        proximo.add(numeroDaLinha)
      }
      return proximo
    })
  }

  async function confirmar() {
    if (!resultado) return

    const linhas = resultado.linhas
      .filter((linha) => selecionadas.has(linha.numeroDaLinha) && linha.telefoneE164)
      .map((linha) => ({ nome: linha.nome, telefoneE164: linha.telefoneE164! }))

    if (linhas.length === 0) {
      toast.error('Selecione ao menos um contato.')
      return
    }

    setConfirmando(true)

    try {
      const resposta = await importacaoDeContatosApi.confirmar(fonteDoResultado, linhas)
      toast.success(
        resposta.ignorados > 0
          ? `${resposta.criados} contato(s) importado(s), ${resposta.ignorados} ignorado(s) (já cadastrados nesse meio-tempo).`
          : `${resposta.criados} contato(s) importado(s).`,
      )
      aoImportar()
      fechar()
    } catch (falha) {
      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível confirmar a importação.')
    } finally {
      setConfirmando(false)
    }
  }

  return (
    <Dialog open={aberto} onOpenChange={(estaAberto) => !estaAberto && fechar()}>
      <DialogContent className="flex max-h-[85dvh] flex-col sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>Importar contatos</DialogTitle>
          <DialogDescription>
            Cadastra contatos em lote. Não envia mensagem nenhuma — só cria a ficha do cliente.
          </DialogDescription>
        </DialogHeader>

        <Tabs value={aba} onValueChange={(valor) => setAba(valor as Aba)}>
          <TabsList>
            <TabsTrigger value="texto">Colar lista</TabsTrigger>
            <TabsTrigger value="csv">Arquivo CSV</TabsTrigger>
            <TabsTrigger value="whatsapp">Contatos do WhatsApp</TabsTrigger>
          </TabsList>

          <TabsContent value="texto" className="flex flex-col gap-3">
            <Textarea
              value={texto}
              onChange={(e) => setTexto(e.target.value)}
              placeholder={'Nome, telefone\nMaria Silva, (38) 99999-0001\n(38) 98888-0002'}
              className="min-h-[140px] font-dado text-xs"
            />
            <Button onClick={() => void analisar(texto)} disabled={analisando} className="self-start">
              {analisando ? <Loader2 className="animate-spin" /> : null}
              Analisar lista
            </Button>
          </TabsContent>

          <TabsContent value="csv" className="flex flex-col gap-3">
            <input
              ref={seletorDeArquivo}
              type="file"
              accept=".csv,text/csv"
              className="hidden"
              onChange={(e) => {
                escolherArquivo(e.target.files?.[0] ?? null)
                e.target.value = ''
              }}
            />
            <Button
              variant="outline"
              onClick={() => seletorDeArquivo.current?.click()}
              disabled={analisando}
              className="self-start"
            >
              {analisando ? <Loader2 className="animate-spin" /> : <FileUp />}
              Escolher arquivo CSV
            </Button>
          </TabsContent>

          <TabsContent value="whatsapp" className="flex flex-col gap-3">
            <p className="text-sm text-tinta-media">
              Lê a agenda salva pelo número que está conectado agora — o mesmo que aparece em
              Configurações → WhatsApp. Trocou de número por lá, a consulta busca do número novo.
            </p>
            <Button onClick={() => void analisarWhatsApp()} disabled={analisando} className="self-start">
              {analisando ? <Loader2 className="animate-spin" /> : <MessageCircle />}
              Buscar contatos do WhatsApp
            </Button>
            {resultado && fonteDoResultado === 'WhatsApp' && resultado.linhas.length === 0 ? (
              <p className="text-sm text-tinta-fraca">
                Nenhum contato encontrado. Isso não é necessariamente erro — a Evolution só devolve o que
                sincronizou no pareamento, e pode não ter sincronizado agenda nenhuma para este número.
              </p>
            ) : null}
          </TabsContent>
        </Tabs>

        {resultado ? (
          <div className="flex flex-col gap-2 border-t border-linha pt-3">
            <div className="flex flex-wrap gap-1.5 text-xs">
              <Badge variant="success">{resultado.resumo.novas} novas</Badge>
              {resultado.resumo.jaCadastradas > 0 ? (
                <Badge variant="secondary">{resultado.resumo.jaCadastradas} já cadastradas</Badge>
              ) : null}
              {resultado.resumo.invalidas > 0 ? (
                <Badge variant="destructive">{resultado.resumo.invalidas} inválidas</Badge>
              ) : null}
              {resultado.resumo.ambiguas > 0 ? (
                <Badge variant="warning">{resultado.resumo.ambiguas} para revisar</Badge>
              ) : null}
              {resultado.resumo.duplicadas > 0 ? (
                <Badge variant="secondary">{resultado.resumo.duplicadas} duplicadas</Badge>
              ) : null}
              <span className="ml-auto text-tinta-fraca">{resultado.resumo.totalLidas} linhas lidas</span>
            </div>

            {/* max-h em dvh, não flex-1: o diálogo é `position: fixed` com
                max-height (não height fixo), então um filho flex-1 não tem
                "espaço restante" nenhum para calcular e cresce pro tamanho
                natural do conteúdo — foi o que causou a tela toda desmontar
                com uma lista de centenas de linhas. dvh resolve contra o
                viewport direto, sem depender da altura do ancestral. */}
            <div className="max-h-[40dvh] overflow-y-auto rounded-md border border-linha">
              <table className="w-full text-sm">
                <tbody>
                  {resultado.linhas.map((linha) => (
                    <tr key={linha.numeroDaLinha} className="border-b border-linha last:border-0">
                      <td className="w-8 px-2 py-1.5">
                        {linha.situacao === 'Nova' ? (
                          <input
                            type="checkbox"
                            className="size-4 accent-tinta"
                            checked={selecionadas.has(linha.numeroDaLinha)}
                            onChange={() => alternarSelecao(linha.numeroDaLinha)}
                            aria-label={`Incluir ${linha.nome ?? linha.telefoneBruto}`}
                          />
                        ) : null}
                      </td>
                      <td className="px-2 py-1.5">
                        <span className="block truncate font-medium text-tinta">{linha.nome ?? '—'}</span>
                        <span className="block font-dado text-xs text-tinta-fraca">
                          {linha.telefoneE164 ? formatarTelefone(linha.telefoneE164) : linha.telefoneBruto}
                        </span>
                      </td>
                      <td className="px-2 py-1.5 text-right">
                        <Badge variant={variantDaSituacao(linha.situacao)}>
                          {linha.situacao === 'JaCadastrada' && linha.nomeDoCadastroExistente
                            ? `É ${linha.nomeDoCadastroExistente}`
                            : rotuloDaSituacao[linha.situacao]}
                        </Badge>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        ) : null}

        <DialogFooter>
          <Button variant="outline" onClick={fechar}>
            Cancelar
          </Button>
          {resultado ? (
            <Button onClick={() => void confirmar()} disabled={confirmando || selecionadas.size === 0}>
              {confirmando ? <Loader2 className="animate-spin" /> : <Upload />}
              Importar{selecionadas.size > 0 ? ` (${selecionadas.size})` : ''}
            </Button>
          ) : null}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
