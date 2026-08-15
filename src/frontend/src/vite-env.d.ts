/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Base da API. Vazio em desenvolvimento: o proxy do Vite resolve. */
  readonly VITE_API_URL?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
