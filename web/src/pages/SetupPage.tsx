import { useQuery } from '@tanstack/react-query'
import { useEffect, useRef, useState } from 'react'
import { Navigate, useNavigate } from 'react-router-dom'
import { api } from '../lib/api'
import type { SetupDatabaseResult, SetupDatabaseState, SetupStatus } from '../lib/types'

type StepKey = 'database' | 'directory' | 'sms' | 'organization' | 'policy' | 'summary'

const STEPS: { key: StepKey; title: string; hint: string }[] = [
  { key: 'database', title: 'پایگاه داده', hint: 'محل ذخیره اطلاعات سامانه' },
  { key: 'directory', title: 'دامنه', hint: 'ورود با حساب‌های Active Directory' },
  { key: 'sms', title: 'پیامک', hint: 'درگاه اطلاع‌رسانی' },
  { key: 'organization', title: 'سازمان و مدیر', hint: 'هویت سامانه و حساب اضطراری' },
  { key: 'policy', title: 'قواعد و ظاهر', hint: 'قواعد مأموریت، نشست، حکم و لاگ' },
  { key: 'summary', title: 'تأیید', hint: 'مرور و ایجاد سامانه' },
]

const DEFAULT_FILTER = '(&(objectClass=user)(sAMAccountName={0}))'

const initialForm = {
  serverName: '',
  databaseName: 'NovaFleetDb',
  useWindowsAuthentication: true,
  userName: '',
  password: '',
  trustServerCertificate: true,
  connectionString: '',

  directoryEnabled: false,
  directoryHost: '',
  directoryPort: 636,
  directoryUseSsl: true,
  directoryDomain: '',
  directoryBaseDn: '',
  directoryBindDn: '',
  directoryBindPassword: '',
  directoryTimeoutSeconds: 15,
  directorySearchFilter: DEFAULT_FILTER,

  smsEnabled: false,
  smsMode: 'Simulation',
  smsEndpoint: '',
  smsOrganization: '',
  smsUsername: '',
  smsSenderNumber: '',
  smsPassword: '',
  smsTimeoutSeconds: 20,
  smsRetryMinutes: 5,
  smsMaxAttempts: 5,

  organizationName: '',
  logoDataUrl: null as string | null,
  adminUsername: 'admin',
  adminPassword: '',
  adminFirstName: '',
  adminLastName: '',
  adminMobile: '',

  minimumGapMinutes: 30,
  futureRequestDays: 30,
  withdrawalAutoCloseHours: 24,
  startReminderMinutes: 60,
  sessionAbsoluteMinutes: 480,
  sessionIdleMinutes: 30,
  orderHeader: '',
  orderFooter: '',
  orderShowLogo: true,
  logLevel: 'Information',
  reportPaperSize: 'A4',
  pdfFontPath: '',
}

type Form = typeof initialForm
type Setter = <K extends keyof Form>(key: K) => (value: Form[K]) => void

