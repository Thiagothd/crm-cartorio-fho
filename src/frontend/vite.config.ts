import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { fileURLToPath, URL } from 'node:url'

const backend = process.env.VITE_API_URL ?? 'http://localhost:5210'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 5173,
    // Proxy no desenvolvimento: o app fala com caminhos relativos e nao precisa
    // saber o endereco da API. O hub do SignalR precisa de ws: para o WebSocket.
    proxy: {
      '/api': { target: backend, changeOrigin: true },
      '/hubs': { target: backend, changeOrigin: true, ws: true },
    },
  },
})
