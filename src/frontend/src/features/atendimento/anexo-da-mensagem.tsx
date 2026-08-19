import { Download, FileText, ImageOff, Loader2 } from 'lucide-react'
import { formatarTamanhoDeArquivo } from '@/lib/formatacao'
import { salvarAnexo, usarAnexo } from './cache-de-anexos'
import { PlayerDeAudio } from './player-de-audio'
import type { Mensagem } from '@/lib/api/tipos'

/**
 * O arquivo de uma mensagem.
 *
 * O binário vem por fetch autenticado e vira object URL, **não** por `src` com
 * token na query: credencial em URL fica no histórico do navegador e em qualquer
 * log de acesso pelo caminho. Quem gerencia essa URL é `cache-de-anexos.ts`, e
 * só ele — o fio, a galeria e o visualizador olham a mesma foto e o binário
 * desce uma vez só.
 *
 * Imagem, áudio e vídeo abrem na própria conversa — o atendente precisa ver a
 * foto do documento sem sair do fio. Qualquer outro tipo vira linha de download:
 * abrir arquivo escolhido pelo cliente dentro do domínio do painel é o que
 * transformaria um anexo em código rodando com a sessão de quem atende.
 *
 * CLICAR NA IMAGEM AMPLIA, NÃO BAIXA. Até 2026-08-19 o clique disparava um
 * `<a download>`, e a foto do RG que o atendente queria conferir ia parar na
 * pasta de downloads de um computador de balcão. Baixar continua existindo, com
 * botão próprio dentro do visualizador.
 */
export function AnexoDaMensagem({
  mensagem,
  aoAbrirImagem,
}: {
  mensagem: Mensagem
  aoAbrirImagem?: (mensagemId: string) => void
}) {
  const mime = mensagem.arquivoTipoMime ?? ''
  const ehImagem = mime.startsWith('image/')
  const ehAudio = mime.startsWith('audio/')
  const ehVideo = mime.startsWith('video/')

  // Documento não é pré-carregado: baixar todo PDF de um fio longo gastaria
  // banda e memória por arquivo que ninguém vai abrir.
  const abreNoFio = ehImagem || ehAudio || ehVideo

  const nome = mensagem.arquivoNome ?? 'anexo'
  const tamanho = formatarTamanhoDeArquivo(mensagem.arquivoTamanhoBytes)

  const { url, erro } = usarAnexo(mensagem.conversaId, mensagem.id, abreNoFio)

  async function baixar() {
    try {
      await salvarAnexo(mensagem.conversaId, mensagem.id, nome)
    } catch {
      // O cache memoriza a falha e a linha passa a mostrá-la; um toast aqui
      // competiria com esse aviso em vez de acrescentar informação.
    }
  }

  if (erro) {
    return (
      <p className="mb-2 flex items-center gap-1.5 rounded-md border border-linha-forte bg-superficie px-2 py-1.5 text-[11px] text-tinta-media">
        <ImageOff className="size-3.5 shrink-0" />
        {erro}
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
        onClick={() => aoAbrirImagem?.(mensagem.id)}
        className="mb-2 block overflow-hidden rounded-md border border-linha-forte transition-opacity hover:opacity-90"
        title={`${nome}${tamanho ? ` — ${tamanho}` : ''}. Clique para ampliar.`}
      >
        {/* 208px, e nao 288px: uma foto sozinha comia o espaco de quatro
            mensagens no fio de um monitor pequeno. */}
        <img src={url} alt={nome} className="max-h-52 w-auto max-w-full object-contain" />
      </button>
    )
  }

  if (ehAudio && url) {
    // O `<audio controls>` nativo quebrava visualmente dentro do balão
    // estreito do fio — o Chromium exige uma largura mínima que o balão não
    // tem, e o widget desenhava como uma mancha escura em vez do player.
    // `PlayerDeAudio` é flexbox comum, então respeita qualquer largura.
    return <PlayerDeAudio url={url} aoBaixar={() => void baixar()} className="mb-2 w-full max-w-72" />
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
