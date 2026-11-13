import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { Area, AreaChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { useAuth } from '../auth/AuthContext'
import { api } from '../lib/api'
import { faNumber, formatFaDateTime, toFa } from '../lib/date'
import type { DashboardStats } from '../lib/types'
import { PageHeader, Spinner, StatCard, StatusBadge } from '../components/ui'
import { Plate } from '../components/Plate'

const vehicleColors = ['#0F766E', '#6366F1', '#F59E0B', '#94A3B8']

export function DashboardPage() {
  const { can } = useAuth()
  const { data, isLoading } = useQuery({ queryKey: ['dashboard'], queryFn: () => api.get<DashboardStats>('/api/dashboard') })

  if (isLoading) return <Spinner />
  if (!data) return null

  return (
    <div>
      <PageHeader title="داشبورد" subtitle="نمای کلی عملیات ناوگان در یک نگاه" />

      <div className="grid grid-cols-2 gap-4 xl:grid-cols-4">
        <StatCard
          title="در انتظار بررسی"
          value={faNumber(data.pendingRequests)}
          hint="درخواست مأموریت بررسی‌نشده"
          tone="gold"
          icon={
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8">
              <circle cx="12" cy="12" r="9" />
              <path d="M12 7v5l3 3" />
            </svg>
          }
        />
        <StatCard
          title="مأموریت فعال"
          value={faNumber(data.activeMissions)}
          hint="تأییدشده یا در حال اجرا"
          tone="emerald"
          icon={
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8">
              <path d="M5 17h14M7 17a2 2 0 1 1-4 0 2 2 0 0 1 4 0Zm14 0a2 2 0 1 1-4 0 2 2 0 0 1 4 0ZM3 13l1.5-5A2 2 0 0 1 6.4 6.5h11.2a2 2 0 0 1 1.9 1.5L21 13v4H3v-4Z" />
            </svg>
          }
        />
        {can('vehicles.view') && (
          <StatCard
            title="خودرو آماده"
            value={faNumber(data.readyVehicles)}
            hint="آماده تخصیص مأموریت"
            tone="sky"
            icon={
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8">
                <path d="M7 17a2 2 0 1 1-4 0 2 2 0 0 1 4 0Zm14 0a2 2 0 1 1-4 0 2 2 0 0 1 4 0ZM5 17h10M3 13l1.5-5A2 2 0 0 1 6.4 6.5h11.2a2 2 0 0 1 1.9 1.5L21 13" />
              </svg>
            }
          />
        )}
        {can('drivers.view') && (
          <StatCard
            title="راننده فعال"
            value={faNumber(data.activeDrivers)}
            hint="در وضعیت همکاری فعال"
            tone="indigo"
            icon={
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8">
                <circle cx="12" cy="8" r="4" />
                <path d="M4 21c0-4 3.6-6 8-6s8 2 8 6" />
              </svg>
            }
          />
        )}
      </div>

      <div className="mt-5 grid gap-5 lg:grid-cols-3">
        <div className="card p-5 lg:col-span-2">
          <div className="mb-4 flex items-center justify-between">
            <h2 className="text-sm font-bold text-emerald-950">مأموریت‌های دو هفته گذشته و آینده</h2>
          </div>
          <ResponsiveContainer width="100%" height={220}>
            <AreaChart data={data.missionsByDay} margin={{ top: 4, right: 8, left: 8, bottom: 0 }}>
              <defs>
                <linearGradient id="missionGradient" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0%" stopColor="#14B8A6" stopOpacity={0.35} />
                  <stop offset="100%" stopColor="#14B8A6" stopOpacity={0.02} />
                </linearGradient>
              </defs>
              <CartesianGrid strokeDasharray="3 3" stroke="#E2E8F0" vertical={false} />
              <XAxis dataKey="label" tickFormatter={(v: string) => toFa(v)} tick={{ fontSize: 10, fill: '#94A3B8' }} reversed axisLine={false} tickLine={false} />
              <YAxis orientation="right" tickFormatter={(v: number) => toFa(v)} tick={{ fontSize: 10, fill: '#94A3B8' }} axisLine={false} tickLine={false} allowDecimals={false} width={34} />
              <Tooltip
                contentStyle={{ direction: 'rtl', borderRadius: 12, border: '1px solid #E2E8F0', fontSize: 12 }}
                labelStyle={{ fontWeight: 700 }}
              />
              <Area type="monotone" dataKey="value" name="مأموریت" stroke="#0F766E" strokeWidth={2.5} fill="url(#missionGradient)" />
            </AreaChart>
          </ResponsiveContainer>
        </div>

        {can('vehicles.view') && (
          <div className="card p-5">
            <h2 className="mb-4 text-sm font-bold text-emerald-950">وضعیت خودروها</h2>
            <ul className="flex min-h-[220px] flex-col justify-center gap-3.5">
              {data.vehicleStatuses.map((item, index) => {
                const total = data.vehicleStatuses.reduce((sum, point) => sum + point.value, 0)
                const share = total === 0 ? 0 : Math.round((item.value / total) * 100)
                return (
                  <li key={item.label}>
                    <div className="flex items-baseline justify-between gap-2">
                      <span className="text-[13px] font-semibold text-slate-600">{item.label}</span>
                      <span className="num text-[13px] font-bold text-emerald-950">
                        {toFa(item.value)}
                        <span className="mr-1 text-[11px] font-normal text-slate-400">{toFa(share)}٪</span>
                      </span>
                    </div>
                    <div className="mt-1.5 h-2.5 overflow-hidden rounded-full bg-slate-100">
                      <div
                        className="h-full rounded-full transition-[width] duration-500"
                        style={{ width: `${share}%`, backgroundColor: vehicleColors[index % vehicleColors.length] }}
                      />
                    </div>
                  </li>
                )
              })}
            </ul>
          </div>
        )}
      </div>

      <div className="mt-5 grid gap-5 lg:grid-cols-3">
        <div className="card lg:col-span-2">
          <div className="flex items-center justify-between border-b border-slate-100 px-5 py-4">
            <h2 className="text-sm font-bold text-emerald-950">مأموریت‌های پیش‌رو</h2>
            <Link to="/missions" className="text-xs font-medium text-emerald-700 hover:underline">
              مشاهده همه
            </Link>
          </div>
          <div className="divide-y divide-slate-50">
            {data.upcomingMissions.length === 0 && <p className="px-5 py-8 text-center text-sm text-slate-400">مأموریت فعالی وجود ندارد.</p>}
            {data.upcomingMissions.map((m) => (
              <Link key={m.id} to={`/missions/${m.id}`} className="flex items-center justify-between gap-3 px-5 py-3.5 transition-colors hover:bg-emerald-50/40">
                <div className="min-w-0">
                  <p className="truncate text-sm font-semibold text-slate-700">{m.title}</p>
                  <p className="mt-0.5 text-xs text-slate-400">
                    <span className="num">{toFa(m.missionNumber)}</span> — {m.driverName ?? 'بدون راننده'} — {m.vehiclePlate ? <Plate value={m.vehiclePlate} size="sm" className="inline-block align-middle" /> : 'بدون خودرو'}
                  </p>
                </div>
                <div className="flex shrink-0 flex-col items-end gap-1.5">
                  <StatusBadge status={m.status} />
                  <span className="text-[11px] text-slate-400">{formatFaDateTime(m.plannedStartAtUtc)}</span>
                </div>
              </Link>
            ))}
          </div>
        </div>

        {can('drivers.view') && data.driverWorkload.length > 0 && (
          <div className="card p-5">
            <h2 className="mb-4 text-sm font-bold text-emerald-950">بار کاری رانندگان</h2>
            <div className="space-y-3">
              {data.driverWorkload.map((driver) => {
                const max = Math.max(...data.driverWorkload.map((d) => d.value), 1)
                return (
                  <div key={driver.name}>
                    <div className="mb-1 flex items-center justify-between text-xs">
                      <span className="font-medium text-slate-600">{driver.name}</span>
                      <span className="num text-slate-400">{toFa(driver.value)} مأموریت</span>
                    </div>
                    <div className="h-2 overflow-hidden rounded-full bg-slate-100">
                      <div className="h-full rounded-full bg-gradient-to-l from-emerald-600 to-gold-500" style={{ width: `${(driver.value / max) * 100}%` }} />
                    </div>
                  </div>
                )
              })}
            </div>
            <div className="mt-5 grid grid-cols-2 gap-3 border-t border-slate-100 pt-4 text-center">
              <div>
                <p className="text-[11px] text-slate-400">مأموریت این ماه</p>
                <p className="num mt-1 text-lg font-extrabold text-emerald-900">{faNumber(data.missionsThisMonth)}</p>
              </div>
              <div>
                <p className="text-[11px] text-slate-400">مسافت این ماه</p>
                <p className="num mt-1 text-lg font-extrabold text-emerald-900">{faNumber(data.distanceThisMonthKm)} <span className="text-xs font-normal">کیلومتر</span></p>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
