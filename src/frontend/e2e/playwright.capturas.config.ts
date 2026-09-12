import { defineConfig, devices } from '@playwright/test'

/**
 * Config PRÓPRIA para gerar os screenshots do roteiro público (`roteiro/`).
 * `testMatch` nunca casa com `**\/*.e2e.ts`, então `npm run e2e` e o check de
 * CI `e2e-playwright` nunca executam isto — ver docs/PLAYWRIGHT-E2E.md.
 */
export default defineConfig({
  testDir: './capturas/especificacoes',
  testMatch: '**/*.capturas.ts',
  fullyParallel: false,
  retries: 0,
  workers: 1,
  reporter: [['list']],
  outputDir: 'capturas/test-results',
  use: {
    baseURL: 'http://127.0.0.1:5173',
    actionTimeout: 8_000,
    navigationTimeout: 15_000,
  },
  projects: [
    { name: 'desktop', testIgnore: '**/mobile.capturas.ts', use: { ...devices['Desktop Chrome'], viewport: { width: 1366, height: 768 } } },
    { name: 'mobile', testMatch: '**/mobile.capturas.ts', use: { ...devices['Desktop Chrome'], viewport: { width: 390, height: 844 }, hasTouch: true, isMobile: true } },
  ],
  webServer: {
    command: 'npm run dev -- --host 127.0.0.1 --configLoader runner',
    cwd: '..',
    url: 'http://127.0.0.1:5173/login',
    reuseExistingServer: !process.env.CI,
    timeout: 60_000,
    stdout: 'pipe',
    stderr: 'pipe',
  },
})
