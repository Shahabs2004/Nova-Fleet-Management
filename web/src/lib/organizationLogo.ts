const maxBytes = 256 * 1024
const maxPixels = 256

function readDataUrl(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(typeof reader.result === 'string' ? reader.result : '')
    reader.onerror = () => reject(new Error('خواندن تصویر لوگو ناموفق بود.'))
    reader.onabort = () => reject(new Error('خواندن تصویر لوگو لغو شد.'))
    reader.readAsDataURL(file)
  })
}

export async function readOrganizationLogo(file: File): Promise<string> {
  if (file.type !== 'image/png') throw new Error('لوگو باید یک تصویر PNG باشد.')
  if (file.size > maxBytes) throw new Error('حجم لوگو باید حداکثر ۲۵۶ کیلوبایت باشد.')

  const dataUrl = await readDataUrl(file)
  let image: ImageBitmap
  try {
    image = await createImageBitmap(file)
  } catch {
    throw new Error('فایل انتخاب‌شده تصویر PNG معتبری نیست.')
  }
  try {
    if (image.width !== image.height || image.width > maxPixels) {
      throw new Error('لوگو باید مربع و حداکثر ۲۵۶ × ۲۵۶ پیکسل باشد.')
    }
  } finally {
    image.close()
  }
  return dataUrl
}
