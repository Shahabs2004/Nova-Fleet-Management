import { jalaliHourToUtcUtc, toFa } from '../lib/date'

const JALALI_MONTHS = [
  'فروردین', 'اردیبهشت', 'خرداد', 'تیر', 'مرداد', 'شهریور',
  'مهر', 'آبان', 'آذر', 'دی', 'بهمن', 'اسفند',
]

export interface JalaliDateValue {
  jy: number
  jm: number
  jd: number
}

export function jalaliDaysInMonth(jy: number, jm: number): number {
  if (jm <= 6) return 31
  if (jm <= 11) return 30
  const remainders = [1, 5, 9, 13, 17, 22, 26, 30]
  return remainders.includes(jy % 33) ? 30 : 29
}

export function jalaliValueToUtc(value: JalaliDateValue | null, hour: number): string | null {
  if (!value) return null
  return jalaliHourToUtcUtc(value.jy, value.jm, value.jd, hour)
}

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
