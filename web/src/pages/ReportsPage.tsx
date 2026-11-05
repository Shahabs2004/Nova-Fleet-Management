import { useMutation, useQuery } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { JalaliDateField, jalaliValueToUtc, type JalaliDateValue } from '../components/JalaliDateField'
import { EmptyState, PageHeader, Spinner } from '../components/ui'
import { useAuth } from '../auth/AuthContext'
import { api, downloadFile } from '../lib/api'
import { faNumber, formatFaDateTime, jalaliOf, toFa } from '../lib/date'
import { MISSION_STATUS, MISSION_TYPE } from '../lib/labels'
import type { DriverDto, ReportDefinition, ReportTable, VehicleDto } from '../lib/types'

interface ReportFilters {
  fromUtc: string | null
  toUtc: string | null
  driverId: string | null
  vehicleId: string | null
  type: number | null
  organizationalUnit: string | null
  status: number | null
}

const EMPTY_FILTERS: ReportFilters = {
  fromUtc: null,
  toUtc: null,
  driverId: null,
  vehicleId: null,
  type: null,
  organizationalUnit: null,
  status: null,
}

const DAY_MS = 86_400_000

function jalaliValue(offsetDays: number): JalaliDateValue {
  const [jy, jm, jd] = jalaliOf(new Date(Date.now() + offsetDays * DAY_MS))
  return { jy, jm, jd }
}

