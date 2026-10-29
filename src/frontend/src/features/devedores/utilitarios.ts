import { formatarReais, formatarTelefone } from '@/lib/formatacao'
import type {
  DevedorConsolidado,
  ItemDeImportacaoDeDevedor,
  MeioDePagamento,
  PendenciaFinanceira,
} from '@/lib/api/tipos'

export { formatarReais }

export const MEIOS_DE_PAGAMENTO: { valor: MeioDePagamento; rotulo: string }[] = [
  { valor: 'Pix', rotulo: 'Pix' },
  { valor: 'Dinheiro', rotulo: 'Dinheiro no balcão' },
  { valor: 'Cartao', rotulo: 'Cartão' },
  { valor: 'Transferencia', rotulo: 'Transferência / TED' },
  { valor: 'Outro', rotulo: 'Outro meio' },
]

export function rotuloMeioDePagamento(meio: MeioDePagamento): string {
  return MEIOS_DE_PAGAMENTO.find((m) => m.valor === meio)?.rotulo ?? meio
}

/** Formata `YYYY-MM-DD` sem deslocamento de fuso horário ("28/10/2026"). */
export function formatarDataIso(dataIso: string | null | undefined): string {
  if (!dataIso) return '—'
  const partes = dataIso.slice(0, 10).split('-')
  if (partes.length !== 3) return dataIso
  return `${partes[2]}/${partes[1]}/${partes[0]}`
}

/** Retorna `YYYY-MM-DD` da data local de hoje. */
export function hojeIsoLocal(): string {
  const agora = new Date()
  const ano = agora.getFullYear()
  const mes = String(agora.getMonth() + 1).padStart(2, '0')
  const dia = String(agora.getDate()).padStart(2, '0')
  return `${ano}-${mes}-${dia}`
}

/** Soma `dias` à data local de hoje e devolve `YYYY-MM-DD`. */
export function somarDiasIsoLocal(dias: number): string {
  const data = new Date()
  data.setDate(data.getDate() + dias)
  const ano = data.getFullYear()
  const mes = String(data.getMonth() + 1).padStart(2, '0')
  const dia = String(data.getDate()).padStart(2, '0')
  return `${ano}-${mes}-${dia}`
}

/** Converte texto de moeda ("1.250,50" ou "1250.50") em número ou null. */
export function interpretarValorMonetario(texto: string): number | null {
  const limpo = texto.replace(/[R$\s]/gi, '').trim()
  if (!limpo) return null

  const normalizado =
    limpo.includes(',') && limpo.includes('.')
      ? limpo.replace(/\./g, '').replace(',', '.')
      : limpo.includes(',')
        ? limpo.replace(',', '.')
        : limpo

  const numero = Number(normalizado)
  if (!Number.isFinite(numero) || numero < 0) return null
  return Math.round(numero * 100) / 100
}

export type EstiloDeMensagemDeCobranca = 'cordial' | 'promessa' | 'formal'

export const ESTILOS_DE_COBRANCA: {
  id: EstiloDeMensagemDeCobranca
  rotulo: string
  descricao: string
}[] = [
  {
    id: 'cordial',
    rotulo: 'Lembrete cordial',
    descricao: 'Tom próximo e amigável para clientes recorrentes',
  },
  {
    id: 'promessa',
    rotulo: 'Compromisso agendado',
    descricao: 'Ideal para promessas que vencem hoje ou já venceram',
  },
  {
    id: 'formal',
    rotulo: 'Aviso institucional',
    descricao: 'Linguagem formal em nome da serventia',
  },
]

export function montarMensagemDeCobranca(opcoes: {
  estilo: EstiloDeMensagemDeCobranca
  clienteNome: string
  cartorioNome: string
  pendencias: Pick<PendenciaFinanceira, 'titulo' | 'saldoRestante'>[]
}): string {
  const nomeLimpo = opcoes.clienteNome.trim() || 'cliente'
  const primeiroNome = nomeLimpo.split(/\s+/)[0] || nomeLimpo
  const cartorio = opcoes.cartorioNome.trim() || 'cartório'

  const titulos = opcoes.pendencias.map((p) => p.titulo.trim()).filter(Boolean)
  const motivo =
    titulos.length === 0
      ? 'serviço realizado no cartório'
      : titulos.length === 1
        ? titulos[0]
        : `${titulos[0]} (+${titulos.length - 1} ${titulos.length - 1 === 1 ? 'item' : 'itens'})`

  const saldoTotal = opcoes.pendencias.reduce((acc, p) => acc + (p.saldoRestante ?? 0), 0)
  const valorTexto = saldoTotal > 0 ? ` no valor de ${formatarReais(saldoTotal)}` : ''

  switch (opcoes.estilo) {
    case 'promessa':
      return `Olá, ${primeiroNome}, tudo bem? Conforme combinamos, passando para confirmar se conseguimos acertar hoje a pendência referente a ${motivo}${valorTexto}, aqui no ${cartorio}. Posso te enviar a chave Pix por aqui?`

    case 'formal':
      return `Prezado(a) ${nomeLimpo}, entramos em contato pelo ${cartorio} para informar que permanece em aberto o lançamento referente a ${motivo}${valorTexto}. Pedimos a gentileza de responder a esta mensagem para combinarmos a regularização.`

    case 'cordial':
    default:
      return `Olá, ${primeiroNome}, tudo bem? Passando apenas para lembrar do acerto referente a ${motivo}${valorTexto}, aqui no ${cartorio}. Se precisar da chave Pix ou de qualquer detalhe, é só me avisar por aqui.`
  }
}

