import { toFa } from '../lib/date'
import { jalaliDaysInMonth, type JalaliDateValue } from '../lib/jalali'

const JALALI_MONTHS = [
  'فروردین', 'اردیبهشت', 'خرداد', 'تیر', 'مرداد', 'شهریور',
  'مهر', 'آبان', 'آذر', 'دی', 'بهمن', 'اسفند',
]

export function JalaliDateField({
  value,
  onChange,
  startYear,
  yearsBack = 3,
  yearsForward = 2,
}: {
  value: JalaliDateValue | null
  onChange: (value: JalaliDateValue | null) => void
  startYear: number
  yearsBack?: number
  yearsForward?: number
}) {
  const years: number[] = []
  for (let y = startYear + yearsForward; y >= startYear - yearsBack; y--) years.push(y)
  const days = value ? jalaliDaysInMonth(value.jy, value.jm) : 31

  return (
    <div className="grid grid-cols-3 gap-2">
      <select
        className="input"
        aria-label="سال شمسی"
        value={value?.jy ?? ''}
        onChange={(e) => {
          const jy = Number(e.target.value)
          if (!jy) return onChange(null)
          const jm = value?.jm ?? 1
          onChange({ jy, jm, jd: Math.min(value?.jd ?? 1, jalaliDaysInMonth(jy, jm)) })
        }}
      >
        <option value="">سال</option>
        {years.map((y) => (
          <option key={y} value={y}>
            {toFa(y)}
          </option>
        ))}
      </select>
      <select
        className="input"
        aria-label="ماه شمسی"
        value={value?.jm ?? ''}
        onChange={(e) => {
          const jm = Number(e.target.value)
          if (!jm) return onChange(null)
          const jy = value?.jy ?? startYear
          onChange({ jy, jm, jd: Math.min(value?.jd ?? 1, jalaliDaysInMonth(jy, jm)) })
        }}
      >
        <option value="">ماه</option>
        {JALALI_MONTHS.map((m, i) => (
          <option key={m} value={i + 1}>
            {m}
          </option>
        ))}
      </select>
      <select
        className="input"
        aria-label="روز شمسی"
        value={value?.jd ?? ''}
        onChange={(e) => {
          const jd = Number(e.target.value)
          if (!jd) return onChange(null)
          onChange({ jy: value?.jy ?? startYear, jm: value?.jm ?? 1, jd })
        }}
      >
        <option value="">روز</option>
        {Array.from({ length: days }, (_, i) => i + 1).map((d) => (
          <option key={d} value={d}>
            {toFa(d)}
          </option>
        ))}
      </select>
    </div>
  )
}