export function SetupPage() {
  const navigate = useNavigate()
  const { data: setup, isLoading } = useQuery({
    queryKey: ['setup-status'],
    queryFn: () => api.get<SetupStatus>('/api/setup/status'),
  })
  const { data: databaseState } = useQuery({
    queryKey: ['setup-database'],
    queryFn: () => api.get<SetupDatabaseState>('/api/setup/database'),
  })

  const [step, setStep] = useState(0)
  const [form, setForm] = useState(initialForm)
  const [databaseReady, setDatabaseReady] = useState(false)
  const [restartPending, setRestartPending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const set: Setter = (key) => (value) => setForm((prev) => ({ ...prev, [key]: value }))

  // اتصال فعلی یک‌بار روی فیلدهای گام دیتابیس می‌نشیند؛ پس از آن تایپ مدیر بازنویسی نمی‌شود.
  const databasePrefilled = useRef(false)
  useEffect(() => {
    if (databasePrefilled.current || !databaseState?.isConfigured) return
    databasePrefilled.current = true
    setForm((prev) => ({
      ...prev,
      serverName: databaseState.serverName ?? prev.serverName,
      databaseName: databaseState.databaseName ?? prev.databaseName,
      useWindowsAuthentication: databaseState.useWindowsAuthentication,
      userName: databaseState.userName ?? prev.userName,
    }))
  }, [databaseState])

  if (setup?.isCompleted) return <Navigate to="/login" replace />

  const goNext = () => {
    setError(null)
    setStep((current) => Math.min(STEPS.length - 1, current + 1))
  }

  const goBack = () => {
    setError(null)
    setStep((current) => Math.max(0, current - 1))
  }

  const submit = async () => {
    setBusy(true)
    setError(null)
    try {
      await api.post('/api/setup/initialize', buildPayload(form))
      navigate('/login')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'راه‌اندازی ناموفق بود.')
    } finally {
      setBusy(false)
    }
  }

  const blocked = restartPending || (step === 0 && !databaseReady)

  return (
    <div className="min-h-screen bg-emerald-950 px-4 py-10">
      <div className="mx-auto w-full max-w-3xl">
        <header className="mb-6 text-center">
          <h1 className="text-2xl font-extrabold text-white">راه‌اندازی اولیه ناوگان نوین</h1>
          <p className="mt-2 text-sm text-emerald-300/80">
            گام {step + 1} از {STEPS.length} — {STEPS[step].hint}
          </p>
        </header>

        <ol className="mb-5 flex flex-wrap items-center gap-2 text-xs font-medium">
          {STEPS.map((item, index) => (
            <li
              key={item.key}
              className={`rounded-full px-3 py-1.5 ring-1 ${
                index === step
                  ? 'bg-white text-emerald-900 ring-white'
                  : index < step
                    ? 'bg-emerald-900 text-emerald-200 ring-emerald-800'
                    : 'bg-emerald-950 text-emerald-400/70 ring-emerald-800'
              }`}
            >
              {item.title}
            </li>
          ))}
        </ol>

        <div className="card p-7">
          {isLoading ? (
            <p className="py-8 text-center text-slate-400">در حال بررسی وضعیت سامانه…</p>
          ) : (
            <>
              {step === 0 && (
                <DatabaseStep
                  form={form}
                  set={set}
                  ready={databaseReady}
                  onApplied={(result) => {
                    setDatabaseReady(true)
                    setRestartPending(result.restartRequired)
                    if (!result.restartRequired) goNext()
                  }}
                  onRestarted={() => {
                    setRestartPending(false)
                    goNext()
                  }}
                  setError={setError}
                />
              )}
              {step === 1 && <DirectoryStep form={form} set={set} />}
              {step === 2 && <SmsStep form={form} set={set} />}
              {step === 3 && <OrganizationStep form={form} set={set} />}
              {step === 4 && <PolicyStep form={form} set={set} />}
              {step === 5 && <SummaryStep form={form} />}

              {error && <div className="mt-5 rounded-xl bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700 ring-1 ring-rose-100">{error}</div>}

              {step > 0 && (
                <div className="mt-7 flex items-center justify-between gap-3">
                  <button type="button" className="btn-ghost" onClick={goBack} disabled={busy}>
                    بازگشت
                  </button>
                  {step === STEPS.length - 1 ? (
                    <button type="button" className="btn-primary" onClick={submit} disabled={busy}>
                      {busy ? 'در حال ایجاد…' : 'ایجاد و شروع'}
                    </button>
                  ) : (
                    <button type="button" className="btn-primary" onClick={goNext} disabled={busy}>
                      گام بعد
                    </button>
                  )}
                </div>
              )}
            </>
          )}
        </div>

        {blocked && !restartPending && (
          <p className="mt-4 text-center text-[11px] text-emerald-300/70">پیش از ادامه، اتصال پایگاه داده را آماده کنید.</p>
        )}
      </div>
    </div>
  )
}

function Field({ label, hint, children }: { label: string; hint?: string; children: React.ReactNode }) {
  return (
    <div>
      <label className="label">{label}</label>
      {children}
      {hint && <p className="mt-1 text-[11px] text-slate-400">{hint}</p>}
    </div>
  )
}

