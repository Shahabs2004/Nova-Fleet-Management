export type PlateParts = {
  firstTwo: string
  letter: string
  nextThree: string
  iranCode: string
}

export const PLATE_LETTER_GROUPS: { label: string; letters: string[] }[] = [
  { label: 'سواری شخصی', letters: ['ب', 'ج', 'د', 'س', 'ص', 'ط', 'ق', 'ل', 'م', 'ن', 'و', 'ه', 'ی'] },
  { label: 'ادارات و نهادهای دولتی', letters: ['الف'] },
  { label: 'پلیس', letters: ['پ'] },
  { label: 'تاکسی', letters: ['ت'] },
  { label: 'سپاه پاسداران', letters: ['ث'] },
  { label: 'وزارت دفاع', letters: ['ز'] },
  { label: 'معلولین و جانبازان', letters: ['ژ'] },
  { label: 'ارتش', letters: ['ش'] },
  { label: 'خودروهای عمومی', letters: ['ع'] },
  { label: 'ستاد کل نیروهای مسلح', letters: ['ف'] },
  { label: 'ماشین‌آلات کشاورزی', letters: ['ک'] },
  { label: 'گذرموقت', letters: ['گ'] },
  { label: 'دیپلمات', letters: ['D'] },
  { label: 'سرویس سفارتخانه', letters: ['S'] },
]

const isDigits = (value: string) => /^[0-9۰-۹٠-٩]+$/.test(value)

// رشته ترکیبی پلاک («12 ب 345 - 67» یا فشرده «12ب345-67») را به چهار جزء می‌شکند.
export function parsePlate(value: string | null | undefined): PlateParts | null {
  if (!value) return null
  const segments = value.split('-')
  if (segments.length !== 2) return null
  const main = segments[0].replace(/\s/g, '')
  const iranCode = segments[1].replace(/\s/g, '')
  if (main.length < 6 || iranCode.length !== 2) return null
  const firstTwo = main.slice(0, 2)
  const nextThree = main.slice(-3)
  const letter = main.slice(2, -3)
  if (!letter || !isDigits(firstTwo) || !isDigits(nextThree) || !isDigits(iranCode)) return null
  return { firstTwo, letter, nextThree, iranCode }
}
