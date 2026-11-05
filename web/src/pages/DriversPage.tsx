import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api } from '../lib/api'
import { formatFaDate } from '../lib/date'
import { DRIVER_STATUS } from '../lib/labels'
import type { DriverDto, PagedResult } from '../lib/types'
import { EmptyState, Modal, PageHeader, Pagination, Spinner } from '../components/ui'

export function DriversPage() {
  const queryClient = useQueryClient()
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [includeArchived, setIncludeArchived] = useState(false)
  const [edit, setEdit] = useState<DriverDto | 'new' | null>(null)

  const params = new URLSearchParams({ page: String(page), pageSize: '12' })
  if (search.trim()) params.set('search', search.trim())
  if (includeArchived) params.set('includeArchived', 'true')

  const { data, isLoading } = useQuery({
    queryKey: ['drivers', params.toString()],
    queryFn: () => api.get<PagedResult<DriverDto>>(`/api/drivers?${params}`),
  })

  const mutation = useMutation({
    mutationFn: ({ path, body }: { path: string; body?: unknown }) =>
      body === undefined ? api.post(path) : api.post(path, body),
    onSuccess: async () => {
      setEdit(null)
      await queryClient.invalidateQueries({ queryKey: ['drivers'] })
    },
  })

  return (
    <div>
      <PageHeader
        title="رانندگان"
        subtitle="پرونده رانندگان، وضعیت همکاری و خودروی پیش‌فرض"
        actions={
          <button className="btn-primary" onClick={() => setEdit('new')}>
            افزودن راننده
          </button>
        }
      />

      <div className="card mb-5 flex flex-wrap items-center gap-3 p-4">
        <input
          className="input min-w-56 flex-1"
          placeholder="جست‌وجوی نام یا شماره موبایل…"
          value={search}
          onChange={(e) => { setSearch(e.target.value); setPage(1) }}
        />
        <label className="flex cursor-pointer items-center gap-2 text-sm text-slate-600">
          <input type="checkbox" className="h-4 w-4 accent-emerald-700" checked={includeArchived} onChange={(e) => { setIncludeArchived(e.target.checked); setPage(1) }} />
          نمایش بایگانی‌شده‌ها
        </label>
      </div>

      <div className="card overflow-hidden">
        {isLoading ? (
          <Spinner />
        ) : (data?.items ?? []).length === 0 ? (
          <EmptyState title="راننده‌ای یافت نشد" hint="با تغییر جست‌وجو دوباره تلاش کنید." />
        ) : (
          <>
            <div className="overflow-x-auto">
              <table className="w-full min-w-[720px] text-sm">
                <thead className="table-head">
                  <tr>
                    <th className="px-4 py-3 text-right font-semibold">نام</th>
                    <th className="px-4 py-3 text-right font-semibold">موبایل</th>
                    <th className="px-4 py-3 text-right font-semibold">وضعیت همکاری</th>
                    <th className="px-4 py-3 text-right font-semibold">خودروی پیش‌فرض</th>
                    <th className="px-4 py-3 text-right font-semibold">تاریخ ثبت</th>
                    <th className="px-4 py-3 text-right font-semibold"></th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-50">
                  {data!.items.map((d) => (
                    <tr key={d.id} className="transition-colors hover:bg-emerald-50/40">
                      <td className="px-4 py-3.5 font-semibold text-slate-700">
                        {d.displayName}
                        {d.isArchived && <span className="badge mr-2 bg-slate-100 text-slate-500">بایگانی</span>}
                      </td>
                      <td className="num px-4 py-3.5 text-slate-600" dir="ltr">{d.mobilePhone}</td>
                      <td className="px-4 py-3.5">
                        <select
                          className="rounded-lg border border-slate-200 bg-white px-2 py-1.5 text-xs"
                          value={d.employmentStatus}
                          onChange={(e) => mutation.mutate({ path: `/api/drivers/${d.id}/employment-status`, body: { status: Number(e.target.value), rowVersion: d.rowVersion } })}
                        >
                          {Object.entries(DRIVER_STATUS).map(([value, title]) => (
                            <option key={value} value={value}>{title}</option>
                          ))}
                        </select>
                      </td>
                      <td className="num px-4 py-3.5 text-slate-600">{d.defaultVehiclePlate ?? '—'}</td>
                      <td className="num px-4 py-3.5 text-xs text-slate-400">{formatFaDate(d.createdAtUtc)}</td>
                      <td className="px-4 py-3.5">
                        <div className="flex justify-end gap-1.5">
                          <button className="btn-ghost btn-sm" onClick={() => setEdit(d)}>ویرایش</button>
                          <button
                            className="btn-ghost btn-sm"
                            onClick={() => mutation.mutate({ path: `/api/drivers/${d.id}/archive`, body: { archived: !d.isArchived } })}
                          >
                            {d.isArchived ? 'بازگردانی' : 'بایگانی'}
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <div className="border-t border-slate-100 px-4">
              <Pagination page={data!.page} pageSize={data!.pageSize} total={data!.totalCount} onChange={setPage} />
            </div>
          </>
        )}
      </div>

      <DriverEditModal
        driver={edit}
        onClose={() => setEdit(null)}
        onSave={async (body, rowVersion) => {
          if (edit === 'new') await api.post('/api/drivers', body)
          else await api.put(`/api/drivers/${(edit as DriverDto).id}`, { ...(body as object), rowVersion })
          setEdit(null)
          await queryClient.invalidateQueries({ queryKey: ['drivers'] })
        }}
      />
    </div>
  )
}

function DriverEditModal({
  driver,
  onClose,
  onSave,
}: {
  driver: DriverDto | 'new' | null
  onClose: () => void
  onSave: (body: { firstName: string; lastName: string; mobilePhone: string }, rowVersion?: string) => Promise<void>
}) {
  const isNew = driver === 'new'
  const existing = driver !== 'new' ? driver : null
  const [firstName, setFirstName] = useState(existing?.firstName ?? '')
  const [lastName, setLastName] = useState(existing?.lastName ?? '')
  const [mobile, setMobile] = useState(existing?.mobilePhone ?? '')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const key = isNew ? 'new' : existing?.id ?? 'closed'

  return (
    <Modal open={driver !== null} title={isNew ? 'افزودن راننده' : `ویرایش ${existing?.displayName}`} onClose={onClose}>
      <form
        key={key}
        className="space-y-4"
        onSubmit={async (e) => {
          e.preventDefault()
          setBusy(true)
          setError(null)
          try {
            await onSave({ firstName, lastName, mobilePhone: mobile }, existing?.rowVersion)
          } catch (err) {
            setError(err instanceof Error ? err.message : 'ذخیره ناموفق بود.')
          } finally {
            setBusy(false)
          }
        }}
      >
        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="label">نام</label>
            <input className="input" value={firstName} onChange={(e) => setFirstName(e.target.value)} required />
          </div>
          <div>
            <label className="label">نام خانوادگی</label>
            <input className="input" value={lastName} onChange={(e) => setLastName(e.target.value)} required />
          </div>
        </div>
        <div>
          <label className="label">شماره موبایل</label>
          <input className="input num" dir="ltr" value={mobile} onChange={(e) => setMobile(e.target.value)} placeholder="09xxxxxxxxx" required />
        </div>
        {error && <div className="rounded-xl bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700 ring-1 ring-rose-100">{error}</div>}
        <button type="submit" disabled={busy} className="btn-primary w-full">
          {busy ? 'در حال ذخیره…' : 'ذخیره'}
        </button>
      </form>
    </Modal>
  )
}