function Toggle({ label, checked, onChange, hint }: { label: string; checked: boolean; onChange: (value: boolean) => void; hint?: string }) {
  return (
    <label className="flex cursor-pointer items-start gap-3 rounded-xl bg-slate-50 px-4 py-3 ring-1 ring-slate-200">
      <input type="checkbox" className="mt-1 h-4 w-4 accent-emerald-700" checked={checked} onChange={(e) => onChange(e.target.checked)} />
      <span>
        <span className="block text-sm font-semibold text-slate-700">{label}</span>
        {hint && <span className="mt-0.5 block text-[11px] leading-5 text-slate-500">{hint}</span>}
      </span>
    </label>
  )
}

function DatabaseStep({
  form,
  set,
  ready,
  onApplied,
  onRestarted,
  setError,
}: {
  form: Form
  set: Setter
  ready: boolean
  onApplied: (result: SetupDatabaseResult) => void
  onRestarted: () => void
  setError: (message: string | null) => void
}) {
  const { data: state } = useQuery({ queryKey: ['setup-database'], queryFn: () => api.get<SetupDatabaseState>('/api/setup/database') })
  const [busy, setBusy] = useState(false)
  const [hint, setHint] = useState<string | null>(null)
  const [waiting, setWaiting] = useState(false)

  const advanced = form.connectionString.trim().length > 0

  const apply = async () => {
    setBusy(true)
    setError(null)
    setHint(null)
    try {
      const result = await api.post<SetupDatabaseResult>('/api/setup/database', {
        serverName: form.serverName,
        databaseName: form.databaseName,
        useWindowsAuthentication: form.useWindowsAuthentication,
        userName: form.userName,
        password: form.password,
        trustServerCertificate: form.trustServerCertificate,
        connectionString: form.connectionString,
      })
      setHint(result.hint)
      setWaiting(result.restartRequired)
      onApplied(result)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'اتصال به پایگاه داده برقرار نشد.')
    } finally {
      setBusy(false)
    }
  }

  const check = async () => {
    setBusy(true)
    setError(null)
    try {
      const current = await api.get<SetupDatabaseState>('/api/setup/database')
      if (current.source === 'BootstrapFile') {
        setWaiting(false)
        onRestarted()
      } else {
        setError(`سرویس هنوز از «${current.source}» می‌خواند؛ آن منبع را بردارید و سرویس را راه‌اندازی مجدد کنید.`)
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'بررسی اتصال ممکن نشد.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="space-y-4">
      <p className="text-sm leading-6 text-slate-500">
        اگر پایگاه داده وجود نداشته باشد ساخته و جداولش ایجاد می‌شود. وقتی سرور یا دیتابیس را عوض کنید،
        سرویس باید یک‌بار restart شود تا گام‌های بعدی روی همان پایگاه داده نوشته شوند.
      </p>

      {state?.isConfigured && (
        <div className="rounded-xl bg-sky-50 px-4 py-3 text-xs text-sky-900 ring-1 ring-sky-100">
          اتصال فعلی: <span className="num font-semibold" dir="ltr">{state.serverName}</span> /{' '}
          <span className="num font-semibold" dir="ltr">{state.databaseName}</span> — منبع:{' '}
          <span className="num font-semibold" dir="ltr">{state.source}</span>
          {state.useWindowsAuthentication ? ' — ورود با ویندوز' : ` — کاربر ${state.userName}`}
        </div>
      )}

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <Field label="نام سرور" hint="مثلاً SHAHABPC یا localhost\\SQLEXPRESS">
          <input className="input" dir="ltr" value={form.serverName} onChange={(e) => set('serverName')(e.target.value)} disabled={advanced} />
        </Field>
        <Field label="نام پایگاه داده">
          <input className="input" dir="ltr" value={form.databaseName} onChange={(e) => set('databaseName')(e.target.value)} disabled={advanced} />
        </Field>
      </div>

      <Toggle
        label="ورود با حساب ویندوز (Integrated Security)"
        checked={form.useWindowsAuthentication}
        onChange={set('useWindowsAuthentication')}
        hint="در نصب محلی معمولاً درست است؛ برای حساب SQL آن را خاموش کنید."
      />

      {!form.useWindowsAuthentication && !advanced && (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Field label="شناسه کاربری SQL">
            <input className="input" dir="ltr" value={form.userName} onChange={(e) => set('userName')(e.target.value)} />
          </Field>
          <Field label="رمز عبور SQL" hint={state?.hasPassword ? 'اگر خالی بماند، رمز اتصال فعلی دوباره استفاده می‌شود.' : undefined}>
            <input className="input" type="password" dir="ltr" value={form.password} onChange={(e) => set('password')(e.target.value)} />
          </Field>
        </div>
      )}

      <Toggle label="TrustServerCertificate" checked={form.trustServerCertificate} onChange={set('trustServerCertificate')} hint="برای گواهی self-signed در شبکه داخلی." />

      {!advanced && (
        <details className="rounded-xl bg-slate-50 px-4 py-3 ring-1 ring-slate-200">
          <summary className="cursor-pointer text-sm font-semibold text-slate-700">رشته اتصال پیشرفته (اختیاری)</summary>
          <div className="mt-3">
            <Field label="ConnectionString کامل" hint="با پر کردن این فیلد، سرور و دیتابیس بالا نادیده گرفته می‌شوند.">
              <textarea className="input min-h-24 font-mono text-xs" dir="ltr" value={form.connectionString} onChange={(e) => set('connectionString')(e.target.value)} />
            </Field>
          </div>
        </details>
      )}

      {hint && <div className="rounded-xl bg-emerald-50 px-4 py-3 text-xs leading-6 text-emerald-900 ring-1 ring-emerald-100">{hint}</div>}

      {waiting ? (
        <div className="rounded-xl bg-amber-50 px-4 py-4 ring-1 ring-amber-200">
          <p className="text-sm font-bold text-amber-900">سرویس باید یک‌بار راه‌اندازی مجدد شود</p>
          <p className="mt-1 text-xs leading-6 text-amber-800">
            رشته اتصال ذخیره شد ولی فرآیند جاری همچنان با اتصال قبلی کار می‌کند. سرویس NovaFleet را restart
            کنید و بعد از بالا آمدن، دکمه «بررسی اتصال» را بزنید.
          </p>
          <button type="button" className="btn-ghost btn-sm mt-3" onClick={check} disabled={busy}>
            {busy ? 'در حال بررسی…' : 'بررسی اتصال'}
          </button>
        </div>
      ) : (
        <button type="button" className="btn-primary w-full" onClick={apply} disabled={busy || (!advanced && !form.serverName.trim())}>
          {busy ? 'در حال اتصال…' : ready ? 'ذخیره و آزمایش دوباره اتصال' : 'اتصال و آماده‌سازی پایگاه داده'}
        </button>
      )}
    </div>
  )
}

function DirectoryStep({ form, set }: { form: Form; set: Setter }) {
  return (
    <div className="space-y-4">
      <Toggle
        label="ورود با حساب دامنه (Active Directory)"
        checked={form.directoryEnabled}
        onChange={set('directoryEnabled')}
        hint="با خاموشی، تنها همان حساب محلی مدیر که در گام بعد می‌سازید می‌تواند وارد شود."
      />
      {form.directoryEnabled && (
        <>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <Field label="سرور دامنه">
              <input className="input" dir="ltr" value={form.directoryHost} onChange={(e) => set('directoryHost')(e.target.value)} />
            </Field>
            <Field label="پورت">
              <input className="input" type="number" dir="ltr" value={form.directoryPort} onChange={(e) => set('directoryPort')(Number(e.target.value))} />
            </Field>
            <Field label="تایم‌اوت (ثانیه)">
              <input className="input" type="number" dir="ltr" value={form.directoryTimeoutSeconds} onChange={(e) => set('directoryTimeoutSeconds')(Number(e.target.value))} />
            </Field>
          </div>
          <Toggle label="اتصال امن LDAPS" checked={form.directoryUseSsl} onChange={set('directoryUseSsl')} hint="پورت پیش‌فرض ۶۳۶." />
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <Field label="نام دامنه">
              <input className="input" dir="ltr" value={form.directoryDomain} onChange={(e) => set('directoryDomain')(e.target.value)} />
            </Field>
            <Field label="BaseDn" hint="محدوده جست‌وجوی کاربران.">
              <input className="input" dir="ltr" value={form.directoryBaseDn} onChange={(e) => set('directoryBaseDn')(e.target.value)} />
            </Field>
            <Field label="حساب Bind" hint="حسابی با حق خواندن محدود.">
              <input className="input" dir="ltr" value={form.directoryBindDn} onChange={(e) => set('directoryBindDn')(e.target.value)} />
            </Field>
            <Field label="رمز حساب Bind" hint="رمزنگاری شده ذخیره می‌شود و هرگز به صفحه برنمی‌گردد.">
              <input className="input" type="password" dir="ltr" value={form.directoryBindPassword} onChange={(e) => set('directoryBindPassword')(e.target.value)} />
            </Field>
          </div>
          <Field label="فیلتر جست‌وجو" hint="باید شامل {0} باشد.">
            <input className="input" dir="ltr" value={form.directorySearchFilter} onChange={(e) => set('directorySearchFilter')(e.target.value)} />
          </Field>
        </>
      )}
    </div>
  )
}

function SmsStep({ form, set }: { form: Form; set: Setter }) {
  return (
    <div className="space-y-4">
      <Toggle
        label="کل سامانه پیامک روشن باشد"
        checked={form.smsEnabled}
        onChange={set('smsEnabled')}
        hint="با خاموشی هیچ پیامکی به سرویس‌دهنده نمی‌رود و نیازی به پیکره‌بندی درگاه نیست."
      />
      {form.smsEnabled && (
        <>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <Field label="روش ارسال" hint="Simulation برای آزمایش بدون هزینه است.">
              <select className="input" value={form.smsMode} onChange={(e) => set('smsMode')(e.target.value)}>
                {['Simulation', 'Wsdl', 'Api'].map((mode) => (
                  <option key={mode} value={mode}>
                    {mode}
                  </option>
                ))}
              </select>
            </Field>
            <Field label="شماره فرستنده">
              <input className="input" dir="ltr" value={form.smsSenderNumber} onChange={(e) => set('smsSenderNumber')(e.target.value)} />
            </Field>
          </div>
          {form.smsMode !== 'Simulation' && (
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <Field label="آدرس سرویس" hint="با http یا https شروع شود.">
                <input className="input" dir="ltr" value={form.smsEndpoint} onChange={(e) => set('smsEndpoint')(e.target.value)} />
              </Field>
              <Field label="سازمان/خوشه سرویس‌دهنده">
                <input className="input" dir="ltr" value={form.smsOrganization} onChange={(e) => set('smsOrganization')(e.target.value)} />
              </Field>
              <Field label="شناسه کاربری">
                <input className="input" dir="ltr" value={form.smsUsername} onChange={(e) => set('smsUsername')(e.target.value)} />
              </Field>
              <Field label="رمز عبور" hint="رمزنگاری شده ذخیره می‌شود و هرگز به صفحه برنمی‌گردد.">
                <input className="input" type="password" dir="ltr" value={form.smsPassword} onChange={(e) => set('smsPassword')(e.target.value)} />
              </Field>
            </div>
          )}
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <Field label="تایم‌اوت (ثانیه)">
              <input className="input" type="number" dir="ltr" value={form.smsTimeoutSeconds} onChange={(e) => set('smsTimeoutSeconds')(Number(e.target.value))} />
            </Field>
            <Field label="فاصله تلاش مجدد (دقیقه)">
              <input className="input" type="number" dir="ltr" value={form.smsRetryMinutes} onChange={(e) => set('smsRetryMinutes')(Number(e.target.value))} />
            </Field>
            <Field label="حداکثر تعداد تلاش">
              <input className="input" type="number" dir="ltr" value={form.smsMaxAttempts} onChange={(e) => set('smsMaxAttempts')(Number(e.target.value))} />
            </Field>
          </div>
        </>
      )}
    </div>
  )
}

function OrganizationStep({ form, set }: { form: Form; set: Setter }) {
  const logoInputRef = useRef<HTMLInputElement>(null)
  const pickLogo = (file: File | undefined) => {
    if (!file) return
    if (file.size > 512 * 1024) {
      set('logoDataUrl')(null)
      return
    }
    const reader = new FileReader()
    reader.onload = () => set('logoDataUrl')(typeof reader.result === 'string' ? reader.result : null)
    reader.readAsDataURL(file)
  }

  return (
    <div className="space-y-4">
      <Field label="نام سازمان" hint="در سربرگ، حکم مأموریت و گزارش‌ها استفاده می‌شود.">
        <input className="input" value={form.organizationName} onChange={(e) => set('organizationName')(e.target.value)} />
      </Field>

      <Field label="لوگو">
        <div className="flex items-center gap-3">
          <div className="flex h-16 w-16 items-center justify-center overflow-hidden rounded-xl border border-slate-200 bg-slate-50">
            {form.logoDataUrl ? (
              <img src={form.logoDataUrl} alt="لوگو" className="h-full w-full object-contain" />
            ) : (
              <span className="text-[10px] text-slate-400">بدون لوگو</span>
            )}
          </div>
          <input
            ref={logoInputRef}
            type="file"
            accept="image/png,image/jpeg,image/svg+xml"
            className="hidden"
            onChange={(e) => {
              pickLogo(e.target.files?.[0])
              // پاک‌کردن مقدار تا انتخاب دوباره همان فایل هم onChange را روشن کند.
              e.target.value = ''
            }}
          />
          <button type="button" className="btn-ghost btn-sm" onClick={() => logoInputRef.current?.click()}>
            انتخاب تصویر
          </button>
          {form.logoDataUrl && (
            <button type="button" className="btn-ghost btn-sm text-rose-600" onClick={() => set('logoDataUrl')(null)}>
              حذف
            </button>
          )}
        </div>
      </Field>

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <Field label="نام مدیر">
          <input className="input" value={form.adminFirstName} onChange={(e) => set('adminFirstName')(e.target.value)} />
        </Field>
        <Field label="نام خانوادگی مدیر">
          <input className="input" value={form.adminLastName} onChange={(e) => set('adminLastName')(e.target.value)} />
        </Field>
        <Field label="نام کاربری مدیر محلی">
          <input className="input" dir="ltr" value={form.adminUsername} onChange={(e) => set('adminUsername')(e.target.value)} />
        </Field>
        <Field label="موبایل مدیر">
          <input className="input" dir="ltr" value={form.adminMobile} onChange={(e) => set('adminMobile')(e.target.value)} placeholder="09xxxxxxxxx" />
        </Field>
      </div>

      <Field label="رمز عبور مدیر (حداقل ۸ نویسه)" hint="این حساب فقط برای مواقع اضطراری و قطع دسترسی دایرکتوری است.">
        <input className="input" type="password" dir="ltr" value={form.adminPassword} onChange={(e) => set('adminPassword')(e.target.value)} minLength={8} />
      </Field>
    </div>
  )
}

function PolicyStep({ form, set }: { form: Form; set: Setter }) {
  return (
    <div className="space-y-6">
      <section className="space-y-4">
        <h3 className="text-sm font-bold text-emerald-950">قواعد مأموریت</h3>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Field label="حداقل فاصله بین دو مأموریت (دقیقه)">
            <input className="input" type="number" dir="ltr" value={form.minimumGapMinutes} onChange={(e) => set('minimumGapMinutes')(Number(e.target.value))} />
          </Field>
          <Field label="حداکثر بازه آینده درخواست (روز)">
            <input className="input" type="number" dir="ltr" value={form.futureRequestDays} onChange={(e) => set('futureRequestDays')(Number(e.target.value))} />
          </Field>
          <Field label="مهلت بسته‌شدن خودکار انصراف (ساعت)">
            <input className="input" type="number" dir="ltr" value={form.withdrawalAutoCloseHours} onChange={(e) => set('withdrawalAutoCloseHours')(Number(e.target.value))} />
          </Field>
          <Field label="یادآوری پیامک پیش از شروع (دقیقه)">
            <input className="input" type="number" dir="ltr" value={form.startReminderMinutes} onChange={(e) => set('startReminderMinutes')(Number(e.target.value))} />
          </Field>
        </div>
      </section>

      <section className="space-y-4">
        <h3 className="text-sm font-bold text-emerald-950">نشست</h3>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Field label="عمر کل نشست (دقیقه)">
            <input className="input" type="number" dir="ltr" value={form.sessionAbsoluteMinutes} onChange={(e) => set('sessionAbsoluteMinutes')(Number(e.target.value))} />
          </Field>
          <Field label="حداکثر بی‌فعالیتی (دقیقه)">
            <input className="input" type="number" dir="ltr" value={form.sessionIdleMinutes} onChange={(e) => set('sessionIdleMinutes')(Number(e.target.value))} />
          </Field>
        </div>
      </section>

      <section className="space-y-4">
        <h3 className="text-sm font-bold text-emerald-950">حکم، گزارش و لاگ</h3>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Field label="سربرگ حکم مأموریت">
            <input className="input" value={form.orderHeader} onChange={(e) => set('orderHeader')(e.target.value)} />
          </Field>
          <Field label="پاورقی حکم مأموریت">
            <input className="input" value={form.orderFooter} onChange={(e) => set('orderFooter')(e.target.value)} />
          </Field>
          <Field label="اندازه کاغذ گزارش">
            <select className="input" value={form.reportPaperSize} onChange={(e) => set('reportPaperSize')(e.target.value)}>
              {['A4', 'A5', 'Letter'].map((size) => (
                <option key={size} value={size}>
                  {size}
                </option>
              ))}
            </select>
          </Field>
          <Field label="سطح لاگ" hint="پس از راه‌اندازی مجدد سرویس اعمال می‌شود.">
            <select className="input" value={form.logLevel} onChange={(e) => set('logLevel')(e.target.value)}>
              {['Critical', 'Error', 'Warning', 'Information', 'Debug', 'Trace'].map((level) => (
                <option key={level} value={level}>
                  {level}
                </option>
              ))}
            </select>
          </Field>
        </div>
        <Field label="مسیر فونت فارسی PDF" hint="مسیر کامل؛ اگر خالی بماند فونت پیش‌فرض ویندوز به کار می‌رود.">
          <input className="input" dir="ltr" value={form.pdfFontPath} onChange={(e) => set('pdfFontPath')(e.target.value)} />
        </Field>
        <Toggle label="لوگو در حکم مأموریت نمایش داده شود" checked={form.orderShowLogo} onChange={set('orderShowLogo')} />
      </section>
    </div>
  )
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-start justify-between gap-4 border-b border-slate-100 py-2 text-sm last:border-0">
      <span className="shrink-0 text-slate-500">{label}</span>
      <span className="text-left font-semibold text-emerald-950" dir="auto">
        {value}
      </span>
    </div>
  )
}

