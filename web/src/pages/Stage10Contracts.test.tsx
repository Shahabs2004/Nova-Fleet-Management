import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ReportsPage } from './ReportsPage'
import { MissionDetailsPage } from './MissionDetailsPage'
import { SmsPage } from './SmsPage'
import { SettingsPage } from './SettingsPage'
import { jalaliOf } from '../lib/date'
import { jalaliValueToUtc } from '../components/JalaliDateField'

vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({ can: () => true, user: { username: 'stage10.user', displayName: 'آزمون' } }),
}))

function response(body: unknown): Response {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

function setup(children: React.ReactNode, initialEntries = ['/']) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  render(<QueryClientProvider client={client}><MemoryRouter initialEntries={initialEntries}>{children}</MemoryRouter></QueryClientProvider>)
}

const report = {
  key: 'missions', title: 'گزارش مأموریت‌ها', columns: [{ key: 'Title', title: 'عنوان' }],
  rows: [['آزمون']], totals: { 'تعداد': 1005 }, generatedAtUtc: new Date().toISOString(),
  totalCount: 1005, rowsTruncated: true, maxRows: 1000,
}

const mission = {
  id: 'mission-1', missionNumber: 'T00001', title: 'مأموریت آزمون', status: 4, type: 1,
  requesterUsername: 'stage10.user', requesterName: 'آزمون', driverId: null, driverName: null,
  vehicleId: null, vehiclePlate: null, origin: 'آ', destination: 'ب', plannedStartAtUtc: '2026-11-15T06:30:00Z',
  plannedEndAtUtc: '2026-11-15T07:30:00Z', createdAtUtc: '2026-11-14T06:30:00Z', updatedAtUtc: '2026-11-14T06:30:00Z',
  rowVersion: 'AAAA', changeRequests: [], intermediateStops: [], requesterPhoneSnapshot: '09120000000',
  purpose: null, actualStartAtUtc: null, driverMobile: null, vehicleDisplayName: null, description: null,
  passengerCount: null, missionOrderNumber: null, missionOrderDate: null, cargoDescription: null,
  consigneeName: null, passengerNames: null, odometerStartKm: null, odometerEndKm: null, distanceKm: null,
  decisionReason: null, statusBeforeWithdrawal: null, relatedMissionNumber: null, splitAtUtc: null,
  splitBy: null, approvedBy: null, approvedAtUtc: null, reviewStartedAtUtc: null,
}

describe('Stage 10 date and search contracts', () => {
  beforeEach(() => { vi.stubGlobal('fetch', vi.fn()) })
  afterEach(() => {
    cleanup()
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('sends the next Tehran midnight as the exclusive report upper bound and shows truncation', async () => {
    vi.mocked(fetch).mockImplementation(async (input) => {
      const path = String(input)
      if (path === '/api/reports') return response([{ key: 'missions', title: report.title, description: 'آزمون' }])
      if (path.startsWith('/api/drivers/options')) return response([])
      if (path.startsWith('/api/vehicles/options')) return response([])
      if (path === '/api/organization-units') return response([])
      if (path.endsWith('/run')) return response(report)
      return response([])
    })
    setup(<ReportsPage />)
    fireEvent.click(await screen.findByRole('button', { name: /گزارش مأموریت‌ها/ }))
    await waitFor(() => expect(fetch).toHaveBeenCalledWith('/api/reports/missions/run', expect.anything()))
    const init = vi.mocked(fetch).mock.calls.find(([path]) => String(path).endsWith('/run'))?.[1]
    const body = JSON.parse(String(init?.body)) as { toUtc: string }
    const [jy, jm, jd] = jalaliOf(new Date())
    const expected = Date.parse(jalaliValueToUtc({ jy, jm, jd }, 0)!) + 86_400_000
    expect(Date.parse(body.toUtc)).toBe(expected)
    expect(await screen.findByText(/نمایش ۱۰۰۰ ردیف اول از مجموع ۱۰۰۵/)).toBeInTheDocument()
  })

  it('submits mission extension as the selected Tehran Jalali hour', async () => {
    vi.mocked(fetch).mockImplementation(async (input) => {
      if (String(input) === '/api/missions/mission-1') return response(mission)
      if (String(input).endsWith('/extend')) return response({})
      return response([])
    })
    setup(<Routes><Route path="/missions/:id" element={<MissionDetailsPage />} /></Routes>, ['/missions/mission-1'])
    fireEvent.click(await screen.findByRole('button', { name: 'تمدید' }))
    fireEvent.change(screen.getByLabelText('سال شمسی'), { target: { value: '1405' } })
    fireEvent.change(screen.getByLabelText('ماه شمسی'), { target: { value: '8' } })
    fireEvent.change(screen.getByLabelText('روز شمسی'), { target: { value: '24' } })
    fireEvent.change(screen.getByLabelText('ساعت پایان'), { target: { value: '23' } })
    fireEvent.change(screen.getByLabelText('علت تمدید'), { target: { value: 'تأخیر آزمون' } })
    fireEvent.click(screen.getByRole('button', { name: 'ثبت تمدید' }))
    await waitFor(() => expect(fetch).toHaveBeenCalledWith('/api/missions/mission-1/extend', expect.anything()))
    const init = vi.mocked(fetch).mock.calls.find(([path]) => String(path).endsWith('/extend'))?.[1]
    expect(JSON.parse(String(init?.body))).toMatchObject({ newPlannedEndAtUtc: '2026-11-15T19:30:00.000Z' })
  })

  it('sends SMS search with the backend search parameter', async () => {
    vi.mocked(fetch).mockImplementation(async (input) => {
      if (String(input).startsWith('/api/sms/summary')) return response({})
      if (String(input).startsWith('/api/sms?')) return response({ items: [], totalCount: 0, page: 1, pageSize: 20 })
      return response([])
    })
    setup(<SmsPage />)
    fireEvent.change(await screen.findByPlaceholderText('گیرنده، شماره یا متن پیامک'), { target: { value: 'شهاب' } })
    await waitFor(() => {
      const urls = vi.mocked(fetch).mock.calls.map(([input]) => String(input)).filter((path) => path.startsWith('/api/sms?'))
      expect(urls.at(-1)).toContain('search=%D8%B4%D9%87%D8%A7%D8%A8')
      expect(urls.at(-1)).not.toContain('q=')
    })
  })

  it('sends location search with the backend search parameter', async () => {
    vi.mocked(fetch).mockImplementation(async () => response([]))
    setup(<SettingsPage />)
    fireEvent.click(await screen.findByRole('button', { name: 'مکان‌ها' }))
    fireEvent.change(await screen.findByPlaceholderText('نام مکان'), { target: { value: 'انبار' } })
    await waitFor(() => {
      const urls = vi.mocked(fetch).mock.calls.map(([input]) => String(input)).filter((path) => path.startsWith('/api/locations?'))
      expect(urls.at(-1)).toContain('search=')
      expect(urls.at(-1)).not.toContain('q=')
    })
  })
})
