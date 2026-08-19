import { useMemo } from 'react'
import {
  File as Arquivo,
  FileSpreadsheet,
  FileText,
  FileType,
  type LucideIcon,
} from 'lucide-react'
import { extensaoDoArquivo } from '@/lib/formatacao'
import type { Mensagem } from '@/lib/api/tipos'

/**
 * A galeria da conversa é DERIVADA das mensagens que já estão na tela.
 *
 * Não há tabela de anexo nem endpoint de galeria, e não deve haver: o anexo mora
 * nas quatro colunas `Arquivo*` da própria mensagem, e `GET /api/conversas/{id}`
 * já devolve o fio inteiro. Derivar daqui significa que a galeria acompanha o
 * tempo real de graça — o SignalR invalida a consulta da conversa, o array muda,
 * e a grade cresce sozinha. Um segundo mecanismo de atualização seria uma
 * segunda verdade.
 */

export type CategoriaDeMidia = 'midia' | 'audio' | 'documento'

export interface ItemDeMidia {
  mensagemId: string
  conversaId: string
  nome: string
  tipoMime: string
  categoria: CategoriaDeMidia
  /** Só imagem abre no visualizador; vídeo entra na aba Mídia mas baixa. */
  ehImagem: boolean
  ehVideo: boolean
  tamanhoBytes: number | null
  enviadaEm: string
  autor: string
  doCliente: boolean
  oculta: boolean
}

export interface ColecaoDeMidias {
  midias: ItemDeMidia[]
  audios: ItemDeMidia[]
  documentos: ItemDeMidia[]
  /** Subconjunto de `midias`: é o que o visualizador navega com as setas. */
  imagens: ItemDeMidia[]
  total: number
  /** Quantos anexos ficaram de fora por estarem em mensagem ocultada. */
  ocultasIgnoradas: number
}

const COLECAO_VAZIA: ColecaoDeMidias = {
  midias: [],
  audios: [],
  documentos: [],
  imagens: [],
  total: 0,
  ocultasIgnoradas: 0,
}

/** Mime que não diz nada e obriga a cair para o enum do backend. */
function mimeEhInutil(mime: string) {
  return mime.length === 0 || mime === 'application/octet-stream'
}

/**
 * Classifica pela categoria da aba.
 *
 * MIME primeiro, enum como rede de segurança. O enum `tipo` é o que o backend
 * INFERIU quando o arquivo chegou; o MIME é o que o arquivo É. Quando o MIME
 * falta ou vem genérico, o palpite do backend ainda é melhor que nada. A
 * extensão do nome nunca decide aba — só aparece no rótulo.
 */
function categoriaDe(mensagem: Mensagem): CategoriaDeMidia {
  const mime = (mensagem.arquivoTipoMime ?? '').toLowerCase()

  if (!mimeEhInutil(mime)) {
    if (mime.startsWith('image/') || mime.startsWith('video/')) {
      return 'midia'
    }

    // Nota de voz do WhatsApp chega como "audio/ogg; codecs=opus".
    if (mime.startsWith('audio/')) {
      return 'audio'
    }

    return 'documento'
  }

  if (mensagem.tipo === 'Imagem' || mensagem.tipo === 'Video') {
    return 'midia'
  }

  if (mensagem.tipo === 'Audio') {
    return 'audio'
  }

  return 'documento'
}

function ehImagemDe(mensagem: Mensagem) {
  const mime = (mensagem.arquivoTipoMime ?? '').toLowerCase()

  return mimeEhInutil(mime) ? mensagem.tipo === 'Imagem' : mime.startsWith('image/')
}

function ehVideoDe(mensagem: Mensagem) {
  const mime = (mensagem.arquivoTipoMime ?? '').toLowerCase()

  return mimeEhInutil(mime) ? mensagem.tipo === 'Video' : mime.startsWith('video/')
}

function autorDe(mensagem: Mensagem, clienteNome: string) {
  if (mensagem.autorTipo === 'Cliente') {
    return clienteNome
  }

  return mensagem.atendenteNome ?? (mensagem.ehAutomatica ? 'Automática' : 'Serventia')
}

