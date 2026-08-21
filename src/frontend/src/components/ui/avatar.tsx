import { useEffect, useState } from 'react'
import { cn } from '@/lib/utils'
import { iniciaisDe } from '@/lib/formatacao'
import { api } from '@/lib/api/cliente-http'

const tamanhos = {
  sm: 'size-7 text-[10px]',
  md: 'size-9 text-[11px]',
  lg: 'size-11 text-xs',
}

// Cache em memória para os object URLs criados (evita múltiplos downloads da mesma foto)
const cacheDeUrls = new Map<string, string>()
const fotosComFalha = new Set<string>()
const promessasEmAndamento = new Map<string, Promise<string | null>>()

/**
 * URL absoluta `http(s)` — foto hospedada FORA da nossa API (o seed de
 * demonstração usa Unsplash, e uma futura integração pode devolver a URL do
 * CDN do WhatsApp).
 */
function ehUrlExterna(url: string) {
  return /^https?:\/\//i.test(url)
}

function carregarFoto(url: string): Promise<string | null> {
  if (cacheDeUrls.has(url)) {
    return Promise.resolve(cacheDeUrls.get(url)!)
  }
  if (fotosComFalha.has(url)) {
    return Promise.resolve(null)
  }
  if (promessasEmAndamento.has(url)) {
    return promessasEmAndamento.get(url)!
  }

  // Blob, data URI e URL EXTERNA vão direto para o `<img src>`, sem passar
  // pelo cliente HTTP da API.
  //
  // A checagem de `ehUrlExterna` faltava, e o comentário original desta linha
  // já dizia "ou url externa pública" — o código só não fazia. Duas
  // consequências, as duas ruins:
  //
  // 1. FUNCIONAL: `baseDaApi` é string vazia por padrão (proxy do Vite em dev,
  //    mesma origem em produção), então `api.blob('https://host/foto.jpg')`
  //    virava `fetch('https://host/foto.jpg')` com header `Authorization`. Isso
  //    dispara preflight CORS, o host de terceiro não libera esse header, o
  //    fetch falha e a foto nunca aparecia — caía silenciosamente nas iniciais.
  //
  // 2. SEGURANÇA: se o host de terceiro POR ACASO liberar `Authorization` no
  //    CORS, o JWT do atendente é enviado para ele. `FotoPerfilUrl` vem do
  //    banco, então bastava um valor apontando para um host controlado por
  //    terceiro para vazar credencial de sessão. Token só vai para a nossa API.
  //
  // Caminho RELATIVO (ex.: `/api/clientes/{id}/foto-perfil`) continua passando
  // pelo `api.blob`, porque ali o token é obrigatório: o endpoint é autenticado
  // e confere tenant antes de devolver os bytes.
  if (url.startsWith('blob:') || url.startsWith('data:') || ehUrlExterna(url)) {
    return Promise.resolve(url)
  }

  const promessa = api
    .blob(url)
    .then((blob) => {
      const objetoUrl = URL.createObjectURL(blob)
      cacheDeUrls.set(url, objetoUrl)
      promessasEmAndamento.delete(url)
      return objetoUrl
    })
    .catch(() => {
      fotosComFalha.add(url)
      promessasEmAndamento.delete(url)
      return null
    })

  promessasEmAndamento.set(url, promessa)
  return promessa
}

/**
 * Iniciais em caixa quadrada ou foto de perfil real quando disponível.
 *
 * Tratamento:
 * - Com `fotoUrl` válido: busca o binário autenticado via Bearer JWT e exibe a
 *   imagem. Caso retorne 404 (sem foto), faz fallback automático e gracioso
 *   para as iniciais.
 * - Sem `corDeFundo`: caixa clara com borda 1px e iniciais em tinta. É o padrão
 *   para avatar sem etapa associada (ex.: cliente na listagem de cadastro, atendente).
 * - Com `corDeFundo`: fundo sólido e iniciais em branco. Usado no atendimento,
 *   onde a cor identifica a ETAPA da conversa (docs/DESIGN.md, seção 2.2).
 */
export function Avatar({
  nome,
  fotoUrl,
  tamanho = 'md',
  corDeFundo,
  className,
}: {
  nome: string
  fotoUrl?: string | null
  tamanho?: keyof typeof tamanhos
  corDeFundo?: string | null
  className?: string
}) {
  const [src, setSrc] = useState<string | null>(() => (fotoUrl && cacheDeUrls.get(fotoUrl)) || null)
  const solido = Boolean(corDeFundo)

  useEffect(() => {
    if (!fotoUrl) {
      setSrc(null)
      return
    }

    if (cacheDeUrls.has(fotoUrl)) {
      setSrc(cacheDeUrls.get(fotoUrl)!)
      return
    }

    let cancelado = false

    carregarFoto(fotoUrl).then((urlCarregada) => {
      if (!cancelado) {
        setSrc(urlCarregada)
      }
    })

    return () => {
      cancelado = true
    }
  }, [fotoUrl])

  if (src) {
    return (
      <span
        className={cn(
          'inline-flex shrink-0 items-center justify-center overflow-hidden rounded-md border border-linha-forte bg-superficie transition-colors',
          tamanhos[tamanho],
          className,
        )}
        title={nome}
      >
        <img
          src={src}
          alt={nome}
          className="size-full object-cover"
          loading="lazy"
          onError={() => setSrc(null)}
        />
      </span>
    )
  }

  return (
    <span
      className={cn(
        'inline-flex shrink-0 items-center justify-center rounded-md border font-titulo font-semibold uppercase tracking-wider transition-colors',
        solido
          ? 'border-transparent text-branco'
          : 'border-linha-forte bg-branco text-tinta-media',
        tamanhos[tamanho],
        className,
      )}
      style={solido ? { backgroundColor: corDeFundo! } : undefined}
      title={nome}
    >
      {iniciaisDe(nome)}
    </span>
  )
}
