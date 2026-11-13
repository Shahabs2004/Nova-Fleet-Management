export const MISSION_STATUS: Record<number, { title: string; badge: string; dot: string; text: string }> = {
  1: { title: 'در انتظار بررسی', badge: 'bg-amber-50 text-amber-700 ring-amber-200', dot: 'bg-amber-500', text: 'text-amber-400' },
  2: { title: 'در حال بررسی', badge: 'bg-sky-50 text-sky-700 ring-sky-200', dot: 'bg-sky-500', text: 'text-sky-400' },
  3: { title: 'تأییدشده', badge: 'bg-emerald-50 text-emerald-800 ring-emerald-200', dot: 'bg-emerald-600', text: 'text-emerald-400' },
  4: { title: 'در حال اجرا', badge: 'bg-indigo-50 text-indigo-700 ring-indigo-200', dot: 'bg-indigo-500', text: 'text-indigo-400' },
  5: { title: 'تکمیل‌شده', badge: 'bg-slate-100 text-slate-600 ring-slate-200', dot: 'bg-slate-400', text: 'text-slate-300' },
  6: { title: 'در انتظار تصمیم انصراف', badge: 'bg-orange-50 text-orange-700 ring-orange-200', dot: 'bg-orange-500', text: 'text-orange-400' },
  7: { title: 'لغوشده', badge: 'bg-rose-50 text-rose-700 ring-rose-200', dot: 'bg-rose-500', text: 'text-rose-400' },
  8: { title: 'ردشده', badge: 'bg-rose-50 text-rose-700 ring-rose-200', dot: 'bg-rose-400', text: 'text-rose-300' },
}

export const MISSION_TYPE: Record<number, string> = {
  1: 'باربری',
  2: 'سرنشین',
}

export const VEHICLE_STATUS: Record<number, string> = {
  1: 'آماده',
  2: 'در مأموریت',
  3: 'تعمیرگاه',
  4: 'خارج از سرویس',
}

export const DRIVER_STATUS: Record<number, string> = {
  1: 'فعال',
  2: 'مرخصی',
  3: 'تعلیق',
  4: 'خاتمه همکاری',
}

export const SMS_STATUS: Record<number, { title: string; badge: string }> = {
  1: { title: 'در صف', badge: 'bg-slate-100 text-slate-600' },
  2: { title: 'در حال ارسال', badge: 'bg-sky-50 text-sky-700' },
  3: { title: 'تحویل‌شده', badge: 'bg-emerald-50 text-emerald-700' },
  4: { title: 'پذیرفته‌شده', badge: 'bg-teal-50 text-teal-700' },
  5: { title: 'ارسال مجدد', badge: 'bg-amber-50 text-amber-700' },
  6: { title: 'ناموفق', badge: 'bg-rose-50 text-rose-700' },
  7: { title: 'خطای دائمی', badge: 'bg-rose-100 text-rose-800' },
  8: { title: 'لغوشده', badge: 'bg-slate-100 text-slate-500' },
}

export const SESSION_METHOD: Record<number, string> = {
  1: 'محلی',
  2: 'LDAP',
  3: 'ویندوز',
}

export const SESSION_STATUS: Record<number, string> = {
  1: 'فعال',
  2: 'پایان‌یافته',
  3: 'منقضی',
  4: 'ناموفق',
}

export const LOCATION_STATUS: Record<number, string> = {
  1: 'در انتظار تأیید',
  2: 'تأییدشده',
  3: 'ردشده',
  4: 'غیرفعال',
}
