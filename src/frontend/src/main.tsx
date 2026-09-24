import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { App } from './app/App'
import './index.css'
import { iniciarTema } from './lib/tema'

const pararTema = iniciarTema()
if (import.meta.hot) import.meta.hot.dispose(pararTema)

const raiz = document.getElementById('root')

if (!raiz) {
  throw new Error('Elemento #root nao encontrado no index.html.')
}

createRoot(raiz).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
