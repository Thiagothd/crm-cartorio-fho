import { screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { renderizarComProvedores } from '@/test/utils'
import { usarAlertasOperacionais } from '@/features/indicadores/consultas'
import { AvisoDeWhatsappDesconectado } from './aviso-de-whatsapp-desconectado'

vi.mock('@/features/indicadores/consultas', () => ({
  usarAlertasOperacionais: vi.fn(),
}))

function comAlertas(alertas: unknown[]) {
  vi.mocked(usarAlertasOperacionais).mockReturnValue({
    data: alertas,
  } as unknown as ReturnType<typeof usarAlertasOperacionais>)
}

describe('AvisoDeWhatsappDesconectado', () => {
  it('aparece, apontando para a tela do canal, quando o WhatsApp está desconectado', () => {
    comAlertas([
      {
        id: 'whatsapp:desconectado',
        tipo: 'whatsapp_desconectado',
        severidade: 'Critica',
        titulo: 'WhatsApp desconectado',
        descricao: 'Mensagens não entram nem saem até reconectar.',
        quando: '2026-10-30T12:00:00Z',
        rota: '/integracoes/whatsapp',
      },
    ])

    renderizarComProvedores(<AvisoDeWhatsappDesconectado habilitado />)

    const aviso = screen.getByRole('link', { name: /WhatsApp desconectado/ })
    expect(aviso).toHaveAttribute('href', '/integracoes/whatsapp')
  })

  it('não aparece com o WhatsApp conectado nem quando só há outros alertas', () => {
    comAlertas([
      {
        id: 'fila:1',
        tipo: 'fila_sem_responsavel',
        severidade: 'Neutra',
        titulo: '2026-00001 está na fila sem responsável',
        descricao: null,
        quando: '2026-10-30T12:00:00Z',
        rota: '/atendimentos/1',
      },
    ])

    renderizarComProvedores(<AvisoDeWhatsappDesconectado habilitado />)

    expect(screen.queryByRole('link')).not.toBeInTheDocument()
  })
})
