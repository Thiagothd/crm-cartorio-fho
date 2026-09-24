import { defineConfig } from '@playwright/test'

// Servidor exclusivo, sem proxy para uma API existente. Toda API e simulada.
export default defineConfig({
  testDir: './tema',
  testMatch: '**/*.e2e.ts',
  fullyParallel: true,
  workers: 2,
  retries: 0,
  reporter: 'list',
  outputDir: 'test-results/tema',
  use: {
    baseURL: 'http://127.0.0.1:5187',
    reducedMotion: 'reduce',
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
  },
  projects: [
    { name: 'celular', use: { viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true } },
    { name: 'tablet', use: { viewport: { width: 768, height: 1024 } } },
    { name: 'desktop', use: { viewport: { width: 1440, height: 900 } } },
  ],
  webServer: {
    command: 'npm run dev -- --host 127.0.0.1 --port 5187 --strictPort --configLoader runner',
    cwd: '..',
    url: 'http://127.0.0.1:5187',
    env: { VITE_API_URL: 'http://127.0.0.1:1' },
    reuseExistingServer: false,
    timeout: 60_000,
  },
})
