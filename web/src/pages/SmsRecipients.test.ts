import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { createElement } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { TemplateForm } from './SmsPage'
import type { SmsTemplate } from '../lib/types'

const template: SmsTemplate = {
  eventType: 1,
  eventKey: 'mission-approved',
  title: 'تأیید مأموریت',
  body: 'متن پیامک',
  isActive: true,
  recipients: ['requester', 'driver'],
}

function response(body: unknown): Response {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

describe('SMS recipient selection', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response({})))
  })

  afterEach(() => {
    cleanup()
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('submits the explicit none token when every recipient is unchecked', async () => {
    const onDone = vi.fn().mockResolvedValue(undefined)
    render(createElement(TemplateForm, { template, onDone }))

    fireEvent.click(screen.getByRole('checkbox', { name: 'درخواست‌کننده' }))
    fireEvent.click(screen.getByRole('checkbox', { name: 'راننده' }))
    expect(screen.getByText('بدون گیرنده — برای این رویداد پیامکی ارسال نمی‌شود.')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'ذخیره قالب' }))

    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1))
    const init = vi.mocked(fetch).mock.calls[0][1]
    expect(JSON.parse(String(init?.body))).toMatchObject({ recipients: ['none'] })
    await waitFor(() => expect(onDone).toHaveBeenCalledTimes(1))
  })

  it('keeps known selected recipients in the request', async () => {
    const onDone = vi.fn().mockResolvedValue(undefined)
    render(createElement(TemplateForm, { template, onDone }))

    fireEvent.click(screen.getByRole('checkbox', { name: 'راننده' }))
    fireEvent.click(screen.getByRole('button', { name: 'ذخیره قالب' }))

    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1))
    const init = vi.mocked(fetch).mock.calls[0][1]
    expect(JSON.parse(String(init?.body))).toMatchObject({ recipients: ['requester'] })
  })
})
