import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useAuth } from '../../auth/AuthContext'
import { api } from '../../lib/api'
import { faNumber, formatFaDate } from '../../lib/date'
import { maintenancePath, pixelAsset, type ServiceRecord } from '../../lib/maintenance'
import { Field, MaintenanceDialog, Notice } from './MaintenanceControls'
import { RevisionHistory } from './OdometerPanel'

export function RecordDetails({ vehicleId, id, onClose, onEdit, onSaved }: { vehicleId: string; id: string; onClose: () => void; onEdit: (r: ServiceRecord) => void; onSaved: () => Promise<void> }) {
  const { can } = useAuth()
  const query = useQuery({ queryKey: ['maintenance', vehicleId, 'record', id], queryFn: () => api.get<ServiceRecord>(`${maintenancePath(vehicleId)}/records/${id}`) })
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [voiding, setVoiding] = useState(false)
  const [reason, setReason] = useState('')
  const r = query.data
  return <MaintenanceDialog title="جزئیات سرویس" onClose={onClose}>
    {query.isLoading && <Notice>در حال دریافت سرویس…</Notice>}{query.error && <Notice error>{query.error.message}</Notice>}
    {r && <div className="m-form"><div className="m-row"><strong>{formatFaDate(r.performedAtUtc)}</strong><span>{faNumber(r.odometerKm)} کیلومتر</span>{r.isVoided && <span className="m-state m-state-due">باطل‌شده</span>}</div>
      <p>تعمیرگاه: {r.workshop || 'ثبت نشده'} · ثبت‌کننده: {r.createdBy}</p>
      {r.items.map((item, i) => <section className="m-panel" key={item.id ?? i}><div className="m-row"><h3>{item.name}</h3><img className="m-pixel" src={pixelAsset(item.assetKey ?? 'general-service')} width={48} height={48} alt="" /></div>
        <p>اجرت: {faNumber(item.laborCostToman)} تومان</p>
        <div className="m-table-wrap"><table><thead><tr><th>قطعه / مشخصات</th><th>تعداد</th><th>قیمت واحد</th><th>ضمانت تا</th></tr></thead><tbody>{item.parts.map((p, pi) => <tr key={p.id ?? pi}><td>{p.name}<small className="block">{p.specification}</small></td><td>{faNumber(p.quantity)}</td><td>{faNumber(p.unitCostToman)} تومان</td><td>{p.warrantyUntil ? formatFaDate(`${p.warrantyUntil}T08:00:00Z`) : '—'}</td></tr>)}</tbody></table></div>
      </section>)}
      <p>هزینهٔ جانبی: {faNumber(r.otherCostToman)} تومان</p><strong>جمع کل: {faNumber(r.totalCostToman)} تومان</strong>{r.notes && <p>{r.notes}</p>}
      <section className="m-list"><h3>فاکتورها و پیوست‌های محافظت‌شده</h3>{r.attachments.length === 0 && <small>پیوستی ثبت نشده است.</small>}{r.attachments.map(a => <a className="m-button" href={`${maintenancePath(vehicleId)}/records/${id}/attachments/${a.id}`} key={a.id} download>{a.fileName} · {faNumber(Math.ceil(a.size / 1024))} کیلوبایت</a>)}
        {can('maintenance.attach') && !r.isVoided && <Field label="افزودن فاکتور" hint="فقط PDF، PNG یا JPEG؛ حداکثر ۵ مگابایت، تا ۲۰ فایل."><input type="file" accept=".pdf,.png,.jpg,.jpeg" disabled={busy || r.attachments.length >= 20} onChange={async e => {
          const file = e.target.files?.[0]; e.target.value = ''; if (!file) return
          if (file.size > 5242880) { setError('حجم فایل بیشتر از ۵ مگابایت است.'); return }
          setBusy(true); setError('')
          try { await api.upload(`${maintenancePath(vehicleId)}/records/${id}/attachments`, file); await onSaved() } catch (err) { setError(err instanceof Error ? err.message : 'بارگذاری ناموفق بود.') } finally { setBusy(false) }
        }} /></Field>}
      </section>
      {can('maintenance.correct') && !r.isVoided && <div className="m-toolbar"><button className="m-button" disabled={busy} onClick={() => onEdit(r)}>اصلاح با حفظ سابقه</button><button className="m-button m-button-danger" disabled={busy || (r.odometerKm != null && !can('maintenance.odometer.correct'))} title={r.odometerKm != null && !can('maintenance.odometer.correct') ? 'ابطال این قرائت به مجوز اصلاح کیلومتر تاریخی نیاز دارد.' : undefined} onClick={() => setVoiding(!voiding)}>باطل‌کردن سرویس</button></div>}
      {voiding && <form className="m-form" onSubmit={async e => {
        e.preventDefault(); setError(''); setBusy(true)
        try { await api.post(`${maintenancePath(vehicleId)}/records/${id}/void`, { version: r.version, reason }); await onSaved(); setVoiding(false) } catch (err) { setError(err instanceof Error ? err.message : 'ابطال ناموفق بود.') } finally { setBusy(false) }
      }}><Notice>سابقه حذف نمی‌شود؛ هزینه و تکمیل برنامه از محاسبات کنار گذاشته می‌شوند.</Notice><Field label="دلیل ابطال"><textarea required maxLength={500} value={reason} onChange={e => setReason(e.target.value)} /></Field><button className="m-button m-button-danger" disabled={busy}>تأیید ابطال</button></form>}
      {busy && <Notice>در حال ذخیره…</Notice>}{error && <Notice error>{error}</Notice>}
      <RevisionHistory vehicleId={vehicleId} entityId={id} />
    </div>}
  </MaintenanceDialog>
}
