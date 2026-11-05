import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { api } from '../lib/api'
import { faNumber, formatFaDateTime, toFa } from '../lib/date'
import { MISSION_STATUS, MISSION_TYPE } from '../lib/labels'
import type { MissionListItem, PagedResult } from '../lib/types'
import { EmptyState, Modal, PageHeader, Pagination, Spinner, StatusBadge } from '../components/ui'
import { MissionForm } from '../components/MissionForm'

export function MissionsPage() {
  const { can } = useAuth()
  const queryClient = useQueryClient()
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<number | ''>('')
  const [type, setType] = useState<number | ''>('')
  const [onlyMine, setOnlyMine] = useState(false)
  const [createOpen, setCreateOpen] = useState(false)

  const params = new URLSearchParams({ page: String(page), pageSize: '12' })
  if (search.trim()) params.set('search', search.trim())
  if (status !== '') params.set('statuses', String(status))
  if (type !== '') params.set('type', String(type))
  if (onlyMine) params.set('onlyMine', 'true')

  const { data, isLoading } = useQuery({
    queryKey: ['missions', params.toString()],
    queryFn: () => api.get<PagedResult<MissionListItem>>(`/api/missions?${params}`),
  })

  const createMutation = useMutation({
    mutationFn: (input: unknown) => api.post('/api/missions', input),
    onSuccess: () => {
      setCreateOpen(false)
      void queryClient.invalidateQueries({ queryKey: ['missions'] })
    },
  })

  return (
    <div>
      <PageHeader
        title="مأموریت‌ها"
        subtitle="ثبت، بررسی و مدیریت چرخه عمر مأموریت‌ها"
        actions={
          can('missions.create') && (
            <button className="btn-primary" onClick={() => setCreateOpen(true)}>
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2">
                <path d="M12 5v14M5 12h14" />
              </svg>
              ثبت درخواست مأموریت
            </button>
          )
        }
      />

      <div className="card mb-5 flex flex-wrap items-center gap-3 p-4">
        <div className="relative min-w-56 flex-1">
          <svg className="absolute right-3 top-1/2 -translate-y-1/2 text-slate-400" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <circle cx="11" cy="11" r="7" />
            <path d="m21 21-4-4" />
          </svg>
          <input
            className="input pr-9"
            placeholder="جست‌وجو در شماره، عنوان، مبدأ، مقصد…"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value)
              setPage(1)
            }}
          />
        </div>
        <select className="input w-44" value={status} onChange={(e) => { setStatus(e.target.value === '' ? '' : Number(e.target.value)); setPage(1) }}>
          <option value="">همه وضعیت‌ها</option>
          {Object.entries(MISSION_STATUS).map(([value, meta]) => (
            <option key={value} value={value}>{meta.title}</option>
          ))}
        </select>
        <select className="input w-36" value={type} onChange={(e) => { setType(e.target.value === '' ? '' : Number(e.target.value)); setPage(1) }}>
          <option value="">همه انواع</option>
          {Object.entries(MISSION_TYPE).map(([value, title]) => (
            <option key={value} value={value}>{title}</option>
          ))}
        </select>
        {can('missions.view') && (
          <label className="flex cursor-pointer select-none items-center gap-2 text-sm text-slate-600">
            <input
              type="checkbox"
              className="h-4 w-4 accent-emerald-700"
              checked={onlyMine}
              onChange={(e) => { setOnlyMine(e.target.checked); setPage(1) }}
            />
            فقط درخواست‌های من
          </label>
        )}
      </div>

      <div className="card overflow-hidden">
        {isLoading ? (
          <Spinner />
        ) : (data?.items ?? []).length === 0 ? (
          <EmptyState
            title="مأموریتی یافت نشد"
            hint="با تغییر فیلترها جست‌وجو کنید یا درخواست مأموریت جدیدی ثبت کنید."
          />
        ) : (
          <>
            <div className="overflow-x-auto">
              <table className="w-full min-w-[820px] text-sm">
                <thead className="table-head">
                  <tr>
                    <th className="px-4 py-3 text-right font-semibold">شماره</th>
                    <th className="px-4 py-3 text-right font-semibold">عنوان</th>
                    <th className="px-4 py-3 text-right font-semibold">مسیر</th>
                    <th className="px-4 py-3 text-right font-semibold">راننده / خودرو</th>
                    <th className="px-4 py-3 text-right font-semibold">زمان</th>
                    <th className="px-4 py-3 text-right font-semibold">وضعیت</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-50">
                  {data!.items.map((m) => (
                    <tr key={m.id} className="group transition-colors hover:bg-emerald-50/40">
                      <td className="px-4 py-3.5">
                        <Link to={`/missions/${m.id}`} className="num font-bold text-emerald-800 group-hover:underline">
                          {toFa(m.missionNumber)}
                        </Link>
                      </td>
                      <td className="max-w-52 px-4 py-3.5">
                        <Link to={`/missions/${m.id}`} className="block truncate font-semibold text-slate-700 group-hover:text-emerald-900">
                          {m.title}
                        </Link>
                        <p className="mt-0.5 truncate text-xs text-slate-400">{m.requesterName}</p>
                      </td>
                      <td className="max-w-56 px-4 py-3.5">
                        <p className="truncate text-[13px] text-slate-600">
                          {m.origin} ← {m.destination}
                        </p>
                      </td>
                      <td className="px-4 py-3.5 text-[13px] text-slate-600">
                        {m.driverName ?? '—'}
                        <span className="num block text-xs text-slate-400">{m.vehiclePlate ?? ''}</span>
                      </td>
                      <td className="px-4 py-3.5 text-xs text-slate-500">
                        <span className="num">{formatFaDateTime(m.plannedStartAtUtc)}</span>
                      </td>
                      <td className="px-4 py-3.5">
                        <StatusBadge status={m.status} />
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

      <Modal open={createOpen} title="ثبت درخواست مأموریت" onClose={() => setCreateOpen(false)} wide>
        <MissionForm
          submitting={createMutation.isPending}
          error={createMutation.error instanceof Error ? createMutation.error.message : null}
          onSubmit={(input) => createMutation.mutate(input)}
        />
      </Modal>
    </div>
  )
}

export function missionRouteHelpers() {
  return { faNumber }
}
