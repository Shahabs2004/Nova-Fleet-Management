import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SettingsPage } from './SettingsPage'
import type { SettingsBundle } from '../lib/types'

vi.mock('../auth/useAuth', () => ({
  useAuth: () => ({ can: () => true, user: { username: 'settings.user', displayName: 'آزمون' } }),
}))

const settings: SettingsBundle = {
  organization: { name: 'سازمان آزمون', logoDataUrl: null },
  missionRules: {
    minimumGapMinutes: 30,
    futureRequestDays: 60,
    withdrawalAutoCloseHours: 24,
    startReminderMinutes: 15,
    backdateRequestDays: 7,
  },
  sessionPolicy: { absoluteMinutes: 720, idleMinutes: 30 },
  missionOrderTemplate: { headerText: 'سربرگ آزمون', footerText: 'پانویس آزمون', showLogo: true },
}

function response(body: unknown): Response {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

function setup() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  const ui = <QueryClientProvider client={queryClient}><SettingsPage /></QueryClientProvider>
  const view = render(ui)
  return { ...view, queryClient, rerender: () => view.rerender(ui) }
}

describe('settings form hydration', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn(async (input) => {
      const path = String(input)
      if (path === '/api/settings') return response(settings)
      return response([])
    }))
  })

  afterEach(() => {
    cleanup()
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('preserves an organization edit across an unrelated parent render', async () => {
    const view = setup()
    const name = await screen.findByDisplayValue('سازمان آزمون')
    fireEvent.change(name, { target: { value: 'پیش‌نویس سازمان' } })

    view.rerender()

    expect(name).toHaveValue('پیش‌نویس سازمان')
  })

  it('hydrates a genuinely new settings query result', async () => {
    const { queryClient } = setup()
    const name = await screen.findByDisplayValue('سازمان آزمون')
    fireEvent.change(name, { target: { value: 'پیش‌نویس سازمان' } })

    act(() => queryClient.setQueryData(['settings', 'settings.user'], {
      ...settings,
      organization: { name: 'سازمان تازه', logoDataUrl: null },
    }))

    await waitFor(() => expect(name).toHaveValue('سازمان تازه'))
  })

  it('hydrates rules, session policy, and mission template from the same query result', async () => {
    setup()
    await screen.findByDisplayValue('سازمان آزمون')

    fireEvent.click(screen.getByRole('button', { name: 'قواعد مأموریت' }))
    expect(await screen.findAllByDisplayValue('30')).toHaveLength(2)
    expect(screen.getByDisplayValue('720')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'قالب حکم' }))
    expect(await screen.findByDisplayValue('سربرگ آزمون')).toBeInTheDocument()
    expect(screen.getByDisplayValue('پانویس آزمون')).toBeInTheDocument()
  })

  it('rejects a non-PNG logo without replacing the current logo', async () => {
    const { container } = setup()
    await screen.findByDisplayValue('سازمان آزمون')
    const input = container.querySelector<HTMLInputElement>('input[type="file"]')!

    expect(input).toHaveAttribute('accept', 'image/png')
    fireEvent.change(input, { target: { files: [new File(['image'], 'logo.jpg', { type: 'image/jpeg' })] } })

    expect(await screen.findByText('لوگو باید یک تصویر PNG باشد.')).toBeInTheDocument()
    expect(screen.getByText('بدون لوگو')).toBeInTheDocument()
    expect(container.querySelector('img[alt="لوگو"]')).not.toBeInTheDocument()
  })
})