export function ReportsPage() {
  const { can } = useAuth()
  const [selected, setSelected] = useState<string | null>(null)
  const [from, setFrom] = useState<JalaliDateValue | null>(() => jalaliValue(-30))
  const [to, setTo] = useState<JalaliDateValue | null>(() => jalaliValue(0))
  const [driverId, setDriverId] = useState('')
  const [vehicleId, setVehicleId] = useState('')
  const [type, setType] = useState('')
  const [orgUnit, setOrgUnit] = useState('')
  const [status, setStatus] = useState('')
  const [exportError, setExportError] = useState<string | null>(null)

  const { data: definitions, isLoading } = useQuery({
    queryKey: ['reports'],
    queryFn: () => api.get<ReportDefinition[]>('/api/reports'),
  })
  const { data: drivers } = useQuery({ queryKey: ['driver-options'], queryFn: () => api.get<DriverDto[]>('/api/drivers/options') })
  const { data: vehicles } = useQuery({ queryKey: ['vehicle-options'], queryFn: () => api.get<VehicleDto[]>('/api/vehicles/options') })
  const { data: orgUnits } = useQuery({
    queryKey: ['organization-units'],
    queryFn: () => api.get<{ id: string; name: string; isActive: boolean }[]>('/api/organization-units'),
  })

  const filters = useMemo<ReportFilters>(
    () => ({
      ...EMPTY_FILTERS,
      fromUtc: jalaliValueToUtc(from, 0),
      toUtc: jalaliValueToUtc(to, 23),
      driverId: driverId || null,
      vehicleId: vehicleId || null,
      type: type ? Number(type) : null,
      organizationalUnit: orgUnit || null,
      status: status ? Number(status) : null,
    }),
    [from, to, driverId, vehicleId, type, orgUnit, status],
  )

  const run = useMutation({
    mutationFn: (key: string) => api.post<ReportTable>(`/api/reports/${key}/run`, filters),
  })

  const selectReport = (key: string) => {
    setSelected(key)
    setExportError(null)
    run.mutate(key)
  }

  const exportFile = async (format: 'excel' | 'pdf') => {
    if (!selected) return
    setExportError(null)
    try {
      await downloadFile(`/api/reports/${selected}/${format}`, filters, `${selected}.${format === 'excel' ? 'xlsx' : 'pdf'}`)
    } catch (error) {
      setExportError((error as Error).message)
    }
  }

  if (isLoading) return <Spinner />

  return (
    <div>
      <PageHeader
        title="گزارش‌ها"
        subtitle="هشت گزارش تحلیلی ناوگان با خروجی Excel و PDF"
        actions={
          selected && can('reports.export') ? (
            <>
              <button className="btn-gold btn-sm" onClick={() => exportFile('excel')}>
                خروجی Excel
              </button>
              <button className="btn-ghost btn-sm" onClick={() => exportFile('pdf')}>
                خروجی PDF
              </button>
            </>
          ) : undefined
        }
      />

      {exportError && <div className="mb-4 rounded-xl bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700 ring-1 ring-rose-100">{exportError}</div>}

      <div className="mb-5 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        {(definitions ?? []).map((definition) => (
          <button
            key={definition.key}
            onClick={() => selectReport(definition.key)}
            className={`card p-4 text-right transition-all hover:-translate-y-0.5 hover:shadow-md ${
              selected === definition.key ? 'ring-2 ring-emerald-700/70' : ''
            }`}
          >
            <p className="text-sm font-bold text-emerald-950">{definition.title}</p>
            <p className="mt-1 text-xs leading-5 text-slate-500">{definition.description}</p>
          </button>
        ))}
      </div>

      <div className="card mb-5 p-4">
        <p className="mb-3 text-xs font-semibold text-slate-500">فیلترهای گزارش</p>
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
          <div>
            <label className="label">از تاریخ</label>
            <JalaliDateField value={from} onChange={setFrom} startYear={new Date().getFullYear() - 621} yearsBack={4} />
          </div>
          <div>
            <label className="label">تا تاریخ</label>
            <JalaliDateField value={to} onChange={setTo} startYear={new Date().getFullYear() - 621} yearsBack={4} />
          </div>
          <div>
            <label className="label">راننده</label>
            <select className="input" value={driverId} onChange={(e) => setDriverId(e.target.value)}>
              <option value="">همه</option>
              {(drivers ?? []).map((item) => (
                <option key={item.id} value={item.id}>
                  {item.displayName}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label className="label">خودرو</label>
            <select className="input" value={vehicleId} onChange={(e) => setVehicleId(e.target.value)}>
              <option value="">همه</option>
              {(vehicles ?? []).map((item) => (
                <option key={item.id} value={item.id}>
                  {item.displayName}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label className="label">نوع مأموریت</label>
            <select className="input" value={type} onChange={(e) => setType(e.target.value)}>
              <option value="">همه</option>
              <option value="1">{MISSION_TYPE[1]}</option>
              <option value="2">{MISSION_TYPE[2]}</option>
            </select>
          </div>
          <div>
            <label className="label">وضعیت</label>
            <select className="input" value={status} onChange={(e) => setStatus(e.target.value)}>
              <option value="">همه</option>
              {Object.entries(MISSION_STATUS).map(([value, meta]) => (
                <option key={value} value={value}>
                  {meta.title}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label className="label">واحد سازمانی</label>
            <select className="input" value={orgUnit} onChange={(e) => setOrgUnit(e.target.value)}>
              <option value="">همه</option>
              {(orgUnits ?? []).map((item) => (
                <option key={item.id} value={item.name}>
                  {item.name}
                </option>
              ))}
            </select>
          </div>
          <div className="flex items-end">
            <button className="btn-primary w-full" disabled={!selected || run.isPending} onClick={() => selected && run.mutate(selected)}>
              {run.isPending ? 'در حال اجرا…' : 'اجرای گزارش'}
            </button>
          </div>
        </div>
      </div>

      {!selected && (
        <div className="card">
          <EmptyState title="یک گزارش را انتخاب کنید" hint="برای مشاهده جدول، یکی از کارت‌های بالا را انتخاب و سپس گزارش را اجرا کنید." />
        </div>
      )}
      {selected && run.isPending && <Spinner label="در حال تهیه گزارش…" />}
      {selected && run.isError && (
        <div className="card p-6 text-sm font-medium text-rose-700">{(run.error as Error)?.message ?? 'خطا در اجرای گزارش.'}</div>
      )}
      {selected && run.data && <ReportTableCard table={run.data} />}
    </div>
  )
}

function ReportTableCard({ table }: { table: ReportTable }) {
  const numericKeys = new Set(table.columns.filter((column) => column.isNumeric).map((column) => column.key))
  const totalEntries = Object.entries(table.totals ?? {})

  return (
    <div className="card overflow-hidden">
      <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-100 px-5 py-4">
        <div>
          <h2 className="text-sm font-bold text-emerald-950">{table.title}</h2>
          {table.subtitle && <p className="mt-0.5 text-xs text-slate-500">{table.subtitle}</p>}
        </div>
        <div className="text-left text-[11px] text-slate-400">
          <p>تولید: {formatFaDateTime(table.generatedAtUtc)}</p>
          <p className="num mt-0.5">{toFa(table.rows.length)} سطر</p>
        </div>
      </div>
      <div className="overflow-x-auto">
        <table className="w-full min-w-[640px] text-sm">
          <thead>
            <tr className="table-head">
              {table.columns.map((column) => (
                <th key={column.key} className={column.isNumeric ? 'text-left' : 'text-right'}>
                  {column.title}
                </th>
              ))}
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-50">
            {table.rows.length === 0 && (
              <tr>
                <td colSpan={table.columns.length} className="px-5 py-10 text-center text-sm text-slate-400">
                  داده‌ای برای این گزارش یافت نشد.
                </td>
              </tr>
            )}
            {table.rows.map((row, index) => (
              <tr key={index} className="hover:bg-slate-50/70">
                {table.columns.map((column, cellIndex) => {
                  const value = row[cellIndex]
                  const numeric = numericKeys.has(column.key)
                  return (
                    <td key={column.key} className={numeric ? 'num text-left font-medium text-emerald-950' : 'text-slate-600'}>
                      {value === null || value === '' ? '—' : typeof value === 'number' ? faNumber(value) : String(value)}
                    </td>
                  )
                })}
              </tr>
            ))}
          </tbody>
          {totalEntries.length > 0 && (
            <tfoot>
              <tr className="bg-emerald-50/60 font-bold text-emerald-950">
                {table.columns.map((column, index) => (
                  <td key={column.key} className={column.isNumeric ? 'num text-left' : 'text-right'}>
                    {index === 0 ? 'جمع کل' : totalEntries.find(([key]) => key === column.key)?.[1] ?? ''}
                  </td>
                ))}
              </tr>
            </tfoot>
          )}
        </table>
      </div>
    </div>
  )
}