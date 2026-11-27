import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { useAuth } from '../auth/useAuth'
import { api } from '../lib/api'
import { faNumber, formatFaDate } from '../lib/date'
import { pixelAsset, stateLabels, type MaintenanceVehicle, type Template } from '../lib/maintenance'
import type { PagedResult } from '../lib/types'
import { Notice, StateBadge } from '../components/maintenance/MaintenanceControls'
import { TemplateEditor } from '../components/maintenance/ScheduleEditor'
import '../components/maintenance/maintenance.css'

export function MaintenanceFleetPage() {
  const { can } = useAuth()
  const client = useQueryClient()
  const [search, setSearch] = useState('')
  const [state, setState] = useState('')
  const [page, setPage] = useState(1)
  const [tab, setTab] = useState('vehicles')
  const [edit, setEdit] = useState<Template | 'new' | null>(null)
  const params = new URLSearchParams({ search, state, page: String(page), pageSize: '12' })
  const query = useQuery({ queryKey: ['maintenance', 'fleet', params.toString()], queryFn: () => api.get<PagedResult<MaintenanceVehicle>>(`/api/maintenance/vehicles?${params}`), enabled: can('maintenance.view') })
  const templates = useQuery({ queryKey: ['maintenance', 'templates'], queryFn: () => api.get<Template[]>('/api/maintenance/templates'), enabled: can('maintenance.view') })
  if (!can('maintenance.view')) return <div className="maintenance"><Notice error>اجازهٔ مشاهدهٔ نگهداری خودرو را ندارید.</Notice></div>
  return <div className="maintenance" dir="rtl"><header className="m-header"><div><p className="m-kicker">ناوگان نوین / نگهداری</p><h1>سلامت ناوگان، سرویس به‌موقع</h1><p className="m-muted">موعدها، سابقهٔ تعمیرات و هزینهٔ هر خودرو در یک دفترچه</p></div><img className="m-pixel m-landscape" src={pixelAsset('dashboard-landscape', 'scenery')} width={256} height={96} alt="جادهٔ پیکسلی ناوگان" /></header>
    <nav className="m-tabs" aria-label="بخش‌های نگهداری"><button aria-current={tab === 'vehicles' ? 'page' : undefined} className={tab === 'vehicles' ? 'm-active' : ''} onClick={() => setTab('vehicles')}>خودروها و موعدها</button><button aria-current={tab === 'templates' ? 'page' : undefined} className={tab === 'templates' ? 'm-active' : ''} onClick={() => setTab('templates')}>الگوهای مشترک</button></nav>
    {tab === 'vehicles' ? <><div className="m-toolbar"><input aria-label="جست‌وجوی خودرو" placeholder="برند، مدل یا سه رقم پلاک" value={search} onChange={e => { setSearch(e.target.value); setPage(1) }} /><select aria-label="فیلتر وضعیت نگهداری" value={state} onChange={e => { setState(e.target.value); setPage(1) }}><option value="">همهٔ وضعیت‌ها</option>{['due', 'approaching', 'unknown', 'healthy'].map(s => <option key={s} value={s}>{stateLabels[s]}</option>)}</select></div>
      {query.isLoading && <Notice>در حال دریافت ناوگان…</Notice>}{query.error && <Notice error>{query.error.message}</Notice>}
      {query.data && <><p className="m-muted">{faNumber(query.data.totalCount)} خودرو مطابق فیلتر</p><div className="m-fleet-grid">{query.data.items.map(v => <article className="m-panel m-list" key={v.vehicleId}><div className="m-row"><h2>{v.displayName}</h2><img className="m-pixel" src={pixelAsset('vehicle-profile', 'vehicles')} width={128} height={64} alt="" /></div><p>{v.plate}</p><strong>{faNumber(v.currentKm)} کیلومتر</strong><small>آخرین سرویس: {formatFaDate(v.lastServiceAtUtc)}</small><div className="m-toolbar">{v.dueCount > 0 && <span className="m-state m-state-due">{faNumber(v.dueCount)} سررسید رسیده</span>}{v.approachingCount > 0 && <span className="m-state m-state-approaching">{faNumber(v.approachingCount)} نزدیک موعد</span>}{(v.hasOdometerConflicts || v.currentKm === null) && <StateBadge state="unknown" />}</div><span>کل هزینه: {faNumber(v.totalCostToman)} تومان</span><Link className="m-button m-button-primary" to={`/maintenance/vehicles/${v.vehicleId}`}>سرویس و نگهداری</Link></article>)}</div>
        {query.data.items.length === 0 && <Notice>خودرویی با این فیلتر پیدا نشد؛ فیلتر را تغییر دهید یا خودرو را در بخش خودروها ثبت کنید.</Notice>}
        <div className="m-pagination"><button className="m-button" disabled={page <= 1} onClick={() => setPage(p => p - 1)}>صفحهٔ قبل</button><span>صفحهٔ {faNumber(page)}</span><button className="m-button" disabled={page * query.data.pageSize >= query.data.totalCount} onClick={() => setPage(p => p + 1)}>صفحهٔ بعد</button></div>
      </>}
    </> : <section className="m-list"><div className="m-row"><h2>الگوهای قابل‌تنظیم برای هر خودرو</h2>{can('maintenance.manage') && <button className="m-button m-button-primary" onClick={() => setEdit('new')}>الگوی جدید</button>}</div>{templates.isLoading && <Notice>در حال دریافت الگوها…</Notice>}{templates.error && <Notice error>{templates.error.message}</Notice>}{templates.data?.length === 0 && <Notice>مدیر نگهداری ابتدا یک الگوی سرویس بسازد.</Notice>}{templates.data?.map(t => <article className="m-panel m-row" key={t.id}><img className="m-icon" src={pixelAsset(t.assetKey)} width={48} height={48} alt="" /><div><h3>{t.name}</h3><small>{t.category} · {t.intervalKm === null ? '' : `${faNumber(t.intervalKm)} کیلومتر`} {t.intervalMonths === null ? '' : `${faNumber(t.intervalMonths)} ماه شمسی`}</small></div>{!t.isActive && <StateBadge state="inactive" />}{can('maintenance.manage') && <button className="m-button" onClick={() => setEdit(t)}>ویرایش</button>}</article>)}</section>}
    {edit && <TemplateEditor template={edit === 'new' ? undefined : edit} onClose={() => setEdit(null)} onSaved={async () => { await client.invalidateQueries({ queryKey: ['maintenance'] }) }} />}
  </div>
}
