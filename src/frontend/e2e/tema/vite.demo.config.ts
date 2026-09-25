import { defineConfig, type Plugin } from 'vite'
import type { Page, Route } from '@playwright/test'
import configuracao from '../../vite.config'
import { criarEstadoDeCaptura, instalarMockDeCapturas } from '../capturas/mock-de-capturas'

function demonstracaoLocal(): Plugin {
  return {
    name: 'demonstracao-local-do-tema',
    transformIndexHtml() {
      return [{
        tag: 'script',
        injectTo: 'head-prepend',
        children: `
          try {
            if (location.pathname !== '/login') localStorage.setItem('crm-cartorio.token', 'token-administrador');
            else localStorage.removeItem('crm-cartorio.token');
            if (!localStorage.getItem('crm-cartorio.tema')) localStorage.setItem('crm-cartorio.tema', 'escuro');
          } catch {}
          document.addEventListener('DOMContentLoaded', () => { document.title = 'DEMONSTRACAO LOCAL | CRM Cartorio'; });
        `,
      }]
    },
    async configureServer(servidor) {
      const estado = criarEstadoDeCaptura()
      estado.atendentes = estado.atendentes.map((atendente) => ({
        ...atendente,
        permissoes: [...atendente.permissoes, 'seguranca.acessos.ver', 'seguranca.acesso_tecnico.aprovar'],
      }))
      let atender: ((route: Route) => Promise<unknown>) | undefined
      // Adaptador apenas da interface de rotas usada pelo mock existente.
      await instalarMockDeCapturas({
        route: async (padrao: string, callback: (route: Route) => Promise<unknown>) => {
          if (padrao === '**/api/**') atender = callback
        },
      } as unknown as Page, estado)

      servidor.middlewares.use(async (req, res, next) => {
        const url = new URL(req.url ?? '/', 'http://127.0.0.1:5197')
        if (!url.pathname.startsWith('/api/') && !url.pathname.startsWith('/hubs/')) return next()
        res.setHeader('Cache-Control', 'no-store')
        res.setHeader('X-CRM-Demonstracao', 'dados-ficticios')
        if (url.pathname.startsWith('/hubs/')) {
          res.writeHead(503).end()
          return
        }
        if (url.pathname === '/api/autenticacao/login') {
          res.writeHead(400, { 'Content-Type': 'application/problem+json' })
          res.end(JSON.stringify({ title: 'Demonstracao local: abra a pagina inicial (/) para entrar sem senha.', status: 400 }))
          return
        }
        if (url.pathname.startsWith('/api/seguranca/')) {
          if (url.pathname.endsWith('/ativa')) { res.writeHead(204).end(); return }
          res.writeHead(200, { 'Content-Type': 'application/json' })
          res.end(JSON.stringify(url.pathname.endsWith('/protecao')
            ? { cifraAtiva: true, cifraDesde: '2026-09-01T12:00:00Z', versaoDaChave: 'demo', totalDeRegistrosDeAcesso: 0 }
            : { itens: [], total: 0, pagina: 1, tamanho: 20 }))
          return
        }
        try {
          const partes: Buffer[] = []
          let tamanho = 0
          for await (const parte of req) {
            tamanho += parte.length
            if (tamanho > 2 * 1024 * 1024) { res.writeHead(413).end(); return }
            partes.push(Buffer.from(parte))
          }
          const corpo = Buffer.concat(partes).toString('utf8')
          await atender!({
            request: () => ({
              url: () => url.href,
              method: () => req.method ?? 'GET',
              postDataJSON: () => corpo ? JSON.parse(corpo) : {},
            }),
            fulfill: async (opcoes: Parameters<Route['fulfill']>[0] = {}) => {
              res.statusCode = opcoes.status ?? 200
              if (opcoes.contentType) res.setHeader('Content-Type', opcoes.contentType)
              res.end(opcoes.body)
            },
          } as unknown as Route)
        } catch {
          if (!res.headersSent) res.writeHead(500, { 'Content-Type': 'application/json' })
          res.end(JSON.stringify({ title: 'Operacao nao disponivel nesta demonstracao.', status: 500 }))
        }
      })
    },
  }
}

export default defineConfig(({ command }) => {
  if (command !== 'serve') throw new Error('Esta configuracao serve apenas para demonstracao local, nunca para build.')
  return {
    ...configuracao,
    plugins: [...(configuracao.plugins ?? []), demonstracaoLocal()],
    // Substitui inteiramente o servidor normal: nao existe proxy para o backend.
    server: { host: '127.0.0.1', port: 5197, strictPort: true, proxy: {} },
  }
})
