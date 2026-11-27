import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api } from '../lib/api'
import { formatFaDate } from '../lib/date'
import { DRIVER_STATUS } from '../lib/labels'
import type { DriverCompositeCreateRequest, DriverCompositeUpdateRequest, DriverDto, DriverListItem, PagedResult, VehicleOptionDto } from '../lib/types'
import { EmptyState, Modal, PageHeader, Pagination, Spinner } from '../components/ui'
import { Plate } from '../components/Plate'
import { Avatar } from '../components/Avatar'
import { AvatarEditor } from '../components/AvatarEditor'

export function DriversPage() {
  const queryClient = useQueryClient()
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [includeArchived, setIncludeArchived] = useState(false)
  const [edit, setEdit] = useState<DriverListItem | 'new' | null>(null)

  const params = new URLSearchParams({ page: String(page), pageSize: '12' })
  if (search.trim()) params.set('search', search.trim())
  if (includeArchived) params.set('includeArchived', 'true')

  const { data, isLoading } = useQuery({
    queryKey: ['drivers', params.toString()],
    queryFn: () => api.get<PagedResult<DriverListItem>>(`/api/drivers?${params}`),
  })

  const vehicles = useQuery({ queryKey: ['vehicle-options'], queryFn: () => api.get<VehicleOptionDto[]>('/api/vehicles/options') })

  const mutation = useMutation({
    mutationFn: ({ path, body }: { path: string; body?: unknown }) =>
      body === undefined ? api.post(path) : api.post(path, body),
    onSuccess: async () => {
      setEdit(null)
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['drivers'] }),
        queryClient.invalidateQueries({ queryKey: ['driver-options'] }),
        queryClient.invalidateQueries({ queryKey: ['timeline'] }),
      ])
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
                        <div className="flex items-center gap-2">
                          <Avatar name={d.displayName} />
                          <span>{d.displayName}</span>
                          {d.isArchived && <span className="badge mr-2 bg-slate-100 text-slate-500">بایگانی</span>}
                        </div>
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
                      <td className="px-4 py-3.5">{d.defaultVehiclePlate ? <Plate value={d.defaultVehiclePlate} size="sm" /> : '—'}</td>
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
        key={edit === 'new' ? 'new' : edit?.id ?? 'closed'}
        driver={edit}
        vehicles={vehicles.data ?? []}
        onClose={() => setEdit(null)}
        onSave={async (body: DriverCompositeCreateRequest | DriverCompositeUpdateRequest) => {
          if (edit === 'new') {
            await api.post<DriverDto>('/api/drivers/composite', body)
          } else {
            await api.put<DriverDto>(`/api/drivers/${edit!.id}/composite`, body)
          }
          setEdit(null)
          await Promise.all([
            queryClient.invalidateQueries({ queryKey: ['drivers'] }),
            queryClient.invalidateQueries({ queryKey: ['driver-options'] }),
            queryClient.invalidateQueries({ queryKey: ['vehicles'] }),
            queryClient.invalidateQueries({ queryKey: ['vehicle-options'] }),
            queryClient.invalidateQueries({ queryKey: ['timeline'] }),
          ])
        }}
      />
    </div>
  )
}

function DriverEditModal({
  driver,
  vehicles,
  onClose,
  onSave,
}: {
  driver: DriverListItem | 'new' | null
  vehicles: VehicleOptionDto[]
  onClose: () => void
  onSave: (body: DriverCompositeCreateRequest | DriverCompositeUpdateRequest) => Promise<void>
}) {
  const detail = useQuery({
    queryKey: ['drivers', 'details', driver === 'new' ? 'new' : driver?.id],
    queryFn: () => api.get<DriverDto>(`/api/drivers/${driver === 'new' ? '' : driver?.id}`),
    enabled: driver !== null && driver !== 'new',
  })
  if (driver === null) return null

  const isNew = driver === 'new'
  return (
    <Modal open title={isNew ? 'افزودن راننده' : `ویرایش ${driver.displayName}`} onClose={onClose}>
      {!isNew && detail.isLoading ? <Spinner /> : !isNew && (detail.isError || !detail.data) ? (
        <div className="rounded-xl bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700 ring-1 ring-rose-100">
          جزئیات راننده بارگذاری نشد. پنجره را ببندید و دوباره تلاش کنید.
        </div>
      ) : (
        <DriverEditForm
          key={isNew ? 'new' : driver.id}
          driver={isNew ? null : detail.data!}
          vehicles={vehicles}
          onSave={onSave}
        />
      )}
    </Modal>
  )
}

function DriverEditForm({
  driver,
  vehicles,
  onSave,
}: {
  driver: DriverDto | null
  vehicles: VehicleOptionDto[]
  onSave: (body: DriverCompositeCreateRequest | DriverCompositeUpdateRequest) => Promise<void>
}) {
  const [firstName, setFirstName] = useState(driver?.firstName ?? '')
  const [lastName, setLastName] = useState(driver?.lastName ?? '')
  const [mobile, setMobile] = useState(driver?.mobilePhone ?? '')
  const [avatarDataUrl, setAvatarDataUrl] = useState(driver?.avatarDataUrl ?? null)
  const [cropping, setCropping] = useState(false)
  const [defaultVehicleId, setDefaultVehicleId] = useState(driver?.defaultVehicleId ?? '')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  return (
    <form
      className="space-y-4"
      onSubmit={async (e) => {
        e.preventDefault()
        if (cropping || busy) return
        setBusy(true)
        setError(null)
        try {
          const profile = { firstName, lastName, mobilePhone: mobile, avatarDataUrl }
          await onSave({
            profile: driver ? { ...profile, rowVersion: driver.rowVersion } : profile,
            defaultVehicleId: defaultVehicleId === '' ? null : defaultVehicleId,
          })
        } catch (err) {
          setError(err instanceof Error ? err.message : 'ذخیره ناموفق بود.')
        } finally {
          setBusy(false)
        }
      }}
    >
      <AvatarEditor name={`${firstName} ${lastName}`} value={avatarDataUrl} onChange={setAvatarDataUrl} onEditingChange={setCropping} disabled={busy} />
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
      <div>
        <label className="label">خودروی پیش‌فرض</label>
        <select className="input" value={defaultVehicleId} onChange={(e) => setDefaultVehicleId(e.target.value)}>
          <option value="">بدون خودرو</option>
          {driver?.defaultVehicleId && !vehicles.some((v) => v.id === driver.defaultVehicleId) && (
            <option value={driver.defaultVehicleId}>{driver.defaultVehiclePlate ?? 'خودروی بایگانی‌شده'}</option>
          )}
          {vehicles.map((v) => (
            <option key={v.id} value={v.id}>
              {v.displayName}
            </option>
          ))}
        </select>
      </div>
      {error && <div className="rounded-xl bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700 ring-1 ring-rose-100">{error}</div>}
      <button type="submit" disabled={busy || cropping} className="btn-primary w-full">
        {busy ? 'در حال ذخیره…' : 'ذخیره'}
      </button>
    </form>
  )
}
