import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { createElement } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SmsPage } from './SmsPage'
import type { SmsItem } from '../lib/types'

const auth = vi.hoisted(() => {
  const user = { username: 'sms.operator', permissions: [] as string[] }
  return { user, can: (permission: string) => user.permissions.includes(permission) }
})

vi.mock('../auth/useAuth', () => ({
  useAuth: () => ({ user: auth.user, can: auth.can }),
}))

const indeterminateId = '11111111-1111-4111-8111-111111111111'
const acceptedId = '22222222-2222-4222-8222-222222222222'
const reconcilePath = `/api/sms/${indeterminateId}/reconcile`
const confirmationLabel = /تصمیم را پس از بررسی شواهد تأیید می‌کنم/
const retryWarning = 'هشدار: ممکن است پیامک قبلاً توسط سرویس‌دهنده تحویل شده باشد. ارسال مجدد می‌تواند پیامک تکراری و هزینه اضافی ایجاد کند.'
const failedWarning = 'با بستن این پیامک به‌عنوان ناموفق، ارسال خودکار دیگری انجام نمی‌شود و تصمیم در ممیزی ثبت می‌شود.'

let items: SmsItem[]
let reconcileResult: () => Response
let queryClient: QueryClient | undefined

function response(status: number, body?: unknown): Response {
  return new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

function indeterminateItem(): SmsItem {
  return {
    id: indeterminateId,
    eventKey: 'mission-approved',
    eventType: 1,
    missionId: '33333333-3333-4333-8333-333333333333',
    missionNumber: 'M-1024',
    recipientName: 'گیرنده آزمایشی',
    recipientMobile: '09120000000',
    messageBody: 'پیامک با نتیجه نامشخص',
    status: 9,
    attemptCount: 1,
    maxAttempts: 3,
    providerReference: '90001234',
    lastError: 'پاسخ درگاه قبل از پایان مهلت خوانده نشد.',
    createdAtUtc: '2026-11-24T08:00:00Z',
    lastAttemptAtUtc: '2026-11-24T08:34:00Z',
    acceptedAtUtc: null,
    deliveredAtUtc: null,
    nextAttemptAtUtc: null,
    gatewayMode: 2,
    sendStartedAtUtc: '2026-11-24T08:34:00Z',
    nextDeliveryPollAtUtc: null,
    canRetry: false,
    canCancel: false,
    canReconcile: true,
  }
}

function acceptedItem(): SmsItem {
  return {
    ...indeterminateItem(),
    id: acceptedId,
    messageBody: 'پیامک پذیرفته‌شده در انتظار تحویل',
    status: 3,
    providerReference: '8888',
    lastError: null,
    acceptedAtUtc: '2026-11-24T08:35:00Z',
    gatewayMode: 1,
    nextDeliveryPollAtUtc: '2026-11-24T08:35:00Z',
    canReconcile: false,
  }
}

function setup() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  queryClient = client
  const invalidateQueries = vi.spyOn(client, 'invalidateQueries')
  render(createElement(QueryClientProvider, { client }, createElement(SmsPage)))
  return { queryClient: client, invalidateQueries }
}

async function waitForQueue() {
  await screen.findByText('پیامک با نتیجه نامشخص')
}

function smsRow(message = 'پیامک با نتیجه نامشخص'): HTMLElement {
  const row = screen.getByText(message).closest('tr')
  if (!(row instanceof HTMLElement)) throw new Error(`Missing SMS row: ${message}`)
  return row
}

async function openDecision(buttonName: string, title: string): Promise<ReturnType<typeof within>> {
  fireEvent.click(screen.getByRole('button', { name: buttonName }))
  const heading = await screen.findByRole('heading', { name: title })
  const modal = heading.closest('.card')
  if (!(modal instanceof HTMLElement)) throw new Error(`Missing confirmation dialog: ${title}`)
  return within(modal)
}

async function confirmDecision(dialog: ReturnType<typeof within>, reason: string) {
  fireEvent.change(dialog.getByRole('textbox', { name: 'دلیل رسیدگی (الزامی)' }), { target: { value: reason } })
  fireEvent.click(dialog.getByRole('checkbox', { name: confirmationLabel }))
}

function reconcileCalls() {
  return vi.mocked(fetch).mock.calls.filter(([input, init]) => String(input) === reconcilePath && init?.method === 'POST')
}

