import { useQuery } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { api } from '../lib/api'
import { jalaliHourToUtcUtc, todayJalali, toFa } from '../lib/date'
import { jalaliDaysInMonth } from './JalaliDateField'
import type { LocationDto, OrgUnitDto } from '../lib/types'

const MONTHS = ['فروردین', 'اردیبهشت', 'خرداد', 'تیر', 'مرداد', 'شهریور', 'مهر', 'آبان', 'آذر', 'دی', 'بهمن', 'اسفند']
const HOURS = Array.from({ length: 24 }, (_, i) => i)

export interface MissionFormOutput {
  title: string
  type: number
  purpose: string
  origin: string
  intermediateStops: string[]
  destination: string
  plannedStartAtUtc: string
  plannedEndAtUtc: string
  requestingGroup: string
  requesterPhoneSnapshot: string
  requesterOrganizationalUnit: string | null
  description: string | null
  passengerCount: number | null
  missionOrderNumber: string | null
  missionOrderDate: string | null
  cargoDescription: string | null
  consigneeName: string | null
  passengerNames: string | null
}

interface DateValue {
  year: number
  month: number
  day: number
  hour: number
}

function todayParts(): DateValue {
  const [year, month, day] = todayJalali()
  return { year, month, day, hour: new Date(Date.now() + 210 * 60_000).getUTCHours() }
}

function DateTimeField({
  label,
  value,
  years,
  onChange,
}: {
  label: string
  value: DateValue
  years: number[]
  onChange: (value: DateValue) => void
}) {
  const daysInMonth = jalaliDaysInMonth(value.year, value.month)
  return (
    <div>
      <label className="label">{label}</label>
      <div className="grid grid-cols-4 gap-2">
        <select className="input" value={value.year} onChange={(e) => onChange({ ...value, year: Number(e.target.value) })}>
          {years.map((y) => (
            <option key={y} value={y}>{toFa(y)}</option>
          ))}
        </select>
        <select className="input" value={value.month} onChange={(e) => onChange({ ...value, month: Number(e.target.value), day: 1 })}>
          {MONTHS.map((name, index) => (
            <option key={name} value={index + 1}>{name}</option>
          ))}
        </select>
        <select className="input" value={value.day} onChange={(e) => onChange({ ...value, day: Number(e.target.value) })}>
          {Array.from({ length: daysInMonth }, (_, i) => i + 1).map((d) => (
            <option key={d} value={d}>{toFa(d)}</option>
          ))}
        </select>
        <select className="input" value={value.hour} onChange={(e) => onChange({ ...value, hour: Number(e.target.value) })}>
          {HOURS.map((h) => (
            <option key={h} value={h}>{toFa(`${h < 10 ? '0' : ''}${h}:00`)}</option>
          ))}
        </select>
      </div>
    </div>
  )
}

