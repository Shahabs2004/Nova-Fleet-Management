import { useState } from 'react'
import { api } from '../../lib/api'
import { assetKeys, maintenancePath, pixelAsset, type Schedule, type Template } from '../../lib/maintenance'
import { DateField, Field, MaintenanceDialog, Notice } from './MaintenanceControls'

type Period = { intervalKm: number | null; intervalMonths: number | null; warningKm: number; warningDays: number }
function PeriodFields({ value, onChange }: { value: Period; onChange: (patch: Partial<Period>) => void }) {
  return <div className="m-form-grid">
    <Field label="دورهٔ کیلومتری" hint="خالی یعنی بدون شرط کیلومتر"><input type="number" min={1} max={10000000} value={value.intervalKm ?? ''} onChange={e => onChange({ intervalKm: e.target.value === '' ? null : Number(e.target.value) })} /></Field>
    <Field label="دورهٔ زمانی (ماه شمسی)" hint="با هر دو شرط، هرکدام زودتر برسد"><input type="number" min={1} max={120} value={value.intervalMonths ?? ''} onChange={e => onChange({ intervalMonths: e.target.value === '' ? null : Number(e.target.value) })} /></Field>
    <Field label="پیش‌هشدار کیلومتر"><input required type="number" min={0} max={1000000} value={value.warningKm} onChange={e => onChange({ warningKm: Number(e.target.value) })} /></Field>
    <Field label="پیش‌هشدار روز"><input required type="number" min={0} max={3650} value={value.warningDays} onChange={e => onChange({ warningDays: Number(e.target.value) })} /></Field>
  </div>
}
export function ScheduleEditor({ vehicleId, templates, schedule, onClose, onSaved }: { vehicleId: string; templates: Template[]; schedule?: Schedule; onClose: () => void; onSaved: () => Promise<void> }) {
  const [form, setForm] = useState(schedule ?? { templateId: '', name: '', intervalKm: null as number | null, intervalMonths: null as number | null, warningKm: 1000, warningDays: 30, baselineKm: null as number | null, baselineAtUtc: null as string | null, isActive: true, version: 0 })
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const patch = (value: Partial<typeof form>) => setForm(f => ({ ...f, ...value }))
  return <MaintenanceDialog title={schedule ? 'ویرایش برنامهٔ سرویس' : 'افزودن برنامهٔ سرویس'} onClose={onClose}>
    <form className="m-form" onSubmit={async e => {
      e.preventDefault(); setBusy(true); setError('')
      try {
        const path = `${maintenancePath(vehicleId)}/schedules`
        if (schedule) await api.put(`${path}/${schedule.id}`, form); else await api.post(path, form)
        await onSaved(); onClose()
      } catch (err) { setError(err instanceof Error ? err.message : 'ذخیره ناموفق بود.') } finally { setBusy(false) }
    }}>
      <Field label="نوع سرویس"><select required disabled={!!schedule} value={form.templateId} onChange={e => {
        const t = templates.find(t => t.id === e.target.value)
        if (t) patch({ templateId: t.id, name: t.name, intervalKm: t.intervalKm, intervalMonths: t.intervalMonths, warningKm: t.warningKm, warningDays: t.warningDays })
      }}><option value="">انتخاب نوع سرویس</option>{templates.filter(t => t.isActive || t.id === form.templateId).map(t => <option key={t.id} value={t.id}>{t.name}</option>)}</select></Field>
      <Field label="نام برنامه"><input required maxLength={100} value={form.name} onChange={e => patch({ name: e.target.value })} /></Field>
      <PeriodFields value={form} onChange={patch} />
      <Field label="کیلومتر مبنا" hint="آخرین سرویس تأییدشده یا مبنای شروع برنامه؛ مقدار نامعلوم را خالی بگذارید."><input required={form.intervalKm !== null} type="number" min={0} max={999999999} value={form.baselineKm ?? ''} onChange={e => patch({ baselineKm: e.target.value === '' ? null : Number(e.target.value) })} /></Field>
      <DateField label="تاریخ مبنا" value={form.baselineAtUtc} onChange={v => patch({ baselineAtUtc: v })} />
      <label className="m-check"><input type="checkbox" checked={form.isActive} onChange={e => patch({ isActive: e.target.checked })} />برنامه فعال است</label>
      <Notice>دوره‌ها مستقل از الگوی مشترک ذخیره می‌شوند. پس از ثبت سرویس مرتبط، موعد بعدی از انجام واقعی محاسبه می‌شود.</Notice>
      {error && <Notice error>{error}</Notice>}
      <button className="m-button m-button-primary" disabled={busy}>{busy ? 'در حال ذخیره…' : 'ذخیره برنامه'}</button>
    </form>
  </MaintenanceDialog>
}
export function TemplateEditor({ template, onClose, onSaved }: { template?: Template; onClose: () => void; onSaved: () => Promise<void> }) {
  const [form, setForm] = useState(template ?? { name: '', category: '', assetKey: 'general-service', intervalKm: null as number | null, intervalMonths: null as number | null, warningKm: 1000, warningDays: 30, isActive: true, version: 0 })
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const patch = (value: Partial<typeof form>) => setForm(f => ({ ...f, ...value }))
  return <MaintenanceDialog title={template ? 'ویرایش الگوی مشترک' : 'الگوی جدید'} onClose={onClose}>
    <form className="m-form" onSubmit={async e => {
      e.preventDefault(); setBusy(true); setError('')
      try {
        if (template) await api.put(`/api/maintenance/templates/${template.id}`, form); else await api.post('/api/maintenance/templates', form)
        await onSaved(); onClose()
      } catch (err) { setError(err instanceof Error ? err.message : 'ذخیره ناموفق بود.') } finally { setBusy(false) }
    }}>
      <div className="m-form-grid"><Field label="نام الگو"><input required maxLength={100} value={form.name} onChange={e => patch({ name: e.target.value })} /></Field>
        <Field label="دسته"><input required maxLength={100} value={form.category} onChange={e => patch({ category: e.target.value })} /></Field></div>
      <fieldset><legend>تصویر سرویس</legend><div className="m-toolbar">{assetKeys.map(key => <button key={key} type="button" className="m-button" aria-label={`تصویر ${key}`} aria-pressed={form.assetKey === key} onClick={() => patch({ assetKey: key })}><img className="m-pixel" src={pixelAsset(key)} width={32} height={32} alt="" /></button>)}</div></fieldset>
      <PeriodFields value={form} onChange={patch} />
      <label className="m-check"><input type="checkbox" checked={form.isActive} onChange={e => patch({ isActive: e.target.checked })} />الگو فعال است</label>
      <Notice>ویرایش الگو، دورهٔ برنامه‌های قبلی خودروها را تغییر نمی‌دهد.</Notice>
      {error && <Notice error>{error}</Notice>}
      <button className="m-button m-button-primary" disabled={busy}>{busy ? 'در حال ذخیره…' : 'ذخیره الگو'}</button>
    </form>
  </MaintenanceDialog>
}
