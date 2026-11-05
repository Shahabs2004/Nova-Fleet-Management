const FA_DIGITS = ['۰', '۱', '۲', '۳', '۴', '۵', '۶', '۷', '۸', '۹']

export const jalaliMonths = [
  'فروردین', 'اردیبهشت', 'خرداد',  'تیر', 'مرداد', 'شهریور',
  'مهر', 'آبان', 'آذر', 'دی', 'بهمن', 'اسفند',
]

const weekdayNames = ['شنبه', 'یکشنبه', 'دوشنبه', 'سه‌شنبه', 'چهارشنبه', 'پنجشنبه', 'جمعه']

export const faWeekdayShort = ['ش', 'ی', 'د', 'س', 'چ', 'پ', 'ج']

/** نمایه روز هفته به وقت تهران؛ ۰ = شنبه تا ۶ = جمعه. */
export function tehranWeekdayIndex(iso: string | Date | null | undefined): number {
  if (!iso) return 0
  return (asTehran(new Date(iso)).getUTCDay() + 1) % 7
}

/** ساعت محلی تهران (۰ تا ۲۳) برای یک مقدار UTC. */
export function tehranHour(iso: string | Date | null | undefined): number {
  if (!iso) return 0
  return asTehran(new Date(iso)).getUTCHours()
}

function div(a: number, b: number) {
  return Math.trunc(a / b)
}

const DAY_MS = 86_400_000

function jalaliParts(gy: number, gm: number, gd: number): [number, number, number] {
  const g_d_m = [0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334]
  let jy = gy <= 1600 ? 0 : 979
  const gy2 = gy <= 1600 ? gy - 621 : gy - 1600
  const gm2 = gm > 2 ? gy2 + 1 : gy2
  let days =
    365 * gy2 +
    div(gm2 + 3, 4) -
    div(gm2 + 99, 100) +
    div(gm2 + 399, 400) -
    80 +
    gd +
    g_d_m[gm - 1]
  jy += 33 * div(days, 12053)
  days %= 12053
  jy += 4 * div(days, 1461)
  days %= 1461
  if (days > 365) {
    jy += div(days - 1, 365)
    days = (days - 1) % 365
  }
  const jm = days < 186 ? 1 + div(days, 31) : 7 + div(days - 186, 30)
  const jd = 1 + (days < 186 ? days % 31 : (days - 186) % 30)
  return [jy, jm, jd]
}

/** تقویم از یک آنِ «منتقل‌شده به تهران» می‌خواند؛ ساعت‌های UTC همان زمان محلی تهران هستند. */
function toJalali(date: Date): [number, number, number] {
  return jalaliParts(date.getUTCFullYear(), date.getUTCMonth() + 1, date.getUTCDate())
}

function pad2(value: number) {
  return value < 10 ? `0${value}` : String(value)
}

export function jalaliOf(date: Date): [number, number, number] {
  return toJalali(asTehran(date))
}

export function todayJalali(): [number, number, number] {
  return jalaliOf(new Date())
}

export function toFa(value: string | number): string {
  return String(value).replace(/\d/g, (d) => FA_DIGITS[Number(d)])
}

function asTehran(date: Date): Date {
  return new Date(date.getTime() + 210 * 60_000)
}

export function formatFaDate(iso: string | Date | null | undefined): string {
  if (!iso) return '—'
  const tehran = asTehran(new Date(iso))
  const [jy, jm, jd] = toJalali(tehran)
  return toFa(`${jy}/${pad2(jm)}/${pad2(jd)}`)
}

export function formatFaDateTime(iso: string | Date | null | undefined): string {
  if (!iso) return '—'
  const tehran = asTehran(new Date(iso))
  return `${formatFaDate(iso)} — ${toFa(`${pad2(tehran.getUTCHours())}:${pad2(tehran.getUTCMinutes())}`)}`
}

export function formatFaDateLong(iso: string | Date): string {
  const tehran = asTehran(new Date(iso))
  const [jy, jm, jd] = toJalali(tehran)
  const weekday = weekdayNames[(tehran.getUTCDay() + 1) % 7]
  return `${weekday} ${toFa(jd)} ${jalaliMonths[jm - 1]} ${toFa(jy)}`
}

export function formatFaTime(iso: string | Date | null | undefined): string {
  if (!iso) return '—'
  const tehran = asTehran(new Date(iso))
  return toFa(`${pad2(tehran.getUTCHours())}:${pad2(tehran.getUTCMinutes())}`)
}

/** مبدأ سال شمسی (۱ فروردین) به صورت milisecond از epoch برای همان الگوریتم رفتاری. */
function nowruzUtcMs(jy: number): number {
  const anchor = Date.UTC(jy + 621, 2, 15)
  for (let offset = 0; offset < 20; offset++) {
    const ms = anchor + offset * DAY_MS
    const date = new Date(ms)
    const [fy, fm, fd] = jalaliParts(date.getUTCFullYear(), date.getUTCMonth() + 1, date.getUTCDate())
    if (fy === jy && fm === 1 && fd === 1) return ms
  }
  throw new Error(`سال ${jy} خارج از بازه پشتیبانی‌شده تقویم است.`)
}

/** تبدیل تاریخ شمسی انتخاب‌شده کاربر به UTC با دقت ساعت (مبنا Tehran +3:30). */
export function jalaliHourToUtcUtc(jy: number, jm: number, jd: number, hour: number): string {
  const dayOfYear = (jm < 7 ? (jm - 1) * 31 : (jm - 7) * 30 + 186) + (jd - 1)
  const ms = nowruzUtcMs(jy) + dayOfYear * DAY_MS + hour * 3_600_000 - 210 * 60_000
  return new Date(ms).toISOString()
}

export function faNumber(value: number | null | undefined): string {
  if (value === null || value === undefined) return '—'
  return toFa(value.toLocaleString('en-US'))
}