export function MissionForm({
  onSubmit,
  submitting,
  error,
}: {
  onSubmit: (output: MissionFormOutput) => void
  submitting: boolean
  error: string | null
}) {
  const today = useMemo(todayParts, [])
  const years = useMemo(() => [today.year, today.year + 1], [today.year])
  const [type, setType] = useState(1)
  const [title, setTitle] = useState('')
  const [purpose, setPurpose] = useState('')
  const [origin, setOrigin] = useState('')
  const [destination, setDestination] = useState('')
  const [stops, setStops] = useState('')
  const [start, setStart] = useState<DateValue>({ ...today, day: today.day, hour: (today.hour + 1) % 24 })
  const [end, setEnd] = useState<DateValue>({ ...today, hour: (today.hour + 3) % 24 })
  const [phone, setPhone] = useState('')
  const [group, setGroup] = useState('')
  const [unit, setUnit] = useState('')
  const [description, setDescription] = useState('')
  const [passengerCount, setPassengerCount] = useState('')
  const [cargoDescription, setCargoDescription] = useState('')
  const [consigneeName, setConsigneeName] = useState('')
  const [passengerNames, setPassengerNames] = useState('')

  const { data: locations } = useQuery({
    queryKey: ['approved-locations'],
    queryFn: () => api.get<LocationDto[]>('/api/locations?status=2'),
  })
  const { data: orgUnits } = useQuery({
    queryKey: ['org-units'],
    queryFn: () => api.get<OrgUnitDto[]>('/api/organization-units'),
  })

  const locationNames = (locations ?? []).map((l) => l.name)
  const datalistId = 'mission-locations'

  const submit = (e: React.FormEvent) => {
    e.preventDefault()
    onSubmit({
      title,
      type,
      purpose,
      origin,
      intermediateStops: stops.split('\n').map((s) => s.trim()).filter(Boolean),
      destination,
      plannedStartAtUtc: jalaliHourToUtcUtc(start.year, start.month, start.day, start.hour),
      plannedEndAtUtc: jalaliHourToUtcUtc(end.year, end.month, end.day, end.hour),
      requestingGroup: group,
      requesterPhoneSnapshot: phone,
      requesterOrganizationalUnit: unit || null,
      description: description || null,
      passengerCount: passengerCount ? Number(passengerCount) : null,
      missionOrderNumber: null,
      missionOrderDate: null,
      cargoDescription: type === 1 ? cargoDescription || null : null,
      consigneeName: type === 1 ? consigneeName || null : null,
      passengerNames: type === 2 ? passengerNames || null : null,
    })
  }

  return (
    <form onSubmit={submit} className="space-y-5">
      <datalist id={datalistId}>
        {locationNames.map((name) => (
          <option key={name} value={name} />
        ))}
      </datalist>

      <div className="grid grid-cols-2 gap-2 rounded-xl bg-slate-100 p-1">
        {[1, 2].map((t) => (
          <button
            key={t}
            type="button"
            onClick={() => setType(t)}
            className={`rounded-lg px-3 py-2 text-sm font-semibold transition-colors ${
              type === t ? 'bg-white text-emerald-900 shadow-sm' : 'text-slate-500 hover:text-slate-700'
            }`}
          >
            {t === 1 ? 'باربری' : 'سرنشین‌دار'}
          </button>
        ))}
      </div>

      <div>
        <label className="label">عنوان مأموریت</label>
        <input className="input" value={title} onChange={(e) => setTitle(e.target.value)} placeholder="مثال: انتقال بار اداری به انبار" required />
      </div>

      <div>
        <label className="label">هدف مأموریت</label>
        <textarea className="input min-h-20 resize-y" value={purpose} onChange={(e) => setPurpose(e.target.value)} required />
      </div>

      <div className="grid gap-4 sm:grid-cols-3">
        <div>
          <label className="label">مبدأ</label>
          <input className="input" list={datalistId} value={origin} onChange={(e) => setOrigin(e.target.value)} required />
        </div>
        <div>
          <label className="label">مقصدهای میانی (اختیاری)</label>
          <textarea
            className="input min-h-10 resize-y text-[13px]"
            value={stops}
            onChange={(e) => setStops(e.target.value)}
            placeholder="هر مقصد در یک سطر"
          />
        </div>
        <div>
          <label className="label">مقصد نهایی</label>
          <input className="input" list={datalistId} value={destination} onChange={(e) => setDestination(e.target.value)} required />
        </div>
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        <DateTimeField label="زمان شروع (به وقت تهران)" value={start} years={years} onChange={setStart} />
        <DateTimeField label="زمان پایان (به وقت تهران)" value={end} years={years} onChange={setEnd} />
      </div>

      <div className="grid gap-4 sm:grid-cols-3">
        <div>
          <label className="label">شماره موبایل درخواست‌کننده</label>
          <input className="input num" dir="ltr" value={phone} onChange={(e) => setPhone(e.target.value)} placeholder="09xxxxxxxxx" required />
        </div>
        <div>
          <label className="label">گروه درخواست‌کننده</label>
          <input className="input" value={group} onChange={(e) => setGroup(e.target.value)} required />
        </div>
        <div>
          <label className="label">واحد سازمانی</label>
          <select className="input" value={unit} onChange={(e) => setUnit(e.target.value)}>
            <option value="">— انتخاب کنید —</option>
            {(orgUnits ?? []).filter((u) => u.isActive).map((u) => (
              <option key={u.id} value={u.name}>{u.name}</option>
            ))}
          </select>
          {(orgUnits ?? []).filter((u) => u.isActive).length === 0 && (
            <p className="mt-1.5 text-[11px] text-gold-600">واحد سازمانی فعالی تعریف نشده؛ از تنظیمات › واحدهای سازمانی اضافه کنید.</p>
          )}
        </div>
      </div>

      {type === 2 && (
        <div className="grid gap-4 rounded-xl bg-emerald-50/60 p-4 ring-1 ring-emerald-100 sm:grid-cols-2">
          <div>
            <label className="label">تعداد همراهان</label>
            <input className="input num" dir="ltr" type="number" min={0} value={passengerCount} onChange={(e) => setPassengerCount(e.target.value)} />
          </div>
          <div>
            <label className="label">نام همراهان</label>
            <textarea className="input min-h-10 resize-y" value={passengerNames} onChange={(e) => setPassengerNames(e.target.value)} />
          </div>
        </div>
      )}

      {type === 1 && (
        <div className="grid gap-4 rounded-xl bg-gold-100/50 p-4 ring-1 ring-gold-500/20 sm:grid-cols-2">
          <div>
            <label className="label">شرح بار</label>
            <input className="input" value={cargoDescription} onChange={(e) => setCargoDescription(e.target.value)} />
          </div>
          <div>
            <label className="label">نام تحویل‌گیرنده</label>
            <input className="input" value={consigneeName} onChange={(e) => setConsigneeName(e.target.value)} />
          </div>
        </div>
      )}

      <div>
        <label className="label">توضیحات تکمیلی (اختیاری)</label>
        <textarea className="input min-h-16 resize-y" value={description} onChange={(e) => setDescription(e.target.value)} />
      </div>

      {error && <div className="rounded-xl bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700 ring-1 ring-rose-100">{error}</div>}

      <div className="flex justify-end gap-2">
        <button type="submit" disabled={submitting} className="btn-primary">
          {submitting ? 'در حال ثبت…' : 'ثبت درخواست'}
        </button>
      </div>
    </form>
  )
}