describe('SMS indeterminate reconciliation', () => {
  beforeEach(() => {
    auth.user.permissions = ['sms.manage']
    items = [indeterminateItem(), acceptedItem()]
    reconcileResult = () => response(204)
    vi.stubGlobal('fetch', vi.fn(async (input) => {
      const path = String(input)
      if (path === '/api/sms/summary') {
        return response(200, {
          queued: 1,
          accepted: 1,
          delivered: 0,
          failed: 0,
          permanentFailure: 0,
          todayCount: 2,
          indeterminate: 2,
        })
      }
      if (path.startsWith('/api/sms?')) {
        return response(200, { items, totalCount: items.length, page: 1, pageSize: 20 })
      }
      if (path === reconcilePath) return reconcileResult()
      throw new Error(`Unexpected request: ${path}`)
    }))
  })

  afterEach(() => {
    cleanup()
    queryClient?.clear()
    queryClient = undefined
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('renders the indeterminate summary, status, and investigation evidence in Tehran time', async () => {
    setup()
    await waitForQueue()

    const summaryTitle = screen.getByText('نتیجه نامشخص', { selector: 'p' })
    const summaryCard = summaryTitle.closest('.card')
    if (!(summaryCard instanceof HTMLElement)) throw new Error('Missing indeterminate summary card')
    expect(within(summaryCard).getByText('۲')).toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'نتیجه نامشخص' })).toHaveValue('9')

    const indeterminateRow = smsRow()
    expect(within(indeterminateRow).getByText('نتیجه نامشخص')).toBeInTheDocument()
    expect(indeterminateRow).toHaveTextContent('حالت ذخیره‌شده درگاه: REST (Api)')
    expect(indeterminateRow).toHaveTextContent('زمان شروع ارسال: ۱۴۰۵/۰۹/۰۳ — ۱۲:۰۴')
    expect(indeterminateRow).toHaveTextContent('مرجع سرویس‌دهنده: ۹۰۰۰۱۲۳۴')
    expect(indeterminateRow).toHaveTextContent('آخرین خطا: پاسخ درگاه قبل از پایان مهلت خوانده نشد.')

    const acceptedRow = smsRow('پیامک پذیرفته‌شده در انتظار تحویل')
    expect(acceptedRow).toHaveTextContent('حالت ذخیره‌شده درگاه: SOAP (Wsdl)')
    expect(acceptedRow).toHaveTextContent('مرجع سرویس‌دهنده: ۸۸۸۸')
    expect(acceptedRow).toHaveTextContent('استعلام بعدی تحویل: ۱۴۰۵/۰۹/۰۳ — ۱۲:۰۵')
  })

  it('requires a nonblank reason of at most 500 characters and explicit confirmation', async () => {
    setup()
    await waitForQueue()
    const dialog = await openDecision('ارسال مجدد پس از بررسی', 'رسیدگی به پیامک با نتیجه نامشخص')
    const submit = dialog.getByRole('button', { name: 'تأیید ارسال مجدد' })
    const reason = dialog.getByRole('textbox', { name: 'دلیل رسیدگی (الزامی)' })

    expect(submit).toBeDisabled()
    fireEvent.change(reason, { target: { value: '   ' } })
    expect(submit).toBeDisabled()
    fireEvent.change(reason, { target: { value: 'x'.repeat(501) } })
    expect(submit).toBeDisabled()
    fireEvent.change(reason, { target: { value: 'پاسخ درگاه بررسی شد' } })
    expect(submit).toBeDisabled()
    fireEvent.click(dialog.getByRole('checkbox', { name: confirmationLabel }))
    expect(submit).toBeEnabled()
    expect(reconcileCalls()).toHaveLength(0)
  })

  it('does not reconcile automatically or when the operator cancels the confirmation', async () => {
    setup()
    await waitForQueue()
    expect(reconcileCalls()).toHaveLength(0)

    const dialog = await openDecision('ارسال مجدد پس از بررسی', 'رسیدگی به پیامک با نتیجه نامشخص')
    await confirmDecision(dialog, 'بررسی انجام شد')
    fireEvent.click(dialog.getByRole('button', { name: 'انصراف' }))

    await waitFor(() => expect(screen.queryByRole('heading', { name: 'رسیدگی به پیامک با نتیجه نامشخص' })).not.toBeInTheDocument())
    expect(reconcileCalls()).toHaveLength(0)
  })

  it('submits the exact retry payload after the duplicate-risk warning and invalidates scoped queries', async () => {
    const { invalidateQueries } = setup()
    await waitForQueue()
    const dialog = await openDecision('ارسال مجدد پس از بررسی', 'رسیدگی به پیامک با نتیجه نامشخص')
    expect(dialog.getByText(retryWarning)).toBeInTheDocument()

    await confirmDecision(dialog, '  پاسخ درگاه بررسی شد  ')
    fireEvent.click(dialog.getByRole('button', { name: 'تأیید ارسال مجدد' }))

    await waitFor(() => expect(reconcileCalls()).toHaveLength(1))
    const [path, init] = reconcileCalls()[0]
    expect(path).toBe(reconcilePath)
    expect(init?.method).toBe('POST')
    expect(init?.body).toBe('{"decision":"retry","reason":"پاسخ درگاه بررسی شد","confirmed":true}')
    expect(invalidateQueries).toHaveBeenNthCalledWith(1, { queryKey: ['sms', 'sms.operator'] })
    expect(invalidateQueries).toHaveBeenNthCalledWith(2, { queryKey: ['sms-summary', 'sms.operator'] })
    await waitFor(() => expect(screen.queryByRole('heading', { name: 'رسیدگی به پیامک با نتیجه نامشخص' })).not.toBeInTheDocument())
  })

  it('submits the exact failed-close payload after its distinct confirmation', async () => {
    const { invalidateQueries } = setup()
    await waitForQueue()
    const dialog = await openDecision('بستن به‌عنوان ناموفق', 'بستن پیامک به‌عنوان ناموفق')
    expect(dialog.getByText(failedWarning)).toBeInTheDocument()
    expect(dialog.queryByText(retryWarning)).not.toBeInTheDocument()

    await confirmDecision(dialog, 'پیام در پنل سرویس‌دهنده یافت نشد')
    fireEvent.click(dialog.getByRole('button', { name: 'تأیید بستن به‌عنوان ناموفق' }))

    await waitFor(() => expect(reconcileCalls()).toHaveLength(1))
    const [path, init] = reconcileCalls()[0]
    expect(path).toBe(reconcilePath)
    expect(init?.method).toBe('POST')
    expect(init?.body).toBe('{"decision":"failed","reason":"پیام در پنل سرویس‌دهنده یافت نشد","confirmed":true}')
    expect(invalidateQueries).toHaveBeenNthCalledWith(1, { queryKey: ['sms', 'sms.operator'] })
    expect(invalidateQueries).toHaveBeenNthCalledWith(2, { queryKey: ['sms-summary', 'sms.operator'] })
  })

  it('shows the server error without closing the decision or invalidating queries', async () => {
    reconcileResult = () => response(409, { message: 'وضعیت پیامک هم‌زمان تغییر کرده است.' })
    const { invalidateQueries } = setup()
    await waitForQueue()
    const dialog = await openDecision('بستن به‌عنوان ناموفق', 'بستن پیامک به‌عنوان ناموفق')
    await confirmDecision(dialog, 'بررسی اولیه انجام شد')
    fireEvent.click(dialog.getByRole('button', { name: 'تأیید بستن به‌عنوان ناموفق' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('وضعیت پیامک هم‌زمان تغییر کرده است.')
    expect(screen.getByRole('heading', { name: 'بستن پیامک به‌عنوان ناموفق' })).toBeInTheDocument()
    expect(dialog.getByRole('textbox', { name: 'دلیل رسیدگی (الزامی)' })).toHaveValue('بررسی اولیه انجام شد')
    expect(dialog.getByRole('checkbox', { name: confirmationLabel })).toBeChecked()
    expect(dialog.getByRole('button', { name: 'تأیید بستن به‌عنوان ناموفق' })).toBeEnabled()
    expect(invalidateQueries).not.toHaveBeenCalled()
  })

  it.each([
    { label: 'the operator lacks sms.manage', permissions: [] as string[], canReconcile: true },
    { label: 'the server disallows reconciliation', permissions: ['sms.manage'], canReconcile: false },
  ])('hides reconciliation controls when $label', async ({ permissions, canReconcile }) => {
    auth.user.permissions = permissions
    items[0].canReconcile = canReconcile
    setup()
    await waitForQueue()

    expect(screen.queryByRole('button', { name: 'ارسال مجدد پس از بررسی' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'بستن به‌عنوان ناموفق' })).not.toBeInTheDocument()
    expect(reconcileCalls()).toHaveLength(0)
  })
})
