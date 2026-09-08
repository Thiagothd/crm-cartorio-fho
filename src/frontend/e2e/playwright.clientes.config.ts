import { defineConfig } from '@playwright/test'
import base from './playwright.config'

// Porta reservada para este worktree. Durante a revisão visual, pode aproveitar
// apenas o Vite já aberto nesta mesma porta.
export default defineConfig({
  ...base,
  workers: 2,
  use: { ...base.use, baseURL: 'http://127.0.0.1:5197' },
  webServer: {
    command: 'npm run dev -- --host 127.0.0.1 --port 5197 --strictPort --configLoader runner',
    cwd: '..',
    url: 'http://127.0.0.1:5197/login',
    reuseExistingServer: true,
    timeout: 60_000,
    stdout: 'pipe',
    stderr: 'pipe',
  },
})
