import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SetupPage } from './SetupPage'

function response(body: unknown): Response {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

function field(label: string): HTMLElement {
  return screen.getByText(label, { selector: 'label' }).nextElementSibling as HTMLElement
}

describe('setup settings', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    cleanup()
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('does not show or submit nonfunctional operations settings', async () => {
    vi.mocked(fetch).mockImplementation(async (input, init) => {
      const path = String(input)
      if (path === '/api/setup/status') return response({ isCompleted: false, hasLocalAdmin: false, userCount: 0, organizationName: null })
      if (path === '/api/setup/database') {
        return response(init?.method === 'POST'
          ? { restartRequired: false, createdDatabase: false, sameAsActive: true, hint: 'آماده شد.' }
          : { isConfigured: false, source: 'None', serverName: null, databaseName: null, useWindowsAuthentication: true, userName: null, hasPassword: false })
      }
      if (path === '/api/setup/initialize') return response({ isCompleted: true, hasLocalAdmin: true, userCount: 1, organizationName: 'سازمان آزمون' })
      throw new Error(`Unexpected request: ${path}`)
    })
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
      <QueryClientProvider client={client}>
        <MemoryRouter>
          <SetupPage />
        </MemoryRouter>
      </QueryClientProvider>,
    )

    await screen.findByText('نام سرور', { selector: 'label' })
    fireEvent.change(field('نام سرور'), { target: { value: 'localhost' } })
    fireEvent.click(screen.getByRole('button', { name: 'اتصال و آماده‌سازی پایگاه داده' }))
    await screen.findByText('ورود با حساب دامنه (Active Directory)')
    fireEvent.click(screen.getByRole('button', { name: 'گام بعد' }))
    await screen.findByText('کل سامانه پیامک روشن باشد')
    fireEvent.click(screen.getByRole('button', { name: 'گام بعد' }))
    await screen.findByText('نام سازمان', { selector: 'label' })
    fireEvent.click(screen.getByRole('button', { name: 'گام بعد' }))
    await screen.findByRole('heading', { name: 'قواعد مأموریت' })

    expect(screen.queryByText('سطح لاگ')).not.toBeInTheDocument()
    expect(screen.queryByText('اندازه کاغذ گزارش')).not.toBeInTheDocument()
    expect(screen.queryByText('مسیر فونت فارسی PDF')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'گام بعد' }))
    await screen.findByRole('heading', { name: 'پایگاه داده' })
    expect(screen.queryByText('کاغذ گزارش / سطح لاگ')).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'ایجاد و شروع' }))

    await waitFor(() => {
      const call = vi.mocked(fetch).mock.calls.find(([input, init]) =>
        String(input) === '/api/setup/initialize' && init?.method === 'POST')
      expect(call).toBeDefined()
      expect(JSON.parse(String(call?.[1]?.body))).not.toHaveProperty('operations')
    })
  })
})
