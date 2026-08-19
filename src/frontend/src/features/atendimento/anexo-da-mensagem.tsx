import { useEffect, useState } from 'react'
import { Download, FileText, ImageOff, Loader2 } from 'lucide-react'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { conversasApi } from '@/lib/api/endpoints'
import type { Mensagem } from '@/lib/api/tipos'

/**
 * Tamanho legível. Cartório recebe foto de documento o dia todo, e "2,4 MB" diz
 * mais a quem vai abrir num computador de balcão do que "2517483".
 */
function formatarTamanho(bytes: number | null): string | null {
  if (bytes === null || bytes <= 0) {
    return null
  }

  if (bytes < 1024) {
    return `${bytes} B`
  }

  const kb = bytes / 1024
  if (kb < 1024) {
    return `${Math.round(kb)} KB`
  }

  return `${(kb / 1024).toFixed(1).replace('.', ',')} MB`
}

/**
 * O arquivo de uma mensagem.
 *
 * O binário vem por fetch autenticado e vira object URL, **não** por `src` com
 * token na query: credencial em URL fica no histórico do navegador e em qualquer
 * log de acesso pelo caminho.
 *
 * Imagem, áudio e vídeo abrem na própria conversa — o atendente precisa ver a
 * foto do documento sem sair do fio. Qualquer outro tipo vira linha de download:
 * abrir arquivo escolhido pelo cliente dentro do domínio do painel é o que
 * transformaria um anexo em código rodando com a sessão de quem atende.
 */
export function AnexoDaMensagem({ mensagem }: { mensagem: Mensagem }) {
  const [url, setUrl] = useState<string | null>(null)
  const [falha, setFalha] = useState<string | null>(null)

  const mime = mensagem.arquivoTipoMime ?? ''
  const ehImagem = mime.startsWith('image/')
  const ehAudio = mime.startsWith('audio/')
  const ehVideo = mime.startsWith('video/')
  const abreNoFio = ehImagem || ehAudio || ehVideo

  const nome = mensagem.arquivoNome ?? 'anexo'
  const tamanho = formatarTamanho(mensagem.arquivoTamanhoBytes)

  useEffect(() => {
    // Documento não é pré-carregado: baixar todo PDF de um fio longo gastaria
    // banda e memória por arquivo que ninguém vai abrir.
    if (!abreNoFio) {
      return
    }

    const cancelar = new AbortController()
    let objeto: string | null = null

    conversasApi
      .baixarAnexo(mensagem.conversaId, mensagem.id, cancelar.signal)
      .then((blob) => {
        objeto = URL.createObjectURL(blob)
        setUrl(objeto)
      })
      .catch((erro: unknown) => {
        if (cancelar.signal.aborted) {
          return
        }

        setFalha(
          erro instanceof ErroDaApi && erro.codigo === 'anexo_nao_encontrado'
            ? 'O arquivo não está no servidor.'
            : 'Não foi possível carregar o arquivo.',
        )
      })

    return () => {
      cancelar.abort()

      // Sem o revoke, cada abertura de conversa deixaria os blobs vivos até
      // recarregar a página — num fio com muitas fotos, isso é memória que só
      // cresce.
      if (objeto) {
        URL.revokeObjectURL(objeto)
      }
    }
  }, [abreNoFio, mensagem.conversaId, mensagem.id])

  async function baixar() {
    try {
      const blob = await conversasApi.baixarAnexo(mensagem.conversaId, mensagem.id)
      const objeto = URL.createObjectURL(blob)

      const link = document.createElement('a')
      link.href = objeto
      link.download = nome
      link.click()

      URL.revokeObjectURL(objeto)
    } catch (erro) {
      setFalha(
        erro instanceof ErroDaApi && erro.codigo === 'anexo_nao_encontrado'
          ? 'O arquivo não está no servidor.'
          : 'Não foi possível baixar o arquivo.',
      )
    }
  }

  if (falha) {
    return (
      <p className="mb-2 flex items-center gap-1.5 rounded-md border border-linha-forte bg-superficie px-2 py-1.5 text-[11px] text-tinta-media">
        <ImageOff className="size-3.5 shrink-0" />
        {falha}
      </p>
    )
  }

  if (abreNoFio && !url) {
    return (
      <p className="mb-2 flex items-center gap-1.5 text-[11px] text-tinta-fraca">
        <Loader2 className="size-3.5 shrink-0 animate-spin" />
        Carregando {nome}…
      </p>
    )
  }

  if (ehImagem && url) {
    return (
      <button
        type="button"
        onClick={() => void baixar()}
        className="mb-2 block overflow-hidden rounded-md border border-linha-forte"
        title={`${nome}${tamanho ? ` — ${tamanho}` : ''}. Clique para baixar.`}
      >
        {/* 208px, e nao 288px: uma foto sozinha comia o espaco de quatro
            mensagens no fio de um monitor pequeno. */}
        <img src={url} alt={nome} className="max-h-52 w-auto max-w-full object-contain" />
      </button>
    )
  }

  if (ehAudio && url) {
    return <audio src={url} controls className="mb-2 w-full max-w-72" />
  }

  if (ehVideo && url) {
    return <video src={url} controls className="mb-2 max-h-52 w-full rounded-md" />
  }

  return (
    <button
      type="button"
      onClick={() => void baixar()}
      className="mb-2 flex w-full items-center gap-2 rounded-md border border-linha-forte bg-superficie px-2.5 py-2 text-left transition-colors hover:bg-branco"
      title={`Baixar ${nome}`}
    >
      <FileText className="size-4 shrink-0 text-tinta-media" />
      <span className="min-w-0 flex-1">
        <span className="block truncate text-xs text-tinta">{nome}</span>
        {tamanho ? <span className="block font-dado text-[10px] text-tinta-fraca">{tamanho}</span> : null}
      </span>
      <Download className="size-3.5 shrink-0 text-tinta-fraca" />
    </button>
  )
}
