import { useState } from 'react'

export function Avatar({ name, src, size = 'md' }: { name: string; src?: string | null; size?: 'sm' | 'md' | 'lg' }) {
  const [failedSource, setFailedSource] = useState<string | null>(null)
  const sizes = { sm: 'h-8 w-8 text-xs', md: 'h-9 w-9 text-xs', lg: 'h-20 w-20 text-2xl' }
  return (
    <span className={`avatar ${sizes[size]}`}>
      {src && src !== failedSource ? (
        <img src={src} alt={`تصویر ${name}`} className="h-full w-full object-cover" onError={() => setFailedSource(src)} />
      ) : (
        <span aria-label={`بدون تصویر: ${name}`}>{name.trim()[0] ?? '؟'}</span>
      )}
    </span>
  )
}
