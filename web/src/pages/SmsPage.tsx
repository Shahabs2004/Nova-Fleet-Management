import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { EmptyState, Modal, PageHeader, Pagination, Spinner, StatCard } from '../components/ui'
import { useAuth } from '../auth/AuthContext'
import { api } from '../lib/api'
import { faNumber, formatFaDateTime, toFa } from '../lib/date'
import { SMS_STATUS } from '../lib/labels'
import type { PagedResult, SmsItem, SmsTemplate } from '../lib/types'

interface SmsSummary {
  queued: number
  accepted: number
  delivered: number
  failed: number
  permanentFailure: number
  todayCount: number
}

function SmsStatusBadge({ status }: { status: number }) {
  const meta = SMS_STATUS[status] ?? { title: 'نامشخص', badge: 'bg-slate-100 text-slate-600' }
  return <span className={`badge ring-1 ring-transparent ${meta.badge}`}>{meta.title}</span>
}

export function SmsPage() {
  const { can } = useAuth()
  const queryClient = useQueryClient()
  const [tab, setTab] = useState<'queue' | 'templates'>('queue')
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState('')
  const [page, setPage] = useState(1)
  const pageSize = 20
  const [testOpen, setTestOpen] = useState(false)

  const summary = useQuery({ queryKey: ['sms-summary'], queryFn: () => api.get<SmsSummary>('/api/sms/summary') })

  const params = new URLSearchParams()
  if (search) params.set('q', search)
  if (status) params.set('status', status)
  params.set('page', String(page))
  params.set('pageSize', String(pageSize))

  const queue = useQuery({
    queryKey: ['sms', search, status, page],
    queryFn: () => api.get<PagedResult<SmsItem>>(`/api/sms?${params.toString()}`),
    enabled: tab === 'queue',
  })

  const retry = useMutation({
    mutationFn: (id: string) => api.post(`/api/sms/${id}/retry`),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['sms'] })
      void queryClient.invalidateQueries({ queryKey: ['sms-summary'] })
    },
  })

  return (
    <div>
      <PageHeader
        title="پیامک‌ها"
        subtitle="صف ارسال، وضعیت تحویل و قالب‌های رویدادهای مأموریت"
        actions={
          can('notifications.sms.templates.manage') && (
            <button className="btn-primary btn-sm" onClick={() => setTestOpen(true)}>
              پیامک آزمایشی
            </button>
          )
        }
      />

      <div className="mb-5 grid grid-cols-2 gap-4 xl:grid-cols-5">
        <StatCard title="در صف" value={faNumber(summary.data?.queued)} tone="gold" icon={icon('M12 7v5l3 3M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18Z')} />
        <StatCard title="پذیرفته‌شده" value={faNumber(summary.data?.accepted)} tone="sky" icon={icon('M22 12h-4l-3 9L9 3l-3 9H2')} />
        <StatCard title="تحویل‌شده" value={faNumber(summary.data?.delivered)} tone="emerald" icon={icon('M20 6 9 17l-5-5')} />
        <StatCard title="ناموفق" value={faNumber((summary.data?.failed ?? 0) + (summary.data?.permanentFailure ?? 0))} tone="rose" icon={icon('M12 8v4m0 4h.01M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18Z')} />
        <StatCard title="امروز" value={faNumber(summary.data?.todayCount)} tone="indigo" icon={icon('M8 2v4m8-4v4M3 10h18M5 4h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2Z')} />
      </div>

      <div className="mb-4 flex gap-1 rounded-xl bg-slate-100/80 p-1 text-sm font-medium">
        <button
          className={`flex-1 rounded-lg px-4 py-2 transition-colors ${tab === 'queue' ? 'bg-white text-emerald-900 shadow-sm' : 'text-slate-500'}`}
          onClick={() => setTab('queue')}
        >
          صف ارسال
        </button>
        <button
          className={`flex-1 rounded-lg px-4 py-2 transition-colors ${tab === 'templates' ? 'bg-white text-emerald-900 shadow-sm' : 'text-slate-500'}`}
          onClick={() => setTab('templates')}
        >
          قالب‌ها
        </button>
      </div>

      {tab === 'queue' && (
        <div>
          <div className="card mb-4 flex flex-wrap items-end gap-3 p-4">
            <div className="min-w-52 flex-1">
              <label className="label">جستجو</label>
              <input
                className="input"
                placeholder="گیرنده، شماره یا متن پیامک"
                value={search}
                onChange={(e) => {
                  setSearch(e.target.value)
                  setPage(1)
                }}
              />
            </div>
            <div className="w-48">
              <label className="label">وضعیت</label>
              <select
                className="input"
                value={status}
                onChange={(e) => {
                  setStatus(e.target.value)
                  setPage(1)
                }}
              >
                <option value="">همه وضعیت‌ها</option>
                {Object.entries(SMS_STATUS).map(([value, meta]) => (
                  <option key={value} value={value}>
                    {meta.title}
                  </option>
                ))}
              </select>
            </div>
          </div>

          <div className="card overflow-hidden">
            {queue.isLoading && <Spinner />}
            {queue.isError && (
              <div className="px-5 py-6 text-sm font-medium text-rose-700">{(queue.error as Error)?.message ?? 'خطا در دریافت صف پیامک.'}</div>
            )}
            {queue.data && queue.data.items.length === 0 && <EmptyState title="پیامکی در صف نیست" hint="با فیلترها جستجو کنید یا پیامک آزمایشی بفرستید." />}
            {queue.data && queue.data.items.length > 0 && (
              <div className="overflow-x-auto">
                <table className="w-full min-w-[760px] text-sm">
                  <thead>
                    <tr className="table-head">
                      <th className="text-right">رویداد</th>
                      <th className="text-right">گیرنده</th>
                      <th className="text-right">متن</th>
                      <th className="text-right">وضعیت</th>
                      <th className="text-right">تلاش</th>
                      <th className="text-right">زمان</th>
                      <th className="text-left">عملیات</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-50">
                    {queue.data.items.map((item) => (
                      <tr key={item.id} className="align-top hover:bg-slate-50/70">
                        <td>
                          <p className="text-xs font-semibold text-emerald-950">{item.eventKey}</p>
                          <p className="num mt-0.5 text-[11px] text-slate-400">{toFa(item.missionNumber)}</p>
                        </td>
                        <td>
                          <p className="text-xs font-medium text-slate-700">{item.recipientName}</p>
                          <p className="num mt-0.5 text-[11px] text-slate-400">{toFa(item.recipientMobile)}</p>
                        </td>
                        <td className="max-w-72">
                          <p className="line-clamp-2 text-xs leading-5 text-slate-600">{item.messageBody}</p>
                          {item.lastError && <p className="mt-0.5 text-[10px] text-rose-600">{item.lastError}</p>}
                        </td>
                        <td>
                          <SmsStatusBadge status={item.status} />
                        </td>
                        <td className="num text-xs text-slate-500">
                          {toFa(item.attemptCount)} / {toFa(item.maxAttempts)}
                        </td>
                        <td className="num text-[11px] text-slate-400">{formatFaDateTime(item.createdAtUtc)}</td>
                        <td className="text-left">
                          {can('notifications.sms.retry') && item.canRetry && (
                            <button className="btn-ghost btn-sm" disabled={retry.isPending} onClick={() => retry.mutate(item.id)}>
                              ارسال مجدد
                            </button>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
            {queue.data && <div className="px-5 pb-4"><Pagination page={page} pageSize={pageSize} total={queue.data.totalCount} onChange={setPage} /></div>}
          </div>
        </div>
      )}

      {tab === 'templates' && <TemplatesTab canEdit={can('notifications.sms.templates.manage')} />}

      <TestSmsModal
        open={testOpen}
        onClose={() => setTestOpen(false)}
        onSent={() => {
          void queryClient.invalidateQueries({ queryKey: ['sms'] })
          void queryClient.invalidateQueries({ queryKey: ['sms-summary'] })
        }}
      />
    </div>
  )
}

function icon(d: string) {
  return (
    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <path d={d} />
    </svg>
  )
}

function TemplatesTab({ canEdit }: { canEdit: boolean }) {
  const queryClient = useQueryClient()
  const [editing, setEditing] = useState<SmsTemplate | null>(null)
  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['sms-templates'],
    queryFn: () => api.get<SmsTemplate[]>('/api/sms/templates'),
  })

  if (isLoading) return <Spinner />
  if (isError) return <div className="card p-6 text-sm font-medium text-rose-700">{(error as Error)?.message ?? 'خطا در دریافت قالب‌ها.'}</div>

  return (
    <div className="grid gap-3 lg:grid-cols-2">
      {(data ?? []).map((template) => (
        <div key={template.eventType} className="card p-4">
          <div className="flex items-start justify-between gap-3">
            <div className="min-w-0">
              <p className="text-sm font-bold text-emerald-950">{template.title}</p>
              <p className="mt-0.5 text-[11px] text-slate-400">{template.eventKey}</p>
            </div>
            <div className="flex shrink-0 items-center gap-2">
              <span className={`badge ${template.isActive ? 'bg-emerald-50 text-emerald-700' : 'bg-slate-100 text-slate-500'}`}>
                {template.isActive ? 'فعال' : 'غیرفعال'}
              </span>
              {canEdit && (
                <button className="btn-ghost btn-sm" onClick={() => setEditing(template)}>
                  ویرایش
                </button>
              )}
            </div>
          </div>
          <p className="mt-2 line-clamp-3 rounded-lg bg-slate-50 px-3 py-2 text-xs leading-6 text-slate-600">{template.body}</p>
          <p className="mt-2 text-[11px] text-slate-400">گیرندگان: {template.recipients.length > 0 ? template.recipients.join('، ') : 'بدون گیرنده'}</p>
        </div>
      ))}
      <Modal open={Boolean(editing)} title={editing ? `ویرایش قالب: ${editing.title}` : ''} onClose={() => setEditing(null)} wide>
        {editing && (
          <TemplateForm
            template={editing}
            onDone={async () => {
              setEditing(null)
              await queryClient.invalidateQueries({ queryKey: ['sms-templates'] })
            }}
          />
        )}
      </Modal>
    </div>
  )
}

function TemplateForm({ template, onDone }: { template: SmsTemplate; onDone: () => Promise<void> }) {
  const [body, setBody] = useState(template.body)
  const [isActive, setIsActive] = useState(template.isActive)
  const [recipients, setRecipients] = useState(template.recipients.join('، '))
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  const submit = async () => {
    setSubmitting(true)
    setError(null)
    try {
      await api.put(`/api/sms/templates/${template.eventType}`, {
        body,
        isActive,
        recipients: recipients
          .split(/[,،\n]/)
          .map((item) => item.trim())
          .filter(Boolean),
      })
      await onDone()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="space-y-4">
      <div>
        <label className="label">متن پیامک</label>
        <textarea className="input min-h-28 resize-y" value={body} onChange={(e) => setBody(e.target.value)} />
        <p className="mt-1 text-[11px] text-slate-400">از جای‌نگهدارهای قالب مانند نام راننده و شماره مأموریت استفاده کنید.</p>
      </div>
      <div>
        <label className="label">گیرندگان (با کاما جدا کنید)</label>
        <input className="input" value={recipients} onChange={(e) => setRecipients(e.target.value)} placeholder="requester، driver، manager" />
      </div>
      <label className="flex items-center gap-2 text-sm text-slate-600">
        <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} className="h-4 w-4 accent-emerald-700" />
        این رویداد فعال باشد
      </label>
      {error && <div className="rounded-xl bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700 ring-1 ring-rose-100">{error}</div>}
      <div className="flex justify-end">
        <button className="btn-primary" disabled={submitting} onClick={submit}>
          {submitting ? 'در حال ذخیره…' : 'ذخیره قالب'}
        </button>
      </div>
    </div>
  )
}

function TestSmsModal({ open, onClose, onSent }: { open: boolean; onClose: () => void; onSent: () => void }) {
  const [mobile, setMobile] = useState('')
  const [body, setBody] = useState('پیامک آزمایشی سامانه ناوگان نوین.')
  const [error, setError] = useState<string | null>(null)
  const [sending, setSending] = useState(false)

  const send = async () => {
    setSending(true)
    setError(null)
    try {
      await api.post('/api/sms/test', { mobile, body })
      onSent()
      onClose()
      setMobile('')
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setSending(false)
    }
  }

  return (
    <Modal open={open} title="ارسال پیامک آزمایشی" onClose={onClose}>
      <div className="space-y-4">
        <div>
          <label className="label">شماره موبایل</label>
          <input className="input num" dir="ltr" value={mobile} onChange={(e) => setMobile(e.target.value)} placeholder="09120000000" />
        </div>
        <div>
          <label className="label">متن پیامک</label>
          <textarea className="input min-h-20 resize-y" value={body} onChange={(e) => setBody(e.target.value)} />
        </div>
        {error && <div className="rounded-xl bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700 ring-1 ring-rose-100">{error}</div>}
        <div className="flex justify-end">
          <button className="btn-primary" disabled={sending || !mobile} onClick={send}>
            {sending ? 'در حال ثبت…' : 'ثبت در صف'}
          </button>
        </div>
      </div>
    </Modal>
  )
}