/**
 * @param incluirOcultas É o MESMO interruptor "Mostrar" do fio, não uma cópia —
 *   ele vive na página e serve os dois. Ocultar é ato registrado do atendente, e
 *   se a galeria mostrasse assim mesmo o botão "Ocultar da conversa" seria
 *   mentira: o arquivo continuaria a um clique, no painel ao lado, na mesma
 *   tela. Mas ocultar não é apagar, então o painel oferece a mesma saída.
 */
export function derivarMidias(
  mensagens: Mensagem[],
  clienteNome: string,
  incluirOcultas: boolean,
): ColecaoDeMidias {
  const comArquivo = mensagens.filter((m) => m.arquivoNome !== null)

  if (comArquivo.length === 0) {
    return COLECAO_VAZIA
  }

  const ocultasIgnoradas = incluirOcultas ? 0 : comArquivo.filter((m) => m.oculta).length
  const visiveis = incluirOcultas ? comArquivo : comArquivo.filter((m) => !m.oculta)

  const itens: ItemDeMidia[] = visiveis.map((mensagem) => ({
    mensagemId: mensagem.id,
    conversaId: mensagem.conversaId,
    nome: mensagem.arquivoNome ?? 'anexo',
    tipoMime: mensagem.arquivoTipoMime ?? '',
    categoria: categoriaDe(mensagem),
    ehImagem: ehImagemDe(mensagem),
    ehVideo: ehVideoDe(mensagem),
    tamanhoBytes: mensagem.arquivoTamanhoBytes,
    enviadaEm: mensagem.enviadaEm,
    autor: autorDe(mensagem, clienteNome),
    doCliente: mensagem.autorTipo === 'Cliente',
    oculta: mensagem.oculta,
  }))

  // Mais recente primeiro na grade e nas listas: numa conversa longa, o que
  // acabou de chegar é o que se procura.
  const recentesPrimeiro = [...itens].sort(
    (a, b) => new Date(b.enviadaEm).getTime() - new Date(a.enviadaEm).getTime(),
  )

  const midias = recentesPrimeiro.filter((i) => i.categoria === 'midia')

  return {
    midias,
    audios: recentesPrimeiro.filter((i) => i.categoria === 'audio'),
    documentos: recentesPrimeiro.filter((i) => i.categoria === 'documento'),
    imagens: midias.filter((i) => i.ehImagem),
    total: itens.length,
    ocultasIgnoradas,
  }
}

export function usarMidiasDaConversa(
  mensagens: Mensagem[],
  clienteNome: string,
  incluirOcultas: boolean,
): ColecaoDeMidias {
  return useMemo(
    () => derivarMidias(mensagens, clienteNome, incluirOcultas),
    [mensagens, clienteNome, incluirOcultas],
  )
}

/**
 * Ícone por tipo de documento.
 *
 * Mora aqui, e não em `lib/formatacao.ts`, porque aquele módulo é string entra,
 * string sai — não importa nada. Trazer `LucideIcon` para lá levaria uma
 * dependência de interface para dentro de um módulo puro de formatação.
 */
export function iconeDeDocumento(tipoMime: string, nome: string): LucideIcon {
  const mime = tipoMime.toLowerCase()
  const extensao = extensaoDoArquivo(nome) ?? ''

  if (mime === 'application/pdf' || extensao === 'PDF') {
    return FileType
  }

  if (mime.includes('spreadsheet') || mime.includes('excel') || extensao === 'XLS' || extensao === 'XLSX') {
    return FileSpreadsheet
  }

  if (mime.includes('word') || mime === 'text/plain' || extensao === 'DOC' || extensao === 'DOCX' || extensao === 'TXT') {
    return FileText
  }

  return Arquivo
}

/** Rótulo curto do tipo, para a linha de metadado do documento. */
export function rotuloDeTipo(tipoMime: string, nome: string): string {
  const extensao = extensaoDoArquivo(nome)

  if (extensao) {
    return extensao
  }

  const mime = tipoMime.toLowerCase()

  if (mime === 'application/pdf') return 'PDF'
  if (mime.includes('spreadsheet') || mime.includes('excel')) return 'Planilha'
  if (mime.includes('word')) return 'Word'
  if (mime === 'text/plain') return 'Texto'

  return 'Arquivo'
}
