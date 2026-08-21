import { render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { Avatar } from './avatar'

const blobDaApi = vi.fn()

vi.mock('@/lib/api/cliente-http', () => ({
  api: {
    blob: (caminho: string) => blobDaApi(caminho),
  },
}))

describe('Avatar', () => {
  beforeEach(() => {
    blobDaApi.mockReset().mockResolvedValue(new Blob(['x'], { type: 'image/jpeg' }))
    // `createObjectURL` não existe no jsdom.
    globalThis.URL.createObjectURL = vi.fn(() => 'blob:fake')
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('mostra as iniciais quando não há foto', () => {
    render(<Avatar nome="Maria Aparecida Souza" />)

    expect(screen.getByTitle('Maria Aparecida Souza')).toBeInTheDocument()
    expect(blobDaApi).not.toHaveBeenCalled()
  })

  /**
   * Regressão de SEGURANÇA, não de aparência.
   *
   * `FotoPerfilUrl` vem do banco e pode ser uma URL absoluta de terceiro (o
   * seed de demonstração usa Unsplash). Passar isso pelo cliente HTTP da API
   * anexava `Authorization: Bearer <JWT>` à requisição — e como `baseDaApi` é
   * string vazia por padrão, o fetch ia direto ao host externo com o token do
   * atendente. Além de vazar credencial, quebrava a foto: o preflight CORS
   * falha porque o host de terceiro não libera o header, e o avatar caía nas
   * iniciais sem dizer por quê.
   */
  it('não manda URL externa para o cliente HTTP da API (o token não pode vazar)', async () => {
    render(
      <Avatar
        nome="Thiago Antunes"
        fotoUrl="https://images.unsplash.com/photo-1534528741775-53994a69daeb?w=100"
      />,
    )

    await waitFor(() => {
      expect(screen.getByRole('img', { name: 'Thiago Antunes' })).toHaveAttribute(
        'src',
        'https://images.unsplash.com/photo-1534528741775-53994a69daeb?w=100',
      )
    })

    expect(blobDaApi).not.toHaveBeenCalled()
  })

  it('busca pela API quando o caminho é relativo — ali o token é obrigatório', async () => {
    render(<Avatar nome="Divani" fotoUrl="/api/clientes/abc-123/foto-perfil" />)

    await waitFor(() => {
      expect(blobDaApi).toHaveBeenCalledWith('/api/clientes/abc-123/foto-perfil')
    })
  })
})
