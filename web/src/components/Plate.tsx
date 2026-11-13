import { useId } from 'react'
import { toFa } from '../lib/date'

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

const PERSONAL = new Set(['ب', 'ج', 'د', 'س', 'ص', 'ط', 'ق', 'ل', 'م', 'ن', 'و', 'ه', 'ی'])
const YELLOW = new Set(['ع', 'ت', 'ک'])
const GREEN = new Set(['پ', 'ث'])
const BLUE = new Set(['ف', 'ز', 'D', 'S'])

function scheme(letter: string): { bg: string; fg: string } {
  if (letter === 'الف') return { bg: '#d21f1f', fg: '#ffffff' }
  if (YELLOW.has(letter)) return { bg: '#ffd400', fg: '#000000' }
  if (GREEN.has(letter)) return { bg: '#006b3f', fg: '#ffffff' }
  if (BLUE.has(letter)) return { bg: '#0a3d91', fg: '#ffffff' }
  if (letter === 'ش') return { bg: '#9b8b5e', fg: '#000000' }
  if (PERSONAL.has(letter) || letter === 'ژ' || letter === 'گ') return { bg: '#ffffff', fg: '#000000' }
  return { bg: '#ffffff', fg: '#000000' }
}

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

const WIDTHS = { sm: 132, md: 190, lg: 280 } as const

export function Plate({
  parts,
  value,
  size = 'md',
  className,
}: {
  parts?: PlateParts | null
  value?: string | null
  size?: keyof typeof WIDTHS
  className?: string
}) {
  const clipId = useId()
  const resolved = parts ?? parsePlate(value)
  const width = WIDTHS[size]

  if (!resolved) {
    return (
      <span className={`num inline-flex items-center gap-1 rounded-lg border-2 border-emerald-900 bg-white px-2 py-1 text-[13px] font-bold text-emerald-950 ${className ?? ''}`} dir="ltr">
        {value ?? '—'}
      </span>
    )
  }

  const { bg, fg } = scheme(resolved.letter)
  const height = (width * 110) / 440

  return (
    <svg
      viewBox="0 0 440 110"
      width={width}
      height={height}
      className={className}
      role="img"
      aria-label={`پلاک ${resolved.firstTwo} ${resolved.letter} ${resolved.nextThree} ایران ${resolved.iranCode}`}
    >
      <defs>
        <clipPath id={clipId}>
          <rect x="0" y="0" width="440" height="110" rx="12" />
        </clipPath>
      </defs>
      <g clipPath={`url(#${clipId})`}>
        <rect x="0" y="0" width="440" height="110" fill={bg} />
        <rect x="0" y="0" width="64" height="110" fill="#0a3d91" />
        {/* پرچم ایران */}
        <rect x="10" y="10" width="44" height="9" fill="#009a44" />
        <rect x="10" y="19" width="44" height="9" fill="#ffffff" />
        <rect x="10" y="28" width="44" height="9" fill="#da0000" />
        <rect x="29" y="20.5" width="6" height="6" fill="#da0000" />
        <text x="32" y="60" textAnchor="middle" fontSize="17" fontWeight="700" fill="#ffffff">I.R.</text>
        <text x="32" y="82" textAnchor="middle" fontSize="17" fontWeight="700" fill="#ffffff">IRAN</text>
        <text x="108" y="84" textAnchor="middle" fontSize="84" fontWeight="700" fill={fg} textLength="70" lengthAdjust="spacingAndGlyphs">{toFa(resolved.firstTwo)}</text>
        <text x="200" y="82" textAnchor="middle" fontSize="74" fontWeight="700" fill={fg} textLength={resolved.letter === 'الف' ? 78 : 40} lengthAdjust="spacingAndGlyphs">{resolved.letter}</text>
        <text x="296" y="84" textAnchor="middle" fontSize="84" fontWeight="700" fill={fg} textLength="102" lengthAdjust="spacingAndGlyphs">{toFa(resolved.nextThree)}</text>
        <text x="396" y="34" textAnchor="middle" fontSize="21" fontWeight="700" fill={fg}>ایران</text>
        <text x="396" y="84" textAnchor="middle" fontSize="42" fontWeight="700" fill={fg}>{toFa(resolved.iranCode)}</text>
      </g>
      <rect x="1.5" y="1.5" width="437" height="107" rx="12" fill="none" stroke={fg} strokeWidth="3" />
      <line x1="64" y1="0" x2="64" y2="110" stroke={fg} strokeWidth="3" />
      <line x1="352" y1="0" x2="352" y2="110" stroke={fg} strokeWidth="3" />
    </svg>
  )
}