import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import { fileURLToPath, URL } from 'node:url'

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: './src/test/setup.ts',
    restoreMocks: true,
    // O runner em threads ficou retido neste workspace Windows ao coletar os
    // módulos Radix. Processo isolado é determinístico e a suíte ainda é curta.
    pool: 'forks',
    maxWorkers: 1,
  },
})