function SummaryStep({ form }: { form: Form }) {
  return (
    <div className="space-y-5">
      <section>
        <h3 className="mb-1 text-sm font-bold text-emerald-950">پایگاه داده</h3>
        <Row label="هدف" value={form.connectionString.trim() ? 'رشته اتصال پیشرفته' : `${form.serverName} \\ ${form.databaseName}`} />
        <Row label="نحوه ورود" value={form.useWindowsAuthentication ? 'حساب ویندوز' : `SQL — ${form.userName}`} />
      </section>

      <section>
        <h3 className="mb-1 text-sm font-bold text-emerald-950">سرویس‌ها</h3>
        <Row label="دامنه" value={form.directoryEnabled ? `${form.directoryHost}:${form.directoryPort}` : 'غیرفعال'} />
        <Row label="پیامک" value={form.smsEnabled ? `${form.smsMode}${form.smsSenderNumber ? ` — ${form.smsSenderNumber}` : ''}` : 'غیرفعال'} />
      </section>

      <section>
        <h3 className="mb-1 text-sm font-bold text-emerald-950">سازمان و مدیر</h3>
        <Row label="نام سازمان" value={form.organizationName || '—'} />
        <Row label="مدیر محلی" value={`${form.adminUsername} — ${form.adminFirstName} ${form.adminLastName}`.trim()} />
        <Row label="موبایل مدیر" value={form.adminMobile || '—'} />
      </section>

      <section>
        <h3 className="mb-1 text-sm font-bold text-emerald-950">قواعد و ظاهر</h3>
        <Row label="فاصله مأموریت / بازه آینده" value={`${form.minimumGapMinutes} دقیقه / ${form.futureRequestDays} روز`} />
        <Row label="نشست" value={`${form.sessionAbsoluteMinutes} دقیقه عمر، ${form.sessionIdleMinutes} دقیقه بی‌فعالیتی`} />
        <Row label="کاغذ گزارش / سطح لاگ" value={`${form.reportPaperSize} / ${form.logLevel}`} />
      </section>

      <p className="rounded-xl bg-slate-50 px-4 py-3 text-xs leading-6 text-slate-500 ring-1 ring-slate-200">
        با تأیید، مدیر محلی ساخته و ویزارد بسته می‌شود. از این پس همین مقادیر در بخش تنظیمات قابل ویرایش
        هستند؛ رمز دایرکتوری و درگاه پیامک دوباره پرسیده می‌شود.
      </p>
    </div>
  )
}

