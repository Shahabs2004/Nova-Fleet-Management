import { useEffect, useId, useRef, type ReactNode } from 'react'
import { JalaliDateField } from '../JalaliDateField'
import type { JalaliDateValue } from '../../lib/jalali'
import { faNumber, jalaliHourToUtcUtc, jalaliOf, tehranHour, todayJalali, toFa } from '../../lib/date'
import { stateLabels } from '../../lib/maintenance'

export function MaintenanceDialog({ title, onClose, children }: { title: string; onClose: () => void; children: ReactNode }) {
  const dialog = useRef<HTMLDialogElement>(null)
  const titleId = useId()
  useEffect(() => { const node = dialog.current!; node.showModal(); return () => node.close() }, [])
  return <dialog ref={dialog} className="maintenance-dialog" aria-labelledby={titleId} onCancel={onClose}>
    <header><h2 id={titleId}>{title}</h2><button type="button" className="m-button" onClick={onClose} aria-label="بستن پنجره">بستن</button></header>
    {children}
  </dialog>
}
export function Field({ label, children, hint }: { label: string; children: ReactNode; hint?: string }) {
  return <label className="m-field"><span>{label}</span>{children}{hint && <small>{hint}</small>}</label>
}
const FA = '۰۱۲۳۴۵۶۷۸۹'
const AR = '٠١٢٣٤٥٦٧٨٩'
export function MoneyInput({ value, onChange, required = false, max = 999999999999, label }: { value: number; onChange: (value: number) => void; required?: boolean; max?: number; label?: string }) {
  return <input type="text" inputMode="numeric" required={required} aria-label={label} value={faNumber(value)}
    onFocus={e => e.target.select()}
    onChange={e => {
      const digits = e.target.value.replace(/[۰-۹]/g, d => String(FA.indexOf(d))).replace(/[٠-٩]/g, d => String(AR.indexOf(d))).replace(/\D/g, '')
      onChange(digits === '' ? 0 : Math.min(max, Number(digits)))
    }} />
}
export function DateField({ label, value, onChange, withTime = true, disabled = false }: { label: string; value: string | null; onChange: (value: string | null) => void; withTime?: boolean; disabled?: boolean }) {
  const parts = value ? jalaliOf(new Date(value)) : null
  const selected: JalaliDateValue | null = parts ? { jy: parts[0], jm: parts[1], jd: parts[2] } : null
  const hour = tehranHour(value)
  const localMinute = value ? new Date(new Date(value).getTime() + 210 * 60000).getUTCMinutes() : 0
  const update = (date: JalaliDateValue | null, h: number, minute: number) => onChange(date ? new Date(new Date(jalaliHourToUtcUtc(date.jy, date.jm, date.jd, h)).getTime() + minute * 60000).toISOString() : null)
  return <fieldset className="m-date" disabled={disabled}><legend>{label}</legend>
    <JalaliDateField value={selected} startYear={todayJalali()[0]} yearsBack={40} yearsForward={15} onChange={d => update(d, hour, localMinute)} />
    {withTime && <div className="m-time"><label>ساعت <select aria-label={`${label} ساعت`} value={hour} onChange={e => update(selected, Number(e.target.value), localMinute)}>{Array.from({ length: 24 }, (_, h) => <option key={h} value={h}>{toFa(h)}</option>)}</select></label>
      <label>دقیقه <input aria-label={`${label} دقیقه`} type="number" min={0} max={59} value={localMinute} onChange={e => update(selected, hour, Math.min(59, Math.max(0, Number(e.target.value))))} /></label></div>}
  </fieldset>
}
export function StateBadge({ state }: { state: string }) { return <span className={`m-state m-state-${state}`}>{stateLabels[state] ?? state}</span> }
export function Notice({ children, error = false }: { children: ReactNode; error?: boolean }) { return <p className={error ? 'm-notice m-error' : 'm-notice'} role={error ? 'alert' : 'status'}>{children}</p> }
