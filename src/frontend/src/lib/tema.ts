import { useSyncExternalStore } from 'react'

export type PreferenciaDeTema = 'claro' | 'escuro' | 'sistema'
export const CHAVE_DO_TEMA = 'crm-cartorio.tema'
const EVENTO_DO_TEMA = 'crm-cartorio:tema'
const CONSULTA_DO_TEMA = '(prefers-color-scheme: dark)'
let preferenciaEmMemoria: PreferenciaDeTema | undefined

function validarPreferencia(valor: string | null): PreferenciaDeTema {
  return valor === 'escuro' || valor === 'sistema' ? valor : 'claro'
}

function lerPreferencia(): PreferenciaDeTema {
  if (preferenciaEmMemoria) return preferenciaEmMemoria
  try {
    return validarPreferencia(localStorage.getItem(CHAVE_DO_TEMA))
  } catch {
    return 'claro'
  }
}

function obterSnapshot() {
  const preferencia = lerPreferencia()
  const escuro = preferencia === 'escuro' ||
    (preferencia === 'sistema' && window.matchMedia(CONSULTA_DO_TEMA).matches)
  return `${preferencia}:${escuro ? 'escuro' : 'claro'}` as const
}

function observarTema(aoMudar: () => void) {
  const media = window.matchMedia(CONSULTA_DO_TEMA)
  const aoSincronizar = (evento: StorageEvent) => {
    if (evento.key === CHAVE_DO_TEMA || evento.key === null) {
      preferenciaEmMemoria = undefined
      aoMudar()
    }
  }
  window.addEventListener(EVENTO_DO_TEMA, aoMudar)
  window.addEventListener('storage', aoSincronizar)
  media.addEventListener('change', aoMudar)
  return () => {
    window.removeEventListener(EVENTO_DO_TEMA, aoMudar)
    window.removeEventListener('storage', aoSincronizar)
    media.removeEventListener('change', aoMudar)
  }
}

export function definirTema(preferencia: PreferenciaDeTema) {
  preferenciaEmMemoria = preferencia
  try {
    localStorage.setItem(CHAVE_DO_TEMA, preferencia)
    preferenciaEmMemoria = undefined
  } catch {
    // A troca continua funcionando quando o navegador bloqueia a persistencia.
  }
  window.dispatchEvent(new Event(EVENTO_DO_TEMA))
}

export function iniciarTema() {
  const aplicar = () => {
    const escuro = obterSnapshot().endsWith(':escuro')
    document.documentElement.dataset.tema = escuro ? 'escuro' : 'claro'
    document.documentElement.style.colorScheme = escuro ? 'dark' : 'light'
    document.querySelector('meta[name="theme-color"]')?.setAttribute('content', escuro ? '#101113' : '#F7F6F3')
    document.querySelectorAll<HTMLLinkElement>('link[rel="icon"]').forEach((icone) => {
      const claro = icone.dataset.iconeClaro ?? icone.getAttribute('href') ?? ''
      icone.dataset.iconeClaro = claro
      icone.href = escuro ? '/brand/crm-cartorio-simbolo-escuro.webp' : claro
      icone.type = escuro ? 'image/webp' : 'image/png'
    })
  }
  aplicar()
  return observarTema(aplicar)
}

export function usarTema() {
  const snapshot = useSyncExternalStore(observarTema, obterSnapshot, () => 'claro:claro')
  const [preferencia, tema] = snapshot.split(':') as [PreferenciaDeTema, 'claro' | 'escuro']
  return { preferencia, tema, definirTema }
}
