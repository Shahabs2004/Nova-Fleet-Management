import { useState } from 'react'
import { useInfiniteQuery, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { api } from '../lib/api'
import { faNumber, formatFaDate } from '../lib/date'
import { maintenancePath, pixelAsset, type MaintenanceVehicle, type Schedule, type ServiceRecord, type Template } from '../lib/maintenance'
import type { PagedResult } from '../lib/types'
import { MaintenanceDialog, Notice, StateBadge } from '../components/maintenance/MaintenanceControls'
import { MaintenanceRoad } from '../components/maintenance/MaintenanceRoad'
import { MaintenanceReports } from '../components/maintenance/MaintenanceReports'
import { OdometerPanel, RevisionHistory } from '../components/maintenance/OdometerPanel'
import { RecordEditor } from '../components/maintenance/RecordEditor'
import { RecordDetails } from '../components/maintenance/RecordDetails'
import { ScheduleEditor } from '../components/maintenance/ScheduleEditor'
import '../components/maintenance/maintenance.css'

const tabs = [['history', 'سوابق سرویس'], ['schedules', 'برنامه‌ها'], ['parts', 'تاریخچهٔ قطعات'], ['odometer', 'کیلومتر'], ['report', 'گزارش هزینه']]
const layouts = ['balanced', 'information', 'road'] as const
export function MaintenanceVehiclePage() {
  const { id } = useParams<{ id: string }>()
  return <VehicleWorkspace key={id} vehicleId={id!} />
}
function VehicleWorkspace({ vehicleId }: { vehicleId: string }) {
  const { can, user } = useAuth()
  const client = useQueryClient()
  const storageKey = `maintenance-layout:${user?.username ?? ''}`
  const [layout, setLayout] = useState<string>(() => { try { const v = localStorage.getItem(storageKey); return layouts.includes(v as typeof layouts[number]) ? v! : 'balanced' } catch { return 'balanced' } })
  const [tab, setTab] = useState('history')
  const [recordId, setRecordId] = useState<string | null>(null)
  const [recordEdit, setRecordEdit] = useState<ServiceRecord | 'new' | null>(null)
  const [schedule, setSchedule] = useState<Schedule | 'new' | null>(null)
  const [scheduleView, setScheduleView] = useState<Schedule | null>(null)
  const [showVoided, setShowVoided] = useState(false)
  const enabled = can('maintenance.view')
  const query = useQuery({ queryKey: ['maintenance', vehicleId, 'vehicle'], queryFn: () => api.get<MaintenanceVehicle>(maintenancePath(vehicleId)), enabled })
  const templates = useQuery({ queryKey: ['maintenance', 'templates'], queryFn: () => api.get<Template[]>('/api/maintenance/templates'), enabled })
  const history = useInfiniteQuery({ queryKey: ['maintenance', vehicleId, 'records'], initialPageParam: 1, queryFn: ({ pageParam }) => api.get<PagedResult<ServiceRecord>>(`${maintenancePath(vehicleId)}/records?page=${pageParam}&pageSize=50&includeVoided=true`), getNextPageParam: last => last.page * last.pageSize < last.totalCount ? last.page + 1 : undefined, enabled })
  const records = history.data?.pages.flatMap(p => p.items) ?? []
  const refresh = async () => { await client.invalidateQueries({ queryKey: ['maintenance'] }); await client.invalidateQueries({ queryKey: ['notifications'] }) }
  const vehicle = query.data
  const viewSchedule = (s: Schedule) => setScheduleView(s)
  const more = () => { void history.fetchNextPage() }
  if (!enabled) return <div className="maintenance"><Notice error>اجازهٔ مشاهدهٔ نگهداری خودرو را ندارید.</Notice></div>
  return <div className="maintenance" dir="rtl"><header className="m-header"><div><Link className="m-kicker" to="/maintenance">نگهداری ناوگان / خودرو</Link><h1>{vehicle?.displayName ?? 'دفترچهٔ خودرو'}</h1><p className="m-muted">{vehicle?.plate}</p></div><div className="m-toolbar"><Link className="m-button" to="/maintenance">همهٔ خودروها</Link>{can('maintenance.create') && vehicle && !vehicle.isArchived && <button className="m-button m-button-primary" disabled={!templates.data} onClick={() => setRecordEdit('new')}>ثبت سرویس جدید</button>}</div></header>
    {query.isLoading && <Notice>در حال دریافت اطلاعات خودرو…</Notice>}{query.error && <Notice error>{query.error.message}</Notice>}{templates.error && <Notice error>{templates.error.message}</Notice>}
    {vehicle && <>{vehicle.isArchived && <Notice>خودرو بایگانی شده است؛ ثبت جدید غیرفعال است و سوابق قابل مشاهده‌اند.</Notice>}
      <div className="m-stats"><div className="m-stat"><span>کیلومتر فعلی</span><strong>{faNumber(vehicle.currentKm)}</strong><small>{formatFaDate(vehicle.odometerAtUtc)}{vehicle.odometerTimeEstimated ? ' · زمان برآوردی' : ''}</small></div><div className="m-stat"><span>کل هزینهٔ نگهداری</span><strong>{faNumber(vehicle.totalCostToman)}</strong><small>تومان</small></div><div className="m-stat"><span>سررسید رسیده</span><strong>{faNumber(vehicle.dueCount)}</strong><small>برنامهٔ فعال</small></div><div className="m-stat"><span>نزدیک موعد</span><strong>{faNumber(vehicle.approachingCount)}</strong><small>نیازمند برنامه‌ریزی</small></div></div>
      <div className="m-split" data-layout={layout}><section className="m-info"><nav className="m-tabs" aria-label="بخش‌های دفترچه">{tabs.map(([key, label]) => <button key={key} className={tab === key ? 'm-active' : ''} aria-current={tab === key ? 'page' : undefined} onClick={() => setTab(key)}>{label}</button>)}</nav>
        {tab === 'history' && <section className="m-list"><div className="m-row"><h2>مراجعه‌ها و عملیات</h2><label className="m-check"><input type="checkbox" checked={showVoided} onChange={e => setShowVoided(e.target.checked)} />نمایش باطل‌شده‌ها</label></div>{history.isLoading && <Notice>در حال دریافت سوابق…</Notice>}{history.error && <Notice error>{history.error.message}</Notice>}{records.filter(r => showVoided || !r.isVoided).map(r => <button className="m-panel m-record" key={r.id} onClick={() => setRecordId(r.id)}><div className="m-row"><h3>{r.items.map(i => i.name).join('، ')}</h3><img className="m-icon" src={pixelAsset(r.items[0]?.assetKey ?? 'general-service')} alt="" /></div><p>{formatFaDate(r.performedAtUtc)} · {faNumber(r.odometerKm)} کیلومتر</p><div className="m-row"><strong>{faNumber(r.totalCostToman)} تومان</strong>{r.isVoided && <span className="m-state m-state-due">باطل‌شده</span>}</div></button>)}{!history.isLoading && records.length === 0 && <Notice>هنوز سرویسی ثبت نشده است. از «ثبت سرویس جدید» شروع کنید.</Notice>}{history.hasNextPage && <button className="m-button" disabled={history.isFetchingNextPage} onClick={more}>سوابق قدیمی‌تر</button>}</section>}
        {tab === 'schedules' && <section className="m-list"><div className="m-row"><h2>برنامه‌های دوره‌ای</h2>{can('maintenance.manage') && !vehicle.isArchived && <button className="m-button" disabled={!templates.data} onClick={() => setSchedule('new')}>افزودن برنامه</button>}</div>{vehicle.schedules.length === 0 && <Notice>مدیر نگهداری می‌تواند از الگوهای مشترک، برنامهٔ مختص این خودرو بسازد.</Notice>}{vehicle.schedules.map(s => <article className="m-panel m-list" key={s.id}><div className="m-row"><h3>{s.name}</h3><StateBadge state={s.state} /></div><p>موعد کیلومتر: {faNumber(s.dueKm)} · موعد تاریخ: {formatFaDate(s.dueAtUtc)}</p><small>آخرین انجام یا مبنا: {faNumber(s.lastKm)} کیلومتر · {formatFaDate(s.lastAtUtc)}</small><button className="m-button" onClick={() => viewSchedule(s)}>جزئیات و سابقه</button>{can('maintenance.manage') && <button className="m-button" onClick={() => setSchedule(s)}>ویرایش برنامه</button>}</article>)}</section>}
        {tab === 'parts' && <section className="m-list"><h2>تاریخچهٔ تعویض و مصرف قطعات</h2><Notice>این فهرست اقلام ثبت‌شده در مراجعه‌هاست؛ وضعیت قطعات قبل از اولین ثبت نامعلوم است و موجودی انبار یا محل چرخ‌ها را نشان نمی‌دهد.</Notice>{records.filter(r => !r.isVoided).flatMap(r => r.items.flatMap(i => i.parts.map((p, pi) => <button className="m-panel m-record" key={`${r.id}-${i.id}-${pi}`} onClick={() => setRecordId(r.id)}><h3>{p.name} · {faNumber(p.quantity)} عدد</h3><p>{p.specification}</p><small>{formatFaDate(r.performedAtUtc)} · {faNumber(r.odometerKm)} کیلومتر · {faNumber(p.unitCostToman)} تومان برای هر واحد</small><p>ضمانت تا: {p.warrantyUntil ? formatFaDate(`${p.warrantyUntil}T08:00:00Z`) : 'ثبت نشده'}</p></button>)))}{history.hasNextPage && <button className="m-button" disabled={history.isFetchingNextPage} onClick={more}>قطعات در سوابق قدیمی‌تر</button>}</section>}
        {tab === 'odometer' && <OdometerPanel vehicleId={vehicleId} archived={vehicle.isArchived} onSaved={refresh} onRecord={setRecordId} />}
        {tab === 'report' && <MaintenanceReports vehicleId={vehicleId} />}
      </section><button className="m-divider" aria-label="تغییر نسبت اطلاعات و جاده" onClick={() => { const next = layouts[(layouts.indexOf(layout as typeof layouts[number]) + 1) % layouts.length]; setLayout(next); try { localStorage.setItem(storageKey, next) } catch { /* Storage can be disabled by browser policy. */ } }}>{layout === 'balanced' ? '۵۰ / ۵۰' : layout === 'information' ? '۷۰ / ۳۰' : '۳۵ / ۶۵'}</button>
        <MaintenanceRoad vehicle={vehicle} records={records} onRecord={setRecordId} onSchedule={viewSchedule} hasMore={history.hasNextPage} loadingMore={history.isFetchingNextPage} onMore={more} />
      </div>
      {recordEdit && <RecordEditor vehicleId={vehicleId} record={recordEdit === 'new' ? undefined : recordEdit} templates={templates.data ?? []} schedules={vehicle.schedules} onClose={() => setRecordEdit(null)} onSaved={async r => { await refresh(); setRecordEdit(null); setRecordId(r.id) }} />}
      {recordId && !recordEdit && <RecordDetails vehicleId={vehicleId} id={recordId} onClose={() => setRecordId(null)} onEdit={r => { setRecordId(null); setRecordEdit(r) }} onSaved={refresh} />}
      {schedule && <ScheduleEditor vehicleId={vehicleId} templates={templates.data ?? []} schedule={schedule === 'new' ? undefined : schedule} onClose={() => setSchedule(null)} onSaved={refresh} />}
      {scheduleView && <MaintenanceDialog title={scheduleView.name} onClose={() => setScheduleView(null)}><div className="m-form"><StateBadge state={scheduleView.state} /><p>موعد: {faNumber(scheduleView.dueKm)} کیلومتر یا {formatFaDate(scheduleView.dueAtUtc)}</p><p>پیش‌هشدار: {faNumber(scheduleView.warningKm)} کیلومتر یا {faNumber(scheduleView.warningDays)} روز</p>{can('maintenance.manage') && <button className="m-button" onClick={() => { setSchedule(scheduleView); setScheduleView(null) }}>ویرایش برنامه</button>}<RevisionHistory vehicleId={vehicleId} entityId={scheduleView.id} /></div></MaintenanceDialog>}
    </>}
  </div>
}