function buildPayload(form: Form) {
  return {
    organizationName: form.organizationName.trim(),
    adminUsername: form.adminUsername.trim(),
    adminPassword: form.adminPassword,
    adminFirstName: form.adminFirstName.trim(),
    adminLastName: form.adminLastName.trim(),
    adminMobile: form.adminMobile.trim(),
    logoDataUrl: form.logoDataUrl,
    directory: {
      enabled: form.directoryEnabled,
      host: form.directoryHost,
      port: form.directoryPort,
      useSsl: form.directoryUseSsl,
      domain: form.directoryDomain,
      baseDn: form.directoryBaseDn,
      bindDn: form.directoryBindDn,
      timeoutSeconds: form.directoryTimeoutSeconds,
      searchFilter: form.directorySearchFilter,
      bindPassword: form.directoryBindPassword,
    },
    sms: {
      enabled: form.smsEnabled,
      mode: form.smsMode,
      endpoint: form.smsEndpoint,
      organization: form.smsOrganization,
      username: form.smsUsername,
      senderNumber: form.smsSenderNumber,
      timeoutSeconds: form.smsTimeoutSeconds,
      retryMinutes: form.smsRetryMinutes,
      maxAttempts: form.smsMaxAttempts,
      password: form.smsPassword,
    },
    operations: {
      logLevel: form.logLevel,
      pdfFontPath: form.pdfFontPath,
      reportPaperSize: form.reportPaperSize,
    },
    rules: {
      minimumGapMinutes: form.minimumGapMinutes,
      futureRequestDays: form.futureRequestDays,
      withdrawalAutoCloseHours: form.withdrawalAutoCloseHours,
      startReminderMinutes: form.startReminderMinutes,
    },
    session: {
      absoluteMinutes: form.sessionAbsoluteMinutes,
      idleMinutes: form.sessionIdleMinutes,
    },
    template: {
      headerText: form.orderHeader,
      footerText: form.orderFooter,
      showLogo: form.orderShowLogo,
    },
  }
}
