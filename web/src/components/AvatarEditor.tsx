import { useEffect, useRef, useState } from 'react'
import { Avatar } from './Avatar'
import { AvatarCropper } from './AvatarCropper'
import { exportCrop, readCropSource } from '../lib/avatarCrop'
import type { Crop, CropSource } from '../lib/avatarCrop'

export function AvatarEditor({ name, value, onChange, onEditingChange, disabled = false }: {
  name: string
  value: string | null
  onChange: (value: string | null) => void
  onEditingChange: (editing: boolean) => void
  disabled?: boolean
}) {
  const input = useRef<HTMLInputElement>(null)
  const generation = useRef(0)
  const [source, setSource] = useState<CropSource | null>(null)
  const [crop, setCrop] = useState<Crop>({ x: 0, y: 0, size: 0 })
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => () => { generation.current++ }, [])

  const select = async (file: File | undefined) => {
    if (!file) return
    const current = ++generation.current
    setError(null)
    setLoading(true)
    onEditingChange(true)
    try {
      const image = await readCropSource(file)
      if (current !== generation.current) return
      const size = Math.min(image.width, image.height) * 0.8
      setSource(image)
      setCrop({ x: (image.width - size) / 2, y: (image.height - size) / 2, size })
    } catch (err) {
      if (current !== generation.current) return
      setError(err instanceof Error ? err.message : 'خواندن تصویر ناموفق بود.')
      onEditingChange(Boolean(source))
    } finally {
      if (current === generation.current) setLoading(false)
    }
  }

  const cancel = () => {
    generation.current++
    setSource(null)
    setLoading(false)
    setError(null)
    onEditingChange(false)
  }

  return (
    <fieldset className="avatar-editor" disabled={disabled}>
      <legend className="label px-1">تصویر پروفایل</legend>
      <div className="flex flex-wrap items-center gap-3">
        <Avatar name={name} src={value} size="lg" />
        <div className="min-w-0 flex-1 space-y-2">
          <div className="flex flex-wrap gap-2">
            <input ref={input} type="file" accept="image/png,image/jpeg,image/webp" className="hidden" aria-label="انتخاب تصویر پروفایل" onChange={(event) => {
              void select(event.target.files?.[0])
              event.target.value = ''
            }} />
            <button type="button" className="btn-ghost btn-sm" disabled={loading} onClick={() => input.current?.click()}>انتخاب تصویر</button>
            {value && <button type="button" className="btn-ghost btn-sm" onClick={() => { cancel(); onChange(null) }}>حذف تصویر</button>}
          </div>
          <p className="avatar-hint">PNG، JPG یا WebP تا ۱۰ مگابایت؛ فقط بخش برش‌خورده ذخیره می‌شود.</p>
        </div>
      </div>
      {loading && <p className="avatar-hint" role="status">در حال آماده‌سازی تصویر…</p>}
      {source && !loading && <AvatarCropper source={source} crop={crop} onChange={setCrop} />}
      {(source || loading) && (
        <div className="flex flex-wrap justify-end gap-2">
          <button type="button" className="btn-ghost btn-sm" onClick={cancel}>انصراف از برش</button>
          <button type="button" className="btn-primary btn-sm" disabled={!source || loading} onClick={() => {
            if (!source) return
            try {
              onChange(exportCrop(source, crop))
              cancel()
            } catch (err) {
              setError(err instanceof Error ? err.message : 'برش تصویر ناموفق بود.')
            }
          }}>اعمال برش</button>
        </div>
      )}
      {error && <div className="avatar-error" role="alert">{error}</div>}
      {!source && !loading && <p className="avatar-hint">برای ثبت تغییر تصویر، دکمهٔ ذخیرهٔ فرم را بزنید.</p>}
    </fieldset>
  )
}
