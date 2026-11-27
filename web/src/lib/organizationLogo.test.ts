import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { readOrganizationLogo } from './organizationLogo'

function bitmap(width: number, height: number): ImageBitmap {
  return { width, height, close: vi.fn() } as unknown as ImageBitmap
}

describe('organization logo input', () => {
  beforeEach(() => {
    vi.stubGlobal('createImageBitmap', vi.fn())
  })

  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('rejects non-PNG files before decoding', async () => {
    const file = new File(['image'], 'logo.jpg', { type: 'image/jpeg' })

    await expect(readOrganizationLogo(file)).rejects.toThrow('PNG')
    expect(createImageBitmap).not.toHaveBeenCalled()
  })

  it('rejects files larger than the server decoded-size limit', async () => {
    const file = new File([new Uint8Array(256 * 1024 + 1)], 'logo.png', { type: 'image/png' })

    await expect(readOrganizationLogo(file)).rejects.toThrow('۲۵۶ کیلوبایت')
    expect(createImageBitmap).not.toHaveBeenCalled()
  })

  it('rejects non-square or oversized dimensions', async () => {
    vi.mocked(createImageBitmap).mockResolvedValueOnce(bitmap(128, 64))
    await expect(readOrganizationLogo(new File(['png'], 'wide.png', { type: 'image/png' })))
      .rejects.toThrow('مربع')

    vi.mocked(createImageBitmap).mockResolvedValueOnce(bitmap(257, 257))
    await expect(readOrganizationLogo(new File(['png'], 'large.png', { type: 'image/png' })))
      .rejects.toThrow('۲۵۶ × ۲۵۶')
  })

  it('returns a PNG data URL for a valid square image and closes the decoder', async () => {
    const source = bitmap(256, 256)
    vi.mocked(createImageBitmap).mockResolvedValueOnce(source)

    const result = await readOrganizationLogo(new File(['png'], 'logo.png', { type: 'image/png' }))

    expect(result).toMatch(/^data:image\/png;base64,/)
    expect(source.close).toHaveBeenCalledOnce()
  })

  it('reports unreadable images without exposing decoder details', async () => {
    vi.mocked(createImageBitmap).mockRejectedValueOnce(new Error('decoder internals'))

    await expect(readOrganizationLogo(new File(['bad'], 'logo.png', { type: 'image/png' })))
      .rejects.toThrow('تصویر PNG معتبر')
  })
})
