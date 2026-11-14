export interface Crop {
  x: number
  y: number
  size: number
}

export interface CropSource {
  url: string
  width: number
  height: number
  canvas: HTMLCanvasElement
}

export const clamp = (value: number, min: number, max: number) => Math.min(Math.max(value, min), max)

export function resizeCrop(crop: Crop, size: number, source: CropSource): Crop {
  const next = clamp(size, Math.min(source.width, source.height) * 0.1, Math.min(source.width, source.height))
  return {
    x: clamp(crop.x + (crop.size - next) / 2, 0, source.width - next),
    y: clamp(crop.y + (crop.size - next) / 2, 0, source.height - next),
    size: next,
  }
}

export async function readCropSource(file: File): Promise<CropSource> {
  if (!['image/png', 'image/jpeg', 'image/webp'].includes(file.type)) {
    throw new Error('لطفاً تصویر PNG، JPG یا WebP انتخاب کنید.')
  }
  if (file.size > 10 * 1024 * 1024) throw new Error('حجم تصویر باید حداکثر ۱۰ مگابایت باشد.')
  let bitmap: ImageBitmap
  try {
    bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' })
  } catch {
    throw new Error('فایل انتخاب‌شده تصویر معتبری نیست یا قابل خواندن نیست.')
  }
  try {
    if (bitmap.width * bitmap.height > 40_000_000 || Math.min(bitmap.width, bitmap.height) < 32) {
      throw new Error('ابعاد تصویر باید حداقل ۳۲ پیکسل و حداکثر ۴۰ مگاپیکسل باشد.')
    }
    const scale = Math.min(1, 1600 / Math.max(bitmap.width, bitmap.height))
    const canvas = document.createElement('canvas')
    canvas.width = Math.max(1, Math.round(bitmap.width * scale))
    canvas.height = Math.max(1, Math.round(bitmap.height * scale))
    const context = canvas.getContext('2d')!
    context.drawImage(bitmap, 0, 0, canvas.width, canvas.height)
    return { url: canvas.toDataURL('image/png'), width: canvas.width, height: canvas.height, canvas }
  } finally {
    bitmap.close()
  }
}

export function exportCrop(source: CropSource, crop: Crop): string {
  const canvas = document.createElement('canvas')
  canvas.width = canvas.height = 256
  const context = canvas.getContext('2d')!
  context.imageSmoothingQuality = 'high'
  context.drawImage(source.canvas, crop.x, crop.y, crop.size, crop.size, 0, 0, 256, 256)
  const result = canvas.toDataURL('image/png')
  if (atob(result.split(',')[1]).length > 256 * 1024) {
    throw new Error('حجم تصویر برش‌خورده بیش از حد مجاز است؛ تصویر ساده‌تری انتخاب کنید.')
  }
  return result
}