export function montarMensagemDeComprovante(opcoes: {
  clienteNome: string
  cartorioNome: string
  titulo: string
  valorPagoAgora: number
  saldoRestanteApos: number | null
}): string {
  const primeiroNome = opcoes.clienteNome.trim().split(/\s+/)[0] || 'cliente'
  const cartorio = opcoes.cartorioNome.trim() || 'cartório'
  const valorFmt = formatarReais(opcoes.valorPagoAgora)

  const restante =
    opcoes.saldoRestanteApos && opcoes.saldoRestanteApos > 0
      ? `Resta o saldo de ${formatarReais(opcoes.saldoRestanteApos)} em aberto. `
      : 'A pendência foi integralmente quitada. '

  return `Olá, ${primeiroNome}! Confirmamos o recebimento de ${valorFmt} referente a ${opcoes.titulo} aqui no ${cartorio}. ${restante}Muito obrigado!`
}

/**
 * Interpreta linhas coladas de planilha, bloco de notas ou WhatsApp:
 * `Nome; Telefone; Valor; Motivo; Promessa (DD/MM/AAAA ou AAAA-MM-DD)`
 */
export function interpretarTextoDeDevedores(
  texto: string,
  tituloPadrao: string,
): ItemDeImportacaoDeDevedor[] {
  const linhas = texto
    .split(/\r?\n/)
    .map((l) => l.trim())
    .filter((l) => l.length > 0 && !/^nome\s*[;|,\t]/i.test(l))

  const resultado: ItemDeImportacaoDeDevedor[] = []

  for (const linha of linhas) {
    const separador = linha.includes(';')
      ? ';'
      : linha.includes('\t')
        ? '\t'
        : linha.includes('|')
          ? '|'
          : ','

    const partes = linha.split(separador).map((p) => p.trim())
    if (partes.length < 2) continue

    const [col1, col2, col3, col4, col5] = partes
    const digitosCol1 = col1.replace(/\D/g, '')
    const digitosCol2 = col2.replace(/\D/g, '')

    const telefone = digitosCol2.length >= 10 ? col2 : digitosCol1.length >= 10 ? col1 : col2
    const nome = digitosCol2.length >= 10 ? col1 : col2

    const valorInterpretado = col3 ? interpretarValorMonetario(col3) : null
    const motivo =
      col4 || (col3 && valorInterpretado === null ? col3 : '') || tituloPadrao || 'Pendência de serviço'

    let promessaIso: string | null = null
    if (col5) {
      const mBr = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec(col5)
      const mIso = /^(\d{4})-(\d{2})-(\d{2})$/.exec(col5)
      if (mBr) promessaIso = `${mBr[3]}-${mBr[2]}-${mBr[1]}`
      else if (mIso) promessaIso = col5
    }

    resultado.push({
      nome: nome || null,
      telefone,
      valorOriginal: valorInterpretado,
      titulo: motivo,
      dataPromessaPagamento: promessaIso,
    })
  }

  return resultado
}

export function exportarDevedoresCsv(
  devedores: DevedorConsolidado[],
  lancamentos: PendenciaFinanceira[],
  visao: 'contatos' | 'lancamentos',
) {
  const cabecalho =
    visao === 'contatos'
      ? ['Cliente', 'WhatsApp', 'Pendencias em aberto', 'Valor Original (R$)', 'Valor Pago (R$)', 'Saldo Devedor (R$)', 'Desde', 'Proxima Promessa', 'Total Cobrancas']
      : ['Cliente', 'WhatsApp', 'Motivo / Ato', 'Servico', 'Status', 'Valor Original (R$)', 'Valor Pago (R$)', 'Saldo Restante (R$)', 'Data Referencia', 'Promessa Pagamento', 'Cobrancas']

  const linhas =
    visao === 'contatos'
      ? devedores.map((d) => [
          d.clienteNome,
          formatarTelefone(d.clienteTelefone),
          String(d.quantidadePendenciasAbertas),
          d.valorOriginalTotal.toFixed(2).replace('.', ','),
          d.valorPagoTotal.toFixed(2).replace('.', ','),
          d.saldoRestanteTotal.toFixed(2).replace('.', ','),
          formatarDataIso(d.dataReferenciaMaisAntiga),
          formatarDataIso(d.proximaPromessaPagamento),
          String(d.totalCobrancas),
        ])
      : lancamentos.map((p) => [
          p.clienteNome,
          formatarTelefone(p.clienteTelefone),
          p.titulo,
          p.tipoServicoNome ?? '',
          p.status,
          p.valorOriginal !== null ? p.valorOriginal.toFixed(2).replace('.', ',') : 'A apurar',
          p.valorPago.toFixed(2).replace('.', ','),
          p.saldoRestante !== null ? p.saldoRestante.toFixed(2).replace('.', ',') : 'A apurar',
          formatarDataIso(p.dataReferencia),
          formatarDataIso(p.dataPromessaPagamento),
          String(p.totalCobrancas),
        ])

  const escapar = (v: string) => `"${v.replace(/"/g, '""')}"`
  const conteudo =
    '\uFEFF' +
    [cabecalho.map(escapar).join(';'), ...linhas.map((l) => l.map(escapar).join(';'))].join('\r\n')

  const blob = new Blob([conteudo], { type: 'text/csv;charset=utf-8;' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = `devedores-cartorio-${hojeIsoLocal()}.csv`
  document.body.appendChild(link)
  link.click()
  document.body.removeChild(link)
  URL.revokeObjectURL(url)
}
