import { useId, useRef } from 'react'
import type { PointerEvent } from 'react'
import { clamp, resizeCrop } from '../lib/avatarCrop'
import type { Crop, CropSource } from '../lib/avatarCrop'

export function AvatarCropper({ source, crop, onChange }: { source: CropSource; crop: Crop; onChange: (crop: Crop) => void }) {
  const stage = useRef<HTMLDivElement>(null)
  const gesture = useRef<{ crop: Crop; x: number; y: number; mode: 'move' | 'resize' } | null>(null)
  const hintId = useId()
  const sizeId = useId()
  const maxSize = Math.min(source.width, source.height)

  const start = (event: PointerEvent<HTMLElement>, mode: 'move' | 'resize') => {
    if (event.button !== 0) return
    event.preventDefault()
    event.stopPropagation()
    event.currentTarget.focus()
    event.currentTarget.setPointerCapture(event.pointerId)
    gesture.current = { crop, x: event.clientX, y: event.clientY, mode }
  }

  const move = (event: PointerEvent<HTMLDivElement>) => {
    if (!gesture.current || !stage.current) return
    const start = gesture.current
    const scale = source.width / stage.current.getBoundingClientRect().width
    const dx = (event.clientX - start.x) * scale
    const dy = (event.clientY - start.y) * scale
    if (start.mode === 'move') {
      onChange({ ...start.crop, x: clamp(start.crop.x + dx, 0, source.width - crop.size), y: clamp(start.crop.y + dy, 0, source.height - crop.size) })
    } else {
      const delta = Math.abs(dx) > Math.abs(dy) ? dx : dy
      onChange(resizeCrop(start.crop, start.crop.size + delta * 2, source))
    }
  }

  return (
    <div className="space-y-4">
      <p id={hintId} className="avatar-hint">دایره را جابه‌جا کنید و برای تغییر اندازه، دستگیرهٔ طلایی یا نوار اندازه را حرکت دهید. با کلیدهای جهت‌نما نیز می‌توانید کادر را تنظیم کنید.</p>
      <div ref={stage} dir="ltr" className="avatar-stage" style={{ width: '100%', maxWidth: Math.min(384, 360 * source.width / source.height), aspectRatio: `${source.width} / ${source.height}` }} onPointerMove={move} onPointerUp={() => { gesture.current = null }} onPointerCancel={() => { gesture.current = null }}>
        <img src={source.url} alt="تصویر انتخاب‌شده برای برش" className="block h-full w-full" draggable={false} />
        <div
          tabIndex={0}
          role="group"
          aria-label="کادر برش تصویر"
          aria-describedby={hintId}
          className="avatar-crop"
          style={{ left: `${crop.x / source.width * 100}%`, top: `${crop.y / source.height * 100}%`, width: `${crop.size / source.width * 100}%`, height: `${crop.size / source.height * 100}%` }}
          onPointerDown={(event) => start(event, 'move')}
          onKeyDown={(event) => {
            const step = maxSize * (event.shiftKey ? 0.05 : 0.01)
            const offsets: Record<string, [number, number]> = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] }
            const offset = offsets[event.key]
            if (!offset) return
            event.preventDefault()
            onChange({ ...crop, x: clamp(crop.x + offset[0], 0, source.width - crop.size), y: clamp(crop.y + offset[1], 0, source.height - crop.size) })
          }}
        >
          <button type="button" aria-label="تغییر اندازه کادر برش" className="avatar-resize" onPointerDown={(event) => start(event, 'resize')} onKeyDown={(event) => {
            if (!['ArrowUp', 'ArrowRight', 'ArrowDown', 'ArrowLeft'].includes(event.key)) return
            event.preventDefault()
            event.stopPropagation()
            const direction = ['ArrowUp', 'ArrowRight'].includes(event.key) ? 1 : -1
            onChange(resizeCrop(crop, crop.size + direction * maxSize * 0.02, source))
          }} />
        </div>
      </div>
      <div className="flex items-center gap-4">
        <div className="avatar-preview" aria-label="پیش‌نمایش تصویر پروفایل" role="img" dir="ltr">
          <img src={source.url} alt="" className="absolute max-w-none" style={{ width: `${source.width / crop.size * 100}%`, height: `${source.height / crop.size * 100}%`, left: `${-crop.x / crop.size * 100}%`, top: `${-crop.y / crop.size * 100}%` }} />
        </div>
        <div className="min-w-0 flex-1">
          <label className="label" htmlFor={sizeId}>اندازهٔ کادر برش</label>
          <input id={sizeId} type="range" className="avatar-range" dir="ltr" min={maxSize * 0.1} max={maxSize} step={maxSize / 100} value={crop.size} onChange={(event) => onChange(resizeCrop(crop, Number(event.target.value), source))} />
        </div>
      </div>
    </div>
  )
}
