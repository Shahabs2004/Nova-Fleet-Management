import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ConnectionsSettingsTab } from './ConnectionsSettings'

vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({ user: { username: 'settings.user' } }),
}))

const routes: Record<string, unknown> = {
  '/api/settings/connections/database': {
    connectionString: 'Server=example;Database=fleet;User Id=svc;Password=stored',
    isConfigured: true,
    source: 'custom',
  },
  '/api/settings/connections/directory': {
    enabled: false,
    host: 'directory.example',
    port: 636,
    useSsl: true,
    domain: 'example',
    baseDn: 'DC=example,DC=local',
    bindDn: 'CN=svc,DC=example,DC=local',
    timeoutSeconds: 15,
    searchFilter: '(&(objectClass=user)(sAMAccountName={0}))',
    hasBindPassword: true,
  },
  '/api/settings/connections/sms': {
    enabled: false,
    mode: 'Simulation',
    endpoint: null,
    organization: null,
    username: null,
    senderNumber: null,
    timeoutSeconds: 20,
    retryMinutes: 5,
    maxAttempts: 5,
    hasPassword: false,
  },
  '/api/settings/connections/operations': {
    logLevel: 'Warning',
    pdfFontPath: 'C:\\Fonts\\persian.ttf',
    reportPaperSize: 'A4',
  },
}

const cases = [
  { title: 'پایگاه داده', save: 'ذخیره رشته اتصال', tests: ['تست اتصال'] },
  { title: 'دامنه (Active Directory)', save: 'ذخیره تنظیمات دامنه', tests: ['تست اتصال'] },
  { title: 'سرویس پیامک', save: 'ذخیره تنظیمات پیامک', tests: ['تست اعتبار درگاه'] },
  { title: 'عملیات', save: 'ذخیره تنظیمات عملیات', tests: [] },
] as const

function response(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

function setup() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={queryClient}>
      <ConnectionsSettingsTab />
    </QueryClientProvider>,
  )
}

function card(title: string): HTMLElement {
  const section = screen.getByRole('heading', { name: title }).closest('section')
  if (!section) throw new Error(`Missing settings card: ${title}`)
  return section
}

describe('connection settings failed reads', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    cleanup()
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it.each(cases)('blocks $title after GET failure and enables it after Retry succeeds', async ({ title, save, tests }) => {
    let failed = true
    vi.mocked(fetch).mockImplementation(async (input, init) => {
      const path = String(input)
      if (init?.method) throw new Error(`Unexpected mutation before settings loaded: ${path}`)
      if (path in routes && !(failed && path === `/api/settings/connections/${
        title === 'پایگاه داده' ? 'database'
          : title === 'دامنه (Active Directory)' ? 'directory'
            : title === 'سرویس پیامک' ? 'sms' : 'operations'
      }`)) {
        return response(200, routes[path])
      }
      return response(500, { message: 'خطای موقت سرور' })
    })

    setup()
    await screen.findByRole('heading', { name: title })
    const section = card(title)
    await waitFor(() => expect(within(section).getByText(/خطا در دریافت تنظیمات/)).toBeInTheDocument())
    expect(within(section).getByRole('button', { name: 'تلاش مجدد' })).toBeEnabled()
    expect(within(section).getByRole('button', { name: save })).toBeDisabled()
    for (const test of tests) expect(within(section).getByRole('button', { name: test })).toBeDisabled()

    fireEvent.click(within(section).getByRole('button', { name: save }))
    for (const test of tests) fireEvent.click(within(section).getByRole('button', { name: test }))
    expect(vi.mocked(fetch).mock.calls.every(([, init]) => !init?.method)).toBe(true)

    failed = false
    fireEvent.click(within(section).getByRole('button', { name: 'تلاش مجدد' }))

    await waitFor(() => {
      const current = card(title)
      expect(within(current).queryByRole('button', { name: 'تلاش مجدد' })).not.toBeInTheDocument()
      expect(within(current).getByRole('button', { name: save })).toBeEnabled()
    })
    const recovered = card(title)
    for (const test of tests) expect(within(recovered).getByRole('button', { name: test })).toBeEnabled()
  })
})
