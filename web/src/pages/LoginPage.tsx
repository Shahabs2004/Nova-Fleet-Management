import { useState } from 'react'
import { Navigate, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { api } from '../lib/api'
import type { SetupStatus } from '../lib/types'
import { useQuery } from '@tanstack/react-query'

export function LoginPage() {
  const { user, loginLocal, loginLdap } = useAuth()
  const navigate = useNavigate()
  const [mode, setMode] = useState<'local' | 'ldap'>('ldap')
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const { data: setup } = useQuery({ queryKey: ['setup-status'], queryFn: () => api.get<SetupStatus>('/api/setup/status') })

  if (user) return <Navigate to="/dashboard" replace />
  if (setup && !setup.isCompleted) return <Navigate to="/setup" replace />

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)
    setBusy(true)
    try {
      if (mode === 'local') await loginLocal(username, password)
      else await loginLdap(username, password)
      navigate('/dashboard')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'ورود ناموفق بود.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="relative flex min-h-screen items-center justify-center overflow-hidden bg-emerald-950 px-4">
      <div className="pointer-events-none absolute inset-0">
        <div className="absolute -top-40 -right-40 h-96 w-96 rounded-full bg-emerald-800/40 blur-3xl" />
        <div className="absolute -bottom-40 -left-40 h-96 w-96 rounded-full bg-gold-500/10 blur-3xl" />
        <svg className="absolute inset-0 h-full w-full opacity-[0.04]" xmlns="http://www.w3.org/2000/svg">
          <defs>
            <pattern id="grid" width="40" height="40" patternUnits="userSpaceOnUse">
              <path d="M40 0H0v40" fill="none" stroke="white" strokeWidth="1" />
            </pattern>
          </defs>
          <rect width="100%" height="100%" fill="url(#grid)" />
        </svg>
      </div>

      <div className="relative w-full max-w-md">
        <div className="mb-8 flex flex-col items-center">
          <div className="mb-4 flex h-16 w-16 items-center justify-center rounded-2xl bg-emerald-900/70 ring-1 ring-gold-500/50">
            <svg width="34" height="34" viewBox="0 0 24 24" fill="none" stroke="#C8A44A" strokeWidth="1.6" strokeLinecap="round">
              <path d="M3 15h4l2-5h6l2 5h4" />
              <circle cx="7" cy="17" r="2" stroke="#2DD4BF" />
              <circle cx="17" cy="17" r="2" stroke="#2DD4BF" />
              <path d="M10 7h5" stroke="#2DD4BF" />
            </svg>
          </div>
          <h1 className="text-2xl font-extrabold text-white">ناوگان نوین</h1>
          <p className="mt-2 text-sm text-emerald-300/80">سامانه جامع مدیریت ناوگان خودرویی</p>
        </div>

        <div className="card p-7">
          <div className="mb-5 grid grid-cols-2 gap-2 rounded-xl bg-slate-100 p-1">
            <button
              onClick={() => setMode('ldap')}
              className={`rounded-lg px-3 py-2 text-sm font-semibold transition-colors ${
                mode === 'ldap' ? 'bg-white text-emerald-900 shadow-sm' : 'text-slate-500 hover:text-slate-700'
              }`}
            >
              حساب سازمانی
            </button>
            <button
              onClick={() => setMode('local')}
              className={`rounded-lg px-3 py-2 text-sm font-semibold transition-colors ${
                mode === 'local' ? 'bg-white text-emerald-900 shadow-sm' : 'text-slate-500 hover:text-slate-700'
              }`}
            >
              حساب محلی
            </button>
          </div>

          <form onSubmit={submit} className="space-y-4">
            <div>
              <label className="label">نام کاربری</label>
              <input
                className="input"
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                placeholder={mode === 'ldap' ? 'نام کاربری سازمانی' : 'نام کاربری محلی'}
                dir="ltr"
                autoComplete="username"
                required
              />
            </div>
            <div>
              <label className="label">رمز عبور</label>
              <input
                className="input"
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                dir="ltr"
                autoComplete="current-password"
                required
              />
            </div>
            {error && (
              <div className="rounded-xl bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700 ring-1 ring-rose-100">{error}</div>
            )}
            <button type="submit" disabled={busy} className="btn-primary w-full">
              {busy ? 'در حال ورود…' : 'ورود به سامانه'}
            </button>
          </form>
        </div>

        <p className="mt-6 text-center text-[11px] text-emerald-500/50">طراحی و توسعه — شهاب صادقی</p>
      </div>
    </div>
  )
}
