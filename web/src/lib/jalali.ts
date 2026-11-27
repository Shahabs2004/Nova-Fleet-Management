import { jalaliHourToUtcUtc } from './date'

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
