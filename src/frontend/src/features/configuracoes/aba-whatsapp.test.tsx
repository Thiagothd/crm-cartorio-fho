import { act, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AbaWhatsApp } from '@/features/configuracoes/aba-whatsapp'
import type { SituacaoDoCanal } from '@/lib/api/tipos'
import { renderizarComProvedores } from '@/test/utils'

const mocks = vi.hoisted(() => ({
  situacao: vi.fn(),
  parear: vi.fn(),
}))

vi.mock('@/lib/api/endpoints', () => ({
  whatsappApi: mocks,
}))

const fechada: SituacaoDoCanal = {
  habilitado: true,
  conectado: false,
  estado: 'close',
  qrCodeBase64: null,
  instancia: 'cartorio-teste',
}

describe('AbaWhatsApp', () => {
  beforeEach(() => {
    mocks.situacao.mockReset().mockResolvedValue(fechada)
    mocks.parear.mockReset()
  })

  it('consulta o estado sem iniciar o pareamento por conta propria', async () => {
    renderizarComProvedores(<AbaWhatsApp />)

    expect(await screen.findByRole('button', { name: 'Conectar número' })).toBeEnabled()
    expect(mocks.situacao).toHaveBeenCalledTimes(1)
    expect(mocks.parear).not.toHaveBeenCalled()
  })

  it('preserva o unico QR durante polls passivos e o remove ao conectar', async () => {
    const usuario = userEvent.setup()
    const comQr: SituacaoDoCanal = {
      ...fechada,
      estado: 'connecting',
      qrCodeBase64: 'data:image/png;base64,QR-UNICO',
    }
    mocks.parear.mockResolvedValue(comQr)

    const { clienteDeConsultas } = renderizarComProvedores(<AbaWhatsApp />)
    await usuario.click(await screen.findByRole('button', { name: 'Conectar número' }))

    expect(await screen.findByAltText('Código para conectar o WhatsApp')).toHaveAttribute(
      'src',
      comQr.qrCodeBase64,
    )
    expect(screen.getByRole('status')).toHaveTextContent('Aguardando leitura')
    expect(screen.queryByRole('button', { name: /conectar|gerar/i })).not.toBeInTheDocument()

    act(() => {
      clienteDeConsultas.setQueryData<SituacaoDoCanal>(['whatsapp-situacao'], {
        ...comQr,
        qrCodeBase64: null,
      })
    })

    expect(screen.getByAltText('Código para conectar o WhatsApp')).toHaveAttribute(
      'src',
      comQr.qrCodeBase64,
    )

    act(() => {
      clienteDeConsultas.setQueryData<SituacaoDoCanal>(['whatsapp-situacao'], {
        ...comQr,
        conectado: true,
        estado: 'open',
        qrCodeBase64: null,
      })
    })

    await waitFor(() => {
      expect(screen.getByText('Conectado')).toBeInTheDocument()
      expect(screen.queryByAltText('Código para conectar o WhatsApp')).not.toBeInTheDocument()
    })
    expect(mocks.parear).toHaveBeenCalledTimes(1)
  })

  it('aguarda uma sessao connecting sem oferecer outro QR', async () => {
    mocks.situacao.mockResolvedValue({
      ...fechada,
      estado: 'connecting',
    } satisfies SituacaoDoCanal)

    renderizarComProvedores(<AbaWhatsApp />)

    expect(await screen.findByRole('status')).toHaveTextContent('Conexão em andamento')
    expect(screen.queryByRole('button', { name: /conectar|gerar/i })).not.toBeInTheDocument()
  })
})
