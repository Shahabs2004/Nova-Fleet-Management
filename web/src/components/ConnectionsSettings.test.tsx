import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ConnectionsSettingsTab } from './ConnectionsSettings'

vi.mock('../auth/useAuth', () => ({
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
}

const cases = [
  { title: 'پایگاه داده', save: 'ذخیره رشته اتصال', tests: ['تست اتصال'] },
  { title: 'دامنه (Active Directory)', save: 'ذخیره تنظیمات دامنه', tests: ['تست اتصال'] },
  { title: 'سرویس پیامک', save: 'ذخیره تنظیمات پیامک', tests: ['تست اعتبار درگاه'] },
] as const

function response(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

function setup() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const ui = (
    <QueryClientProvider client={queryClient}>
      <ConnectionsSettingsTab />
    </QueryClientProvider>
  )
  const view = render(ui)
  return { ...view, queryClient, rerender: () => view.rerender(ui) }
}

function card(title: string): HTMLElement {
  const section = screen.getByRole('heading', { name: title }).closest('section')
  if (!section) throw new Error(`Missing settings card: ${title}`)
  return section
}

describe('connection settings surface', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    cleanup()
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('does not expose or request the removed operations settings', async () => {
    vi.mocked(fetch).mockImplementation(async (input) => {
      const path = String(input)
      if (path in routes) return response(200, routes[path])
      return response(500, { message: 'unexpected request' })
    })

    setup()
    await screen.findByRole('heading', { name: 'پایگاه داده' })
    await waitFor(() => expect(vi.mocked(fetch).mock.calls.map(([input]) => String(input))).toEqual(
      expect.arrayContaining([
        '/api/settings/connections/database',
        '/api/settings/connections/directory',
        '/api/settings/connections/sms',
      ]),
    ))

    expect(screen.queryByRole('heading', { name: 'عملیات' })).not.toBeInTheDocument()
    expect(screen.queryByText('سطح لاگ')).not.toBeInTheDocument()
    expect(screen.queryByText('مسیر فونت فارسی PDF')).not.toBeInTheDocument()
    expect(vi.mocked(fetch).mock.calls.map(([input]) => String(input))).not.toContain(
      '/api/settings/connections/operations',
    )
  })
})

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

describe('connection settings hydration', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    cleanup()
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('hydrates an empty database connection string after a successful refetch', async () => {
    const databasePath = '/api/settings/connections/database'
    let database = routes[databasePath] as { connectionString: string; isConfigured: boolean; source: string }
    vi.mocked(fetch).mockImplementation(async (input, init) => {
      const path = String(input)
      if (path === databasePath) {
        if (init?.method === 'PUT') {
          database = { ...database, connectionString: '' }
          return response(200, { restartRequired: true, hint: null })
        }
        return response(200, database)
      }
      if (path in routes) return response(200, routes[path])
      throw new Error(`Unexpected request: ${path}`)
    })

    setup()
    await screen.findByRole('heading', { name: 'پایگاه داده' })
    const section = card('پایگاه داده')
    const textarea = section.querySelector('textarea')
    if (!textarea) throw new Error('Missing database connection textarea')
    await waitFor(() => expect(textarea).toHaveValue('Server=example;Database=fleet;User Id=svc;Password=stored'))

    fireEvent.click(within(section).getByRole('button', { name: 'ذخیره رشته اتصال' }))

    await waitFor(() => expect(textarea).toHaveValue(''))
  })

  it('preserves an edited draft across an unrelated parent render', async () => {
    vi.mocked(fetch).mockImplementation(async (input) => {
      const path = String(input)
      if (path in routes) return response(200, routes[path])
      throw new Error(`Unexpected request: ${path}`)
    })

    const view = setup()
    await screen.findByRole('heading', { name: 'پایگاه داده' })
    const textarea = card('پایگاه داده').querySelector('textarea')
    if (!textarea) throw new Error('Missing database connection textarea')
    await waitFor(() => expect(textarea).toHaveValue('Server=example;Database=fleet;User Id=svc;Password=stored'))
    fireEvent.change(textarea, { target: { value: 'unfinished draft' } })

    view.rerender()

    expect(textarea).toHaveValue('unfinished draft')
  })
})

describe('SMS password intent', () => {
  const smsPath = '/api/settings/connections/sms'

  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    cleanup()
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  async function renderSmsCard(): Promise<HTMLElement> {
    vi.mocked(fetch).mockImplementation(async (input, init) => {
      const path = String(input)
      if (path === smsPath) {
        if (init?.method === 'PUT') return response(200, { restartRequired: false, hint: null })
        return response(200, {
          enabled: false,
          mode: 'Wsdl',
          endpoint: 'https://sms.example/soap',
          organization: 'organization',
          username: 'svc.user',
          senderNumber: '1000999',
          timeoutSeconds: 20,
          retryMinutes: 5,
          maxAttempts: 5,
          hasPassword: true,
        })
      }
      if (path in routes) return response(200, routes[path])
      throw new Error(`Unexpected request: ${path}`)
    })

    setup()
    await screen.findByRole('heading', { name: 'سرویس پیامک' })
    const section = card('سرویس پیامک')
    await waitFor(() => expect(passwordInput(section)).toBeInTheDocument())
    return section
  }

  function passwordInput(section: HTMLElement): HTMLInputElement {
    const input = section.querySelector<HTMLInputElement>('input[type="password"]')
    if (!input) throw new Error('Missing SMS password input')
    return input
  }

  function savedPassword(): string | null | undefined {
    const call = vi.mocked(fetch).mock.calls.find(([input, init]) =>
      String(input) === smsPath && init?.method === 'PUT')
    if (!call) throw new Error('SMS settings were not saved')
    return (JSON.parse(String(call[1]?.body)) as { password?: string | null }).password
  }

  it.each(['', '   '])('preserves the stored password for untouched or whitespace input (%j)', async (input) => {
    const section = await renderSmsCard()
    fireEvent.change(passwordInput(section), { target: { value: input } })

    fireEvent.click(within(section).getByRole('button', { name: 'ذخیره تنظیمات پیامک' }))

    await waitFor(() => expect(savedPassword()).toBeNull())
  })

  it('sends a replacement password without clear intent', async () => {
    const section = await renderSmsCard()
    fireEvent.change(passwordInput(section), { target: { value: 'replacement-secret' } })

    fireEvent.click(within(section).getByRole('button', { name: 'ذخیره تنظیمات پیامک' }))

    await waitFor(() => expect(savedPassword()).toBe('replacement-secret'))
  })

  it('sends an empty password only after explicit clear and confirmation', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true)
    const section = await renderSmsCard()
    const clear = within(section).getByRole('checkbox', { name: /پاک کردن رمز ذخیره‌شده/ })
    fireEvent.click(clear)

    fireEvent.click(within(section).getByRole('button', { name: 'ذخیره تنظیمات پیامک' }))

    expect(confirm).toHaveBeenCalledTimes(1)
    await waitFor(() => expect(savedPassword()).toBe(''))
  })

  it('does not save or clear when the destructive confirmation is canceled', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false)
    const section = await renderSmsCard()
    const clear = within(section).getByRole('checkbox', { name: /پاک کردن رمز ذخیره‌شده/ })
    fireEvent.click(clear)

    fireEvent.click(within(section).getByRole('button', { name: 'ذخیره تنظیمات پیامک' }))

    expect(confirm).toHaveBeenCalledTimes(1)
    expect(clear).toBeChecked()
    expect(vi.mocked(fetch).mock.calls.every(([, init]) => init?.method !== 'PUT')).toBe(true)
  })
})
