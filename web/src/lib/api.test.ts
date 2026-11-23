import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { api, downloadFile } from './api'

function response(status: number, body?: unknown): Response {
  return new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

describe('API unauthorized events', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('dispatches nf-unauthorized for a JSON HTTP 401', async () => {
    const handler = vi.fn()
    window.addEventListener('nf-unauthorized', handler)
    vi.mocked(fetch).mockResolvedValue(response(401, { message: 'expired' }))

    await expect(api.get('/api/private')).rejects.toMatchObject({ status: 401 })
    expect(handler).toHaveBeenCalledOnce()
    window.removeEventListener('nf-unauthorized', handler)
  })

  it('dispatches nf-unauthorized for a file-download HTTP 401', async () => {
    const handler = vi.fn()
    window.addEventListener('nf-unauthorized', handler)
    vi.mocked(fetch).mockResolvedValue(response(401, { message: 'expired' }))

    await expect(downloadFile('/api/export', {}, 'report.pdf')).rejects.toMatchObject({ status: 401 })
    expect(handler).toHaveBeenCalledOnce()
    window.removeEventListener('nf-unauthorized', handler)
  })

  it.each([
    ['network failure', () => Promise.reject(new TypeError('offline'))],
    ['HTTP 500', () => Promise.resolve(response(500, { message: 'server error' }))],
  ])('does not dispatch unauthorized for %s', async (_label, failure) => {
    const handler = vi.fn()
    window.addEventListener('nf-unauthorized', handler)
    vi.mocked(fetch).mockImplementationOnce(failure)

    await expect(api.get('/api/private')).rejects.toThrow()
    expect(handler).not.toHaveBeenCalled()
    window.removeEventListener('nf-unauthorized', handler)
  })
})
