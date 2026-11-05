import { useState } from 'react'
import { Navigate, useNavigate } from 'react-router-dom'
import { api } from '../lib/api'
import type { SetupStatus } from '../lib/types'
import { useQuery } from '@tanstack/react-query'

export function SetupPage() {
  const navigate = useNavigate()
  const { data: setup, isLoading } = useQuery({ queryKey: ['setup-status'], queryFn: () => api.get<SetupStatus>('/api/setup/status') })
  const [form, setForm] = useState({
    organizationName: '',
    adminUsername: 'admin',
    adminPassword: '',
    adminFirstName: '',
    adminLastName: '',
    adminMobile: '',
  })
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  if (setup?.isCompleted) return <Navigate to="/login" replace />

  const set = (key: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setForm((prev) => ({ ...prev, [key]: e.target.value }))

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await api.post('/api/setup/initialize', form)
      navigate('/login')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'راه‌اندازی ناموفق بود.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-emerald-950 px-4">
      <div className="w-full max-w-xl">
        <div className="mb-8 text-center">
          <h1 className="text-2xl font-extrabold text-white">راه‌اندازی اولیه ناوگان نوین</h1>
          <p className="mt-2 text-sm text-emerald-300/80">نام سازمان و حساب مدیر محلی اضطراری را ایجاد کنید</p>
        </div>
        <div className="card p-7">
          {isLoading ? (
            <p className="py-8 text-center text-slate-400">در حال بررسی وضعیت سامانه…</p>
          ) : (
            <form onSubmit={submit} className="space-y-4">
              <div>
                <label className="label">نام سازمان</label>
                <input className="input" value={form.organizationName} onChange={set('organizationName')} required />
              </div>
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="label">نام مدیر</label>
                  <input className="input" value={form.adminFirstName} onChange={set('adminFirstName')} required />
                </div>
                <div>
                  <label className="label">نام خانوادگی مدیر</label>
                  <input className="input" value={form.adminLastName} onChange={set('adminLastName')} required />
                </div>
              </div>
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="label">نام کاربری مدیر</label>
                  <input className="input" dir="ltr" value={form.adminUsername} onChange={set('adminUsername')} required />
                </div>
                <div>
                  <label className="label">موبایل مدیر</label>
                  <input className="input" dir="ltr" value={form.adminMobile} onChange={set('adminMobile')} placeholder="09xxxxxxxxx" required />
                </div>
              </div>
              <div>
                <label className="label">رمز عبور مدیر (حداقل ۸ نویسه)</label>
                <input className="input" type="password" dir="ltr" value={form.adminPassword} onChange={set('adminPassword')} minLength={8} required />
                <p className="mt-1.5 text-[11px] text-slate-400">
                  این حساب فقط برای مواقع اضطراری و قطع دسترسی دایرکتوری است؛ ورود روزمره از طریق حساب سازمانی انجام می‌شود.
                </p>
              </div>
              {error && <div className="rounded-xl bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700 ring-1 ring-rose-100">{error}</div>}
              <button type="submit" disabled={busy} className="btn-primary w-full">
                {busy ? 'در حال ایجاد…' : 'ایجاد و شروع'}
              </button>
            </form>
          )}
        </div>
      </div>
    </div>
  )
}
