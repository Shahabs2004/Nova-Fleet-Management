import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useAuth } from '../../auth/useAuth'
import { api } from '../../lib/api'
import { faNumber, formatFaDate, formatFaDateTime } from '../../lib/date'
import { maintenancePath, type OdometerSummary, type Reading, type Revision } from '../../lib/maintenance'
import { DateField, Field, MaintenanceDialog, Notice } from './MaintenanceControls'

const sources: Record<string, string> = { manual: 'قرائت دستی', maintenance: 'سرویس', 'mission-start': 'شروع مأموریت', 'mission-end': 'پایان مأموریت' }
export function RevisionHistory({ vehicleId, entityId }: { vehicleId: string; entityId: string }) {
  const { user } = useAuth()
  const query = useQuery({ queryKey: ['maintenance', user?.username, vehicleId, 'revisions', entityId], queryFn: () => api.get<Revision[]>(`${maintenancePath(vehicleId)}/revisions/${entityId}`) })
  return <section className="m-list"><h3>سابقهٔ اصلاحات</h3>{query.isLoading && <Notice>در حال دریافت…</Notice>}{query.error && <Notice error>{query.error.message}</Notice>}
    {query.data?.length === 0 && <Notice>اصلاحی ثبت نشده است.</Notice>}{query.data?.map(r => <details className="m-panel" key={r.id}><summary>نسخهٔ {faNumber(r.version)} — {r.reason}</summary><p>{r.actor} · {formatFaDateTime(r.createdAtUtc)}</p><pre className="m-revision" dir="ltr">{JSON.stringify(JSON.parse(r.beforeJson), null, 2)}</pre></details>)}
  </section>
}
function ReadingEditor({ vehicleId, reading, onClose, onSaved }: { vehicleId: string; reading?: Reading; onClose: () => void; onSaved: () => Promise<void> }) {
  const [requestId] = useState(() => crypto.randomUUID())
  const [date, setDate] = useState<string | null>(() => reading?.observedAtUtc ?? new Date().toISOString())
  const [km, setKm] = useState(reading?.kilometers.toString() ?? '')
  const [reason, setReason] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  return <MaintenanceDialog title={reading ? 'اصلاح قرائت کیلومتر' : 'ثبت کیلومتر فعلی'} onClose={onClose}>
    <form className="m-form" onSubmit={async e => {
      e.preventDefault(); setError(''); if (!date) { setError('تاریخ قرائت الزامی است.'); return }
      setBusy(true)
      try {
        const body = { requestId, observedAtUtc: date, kilometers: Number(km), version: reading?.version ?? 0, reason: reading ? reason : null }
        const path = `${maintenancePath(vehicleId)}/odometer`
        if (reading) await api.put(`${path}/${reading.id}`, body); else await api.post(path, body)
        await onSaved(); onClose()
      } catch (err) { setError(err instanceof Error ? err.message : 'ذخیره ناموفق بود.') } finally { setBusy(false) }
    }}>
      <DateField label="تاریخ قرائت" value={date} onChange={setDate} withTime={false} disabled={!!reading?.sourceKind.startsWith('mission-')} />
      <Field label="کیلومتر شمارنده"><input required type="number" min={0} max={999999999} value={km} onChange={e => setKm(e.target.value)} /></Field>
      {reading && <Field label="دلیل اصلاح"><textarea required maxLength={500} value={reason} onChange={e => setReason(e.target.value)} /></Field>}
      <Notice>کیلومتر با تمام سوابق قبل و بعد کنترل می‌شود. تعویض یا صفرشدن شمارنده در این نسخه پشتیبانی نمی‌شود.</Notice>
      {error && <Notice error>{error}</Notice>}
      <button disabled={busy} className="m-button m-button-primary">{busy ? 'در حال ذخیره…' : 'ثبت کیلومتر'}</button>
    </form>
  </MaintenanceDialog>
}
export function OdometerPanel({ vehicleId, archived, onSaved, onRecord }: { vehicleId: string; archived: boolean; onSaved: () => Promise<void>; onRecord: (id: string) => void }) {
  const { can, user } = useAuth()
  const [edit, setEdit] = useState<Reading | 'new' | null>(null)
  const [history, setHistory] = useState<string | null>(null)
  const query = useQuery({ queryKey: ['maintenance', user?.username, vehicleId, 'odometer'], queryFn: () => api.get<OdometerSummary>(`${maintenancePath(vehicleId)}/odometer`) })
  return <div className="m-list"><div className="m-row"><h2>دفترچهٔ مشترک کیلومتر</h2>{can('maintenance.odometer.create') && !archived && <button className="m-button" onClick={() => setEdit('new')}>ثبت کیلومتر</button>}</div>
    {query.isLoading && <Notice>در حال دریافت قرائت‌ها…</Notice>}{query.error && <Notice error>{query.error.message}</Notice>}
    {query.data?.hasConflicts && <Notice error>سوابق ناسازگار وجود دارد؛ محاسبات کیلومتری تا اصلاح مدیر معتبر نیستند.</Notice>}
    <div className="m-table-wrap"><table><thead><tr><th>زمان</th><th>کیلومتر</th><th>منبع</th><th>عملیات</th></tr></thead><tbody>{query.data?.readings.map(r => <tr key={r.id}>
      <td>{r.sourceKind === 'maintenance' ? formatFaDate(r.observedAtUtc) : formatFaDateTime(r.observedAtUtc)}{r.isTimeEstimated && <small> · زمان برآوردی</small>}</td><td>{faNumber(r.kilometers)}{r.hasConflict && <span className="m-state m-state-due">ناسازگار</span>}{r.isVoided && <small> · باطل‌شده</small>}</td><td>{sources[r.sourceKind] ?? r.sourceKind}</td>
      <td><div className="m-toolbar">{!r.isVoided && (r.sourceKind === 'maintenance' ? <button className="m-button" onClick={() => onRecord(r.sourceId)}>سرویس مرتبط</button> : can('maintenance.odometer.correct') && <button className="m-button" onClick={() => setEdit(r)}>اصلاح</button>)}<button className="m-button" onClick={() => setHistory(r.id)}>سابقه</button></div></td>
    </tr>)}</tbody></table></div>
    {query.data?.readings.length === 0 && <Notice>هنوز کیلومتری ثبت نشده است؛ مقدار نامعلوم، صفر در نظر گرفته نمی‌شود.</Notice>}
    {edit && <ReadingEditor vehicleId={vehicleId} reading={edit === 'new' ? undefined : edit} onClose={() => setEdit(null)} onSaved={onSaved} />}
    {history && <MaintenanceDialog title="تاریخچهٔ قرائت" onClose={() => setHistory(null)}><RevisionHistory vehicleId={vehicleId} entityId={history} /></MaintenanceDialog>}
  </div>
}
