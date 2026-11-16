import { useState } from 'react'
import { api } from '../../lib/api'
import { faNumber } from '../../lib/date'
import { maintenancePath, type Part, type Schedule, type ServiceItem, type ServiceRecord, type Template } from '../../lib/maintenance'
import { DateField, Field, MaintenanceDialog, Notice } from './MaintenanceControls'

const emptyItem = (): ServiceItem => ({ templateId: '', scheduleId: null, laborCostToman: 0, parts: [] })
export function RecordEditor({ vehicleId, record, templates, schedules, onClose, onSaved }: { vehicleId: string; record?: ServiceRecord; templates: Template[]; schedules: Schedule[]; onClose: () => void; onSaved: (record: ServiceRecord) => Promise<void> }) {
  const [requestId] = useState(() => crypto.randomUUID())
  const [date, setDate] = useState<string | null>(record?.performedAtUtc ?? new Date().toISOString())
  const [km, setKm] = useState(record?.odometerKm?.toString() ?? '')
  const [workshop, setWorkshop] = useState(record?.workshop ?? '')
  const [notes, setNotes] = useState(record?.notes ?? '')
  const [other, setOther] = useState(record?.otherCostToman ?? 0)
  const [items, setItems] = useState<ServiceItem[]>(record?.items.map(i => ({ ...i, parts: i.parts.map(p => ({ ...p })) })) ?? [emptyItem()])
  const [reason, setReason] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const patchItem = (index: number, patch: Partial<ServiceItem>) => setItems(all => all.map((item, i) => i === index ? { ...item, ...patch } : item))
  const patchPart = (itemIndex: number, partIndex: number, patch: Partial<Part>) => patchItem(itemIndex, { parts: items[itemIndex].parts.map((p, i) => i === partIndex ? { ...p, ...patch } : p) })
  const total = other + items.reduce((sum, i) => sum + i.laborCostToman + i.parts.reduce((s, p) => s + p.quantity * p.unitCostToman, 0), 0)
  return <MaintenanceDialog title={record ? 'اصلاح سابقهٔ سرویس' : 'ثبت سرویس جدید'} onClose={onClose}>
    <form className="m-form" onSubmit={async e => {
      e.preventDefault(); setError('')
      if (!date) { setError('تاریخ انجام سرویس را انتخاب کنید.'); return }
      setBusy(true)
      try {
        const body = { requestId, performedAtUtc: date, odometerKm: km === '' ? null : Number(km), workshop, notes, otherCostToman: other, items, version: record?.version ?? 0, reason: record ? reason : null }
        const path = `${maintenancePath(vehicleId)}/records`
        const saved = record ? await api.put<ServiceRecord>(`${path}/${record.id}`, body) : await api.post<ServiceRecord>(path, body)
        await onSaved(saved)
      } catch (err) { setError(err instanceof Error ? err.message : 'ثبت سرویس ناموفق بود.') } finally { setBusy(false) }
    }}>
      <DateField label="تاریخ انجام سرویس" value={date} onChange={setDate} withTime={false} />
      <div className="m-form-grid"><Field label="کیلومتر هنگام سرویس" hint="نامعلوم را خالی بگذارید؛ صفر یک مقدار واقعی است."><input type="number" min={0} max={999999999} value={km} onChange={e => setKm(e.target.value)} /></Field>
        <Field label="تعمیرگاه (اختیاری)"><input maxLength={200} value={workshop} onChange={e => setWorkshop(e.target.value)} /></Field></div>
      {items.map((item, index) => <section className="m-panel m-form" key={index} aria-label={`عملیات ${index + 1}`}>
        <div className="m-row"><h3>عملیات {faNumber(index + 1)}</h3><button type="button" className="m-button m-button-danger" disabled={items.length === 1} onClick={() => setItems(all => all.filter((_, i) => i !== index))}>حذف عملیات</button></div>
        <div className="m-form-grid"><Field label="نوع عملیات"><select required value={item.templateId} onChange={e => {
          const matching = schedules.find(s => s.templateId === e.target.value && s.isActive)
          patchItem(index, { templateId: e.target.value, scheduleId: matching?.id ?? null })
        }}><option value="">انتخاب عملیات</option>{templates.filter(t => t.isActive || t.id === item.templateId).map(t => <option key={t.id} value={t.id}>{t.name}</option>)}</select></Field>
          <Field label="اجرت (تومان)"><input required type="number" min={0} max={999999999999} step={1} value={item.laborCostToman} onChange={e => patchItem(index, { laborCostToman: Number(e.target.value) })} /></Field></div>
        <Field label="تکمیل برنامهٔ دوره‌ای"><select value={item.scheduleId ?? ''} onChange={e => patchItem(index, { scheduleId: e.target.value || null })}><option value="">بدون ارتباط با برنامه</option>{schedules.filter(s => s.templateId === item.templateId && (s.isActive || s.id === item.scheduleId)).map(s => <option key={s.id} value={s.id}>{s.name}</option>)}</select></Field>
        {item.parts.map((part, pi) => <div className="m-part" key={pi}>
          <Field label="نام قطعه"><input required maxLength={150} value={part.name} onChange={e => patchPart(index, pi, { name: e.target.value })} /></Field>
          <Field label="تعداد"><input required type="number" min={1} max={1000} value={part.quantity} onChange={e => patchPart(index, pi, { quantity: Number(e.target.value) })} /></Field>
          <Field label="قیمت واحد (تومان)"><input required type="number" min={0} max={999999999999} step={1} value={part.unitCostToman} onChange={e => patchPart(index, pi, { unitCostToman: Number(e.target.value) })} /></Field>
          <button type="button" className="m-button m-button-danger" onClick={() => patchItem(index, { parts: item.parts.filter((_, i) => i !== pi) })}>حذف قطعه</button>
          <div className="m-wide m-form-grid"><Field label="مشخصات قطعه"><input maxLength={500} value={part.specification ?? ''} onChange={e => patchPart(index, pi, { specification: e.target.value })} /></Field>
            <DateField label="پایان ضمانت (اختیاری)" withTime={false} value={part.warrantyUntil ? `${part.warrantyUntil}T08:00:00Z` : null} onChange={v => patchPart(index, pi, { warrantyUntil: v ? new Date(new Date(v).getTime() + 210 * 60000).toISOString().slice(0, 10) : null })} /></div>
        </div>)}
        <button type="button" className="m-button" disabled={item.parts.length >= 30} onClick={() => patchItem(index, { parts: [...item.parts, { name: '', specification: null, quantity: 1, unitCostToman: 0, warrantyUntil: null }] })}>افزودن قطعهٔ مصرفی یا تعویضی</button>
      </section>)}
      <button type="button" className="m-button" disabled={items.length >= 30} onClick={() => setItems(all => [...all, emptyItem()])}>افزودن عملیات دیگر</button>
      <Field label="هزینهٔ جانبی (تومان)"><input required type="number" min={0} max={999999999999} step={1} value={other} onChange={e => setOther(Number(e.target.value))} /></Field>
      <Field label="توضیحات"><textarea maxLength={2000} value={notes} onChange={e => setNotes(e.target.value)} /></Field>
      {record && <Field label="دلیل اصلاح"><textarea required maxLength={500} value={reason} onChange={e => setReason(e.target.value)} /></Field>}
      <div className="m-row"><span>جمع هزینهٔ مراجعه</span><strong>{faNumber(total)} تومان</strong></div>
      <Notice>فاکتور را پس از ذخیره، از جزئیات سرویس پیوست کنید. ثبت سرویس، وضعیت عملیاتی خودرو را تغییر نمی‌دهد.</Notice>
      {error && <Notice error>{error}</Notice>}
      <button className="m-button m-button-primary" disabled={busy}>{busy ? 'در حال ذخیره…' : record ? 'ثبت اصلاح با حفظ نسخهٔ قبلی' : 'ثبت سرویس'}</button>
    </form>
  </MaintenanceDialog>
}
