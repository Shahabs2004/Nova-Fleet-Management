import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api } from '../lib/api'
import { formatFaDate } from '../lib/date'
import { VEHICLE_STATUS } from '../lib/labels'
import type { DriverDto, PagedResult, VehicleDto } from '../lib/types'
import { EmptyState, Modal, PageHeader, Pagination, Spinner } from '../components/ui'
import { Plate, PLATE_LETTER_GROUPS } from '../components/Plate'

const emptyPlate = { brand: '', model: '', plateFirstTwo: '', plateLetter: '', plateNextThree: '', plateIranCode: '' }

export function VehiclesPage() {
  const queryClient = useQueryClient()
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [includeArchived, setIncludeArchived] = useState(false)
  const [edit, setEdit] = useState<VehicleDto | 'new' | null>(null)

  const params = new URLSearchParams({ page: String(page), pageSize: '12' })
  if (search.trim()) params.set('search', search.trim())
  if (includeArchived) params.set('includeArchived', 'true')

  const { data, isLoading } = useQuery({
    queryKey: ['vehicles', params.toString()],
    queryFn: () => api.get<PagedResult<VehicleDto>>(`/api/vehicles?${params}`),
  })

  const drivers = useQuery({ queryKey: ['driver-options'], queryFn: () => api.get<DriverDto[]>('/api/drivers/options') })

  const mutation = useMutation({
    mutationFn: ({ path, body }: { path: string; body?: unknown }) => api.post(path, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['vehicles'] }),
  })

  return (
    <div>
      <PageHeader
        title="خودروها"
        subtitle="ناوگان خودرویی، پلاک‌ها و وضعیت بهره‌برداری"
        actions={<button className="btn-primary" onClick={() => setEdit('new')}>افزودن خودرو</button>}
      />

      <div className="card mb-5 flex flex-wrap items-center gap-3 p-4">
        <input
          className="input min-w-56 flex-1"
          placeholder="جست‌وجوی برند، مدل یا پلاک…"
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
          <EmptyState title="خودرویی یافت نشد" hint="با تغییر جست‌وجو دوباره تلاش کنید." />
        ) : (
          <>
            <div className="overflow-x-auto">
              <table className="w-full min-w-[760px] text-sm">
                <thead className="table-head">
                  <tr>
                    <th className="px-4 py-3 text-right font-semibold">خودرو</th>
                    <th className="px-4 py-3 text-right font-semibold">پلاک</th>
                    <th className="px-4 py-3 text-right font-semibold">وضعیت دستی</th>
                    <th className="px-4 py-3 text-right font-semibold">وضعیت مؤثر</th>
                    <th className="px-4 py-3 text-right font-semibold">تاریخ ثبت</th>
                    <th className="px-4 py-3 text-right font-semibold"></th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-50">
                  {data!.items.map((v) => (
                    <tr key={v.id} className="transition-colors hover:bg-emerald-50/40">
                      <td className="px-4 py-3.5">
                        <span className="font-semibold text-slate-700">{v.brand} {v.model}</span>
                        {v.isArchived && <span className="badge mr-2 bg-slate-100 text-slate-500">بایگانی</span>}
                      </td>
                      <td className="px-4 py-3.5"><Plate parts={{ firstTwo: v.plateFirstTwo, letter: v.plateLetter, nextThree: v.plateNextThree, iranCode: v.plateIranCode }} size="sm" /></td>
                      <td className="px-4 py-3.5">
                        <select
                          className="rounded-lg border border-slate-200 bg-white px-2 py-1.5 text-xs"
                          value={v.manualStatus}
                          onChange={(e) => mutation.mutate({ path: `/api/vehicles/${v.id}/manual-status`, body: { status: Number(e.target.value), rowVersion: v.rowVersion } })}
                        >
                          <option value={1}>آماده</option>
                          <option value={3}>تعمیرگاه</option>
                          <option value={4}>خارج از سرویس</option>
                        </select>
                      </td>
                      <td className="px-4 py-3.5">
                        <span className={`badge ${v.effectiveStatus === 1 ? 'bg-emerald-50 text-emerald-700' : v.effectiveStatus === 2 ? 'bg-indigo-50 text-indigo-700' : 'bg-amber-50 text-amber-700'}`}>
                          {VEHICLE_STATUS[v.effectiveStatus]}
                        </span>
                      </td>
                      <td className="num px-4 py-3.5 text-xs text-slate-400">{formatFaDate(v.createdAtUtc)}</td>
                      <td className="px-4 py-3.5">
                        <div className="flex justify-end gap-1.5">
                          <button className="btn-ghost btn-sm" onClick={() => setEdit(v)}>ویرایش</button>
                          <button className="btn-ghost btn-sm" onClick={() => mutation.mutate({ path: `/api/vehicles/${v.id}/archive`, body: { archived: !v.isArchived } })}>
                            {v.isArchived ? 'بازگردانی' : 'بایگانی'}
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

      <VehicleEditModal
        key={edit === 'new' ? 'new' : edit?.id ?? 'closed'}
        vehicle={edit}
        drivers={drivers.data ?? []}
        driversLoaded={drivers.data !== undefined}
        onClose={() => setEdit(null)}
        onSaved={async () => {
          setEdit(null)
          await queryClient.invalidateQueries({ queryKey: ['vehicles'] })
          await queryClient.invalidateQueries({ queryKey: ['drivers'] })
          await queryClient.invalidateQueries({ queryKey: ['driver-options'] })
        }}
      />
    </div>
  )
}

function VehicleEditModal({
  vehicle,
  drivers,
  driversLoaded,
  onClose,
  onSaved,
}: {
  vehicle: VehicleDto | 'new' | null
  drivers: DriverDto[]
  driversLoaded: boolean
  onClose: () => void
  onSaved: () => Promise<void>
}) {
  const isNew = vehicle === 'new'
  const existing = vehicle !== 'new' ? vehicle : null
  const [form, setForm] = useState(existing ?? emptyPlate)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const holder = drivers.find((d) => d.defaultVehicleId === existing?.id)
  const [driverOverride, setDriverOverride] = useState<string | undefined>(undefined)
  const driverId = driverOverride !== undefined ? driverOverride : holder?.id ?? ''

  const set = (field: keyof typeof emptyPlate) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setForm((prev) => ({ ...prev, [field]: e.target.value }))

  return (
    <Modal open={vehicle !== null} title={isNew ? 'افزودن خودرو' : `ویرایش ${existing?.displayName}`} onClose={onClose}>
      <form
        className="space-y-4"
        onSubmit={async (e) => {
          e.preventDefault()
          setBusy(true)
          setError(null)
          try {
            const saved = isNew
              ? await api.post<VehicleDto>('/api/vehicles', form)
              : await api.put<VehicleDto>(`/api/vehicles/${existing!.id}`, { ...form, rowVersion: existing!.rowVersion })
            if (driversLoaded) {
              const previousId = holder?.id ?? null
              const targetId = driverId === '' ? null : driverId
              if (targetId !== previousId) {
                await api.post(`/api/vehicles/${saved.id}/driver`, { driverId: targetId })
              }
            }
            await onSaved()
          } catch (err) {
            setError(err instanceof Error ? err.message : 'ذخیره ناموفق بود.')
          } finally {
            setBusy(false)
          }
        }}
      >
        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="label">برند</label>
            <input className="input" value={form.brand} onChange={set('brand')} required />
          </div>
          <div>
            <label className="label">مدل</label>
            <input className="input" value={form.model} onChange={set('model')} required />
          </div>
        </div>
        <div>
          <label className="label">پلاک خودرو</label>
          <div className="flex items-center gap-2" dir="ltr">
            <input className="input num w-16 text-center" value={form.plateFirstTwo} onChange={set('plateFirstTwo')} placeholder="12" maxLength={2} required />
            <select
              className="input w-24 text-center"
              value={form.plateLetter}
              onChange={(e) => setForm((prev) => ({ ...prev, plateLetter: e.target.value }))}
              required
            >
              <option value="" disabled>حرف</option>
              {PLATE_LETTER_GROUPS.map((group) => (
                <optgroup key={group.label} label={group.label}>
                  {group.letters.map((letter) => (
                    <option key={letter} value={letter}>{letter}</option>
                  ))}
                </optgroup>
              ))}
            </select>
            <input className="input num w-20 text-center" value={form.plateNextThree} onChange={set('plateNextThree')} placeholder="345" maxLength={3} required />
            <span className="text-slate-400">─</span>
            <input className="input num w-16 text-center" value={form.plateIranCode} onChange={set('plateIranCode')} placeholder="67" maxLength={2} required />
          </div>
        </div>
        <div className="rounded-xl bg-emerald-50/60 p-3 text-center ring-1 ring-emerald-100">
          <Plate parts={{ firstTwo: form.plateFirstTwo, letter: form.plateLetter, nextThree: form.plateNextThree, iranCode: form.plateIranCode }} size="md" />
        </div>
        <div>
          <label className="label">راننده این خودرو</label>
          <select
            className="input"
            value={driverId}
            disabled={!driversLoaded}
            onChange={(e) => setDriverOverride(e.target.value)}
          >
            <option value="">بدون راننده</option>
            {drivers.map((d) => (
              <option key={d.id} value={d.id}>{d.displayName}</option>
            ))}
          </select>
          <p className="mt-1 text-[11px] text-slate-400">با انتخاب راننده، این خودرو به‌عنوان خودروی پیش‌فرض او ثبت می‌شود.</p>
        </div>
        {error && <div className="rounded-xl bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700 ring-1 ring-rose-100">{error}</div>}
        <button type="submit" disabled={busy} className="btn-primary w-full">
          {busy ? 'در حال ذخیره…' : 'ذخیره'}
        </button>
      </form>
    </Modal>
  )
}
